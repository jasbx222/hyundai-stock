"""Destructive E2E only for a disposable loopback Development demo database.
Run with BASE, E2E_DB and DEMO_PASSWORD; never point at a live database.
Uses the exact mobile HTTP contract and cookie/CSRF dashboard approvals.
"""
import html
import json
import os
import re
import sqlite3
import uuid
from urllib.parse import urlparse

import requests

BASE = os.environ.get('BASE', 'http://127.0.0.1:5276').rstrip('/')
assert urlparse(BASE).hostname in ('127.0.0.1', 'localhost'), 'Loopback only'
assert os.environ.get('E2E_ALLOW_DISPOSABLE') == 'YES', 'Explicit disposable-data opt-in required'
DB = os.environ['E2E_DB']
PASSWORD = os.environ['DEMO_PASSWORD']
db = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
checks = []

def check(name, ok):
    checks.append({'name': name, 'ok': bool(ok)})
    print(('PASS ' if ok else 'FAIL ') + name)
    assert ok, name

def token(s, path):
    r = s.get(BASE + path, timeout=20)
    r.raise_for_status()
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text)[1]

def login(suffix):
    s = requests.Session()
    r = s.post(BASE + '/Account/Login', data={'PhoneNumber': f'077900000{suffix:02}', 'Password': PASSWORD,
        '__RequestVerificationToken': token(s, '/Account/Login')}, allow_redirects=False, timeout=20)
    check(f'dashboard role {suffix} login', r.status_code == 302)
    return s

def post(s, path, data, ref):
    r = s.post(BASE + path, data={**data, '__RequestVerificationToken': token(s, ref)}, timeout=20)
    check(path + ' HTTP', r.status_code == 200)
    if '/Account/Login' in r.url: raise AssertionError('Lost dashboard login: ' + path)
    return html.unescape(r.text)

api = requests.Session()
r = api.post(BASE + '/api/v1/auth/login', json={'phoneNumber': '07790000001', 'password': PASSWORD}, timeout=20)
check('mobile login', r.status_code == 200)
api.headers['Authorization'] = 'Bearer ' + r.json()['data']['accessToken']

def call(method, path, data=None, status=200):
    r = api.request(method, BASE + '/api/v1/' + path, json=data, timeout=20)
    check(f'{method} {path} -> {status}', r.status_code == status)
    return r.json().get('data')

def listed(path, id):
    return next(x for x in call('GET', path + '?pageSize=100')['items'] if x['id'] == id)

admin, branch, warehouse, spare, accountant, other = [login(i) for i in [0, 2, 3, 4, 5, 7]]
warehouse_html = admin.get(BASE + '/Warehouses').text
check('warehouse type removed from table and form', '<th>النوع</th>' not in html.unescape(warehouse_html) and 'name="type"' not in warehouse_html)
profile = call('GET', 'me')
wh = profile['warehouses'][0]
warehouse_name = 'E2E-WH-' + uuid.uuid4().hex[:8]
number = max(8000, db.execute('select coalesce(max(Number),0) from Warehouses').fetchone()[0]) + 1
post(admin, '/Warehouses/Save', {'name': warehouse_name, 'branchId': wh['branchId'], 'number': number}, '/Warehouses')
check('warehouse creates without type and defaults to Type1', db.execute('select Type from Warehouses where Name=?', (warehouse_name,)).fetchone() == (1,))

