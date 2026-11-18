"""End-to-end smoke test: seeds data via the dashboard + mobile API and checks every page/endpoint."""
import os, re, sys, io, json, requests, openpyxl
from pathlib import Path
B = os.environ.get("BASE", "http://127.0.0.1:5080")
APHONE, APW = os.environ["HYUNDAI_SMOKE_ADMIN_PHONE"], os.environ["HYUNDAI_SMOKE_ADMIN_PASSWORD"]
results = []
def check(name, ok, info=""):
    info = "" if ok else re.sub(r'("(?:accessToken|token)"\s*:\s*")[^"]+', r'\1[redacted]', str(info))
    results.append((name, bool(ok), info)); print(("PASS " if ok else "FAIL ") + name + (" — " + str(info) if info and not ok else ""))
def tok(html): m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html) or re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html); return m.group(1)
def login(phone, pw):
    s = requests.Session(); r = s.get(B + "/Account/Login")
    r = s.post(B + "/Account/Login", data={"PhoneNumber": phone, "Password": pw, "__RequestVerificationToken": tok(r.text)}, allow_redirects=False)
    return s, r
def post(s, path, data, files=None, ref="/"):
    t = tok(s.get(B + ref).text); data = dict(data); data["__RequestVerificationToken"] = t
    return s.post(B + path, data=data, files=files, headers={"Referer": B + ref}, allow_redirects=True)
def flash(r): m = re.search(r'class="toast (ok|er) alert"[^>]*>.*?class="tx">(.*?)</div>', r.text, re.S) or re.search(r'class="alert (ok|er)"[^>]*>.*?<span>(.*?)</span>', r.text, re.S); return (m.group(1), m.group(2)) if m else (None, None)

# --- auth
_, r = login(APHONE, "wrong-password"); check("login wrong password rejected", r.status_code == 200 and "غير صحيحة" in r.text)
_, r = login("123", "x"); check("login phone validation", "رقم الهاتف" in r.text and r.status_code == 200)
s, r = login(APHONE, APW); check("main admin login", r.status_code == 302, r.status_code)
check("anon redirected to login", requests.get(B + "/Users", allow_redirects=False).headers.get("Location", "").startswith("/Account/Login") or "Login" in requests.get(B + "/Users", allow_redirects=False).headers.get("Location", ""))

# --- org
r = post(s, "/Branches/Save", {"name": "فرع بغداد", "number": 1}, ref="/Branches"); check("create branch", flash(r)[0] == "ok", flash(r))
post(s, "/Branches/Save", {"name": "فرع البصرة", "number": 2}, ref="/Branches")
r = post(s, "/Branches/Save", {"name": "فرع رقم مكرر", "number": 2}, ref="/Branches"); check("duplicate branch number rejected", flash(r)[0] == "er", flash(r))
check("branch number listed", 'data-v-number="2"' in s.get(B + "/Branches").text)
r = post(s, "/Branches/Save", {"name": "فرع بغداد"}, ref="/Branches"); check("duplicate branch rejected", flash(r)[0] == "er", flash(r))
r = post(s, "/Branches/Save", {"name": ""}, ref="/Branches"); check("empty branch rejected", flash(r)[0] == "er", flash(r))
html = s.get(B + "/Branches").text
bids = dict((n, int(i)) for i, n in re.findall(r'data-v-id="(\d+)" data-v-name="([^"]+)"', html))
check("branches listed", "فرع بغداد" in bids and "فرع البصرة" in bids, bids)
bg, bs = bids.get("فرع بغداد"), bids.get("فرع البصرة")
post(s, "/Branches/Save", {"name": "فرع حذف", "number": 99}, ref="/Branches")
did = int(re.findall(r'data-v-id="(\d+)" data-v-name="فرع حذف"', s.get(B + "/Branches").text)[0])
r = post(s, "/Branches/Delete", {"id": did}, ref="/Branches"); check("delete empty branch", flash(r)[0] == "ok", flash(r))
for n, t, b, no in [("مخزن بغداد الرئيسي", 1, bg, 1), ("مخزن بغداد المركبات", 2, bg, 2), ("مخزن البصرة", 1, bs, 6)]:
    r = post(s, "/Warehouses/Save", {"name": n, "type": t, "branchId": b, "number": no}, ref="/Warehouses"); check(f"create warehouse {n}", flash(r)[0] == "ok", flash(r))
