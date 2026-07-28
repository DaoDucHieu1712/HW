# Phần 8 — Entity Framework Core & Database

[⬅️ Phần 7](interview.NET.07-DSA.md) | [Mục lục](interview.NET.md) | [Phần 9 — Kiến trúc & Testing ➡️](interview.NET.09-Architecture-Testing.md)

---

## 81. EF Core Change Tracking hoạt động thế nào?

```csharp
// DbContext theo dõi trạng thái: Added, Modified, Deleted, Unchanged, Detached
var user = await _db.Users.FindAsync(1); // Unchanged
user.Name = "New Name";                   // → Modified (tracker phát hiện)

var newUser = new User { Name = "Bob" };
_db.Users.Add(newUser);                    // → Added

_db.Users.Remove(user);                    // → Deleted

// SaveChanges sinh SQL dựa trên trạng thái
await _db.SaveChangesAsync(); // UPDATE, INSERT, DELETE tương ứng

// Xem trạng thái
var state = _db.Entry(user).State; // EntityState.Modified
```

---

## 82. `AsNoTracking()` dùng khi nào?

```csharp
// ❌ Query chỉ đọc nhưng vẫn tracking → tốn bộ nhớ + CPU
var users = await _db.Users.ToListAsync();

// ✅ Read-only → AsNoTracking (nhanh hơn, ít RAM)
var readOnly = await _db.Users.AsNoTracking().ToListAsync();

// ⚠️ KHÔNG dùng khi cần update sau đó
var toUpdate = await _db.Users.FirstAsync(u => u.Id == 1); // có tracking
toUpdate.Name = "X";
await _db.SaveChangesAsync(); // hoạt động vì có tracking
```

---

## 83. Eager, Lazy, Explicit loading?

```csharp
// Eager: Include - load cùng query gốc (1 query JOIN)
var orders = await _db.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items).ThenInclude(i => i.Product)
    .ToListAsync();

// Lazy: load khi truy cập navigation lần đầu (cần proxy) - dễ N+1
// (cấu hình UseLazyLoadingProxies)
var order = await _db.Orders.FirstAsync();
var customer = order.Customer; // trigger query riêng lúc này!

// Explicit: load thủ công khi cần
await _db.Entry(order).Reference(o => o.Customer).LoadAsync();
await _db.Entry(order).Collection(o => o.Items).LoadAsync();
```

---

## 84. Migration trong EF Core là gì?

```bash
# Sinh migration từ thay đổi model
dotnet ef migrations add AddUserEmailColumn

# Áp dụng vào database
dotnet ef database update

# Rollback về migration trước
dotnet ef database update PreviousMigrationName

# Sinh script SQL (cho production, không chạy trực tiếp)
dotnet ef migrations script
```

```csharp
// Migration được sinh ra
public partial class AddUserEmailColumn : Migration
{
    protected override void Up(MigrationBuilder mb) =>
        mb.AddColumn<string>("Email", "Users", nullable: true);
    protected override void Down(MigrationBuilder mb) =>
        mb.DropColumn("Email", "Users");
}
```

---

## 85. `SaveChanges` và transaction?

```csharp
// SaveChanges mặc định bọc TẤT CẢ thay đổi trong 1 transaction (all-or-nothing)
_db.Accounts.Add(a);
_db.Orders.Add(b);
await _db.SaveChangesAsync(); // cả 2 cùng commit hoặc cùng rollback

// Nhiều SaveChanges cần transaction thủ công
using var tx = await _db.Database.BeginTransactionAsync();
try
{
    await _db.SaveChangesAsync();
    await _otherService.DoWorkAsync();
    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
catch
{
    await tx.RollbackAsync();
    throw;
}
```

---

## 86. Optimistic vs Pessimistic concurrency?

