import sqlite3

db = sqlite3.connect(r"src/HyundaiStock.Web/App_Data/hyundai-stock.db")
db.row_factory = sqlite3.Row
c = db.cursor()

print("=== PARTS 00H72AQ210 ===")
parts = list(c.execute(
    """SELECT Id, PartNumber, Description, WarehouseId, BranchId, CountedByUserId,
              CountedQuantity, SessionId, SystemLocation
       FROM InventoryParts WHERE PartNumber = ?""",
    ("00H72AQ210",),
))
for r in parts:
    print(dict(r))

print("\n=== WAREHOUSES ===")
for r in c.execute("SELECT Id, Name, Number, BranchId, Type FROM Warehouses ORDER BY Id"):
    print(dict(r))

print("\n=== BRANCHES ===")
for r in c.execute("SELECT Id, Name, Number FROM Branches ORDER BY Id"):
    print(dict(r))

print("\n=== APP USERS ===")
for r in c.execute("SELECT Id, Name, PhoneNumber, IsActive, CreatedByAdminId FROM AppUsers ORDER BY Id"):
    print(dict(r))

print("\n=== APP USER WAREHOUSES ===")
for r in c.execute(
    """SELECT uw.AppUserId, u.Name, u.IsActive, uw.WarehouseId, w.Name AS WarehouseName, w.BranchId
       FROM AppUserWarehouses uw
       JOIN AppUsers u ON u.Id = uw.AppUserId
       JOIN Warehouses w ON w.Id = uw.WarehouseId
       ORDER BY uw.AppUserId, uw.WarehouseId"""
):
    print(dict(r))

print("\n=== APP USER BRANCHES ===")
for r in c.execute(
    """SELECT ub.AppUserId, u.Name, u.IsActive, ub.BranchId, b.Name AS BranchName
       FROM AppUserBranches ub
       JOIN AppUsers u ON u.Id = ub.AppUserId
       JOIN Branches b ON b.Id = ub.BranchId
       ORDER BY ub.AppUserId, ub.BranchId"""
):
    print(dict(r))

wh_ids = {p["WarehouseId"] for p in parts if p["WarehouseId"] is not None}
br_ids = {p["BranchId"] for p in parts if p["BranchId"] is not None}
print("\n=== PART WAREHOUSE IDS", wh_ids, "BRANCH IDS", br_ids)

if parts:
    print("\n=== USERS ON PART WAREHOUSE ===")
    for p in parts:
        wid = p["WarehouseId"]
        bid = p["BranchId"]
        print("--- part", p["Id"], "wh", wid, "br", bid)
        if wid is not None:
            rows = list(c.execute(
                """SELECT u.Id, u.Name, u.IsActive, uw.WarehouseId
                   FROM AppUsers u
                   JOIN AppUserWarehouses uw ON uw.AppUserId = u.Id
                   WHERE uw.WarehouseId = ?""",
                (wid,),
            ))
            print("  warehouse links:", [dict(r) for r in rows])
        if bid is not None:
            rows = list(c.execute(
                """SELECT u.Id, u.Name, u.IsActive, ub.BranchId
                   FROM AppUsers u
                   JOIN AppUserBranches ub ON ub.AppUserId = u.Id
                   WHERE ub.BranchId = ?""",
                (bid,),
            ))
            print("  branch links:", [dict(r) for r in rows])
            rows = list(c.execute(
                """SELECT u.Id, u.Name, u.IsActive, uw.WarehouseId, w.Name
                   FROM AppUsers u
                   JOIN AppUserWarehouses uw ON uw.AppUserId = u.Id
                   JOIN Warehouses w ON w.Id = uw.WarehouseId
                   WHERE w.BranchId = ?""",
                (bid,),
            ))
            print("  users via warehouses of branch:", [dict(r) for r in rows])

print("\n=== COLUMNS InventoryParts ===")
print([r[1] for r in c.execute("PRAGMA table_info(InventoryParts)")])
print("\n=== COLUMNS AppUserWarehouses ===")
print([r[1] for r in c.execute("PRAGMA table_info(AppUserWarehouses)")])
