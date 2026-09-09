# How — Sổ tay thi PCCP (C#)

Bộ note các **hàm dựng sẵn** và **kỹ thuật** hay dùng khi thi PCCP / coding test bằng C#.
Mục tiêu: mở ra là copy được ngay, không phải nhớ cú pháp.

| File | Nội dung |
|---|---|
| [01-csharp-cheatsheet.md](01-csharp-cheatsheet.md) | Hàm dựng sẵn: `string`, `char`, `Array`, `Math`, parse/format |
| [02-collections.md](02-collections.md) | `List` / `Dictionary` / `HashSet` / `Queue` / `Stack` / `PriorityQueue` / `SortedSet` — chọn cái nào, độ phức tạp |
| [03-linq.md](03-linq.md) | Các mẫu LINQ hay dùng để rút ngắn code |
| [04-algorithms.md](04-algorithms.md) | Template thuật toán: BFS/DFS, binary search, two pointer, prefix sum, DP, greedy, Union-Find, Dijkstra |
| [05-pitfalls.md](05-pitfalls.md) | Bẫy hay dính + checklist trước khi bấm nộp |
| [06-shorthand.md](06-shorthand.md) | Cách viết tắt C#: tuple swap, `^`/`..`, `??=`, pattern matching, switch expression, local function |
| [HowDemo.cs](HowDemo.cs) | **Demo chạy được của toàn bộ 6 file trên** — mỗi dòng note là 1 dòng output thật |

## Chạy demo

```bash
dotnet run --project HW.PCCP/HW.PCCP.csproj -- how
```

In ra kết quả thật của mọi API và thuật toán trong sổ tay (kể cả các bẫy: tràn `int`,
`Math.Round(2.5) == 2`, `int[,].Length`, chia sẻ tham chiếu, cập nhật đồng thời sai).
Không truyền `how` thì `Program.cs` chạy bài tập như cũ.

---

## Quy ước bài thi PCCP

- Hàm nộp luôn tên `solution`, viết trong `public class Solution`.
- **Không sửa chữ ký hàm**, không đổi tên tham số (chúng thường là `snake_case` kiểu Korean — cứ để nguyên).
- Không đọc `Console.ReadLine()` — input đã được truyền qua tham số.
- Có thể viết thêm hàm/field phụ trong `class Solution`, nhưng **đừng dùng `static` field lưu trạng thái**
  giữa các test case (grader gọi lại nhiều lần → dữ liệu cũ còn sót → sai).

Template chuẩn:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

public class Solution
{
    public int solution(string message, int[,] spoiler_ranges)
    {
        // ...
        return answer;
    }
}
```

## Ước lượng độ phức tạp (mốc nhớ nhanh)

Grader thường cho ~1–5 giây. Với C#, ước lượng an toàn **~10^8 phép tính / giây**.

| `n` tối đa | Độ phức tạp cho phép | Kỹ thuật thường dùng |
|---|---|---|
| ≤ 10 | `O(n!)`, `O(2^n · n)` | brute force, hoán vị, bitmask |
| ≤ 20 | `O(2^n)` | bitmask DP |
| ≤ 500 | `O(n^3)` | Floyd–Warshall, DP 2 chiều nặng |
| ≤ 5.000 | `O(n^2)` | DP 2 chiều, hai vòng lặp lồng |
| ≤ 100.000 | `O(n log n)` | sort, binary search, heap, set/map |
| ≤ 1.000.000 | `O(n)` / `O(n log n)` | two pointer, prefix sum, hash |
| ≤ 10^9 | `O(log n)` / `O(1)` | binary search trên đáp án, công thức toán |

> Thấy `n ≤ 200.000` mà đang định viết 2 vòng `for` lồng nhau → dừng lại, tìm cách khác.

## Cách chạy thử trong repo này

```bash
dotnet run --project HW.PCCP/HW.PCCP.csproj
```

- Đề bài + phân tích: `HW.PCCP/Exes/lv{1,2,3}/`
- Code giải: `HW.PCCP/Solutions/`
- Gọi thử + in kết quả mong đợi: `HW.PCCP/Program.cs`