r = post(s, "/Warehouses/Save", {"name": "مخزن رقم مكرر", "type": 1, "branchId": bs, "number": 6}, ref="/Warehouses"); check("duplicate warehouse number rejected", flash(r)[0] == "er", flash(r))
r = post(s, "/Branches/Delete", {"id": bg}, ref="/Branches"); check("delete branch with warehouses blocked", flash(r)[0] == "er", flash(r))
whtml = s.get(B + "/Warehouses").text
wids = dict((n, int(i)) for i, n in re.findall(r'data-v-id="(\d+)" data-v-name="([^"]+)"', whtml))
check("warehouse filter by branch", "مخزن البصرة" not in s.get(B + f"/Warehouses?branchId={bg}").text)
check("warehouse search", "مخزن البصرة" not in s.get(B + "/Warehouses?search=المركبات").text and "مخزن بغداد المركبات" in s.get(B + "/Warehouses?search=المركبات").text)

# --- admins
r = post(s, "/Admins/Create", {"Name": "أدمن بغداد", "PhoneNumber": "07711111111", "Password": "Admin#2026x", "BranchId": bg, "Role": 2}, ref="/Admins/Create"); check("create admin (role 2)", "تمت إضافة المشرف" in r.text, flash(r))
r = post(s, "/Admins/Create", {"Name": "سوبر", "PhoneNumber": "07822222222", "Password": "Super#2026x", "Role": 1}, ref="/Admins/Create"); check("create super admin", "تمت إضافة المشرف" in r.text)
r = post(s, "/Admins/Create", {"Name": "x", "PhoneNumber": "0691234567", "Password": "short", "Role": 2}, ref="/Admins/Create"); check("admin validation (phone/password)", r.url.endswith("/Admins/Create") and "hint e" in r.text)
ahtml = s.get(B + "/Admins").text
aid = int(re.search(r'data-remote="/Admins/Details/(\d+)">[^<]*<svg', ahtml) and re.findall(r'/Admins/Details/(\d+)', ahtml)[-1])
admin_ids = re.findall(r'/Admins/Details/(\d+)', ahtml)
check("admins list", len(admin_ids) == 3, admin_ids)
check("admin details partial", "تفاصيل المشرف" in s.get(B + f"/Admins/Details/{admin_ids[0]}").text)

# --- users
r = post(s, "/Users/Create", {"Name": "علي حسن", "PhoneNumber": "07733333333", "Password": "User#2026x", "ConfirmPassword": "User#2026x", "BranchIds": [bg], "WarehouseIds": [wids["مخزن بغداد الرئيسي"], wids["مخزن بغداد المركبات"]]}, ref="/Users/Create"); check("create user", "تمت إضافة المستخدم" in r.text, flash(r))
r = post(s, "/Users/Create", {"Name": "حسين كاظم", "PhoneNumber": "07744444444", "Password": "User#2026y", "ConfirmPassword": "User#2026y", "BranchIds": [bg], "WarehouseIds": [wids["مخزن بغداد الرئيسي"]]}, ref="/Users/Create"); check("create 2nd user", "تمت إضافة المستخدم" in r.text)
r = post(s, "/Users/Create", {"Name": "x", "PhoneNumber": "07733333333", "Password": "a", "ConfirmPassword": "b"}, ref="/Users/Create"); check("user validation (dup phone/mismatch/branch)", "hint e" in r.text and r.url.endswith("/Users/Create"))
r = post(s, "/Users/Create", {"Name": "x", "PhoneNumber": "07755555555", "Password": "User#2026z", "ConfirmPassword": "User#2026z", "BranchIds": [bg], "WarehouseIds": [wids["مخزن البصرة"]]}, ref="/Users/Create"); check("warehouse outside branch rejected", "hint e" in r.text)
uids = re.findall(r'/Users/Edit/(\d+)', s.get(B + "/Users").text); check("users list", len(uids) == 2, uids)
r = s.get(B + f"/Users/Edit/{uids[0]}"); check("user edit form", r.status_code == 200 and "تعديل مستخدم" in r.text)
check("user details partial", "تفاصيل المستخدم" in s.get(B + f"/Users/Details/{uids[0]}").text)

