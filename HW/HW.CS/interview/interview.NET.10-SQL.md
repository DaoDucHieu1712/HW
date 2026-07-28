# Phần 10 — SQL

[⬅️ Phần 9](interview.NET.09-Architecture-Testing.md) | [Mục lục](interview.NET.md)

> Trọng tâm: hiểu bản chất (index làm gì, khoá hoạt động ra sao), không chỉ viết được câu lệnh.

---

## 🔗 Truy vấn & JOIN

## SQL-1. Các loại JOIN khác nhau thế nào?

```sql
-- INNER JOIN: chỉ bản ghi khớp cả 2 bảng
SELECT o.Id, c.Name
FROM Orders o
INNER JOIN Customers c ON o.CustomerId = c.Id;

-- LEFT JOIN: tất cả Orders + Customer khớp (không khớp → NULL)
SELECT o.Id, c.Name
FROM Orders o
LEFT JOIN Customers c ON o.CustomerId = c.Id;

-- Tìm order KHÔNG có customer (anti-join)
SELECT o.Id
FROM Orders o
LEFT JOIN Customers c ON o.CustomerId = c.Id
WHERE c.Id IS NULL;

-- SELF JOIN: nhân viên ↔ quản lý
SELECT e.Name AS Employee, m.Name AS Manager
FROM Employees e
LEFT JOIN Employees m ON e.ManagerId = m.Id;
```

- INNER (khớp cả 2), LEFT/RIGHT (giữ 1 bên), FULL OUTER (giữ cả 2), CROSS (tích Descartes).

---

## SQL-2. `WHERE` và `HAVING` khác nhau?

```sql
-- WHERE: lọc TỪNG DÒNG trước gom nhóm (không dùng aggregate)
-- HAVING: lọc SAU GROUP BY (dùng với aggregate)
SELECT CustomerId, COUNT(*) AS OrderCount, SUM(Total) AS TotalSpent
FROM Orders
WHERE Status = 'Completed'        -- lọc dòng trước
GROUP BY CustomerId
HAVING COUNT(*) > 5;               -- lọc nhóm sau

-- ❌ Sai: dùng aggregate trong WHERE
-- WHERE COUNT(*) > 5  → lỗi
```

---

## SQL-3. Thứ tự thực thi logic của SELECT?

```sql
-- Viết:    SELECT → FROM → WHERE → GROUP BY → HAVING → ORDER BY
-- Thực thi: FROM/JOIN → WHERE → GROUP BY → HAVING → SELECT → DISTINCT → ORDER BY → TOP/LIMIT

-- ❌ Không dùng được alias của SELECT trong WHERE (vì SELECT chạy SAU WHERE)
SELECT Price * Quantity AS Total
FROM OrderItems
WHERE Price * Quantity > 100;   -- phải lặp lại biểu thức, KHÔNG dùng 'Total'

-- ✅ Alias dùng được trong ORDER BY (chạy sau SELECT)
SELECT Price * Quantity AS Total
FROM OrderItems
ORDER BY Total DESC;
```

---

## SQL-4. `UNION` và `UNION ALL` khác nhau?

```sql
-- UNION: gộp + LOẠI TRÙNG (tốn bước sort/distinct)
SELECT City FROM Customers
UNION
SELECT City FROM Suppliers;

-- ✅ UNION ALL: gộp + GIỮ TRÙNG → nhanh hơn (ưu tiên nếu không cần loại trùng)
SELECT City FROM Customers
UNION ALL
SELECT City FROM Suppliers;
```

---

## SQL-5. `IN`, `EXISTS`, `JOIN` — cái nào tối ưu hơn?

```sql
-- EXISTS: dừng ngay khi tìm thấy 1 dòng khớp (tốt cho subquery lớn)
SELECT * FROM Customers c
WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);

-- IN: phù hợp danh sách nhỏ
SELECT * FROM Products WHERE CategoryId IN (1, 2, 3);

-- ❌ BẪY: NOT IN với cột có NULL → kết quả SAI (rỗng)
SELECT * FROM Customers WHERE Id NOT IN (SELECT CustomerId FROM Orders);
-- nếu CustomerId có NULL → toàn bộ trả rỗng!

-- ✅ Dùng NOT EXISTS thay thế
SELECT * FROM Customers c
WHERE NOT EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
```

---

## SQL-6. Subquery và CTE khác nhau?