call('GET', 'sessions/current')
part = call('GET', 'parts?counted=false&pageSize=1')['items'][0]
pid = part['id']
gps = {'latitude': 33.3152, 'longitude': 44.3661}
call('POST', 'counts', {'partId': pid, 'quantity': 6, 'location': 'E2E-A1', **gps})
check('count persisted in database', db.execute('select CountedQuantity from InventoryParts where Id=?', (pid,)).fetchone()[0] == 6)
check('count visible in dashboard', 'E2E-A1' in html.unescape(admin.get(BASE + f'/Parts/Details/{pid}').text))
call('GET', f'parts/{pid}')
call('GET', 'parts?mine=true')
call('GET', 'parts?code=' + part['partNumber'].replace('-', ''))
call('GET', 'history?type=part')
# Owner recounts directly; quantity-correction requests no longer exist.
call('POST', 'counts', {'partId': pid, 'quantity': 10, 'location': 'E2E-A1', **gps})
check('owner recount persisted', db.execute('select CountedQuantity from InventoryParts where Id=?', (pid,)).fetchone()[0] == 10)
call('GET', 'qty-requests', status=404)
post(warehouse, f'/Parts/Recount/{pid}', {'mode': 'same'}, '/Parts')
rid = next(x['id'] for x in call('GET', 'recounts')['items'] if x['partId'] == pid)
call('POST', f'recounts/{rid}/submit', {'quantity': 12, 'location': 'E2E-R1', **gps})
check('recount persisted', db.execute('select ResultQuantity from RecountRequests where Id=?', (rid,)).fetchone()[0] == 12)
check('completed recount leaves pending-only task list', all(x['id'] != rid for x in call('GET', 'recounts')['items']))
check('completed recount status and quantity flow to app', next(x for x in call('GET', 'recounts?includeCompleted=true')['items'] if x['id'] == rid)['resultQuantity'] == 12)
call('POST', f'recounts/{rid}/submit', {'quantity': 13, **gps}, 409)
check('recount visible in dashboard', 'E2E-R1' in html.unescape(admin.get(BASE + f'/Parts/Details/{pid}').text))
code = 'E2E-' + uuid.uuid4().hex[:10].upper()
new = {'partNumber': code, 'description': 'E2E spare part', 'branchId': wh['branchId'], 'warehouseId': wh['id'], 'location': 'E2E-N1', 'quantity': 4, **gps}
n = call('POST', 'new-part-requests', new, 201); nid = n['id']
check('new part IDs in mobile contract', n['warehouseId'] == wh['id'] and n['branchId'] == wh['branchId'])
check('new spare part in dashboard', code in html.unescape(spare.get(BASE + '/NewParts?search=' + code).text))
post(spare, '/NewParts/Return', {'id': nid, 'reason': 'E2E correct description'}, '/NewParts')
check('return reason visible in app', listed('new-part-requests', nid)['returnReason'] == 'E2E correct description')
new['description'] = 'E2E corrected spare part'; new['quantity'] = 5
call('PUT', f'new-part-requests/{nid}', new)
check('resubmitted under same ID', listed('new-part-requests', nid)['statusCode'] == 'PendingSparePartsManager')
post(spare, '/NewParts/Approve', {'id': nid}, '/NewParts')
check('spare parts approval sends the request to accounts', listed('new-part-requests', nid)['statusCode'] == 'PendingAccountant')
check('nothing added before accounts approve', db.execute('select count(*) from InventoryParts where PartNumber=?', (code,)).fetchone()[0] == 0)
post(accountant, '/NewParts/FinalApprove', {'id': nid}, '/NewParts')
check('new part approved visible', listed('new-part-requests', nid)['statusCode'] == 'Approved')
check('exactly one approved part persisted', db.execute('select count(*) from InventoryParts where PartNumber=?', (code,)).fetchone()[0] == 1)
check('new part listed in app', call('GET', 'parts?code=' + code)['items'][0]['countedQuantity'] == 5)
post(accountant, '/NewParts/FinalApprove', {'id': nid}, '/NewParts')
check('duplicate approval cannot duplicate part', db.execute('select count(*) from InventoryParts where PartNumber=?', (code,)).fetchone()[0] == 1)
call('PUT', f'new-part-requests/{nid}', new, 409)
new['partNumber'] += '-C'
cancel = call('POST', 'new-part-requests', new, 201)
post(spare, '/NewParts/Return', {'id': cancel['id'], 'reason': 'cancel test'}, '/NewParts')
check('returned request cancels in app', call('POST', f"new-part-requests/{cancel['id']}/cancel")['statusCode'] == 'Cancelled')
# Distinct valid VINs not in seeded scans.
vin = 'KMH' + uuid.uuid4().hex[:14].upper().replace('I','1').replace('O','0').replace('Q','2')
vwh = profile['warehouses'][-1]['id']
call('GET', 'vins/lookup?vin=' + vin)
v = call('POST', 'vins/scan', {'vin': vin, 'warehouseId': vwh, **gps}, 201)
check('VIN in dashboard', vin in admin.get(BASE + '/Cars?search=' + vin).text)
call('GET', 'vins'); call('GET', 'history?type=vin')
newvin = vin[:-1] + ('1' if vin[-1] != '1' else '2')
vr = call('POST', 'vin-corrections', {'scannedVinId': v['id'], 'newVin': newvin, 'reason': 'E2E VIN correction'}, 201)
check('VIN correction in dashboard', newvin in branch.get(BASE + '/VinRequests').text)
post(branch, '/VinRequests/Approve', {'id': vr['id']}, '/VinRequests')
check('VIN approval visible in app', listed('vin-corrections', vr['id'])['status'] == 'موافق عليه')
check('corrected VIN in app and DB', listed('vins', v['id'])['vin'] == newvin and db.execute('select Vin from ScannedVins where Id=?', (v['id'],)).fetchone()[0] == newvin)
vr = call('POST', 'vin-corrections', {'scannedVinId': v['id'], 'newVin': vin, 'reason': 'E2E rejection'}, 201)
post(branch, '/VinRequests/Reject', {'id': vr['id']}, '/VinRequests')
check('VIN rejection visible', listed('vin-corrections', vr['id'])['status'] == 'مرفوض')
call('GET', 'history')
call('POST', 'me/password', {'currentPassword': PASSWORD, 'newPassword': PASSWORD + '-changed'})
call('POST', 'me/password', {'currentPassword': PASSWORD + '-changed', 'newPassword': PASSWORD})
for path in ['/Requests', '/NewParts', '/Parts', '/Cars', '/Finance']:
    check('dashboard page ' + path, admin.get(BASE + path).status_code == 200)
print(f'{len(checks)}/{len(checks)} passed')
if os.environ.get('E2E_RESULTS'):
    with open(os.environ['E2E_RESULTS'], 'w') as f: json.dump(checks, f, indent=2)
