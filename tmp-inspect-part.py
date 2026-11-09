import sqlite3

con = sqlite3.connect(r"src/HyundaiStock.Web/App_Data/hyundai-stock.db")
cur = con.cursor()
print("branch-level candidates for 00H72AQ210")
for row in cur.execute(
    """
    SELECT u.Id, u.Name
    FROM AppUsers u
    WHERE u.IsActive = 1 AND u.Id IN (
        SELECT auw.AppUserId
        FROM AppUserWarehouses auw
        JOIN Warehouses w ON w.Id = auw.WarehouseId
        JOIN AppUsers au ON au.Id = auw.AppUserId AND au.IsActive = 1
        WHERE w.BranchId = (
            SELECT BranchId FROM InventoryParts WHERE PartNumber = '00H72AQ210' AND WarehouseId IS NULL
        )
    )
    ORDER BY u.Name, u.Id
    """
):
    print(row)