```sql
-- CTE: đặt tên tập kết quả tạm, dễ đọc, tái sử dụng, hỗ trợ ĐỆ QUY
WITH HighValueOrders AS (
    SELECT CustomerId, SUM(Total) AS Total
    FROM Orders
    GROUP BY CustomerId
    HAVING SUM(Total) > 1000
)
SELECT c.Name, h.Total
FROM HighValueOrders h
JOIN Customers c ON c.Id = h.CustomerId;

-- Recursive CTE: duyệt cây phân cấp (org chart)
WITH OrgChart AS (
    SELECT Id, Name, ManagerId, 0 AS Level
    FROM Employees WHERE ManagerId IS NULL
    UNION ALL
    SELECT e.Id, e.Name, e.ManagerId, oc.Level + 1
    FROM Employees e
    JOIN OrgChart oc ON e.ManagerId = oc.Id
)
SELECT * FROM OrgChart;
```

---

## SQL-7. Window function là gì?

```sql
-- Tính trên "cửa sổ" các dòng mà KHÔNG gom nhóm (giữ nguyên số dòng)
SELECT
    Name,
    Department,
    Salary,
    AVG(Salary) OVER (PARTITION BY Department) AS DeptAvg,
    SUM(Salary) OVER (ORDER BY Id) AS RunningTotal   -- running total
FROM Employees;
```

---

## SQL-8. `RANK`, `DENSE_RANK`, `ROW_NUMBER` khác nhau?

```sql
SELECT
    Name, Score,
    ROW_NUMBER() OVER (ORDER BY Score DESC) AS RowNum,    -- 1,2,3,4 (luôn duy nhất)
    RANK()       OVER (ORDER BY Score DESC) AS Rank,      -- 1,1,3 (bỏ số sau khi trùng)
    DENSE_RANK() OVER (ORDER BY Score DESC) AS DenseRank  -- 1,1,2 (không bỏ số)
FROM Students;

-- Ứng dụng: Top-N mỗi nhóm (top 3 lương mỗi phòng ban)
WITH Ranked AS (
    SELECT Name, Department, Salary,
           ROW_NUMBER() OVER (PARTITION BY Department ORDER BY Salary DESC) AS rn
    FROM Employees
)
SELECT * FROM Ranked WHERE rn <= 3;
```

---

## 📇 Index & Hiệu năng

## SQL-9. Index là gì? Vì sao tăng tốc query?

```sql
-- Không index: full table scan O(n)
-- Có index (B-tree): tìm O(log n)
CREATE INDEX IX_Users_Email ON Users(Email);

SELECT * FROM Users WHERE Email = 'test@example.com'; -- dùng index seek
```

- **Đánh đổi**: tốn dung lượng + làm chậm INSERT/UPDATE/DELETE (phải cập nhật index). Không index bừa bãi.

---

## SQL-10. Clustered vs Non-clustered index?

```sql
-- Clustered: quyết định THỨ TỰ VẬT LÝ của bảng, mỗi bảng chỉ 1 (thường là PK)
-- Lá của B-tree CHÍNH LÀ dữ liệu
CREATE CLUSTERED INDEX IX_Orders_Id ON Orders(Id);

-- Non-clustered: cấu trúc riêng chứa cột index + con trỏ tới dòng, mỗi bảng NHIỀU
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId);
```

---

## SQL-11. Composite index & "leftmost prefix"?

```sql
CREATE INDEX IX_Orders_Cust_Date ON Orders(CustomerId, OrderDate);

-- ✅ Dùng được index (tiền tố trái)
SELECT * FROM Orders WHERE CustomerId = 5;                        -- OK
SELECT * FROM Orders WHERE CustomerId = 5 AND OrderDate > '2024'; -- OK

-- ❌ KHÔNG dùng được index (thiếu cột đầu CustomerId)
SELECT * FROM Orders WHERE OrderDate > '2024';                   -- không dùng!
```

- **Điểm nhấn**: thứ tự cột trong composite index rất quan trọng.

---

## SQL-12. Covering index là gì?

```sql
-- Index chứa TẤT CẢ cột query cần → không cần truy ngược về bảng (key lookup)
CREATE INDEX IX_Orders_Covering
ON Orders(CustomerId)
INCLUDE (OrderDate, Total);   -- các cột SELECT

-- Query này đọc xong ngay từ index
SELECT OrderDate, Total FROM Orders WHERE CustomerId = 5;
```

---

## SQL-13. Vì sao index đôi khi KHÔNG được dùng?

```sql
-- ❌ Dùng hàm trên cột index → không "sargable"
SELECT * FROM Orders WHERE YEAR(OrderDate) = 2024;   -- không dùng index!

-- ✅ Viết lại thành range
SELECT * FROM Orders
WHERE OrderDate >= '2024-01-01' AND OrderDate < '2025-01-01';

-- ❌ LIKE với wildcard đầu
SELECT * FROM Users WHERE Name LIKE '%son';          -- không dùng index

-- ❌ Implicit conversion (cột nvarchar so với số)
SELECT * FROM Users WHERE PhoneNumber = 12345;       -- ép kiểu → scan
```