```csharp
// ✅ Optimistic: dùng RowVersion, phát hiện xung đột lúc save
public class Product
{
    public int Id { get; set; }
    public int Stock { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } // concurrency token
}

try
{
    product.Stock--;
    await _db.SaveChangesAsync();
}
catch (DbUpdateConcurrencyException) // ai đó đã sửa trước!
{
    // reload và retry, hoặc báo user
    Console.WriteLine("Dữ liệu đã bị thay đổi bởi người khác");
}

// Pessimistic: khoá bản ghi (cần raw SQL trong EF)
// SELECT * FROM Products WITH (UPDLOCK) WHERE Id = 1
```

---

## 87. `Find()` và `FirstOrDefault()` trong EF?

```csharp
// Find: tìm theo PK, KIỂM TRA CACHE trước khi query DB
var user1 = await _db.Users.FindAsync(1); // nếu đã load → lấy từ cache, không query
var user2 = await _db.Users.FindAsync(1); // lấy từ cache (không query lần 2)

// FirstOrDefault: LUÔN tạo query SQL
var user3 = await _db.Users.FirstOrDefaultAsync(u => u.Id == 1); // query mỗi lần
```

- **Điểm nhấn**: `Find` nhanh hơn khi entity đã trong change tracker.

---

## 88. Cách tối ưu performance query EF Core?

```csharp
// ✅ AsNoTracking cho read-only
// ✅ Projection - chỉ lấy field cần (tránh SELECT *)
var dtos = await _db.Users
    .Where(u => u.IsActive)
    .Select(u => new UserDto { Id = u.Id, Name = u.Name }) // chỉ 2 cột
    .ToListAsync();

// ✅ Pagination
var page = await _db.Users
    .OrderBy(u => u.Id)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();

// ✅ Split query khi nhiều Include (tránh cartesian explosion)
var orders = await _db.Orders
    .Include(o => o.Items)
    .AsSplitQuery()
    .ToListAsync();

// ✅ Compiled query cho query gọi lặp lại nhiều
private static readonly Func<AppDbContext, int, Task<User?>> _getUser =
    EF.CompileAsyncQuery((AppDbContext db, int id) =>
        db.Users.FirstOrDefault(u => u.Id == id));
```

---

## 89. Raw SQL trong EF Core? An toàn injection?

```csharp
// ✅ FromSqlInterpolated - tự động tham số hoá (an toàn)
var users = await _db.Users
    .FromSqlInterpolated($"SELECT * FROM Users WHERE Age > {minAge}")
    .ToListAsync();
// {minAge} → @p0 parameter, KHÔNG nối chuỗi

// ✅ ExecuteSqlInterpolated cho non-query
await _db.Database.ExecuteSqlInterpolatedAsync(
    $"UPDATE Users SET IsActive = 0 WHERE LastLogin < {cutoff}");

// ❌ TUYỆT ĐỐI KHÔNG nối chuỗi input người dùng
// var sql = $"SELECT * FROM Users WHERE Name = '{userInput}'"; // SQL INJECTION!
// _db.Users.FromSqlRaw(sql); // nguy hiểm
```

---

## 90. Connection pooling và `DbContext` lifetime?

```csharp
// DbContext nên Scoped (mỗi request 1 instance) - KHÔNG thread-safe
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(connStr)); // mặc định Scoped

// ✅ DbContextPool - tái sử dụng instance, giảm allocation (throughput cao)
builder.Services.AddDbContextPool<AppDbContext>(opt =>
    opt.UseSqlServer(connStr));

// ❌ KHÔNG dùng chung 1 DbContext giữa nhiều thread
// await Task.WhenAll(
//     Task.Run(() => _db.Users.ToList()),  // race condition!
//     Task.Run(() => _db.Orders.ToList())
// );
```

- ADO.NET tự pool connection ở tầng dưới; `DbContext` chỉ là abstraction phía trên.

---

[⬅️ Phần 7](interview.NET.07-DSA.md) | [Mục lục](interview.NET.md) | [Phần 9 — Kiến trúc & Testing ➡️](interview.NET.09-Architecture-Testing.md)