# --- session upload
# Hyundai system format: Parts (Quantity, Description, Location, Serial, BranchNo.) + Vins (VIN, WarehouseNo.)
wb = openpyxl.Workbook(); p = wb.active; p.title = "Parts"
p.append(["Quantity", "Description", "Location", "Serial", "BranchNo."])
for i in range(1, 24): p.append([i * 2, f"قطعة تجريبية {i}", f"A-{i:02d}-01" if i != 3 else "NULL", f"58101-D3A{i:02d}", 1 if i % 4 else 2])
p.append([5, "قطعة بصفر بالبداية", "-", "0263001080", 2])
p.append([1, "قطعة بموقع NULL", "NULL", "58101-NUL01", 2])
v = wb.create_sheet("Vins"); v.append(["VIN", "WarehouseNo."])
for i in range(1, 13): v.append([f"KMHD341CBMU{100000 + i}" + ("*" if i == 12 else ""), 2])
v.append([None, None])
buf = io.BytesIO(); wb.save(buf); open("/tmp/session.xlsx", "wb").write(buf.getvalue())
bad = openpyxl.Workbook(); bp = bad.active; bp.title = "Parts"; bp.append(["Quantity", "Description", "Location", "Serial", "BranchNo."]); bp.append([1, "x", "A", "P-1", 99]); bb = io.BytesIO(); bad.save(bb)
r = post(s, "/Sessions/Start", {"year": 2024}, files={"file": ("bad-branch.xlsx", bb.getvalue(), "application/octet-stream")}, ref="/Sessions"); check("unknown BranchNo. rejected", flash(r)[0] == "er" and "99" in (flash(r)[1] or ""), flash(r))
r = post(s, "/Sessions/Start", {"year": 2026}, files={"file": ("session-2026.xlsx", buf.getvalue(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")}, ref="/Sessions"); check("start session with xlsx", flash(r)[0] == "ok", flash(r))
r = post(s, "/Sessions/Start", {"year": 2026}, files={"file": ("again.xlsx", buf.getvalue(), "application/octet-stream")}, ref="/Sessions"); check("second in-progress session blocked", flash(r)[0] == "er", flash(r))
r = post(s, "/Sessions/Start", {"year": 2025}, files={"file": ("bad.txt", b"nope", "text/plain")}, ref="/Sessions"); check("non-excel file rejected", flash(r)[0] == "er", flash(r))
sid = int(re.findall(r'/Sessions/File/(\d+)', s.get(B + "/Sessions").text)[0])
r = s.get(B + f"/Sessions/File/{sid}"); check("download session file", r.status_code == 200 and r.content[:2] == b"PK")
r = s.get(B + "/Sessions/Template"); check("download template", r.status_code == 200 and r.content[:2] == b"PK")
tw = openpyxl.load_workbook(io.BytesIO(r.content)); check("template = Hyundai import format", tw.sheetnames == ["Parts", "Vins"] and [c.value for c in tw["Parts"][1]] == ["Quantity", "Description", "Location", "Serial", "BranchNo."] and [c.value for c in tw["Vins"][1]] == ["VIN", "WarehouseNo.", "VehicleType"])

# --- mobile API
api = requests.Session()
r = api.post(B + "/api/v1/auth/login", json={"phoneNumber": "07733333333", "password": "wrong"}); check("API login wrong → 401", r.status_code == 401 and r.json()["success"] is False)
r = api.post(B + "/api/v1/auth/login", json={"phoneNumber": "07733333333", "password": "User#2026x"}); check("API login", r.status_code == 200 and r.json()["data"]["accessToken" if "accessToken" in r.json()["data"] else "token"], r.text[:200])
d = r.json()["data"]; T = d.get("accessToken") or d.get("token"); H = {"Authorization": "Bearer " + T}
check("API 401 without token", requests.get(B + "/api/v1/me").status_code == 401 and requests.get(B + "/api/v1/me").json()["success"] is False)
r = api.get(B + "/api/v1/me", headers=H); check("API me", r.status_code == 200 and r.json()["data"], r.text[:200])
r = api.get(B + "/api/v1/sessions/current", headers=H); check("API current session", r.status_code == 200 and r.json()["data"], r.text[:200])
r = api.get(B + "/api/v1/parts?page=1&pageSize=10", headers=H); j = r.json()
check("API parts paged", r.status_code == 200 and set(["items", "page", "pageSize", "totalCount", "totalPages"]) <= set(j["data"]), r.text[:300])
parts = j["data"]["items"]; check("API parts scoped to user warehouses (no Basra parts)", j["data"]["totalCount"] == 18, j["data"]["totalCount"])
r = api.get(B + "/api/v1/parts?search=D3A05", headers=H); check("API parts search", r.json()["data"]["totalCount"] == 1, r.text[:200])
r = api.get(B + "/api/v1/parts", params={"code": "58101 d3a05"}, headers=H); check("API parts normalized code lookup", r.json()["data"]["totalCount"] == 1 and r.json()["data"]["items"][0]["partNumber"] == "58101-D3A05", r.text[:200])
r = api.get(B + "/api/v1/parts", params={"code": "58101D3A0"}, headers=H); check("API parts code lookup is exact", r.json()["data"]["totalCount"] == 0, r.text[:200])
r = api.get(B + "/api/v1/parts", params={"search": "58101D3A05"}, headers=H); check("API parts search ignores dashes", r.json()["data"]["totalCount"] == 1, r.text[:200])
pid = r.json()["data"]["items"][0]["id"]
r = api.get(B + f"/api/v1/parts/{pid}", headers=H); check("API part by id", r.status_code == 200)
r = api.get(B + "/api/v1/parts/999999", headers=H); check("API part 404", r.status_code == 404 and r.json()["success"] is False)
r = api.post(B + "/api/v1/counts", headers=H, json={"partId": pid, "quantity": 7, "location": "A-05-02", "latitude": 33.3152, "longitude": 44.3661}); check("API submit count", r.status_code in (200, 201), r.text[:300])
for x in parts[:5]:
    if x["id"] != pid: api.post(B + "/api/v1/counts", headers=H, json={"partId": x["id"], "quantity": 3, "location": "B-1", "latitude": 33.31, "longitude": 44.36})
r = api.post(B + "/api/v1/counts", headers=H, json={"partId": pid, "quantity": -1}); check("API count validation", r.status_code == 400 and r.json()["success"] is False, r.text[:200])
r = api.post(B + "/api/v1/counts", headers=H, json={"partNumber": "NEW-PART-1", "description": "قطعة جديدة غير موجودة", "warehouseId": wids["مخزن بغداد الرئيسي"], "quantity": 2, "location": "Z-1", "latitude": 33.3, "longitude": 44.3}); check("API unknown part count rejected (use missing-part request)", r.status_code == 404, r.text[:300])
r = api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": "KMHD341CBMU100001", "warehouseId": wids["مخزن بغداد المركبات"], "latitude": 33.3152, "longitude": 44.3661}); check("API VIN scan", r.status_code in (200, 201), r.text[:300])
vid = r.json()["data"]["id"]
for i in range(2, 6): api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": f"KMHD341CBMU{100000+i}", "warehouseId": wids["مخزن بغداد المركبات"], "latitude": 33.31, "longitude": 44.36})
r = api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": "KMHD341CBMU100001", "warehouseId": wids["مخزن بغداد المركبات"], "latitude": 33.3, "longitude": 44.3}); check("API duplicate VIN rejected", r.status_code in (400, 409), r.status_code)
r = api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": "SHORT", "warehouseId": wids["مخزن بغداد المركبات"], "latitude": 33.3, "longitude": 44.3}); check("API VIN format validation", r.status_code == 400)
r = api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": "KMHD341CBMU100009", "warehouseId": wids["مخزن بغداد المركبات"]}); check("API VIN scan requires GPS", r.status_code == 400, r.status_code)
r = api.post(B + "/api/v1/vins/scan", headers=H, json={"vin": "KMHD341CBMU100009", "warehouseId": wids["مخزن البصرة"], "latitude": 1, "longitude": 1}); check("API VIN foreign warehouse rejected", r.status_code in (400, 403), r.status_code)
r = api.get(B + "/api/v1/vins", headers=H); check("API my VINs", r.status_code == 200 and r.json()["data"]["totalCount"] == 5, r.text[:200])
r = api.post(B + "/api/v1/vin-corrections", headers=H, json={"scannedVinId": vid, "newVin": "KMHD341CBMU199999", "reason": "خطأ بالمسح"}); check("API VIN correction", r.status_code in (200, 201), r.text[:300])
vid2 = api.get(B + "/api/v1/vins", headers=H).json()["data"]["items"][0]["id"]
r = api.post(B + "/api/v1/vin-corrections", headers=H, json={"scannedVinId": vid2 if vid2 != vid else vid + 1, "newVin": "KMHD341CBMU188888", "reason": "تجربة رفض"}); check("API VIN correction #2", r.status_code in (200, 201), r.text[:200])
r = api.get(B + "/api/v1/vin-corrections", headers=H); check("API my VIN corrections", r.json()["data"]["totalCount"] == 2)
r = api.get(B + "/api/v1/history?type=part", headers=H); check("API history parts", r.status_code == 200 and r.json()["data"]["totalCount"] >= 5, r.text[:200])
r = api.get(B + "/api/v1/history?type=vin", headers=H); check("API history vins", r.status_code == 200 and r.json()["data"]["totalCount"] == 5)
r = api.get(B + "/api/v1/history", headers=H); check("API history all", r.status_code == 200)
r = api.get(B + "/api/v1/history?search=100001", headers=H); check("API history search", r.status_code == 200 and r.json()["data"]["totalCount"] >= 1, r.text[:200])
r = api.get(B + "/api/v1/parts?mine=true&pageSize=50", headers=H); mine = r.json()["data"]; check("API parts mine", r.status_code == 200 and mine["totalCount"] >= 1 and all(x["countedByMe"] for x in mine["items"]), r.text[:200])
mp = mine["items"][0]["id"]
r = api.get(B + "/api/v1/qty-requests", headers=H); check("API qty requests removed (404)", r.status_code == 404, r.status_code)
r = api.post(B + "/api/v1/me/password", json={"currentPassword": "wrong", "newPassword": "NewPass#2026"}, headers=H); check("API change password wrong current", r.status_code == 400)
r = api.post(B + "/api/v1/me/password", json={"currentPassword": "User#2026x", "newPassword": "NewPass#2026"}, headers=H); check("API change password", r.status_code == 200)
r = api.post(B + "/api/v1/auth/login", json={"phoneNumber": "07733333333", "password": "NewPass#2026"}); check("API login with new password", r.status_code == 200)
api.post(B + "/api/v1/me/password", json={"currentPassword": "NewPass#2026", "newPassword": "User#2026x"}, headers=H)
r = requests.get(B + "/swagger/v1/swagger.json"); check("swagger json", r.status_code == 200 and "/api/v1/auth/login" in r.text)

# --- dashboard inventory
html = s.get(B + "/Parts").text; check("parts list paged 10", html.count("/Parts/Details/") >= 10 and "عرض <b" in html)
check("parts search", "D3A05" in s.get(B + "/Parts?search=D3A05").text)
check("parts page 2", s.get(B + "/Parts?page=2").status_code == 200)
r = s.get(B + f"/Parts/Details/{pid}"); check("part details", r.status_code == 200 and "سجل التغييرات" in r.text)
check("part info partial (no price shown)", "السعر" not in s.get(B + f"/Parts/Info/{pid}").text and "<span>السعر</span>" not in s.get(B + f"/Parts/Details/{pid}").text)
check("recount form partial", "طلب إعادة عد" in s.get(B + f"/Parts/RecountForm/{pid}").text)
check("quantity form removed", s.get(B + f"/Parts/QuantityForm/{pid}", allow_redirects=False).status_code in (302, 404))
r = post(s, f"/Parts/Recount/{pid}", {"mode": "other", "userId": uids[0] if False else ""}, ref="/Parts"); check("recount other w/o user rejected", flash(r)[0] == "er", flash(r))
r = post(s, f"/Parts/Recount/{pid}", {"mode": "same"}, ref="/Parts"); check("recount same user", flash(r)[0] == "ok", flash(r))
r = api.get(B + "/api/v1/recounts", headers=H); check("API my recounts", r.json()["data"]["totalCount"] == 1, r.text[:200])
rid = r.json()["data"]["items"][0]["id"]
r = api.post(B + f"/api/v1/recounts/{rid}/submit", headers=H, json={"quantity": 9, "location": "A-05-02", "latitude": 33.3, "longitude": 44.3}); check("API submit recount", r.status_code == 200, r.text[:300])
r = api.post(B + f"/api/v1/recounts/{rid}/submit", headers=H, json={"quantity": 9, "latitude": 33.3, "longitude": 44.3}); check("API recount twice rejected", r.status_code in (400, 409), r.status_code)
r = s.get(B + f"/Parts/Details/{pid}?tab=logs"); check("change log tab", r.status_code == 200 and "سجل التغييرات" in r.text)
check("recount tasks page (no quantity tab)", "مهام إعادة العد" in s.get(B + "/Requests").text and "تصحيح الكمية" not in s.get(B + "/Requests").text)

# --- cars
html = s.get(B + "/Cars").text; check("cars list", html.count("/Cars/Edit/") == 5, html.count("/Cars/Edit/"))
check("cars search", s.get(B + "/Cars?search=100003").text.count("/Cars/Edit/") == 1)
cid = re.findall(r'/Cars/Edit/(\d+)', html)[0]
check("car edit form", s.get(B + f"/Cars/Edit/{cid}").status_code == 200)
r = post(s, f"/Cars/Edit/{cid}", {"vin": "BAD", "warehouseId": wids["مخزن بغداد المركبات"]}, ref=f"/Cars/Edit/{cid}"); check("car edit validation", "hint e" in r.text)

# --- exports
for path in ["/Exports/Parts?kind=all", "/Exports/Parts?kind=counted", "/Exports/Parts?kind=uncounted", "/Exports/Cars", "/Exports/Cars?all=true", f"/Exports/Cars?BranchId={bg}", "/Exports/Vins", "/Exports/Vins?missing=true", "/Exports/Branches", "/Exports/Warehouses", f"/Exports/Warehouses?branchId={bg}"]:
    r = s.get(B + path); ok = r.status_code == 200 and r.content[:2] == b"PK"
    if ok:
        w = openpyxl.load_workbook(io.BytesIO(r.content)); ok = w.active.max_row >= 1
    check("export " + path, ok, r.status_code)
w = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Parts?kind=counted").content)); check("counted export rows", w.active.max_row - 1 == 5, w.active.max_row)
FMT = {"/Exports/Parts?kind=counted": ("Data", ["Part Number", "Description", "Location In Stock", "System Quantity", "Total Counted", "Difference", "Counted Location", "Counted Quantity", "User", "Branch", "Date", "Links"]),
       "/Exports/Parts?kind=all": ("Data", None), "/Exports/Parts?kind=uncounted": ("Not Counted Parts", ["Part Number", "Description", "Location In Stock", "System Quantity", "Branch"]),
       "/Exports/Cars": ("Vins", ["VIN", "Warehouse", "Branch", "User", "Date", "Latitude", "Longitude", "Map Link"]),
       "/Exports/Vins?missing=true": ("Cars Not Found", ["VIN", "Warehouse", "Branch", "Date Added"]), "/Exports/Warehouses": ("Warehouses", ["WarehouseNo.", "Name", "Branch"])}
for path, (sheet, hdr) in FMT.items():
    w = openpyxl.load_workbook(io.BytesIO(s.get(B + path).content)); ws = w.active
    check(f"export format {path}", w.sheetnames == [sheet] and (hdr is None or [c.value for c in ws[1]] == hdr) and not ws.sheet_view.rightToLeft and ws.freeze_panes is None and ws["A1"].fill.fgColor.rgb == "FFD3D3D3", (w.sheetnames, [c.value for c in ws[1]]))
ws = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Parts?kind=uncounted").content)).active
rows = {r[0]: r for r in ws.iter_rows(min_row=2, values_only=True)}
check("uncounted export keeps leading zero + text part no", "0263001080" in rows and rows["0263001080"][2] == "-" and rows["0263001080"][3] == 5 and rows["0263001080"][4] == "فرع البصرة", rows.get("0263001080"))
check("uncounted export keeps NULL location", any(r[2] == "NULL" for r in rows.values()))
ws = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Parts?kind=counted").content)).active; r2 = [c.value for c in ws[2]]
check("counted export values", isinstance(r2[4], int) and r2[5] == r2[4] - r2[3] and re.match(r"\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$", r2[10] or "") and (r2[11] or "").startswith("https://www.google.com/maps?q="), r2)
ws = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Vins?missing=true").content)).active; r2 = [c.value for c in ws[2]]
check("cars not found values", r2[1] == "مخزن بغداد المركبات" and r2[2] == "فرع بغداد" and re.match(r"\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$", r2[3] or ""), r2)
ws = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Warehouses").content)).active
check("warehouses export values", [c.value for c in ws[2]] == [1, "مخزن بغداد الرئيسي", "فرع بغداد"], [c.value for c in ws[2]])
w = openpyxl.load_workbook(io.BytesIO(s.get(B + "/Exports/Vins?missing=true").content)); check("missing VIN export rows", w.active.max_row - 1 == 7, w.active.max_row)

# --- role 2 admin
a2, r = login("07711111111", "Admin#2026x"); check("role-2 admin login", r.status_code == 302)
h = a2.get(B + "/").text
sidebar = re.search(r"<aside\b.*?</aside>", h, re.S).group(0)
check("role-2 menu hides admins/sessions", "/Admins" not in sidebar and "/Sessions" not in sidebar and "/VinRequests" in sidebar)
check("role-2 blocked from /Admins", a2.get(B + "/Admins", allow_redirects=False).status_code in (302, 403))
check("role-2 blocked from /Sessions", a2.get(B + "/Sessions", allow_redirects=False).status_code in (302, 403))
check("role-2 branches scoped", "فرع البصرة" not in a2.get(B + "/Branches").text and "فرع بغداد" in a2.get(B + "/Branches").text)
check("role-2 sees no Basra parts", "مخزن البصرة" not in a2.get(B + "/Parts?page=1").text)
vr = a2.get(B + "/VinRequests").text; vids = re.findall(r'data-action="/VinRequests/Approve" data-hidden=\'\{"id":"(\d+)"\}', vr); check("VIN requests list", len(vids) == 2, vids)
r = post(a2, "/VinRequests/Approve", {"id": vids[-1]}, ref="/VinRequests"); check("VIN request approve", flash(r)[0] == "ok", flash(r))
r = post(a2, "/VinRequests/Reject", {"id": vids[0]}, ref="/VinRequests"); check("VIN request reject", flash(r)[0] == "ok", flash(r))
check("VIN correction applied", "KMHD341CBMU199999" in s.get(B + "/Cars").text or "KMHD341CBMU188888" in s.get(B + "/Cars").text)

# --- toggles
r = post(s, "/Users/SetActive", {"id": uids[1], "active": "false"}, ref="/Users"); check("deactivate user", flash(r)[0] == "ok", flash(r))
r = api.post(B + "/api/v1/auth/login", json={"phoneNumber": "07744444444", "password": "User#2026y"}); check("inactive user API login refused", r.status_code == 401)
r = post(s, "/Users/SetActive", {"id": uids[1], "active": "true"}, ref="/Users"); check("activate user", flash(r)[0] == "ok")
aid2 = [i for i in admin_ids][0]
r = post(s, "/Admins/ResetPassword", {"id": admin_ids[-1] if False else re.findall(r'data-modal="#reset-m" data-v-id="(\d+)"', s.get(B + "/Admins").text)[0], "password": "Admin#2026x"}, ref="/Admins"); check("reset admin password", flash(r)[0] == "ok", flash(r))
r = post(s, "/Admins/AssignBranch", {"id": re.findall(r'data-modal="#assign-m" data-v-id="(\d+)"', s.get(B + "/Admins").text)[0], "branchId": bg}, ref="/Admins"); check("assign branch", flash(r)[0] == "ok", flash(r))
adm2 = re.findall(r'data-modal="#assign-m" data-v-id="(\d+)"', s.get(B + "/Admins").text)[0]
r = post(s, "/Admins/SetActive", {"id": adm2, "active": "false"}, ref="/Admins"); check("deactivate admin", flash(r)[0] == "ok")
check("deactivated admin session killed", "Login" in a2.get(B + "/Parts", allow_redirects=False).headers.get("Location", ""))
post(s, "/Admins/SetActive", {"id": adm2, "active": "true"}, ref="/Admins")

# --- misc
check("404 page", requests.get(B + "/nope/nothing").status_code == 404 and "404" in requests.get(B + "/nope/nothing").text)
check("API 404 json", requests.get(B + "/api/v1/nothing").status_code == 404)
check("security headers", "X-Content-Type-Options" in requests.get(B + "/Account/Login").headers)
check("csrf enforced", s.post(B + "/Branches/Save", data={"name": "csrf"}, allow_redirects=False).status_code == 400)
check("home dashboard summary", "قطع الغيار المجرودة" in s.get(B + "/").text and "لوحة التحكم" in s.get(B + "/").text)

fails = [r for r in results if not r[1]]
print(f"\n{len(results) - len(fails)}/{len(results)} passed")
json.dump([{"name": n, "ok": o, "info": str(i)} for n, o, i in results], open(os.environ.get("SMOKE_RESULTS", str(Path(__file__).with_name("smoke-results.json"))), "w"), ensure_ascii=False, indent=1)
sys.exit(1 if fails else 0)
