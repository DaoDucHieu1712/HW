<!--
  MAU CLAUDE.md — chep ra GOC PROJECT thanh CLAUDE.md, khong de trong .claude/.

  NGUYEN TAC SO MOT: file nay duoc nap vao context o MOI LUOT — ban tra tien cho no
  mai mai. Vi vay no phai NGAN.

  Thu gi KHONG thuoc ve day:
    - Quy trinh dai       -> skill (progressive disclosure, chi nap khi lien quan)
    - Viec phai chac chan  -> hook (chi dan o day la XAC SUAT, se bi bo sot)
    - Tai lieu nghiep vu   -> docs/, doc khi can

  Muc tieu: duoi 60 dong. Neu dai hon, gan nhu chac chan co thu dang le la skill.
-->

# <Tên project>

## Lệnh

```bash
dotnet build                 # build
dotnet test                  # test
dotnet format                # format (đã có PostToolUse hook tự chạy — không cần nhắc)
```

## Kiến trúc

```
HW.Domain          entity, domain event, không phụ thuộc gì
HW.Application     CQRS handler, pipeline behavior, interface
HW.Infrastructure  EF Core, outbox processor, cài đặt interface
HW.Api             endpoint
```

Feature mẫu chuẩn: **Blog** — thêm feature mới thì copy hình dạng của nó.

## Quy ước

- Command/Query đi qua MediatR; validation ở `ValidationBehavior`, không validate trong handler.
- Domain event **không** gửi trực tiếp — ghi vào `OutboxMessage` trong cùng transaction.
- Không thêm dependency mới nếu chưa hỏi.

## Cạm bẫy

- <cái đã làm ai đó mất nửa ngày — mục này giá trị nhất của cả file>

## Loop

Task nhiều bước → skill `loop-agentic`. Verifier:

```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full
```

"Xong" = verifier xanh, không phải "trông có vẻ ổn".