---

## SQL-14. `EXPLAIN` / execution plan dùng để làm gì?

```sql
-- SQL Server
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
-- rồi xem "Actual Execution Plan"

-- PostgreSQL / MySQL
EXPLAIN ANALYZE
SELECT * FROM Orders WHERE CustomerId = 5;
```

- **Dấu hiệu cần tối ưu**: Table/Index **Scan** (thay vì Seek), nhiều **Key Lookup**, ước tính lệch thực tế (statistics cũ).

---

## SQL-15. Quy trình tối ưu query chậm?

```sql
-- 1. Xem execution plan tìm scan/lookup/sort đắt
-- 2. Thêm index phù hợp (cột trong WHERE/JOIN/ORDER BY)
CREATE INDEX IX_Orders_Status_Date ON Orders(Status, OrderDate);

-- 3. Viết sargable, chỉ SELECT cột cần (tránh SELECT *)
SELECT Id, Total FROM Orders WHERE Status = 'Pending';

-- 4. Pagination thay vì lấy hết
SELECT Id, Total FROM Orders
ORDER BY Id OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;

-- 5. Cập nhật statistics
UPDATE STATISTICS Orders;
```

---

## 🔒 Transaction & Toàn vẹn

## SQL-16. ACID là gì?

```sql
BEGIN TRANSACTION;
    UPDATE Accounts SET Balance = Balance - 100 WHERE Id = 1;  -- Atomicity
    UPDATE Accounts SET Balance = Balance + 100 WHERE Id = 2;  -- cả 2 hoặc không
    -- Consistency: ràng buộc (Balance >= 0) luôn đúng
COMMIT;  -- Durability: đã commit thì tồn tại kể cả crash
-- Isolation: transaction đồng thời không ảnh hưởng sai lẫn nhau
```

- **A**tomicity, **C**onsistency, **I**solation, **D**urability.

---

## SQL-17. Các Isolation Level?

| Level | Dirty Read | Non-repeatable | Phantom |
|-------|-----------|----------------|---------|
| Read Uncommitted | ✗ bị | ✗ bị | ✗ bị |
| Read Committed | ✓ chặn | ✗ bị | ✗ bị |
| Repeatable Read | ✓ | ✓ chặn | ✗ bị |
| Serializable | ✓ | ✓ | ✓ chặn |

```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED; -- mặc định SQL Server
BEGIN TRANSACTION;
    SELECT * FROM Products WHERE Id = 1;
COMMIT;
```

- Càng cao càng an toàn nhưng nhiều khoá → giảm concurrency.

---

## SQL-18. Dirty / Non-repeatable / Phantom read?

```sql
-- Dirty read: đọc dữ liệu transaction khác CHƯA COMMIT (có thể rollback)
-- Non-repeatable: đọc CÙNG 1 DÒNG 2 lần ra 2 giá trị (bị UPDATE giữa chừng)
-- Phantom: chạy CÙNG 1 QUERY 2 lần ra số dòng khác (bị INSERT dòng mới khớp điều kiện)

-- Ví dụ non-repeatable:
-- T1: SELECT Balance FROM Accounts WHERE Id=1;  → 100
-- T2: UPDATE Accounts SET Balance=200 WHERE Id=1; COMMIT;
-- T1: SELECT Balance FROM Accounts WHERE Id=1;  → 200 (khác lần đầu!)
```

---

## SQL-19. Deadlock trong database? Cách giảm thiểu?

```sql
-- 2 transaction giữ khoá + chờ khoá của nhau → DB kill 1 victim
-- T1: lock A, chờ B    |  T2: lock B, chờ A  → deadlock

-- ✅ Giảm thiểu:
-- 1. Truy cập bảng theo THỨ TỰ NHẤT QUÁN (luôn A rồi B)
-- 2. Giữ transaction NGẮN
-- 3. Index hợp lý (giảm phạm vi khoá)
-- 4. Retry logic khi bị chọn làm victim (error 1205)
```

---

## SQL-20. Optimistic vs Pessimistic locking?

```sql
-- Pessimistic: khoá khi đọc, chặn người khác
BEGIN TRANSACTION;
SELECT * FROM Products WITH (UPDLOCK, ROWLOCK) WHERE Id = 1;
UPDATE Products SET Stock = Stock - 1 WHERE Id = 1;
COMMIT;

-- Optimistic: không khoá, dùng version, check lúc update
UPDATE Products
SET Stock = Stock - 1, Version = Version + 1
WHERE Id = 1 AND Version = @originalVersion;  -- 0 dòng affected → xung đột
```

