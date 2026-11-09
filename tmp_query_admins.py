import sqlite3

db = sqlite3.connect(r"src/HyundaiStock.Web/App_Data/hyundai-stock.db")
db.row_factory = sqlite3.Row
c = db.cursor()

print("=== ADMINS ===")
cols = [r[1] for r in c.execute("PRAGMA table_info(Admins)")]
print(cols)
for r in c.execute("SELECT * FROM Admins"):
    d = dict(r)
    d.pop("PasswordHash", None)
    print(d)

print("\n=== PARTS NULL WAREHOUSE COUNT ===")
print(dict(c.execute("SELECT COUNT(*) AS n, SUM(CASE WHEN WarehouseId IS NULL THEN 1 ELSE 0 END) AS nullWh FROM InventoryParts").fetchone()))

print("\n=== SAMPLE PARTS WITH WAREHOUSE ===")
for r in c.execute("SELECT Id, PartNumber, WarehouseId, BranchId FROM InventoryParts WHERE WarehouseId IS NOT NULL LIMIT 5"):
    print(dict(r))

print("\n=== SQLITE FILES ===")
import os
root = r"src/HyundaiStock.Web/App_Data"
for name in os.listdir(root):
    p = os.path.join(root, name)
    print(name, os.path.getsize(p))
