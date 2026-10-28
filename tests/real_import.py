"""Imports the real Hyundai sample (Parts ~15.7k rows + Vins) into a fresh local app and times it.
Usage: BASE=http://127.0.0.1:5090 FILE=/path/01M6157FTQ....xlsx python3 tests/real_import.py"""
import os, re, io, sys, time, openpyxl
sys.argv = sys.argv[:1]
src = open(os.path.join(os.path.dirname(__file__), "smoke.py"), encoding="utf-8").read()
exec(src[:src.index("# --- org")])  # helpers + admin login (s)
for no, n in [(1, "بغداد"), (2, "بصرة"), (3, "النجف"), (4, "أربيل")]: post(s, "/Branches/Save", {"name": n, "number": no}, ref="/Branches")
bids = dict((n, int(i)) for i, n in re.findall(r'data-v-id="(\d+)" data-v-name="([^"]+)"', s.get(B + "/Branches?pageSize=50").text))
for no, n, br in [(1, "فرع النهضة/ صالة العرض", "بغداد"), (2, "مخزن فرع النهضة", "بغداد"), (3, "فرع البياع/ صالة العرض", "بغداد"), (4, "مخزن اليوسفية", "بغداد"),
                  (5, "فرع البصرة / صالة العرض", "بصرة"), (6, "مخزن سفوان", "بصرة"), (7, "فرع النجف / صالة العرض", "النجف"), (8, "مخزن أربيل لازانو", "أربيل"), (9, "فرع أربيل / صالة العرض", "أربيل")]:
    post(s, "/Warehouses/Save", {"name": n, "type": 1, "branchId": bids[br], "number": no}, ref="/Warehouses")
data = open(os.environ["FILE"], "rb").read()
t = time.time()
r = post(s, "/Sessions/Start", {"year": 2027}, files={"file": ("real.xlsx", data, "application/octet-stream")}, ref="/Sessions")
dt = time.time() - t
print("import:", flash(r), f"{dt:.2f}s")
html = s.get(B + "/Sessions").text
print("session row:", re.findall(r'(\d[\d,]*)\s*</td>', html)[:6])
for path in ["/Exports/Parts?kind=uncounted", "/Exports/Vins?missing=true", "/Exports/Warehouses", "/Exports/Parts?kind=all"]:
    t = time.time(); c = s.get(B + path).content; e = time.time() - t
    w = openpyxl.load_workbook(io.BytesIO(c)); ws = w.active
    open("/tmp/exp_" + re.sub(r"\W", "_", path) + ".xlsx", "wb").write(c)
    print(path, w.sheetnames, ws.max_row - 1, "rows", f"{e:.2f}s", [c.value for c in ws[2]])