---

## 🗄️ Thiết kế & Ngôn ngữ

## SQL-21. Normalization 1NF, 2NF, 3NF?

```sql
-- ❌ Chưa chuẩn: nhóm lặp, trùng lặp
-- Orders(Id, CustomerName, CustomerEmail, Product1, Product2, Product3)

-- ✅ 1NF: mỗi ô 1 giá trị nguyên tử, không nhóm lặp
-- Orders(Id, CustomerId), OrderItems(OrderId, ProductId)

-- ✅ 2NF: bỏ partial dependency (cột phụ thuộc TOÀN BỘ khoá)
-- ✅ 3NF: bỏ transitive dependency (non-key không phụ thuộc non-key)
CREATE TABLE Customers (Id INT PRIMARY KEY, Name NVARCHAR(100), Email NVARCHAR(100));
CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT REFERENCES Customers(Id));
CREATE TABLE OrderItems (OrderId INT, ProductId INT, Quantity INT);

-- Denormalization: cố ý phá chuẩn để tăng tốc đọc (chấp nhận trùng lặp)
```

---

## SQL-22. Primary key, Foreign key, Unique constraint?

```sql
CREATE TABLE Users (
    Id INT PRIMARY KEY,                    -- duy nhất, KHÔNG NULL, 1 cái/bảng
    Email NVARCHAR(100) UNIQUE,            -- duy nhất, CHO PHÉP NULL, nhiều cái/bảng
    DepartmentId INT,
    CONSTRAINT FK_Dept FOREIGN KEY (DepartmentId)
        REFERENCES Departments(Id)          -- toàn vẹn tham chiếu
        ON DELETE CASCADE                   -- xoá dept → xoá user
);
```

---

## SQL-23. `DELETE`, `TRUNCATE`, `DROP` khác nhau?

```sql
-- DELETE: xoá dòng theo điều kiện, log từng dòng, rollback được, giữ identity
DELETE FROM Orders WHERE Status = 'Cancelled';

-- TRUNCATE: xoá TOÀN BỘ nhanh, minimal log, reset identity, không WHERE
TRUNCATE TABLE Orders;

-- DROP: xoá CẢ BẢNG (cấu trúc + dữ liệu)
DROP TABLE Orders;
```

| | DELETE | TRUNCATE | DROP |
|--|--------|----------|------|
| WHERE | Có | Không | Không |
| Reset identity | Không | Có | N/A |
| Giữ cấu trúc | Có | Có | Không |

---

## SQL-24. Xử lý NULL trong SQL?

```sql
-- ❌ = NULL luôn trả UNKNOWN
SELECT * FROM Users WHERE MiddleName = NULL;   -- không trả gì!

-- ✅ Dùng IS NULL / IS NOT NULL
SELECT * FROM Users WHERE MiddleName IS NULL;

-- Phép toán với NULL → NULL
SELECT 100 + NULL;   -- NULL

-- COUNT(col) BỎ QUA NULL, COUNT(*) thì không
SELECT COUNT(MiddleName), COUNT(*) FROM Users;

-- COALESCE: giá trị non-null đầu tiên
SELECT COALESCE(MiddleName, 'N/A') FROM Users;
SELECT NULLIF(Value, 0) FROM Data;  -- trả NULL nếu Value = 0
```

---

## SQL-25. Cách phòng chống SQL Injection?

```sql
-- ❌ Nối chuỗi input người dùng → SQL Injection
-- "SELECT * FROM Users WHERE Name = '" + userInput + "'"
-- input = "' OR '1'='1" → trả toàn bộ user!
```

```csharp
// ✅ Parameterized query (ADO.NET)
using var cmd = new SqlCommand("SELECT * FROM Users WHERE Name = @name", conn);
cmd.Parameters.AddWithValue("@name", userInput); // input được tham số hoá

// ✅ EF Core FromSqlInterpolated (tự tham số hoá)
var users = _db.Users.FromSqlInterpolated(
    $"SELECT * FROM Users WHERE Name = {userInput}");

// ✅ Stored procedure với tham số
// EXEC GetUserByName @Name = @userInput
```

- **Chốt**: "Không bao giờ tin input, luôn tham số hoá — không nối chuỗi." + least privilege cho DB account.

---

[⬅️ Phần 9](interview.NET.09-Architecture-Testing.md) | [Mục lục](interview.NET.md)
