# Kịch bản quay Demo — Mini Customer Management System

Mục tiêu: quay 1 video ngắn (khuyến nghị 6-9 phút) trình bày đầy đủ các chức năng đã làm được, theo đúng thứ tự dưới đây.

**Chuẩn bị trước khi quay:**

- Đã hoàn tất User Secrets (connection string, `Jwt:Secret`, `AdminSeed`) và chạy `dotnet ef database update` — repo đã có sẵn 3 migration (`InitialCreate`, `AddAuditLogs`, `AddRefreshTokenAndLockout`).
- Chạy sẵn API (`https://localhost:7050`) và Blazor (`http://localhost:5100`) — VS Code: cấu hình **"API + Blazor (chạy song song)"** rồi F5, hoặc `dotnet run` (README mục IV).
- Mở sẵn: trình duyệt (Blazor UI), Postman hoặc Swagger ở cửa sổ phụ, SSMS/Azure Data Studio (nếu muốn chứng minh soft delete).
- Tạo sẵn 3-5 khách hàng mẫu (có cả khách hàng đang hoạt động và ngừng hoạt động) để Dashboard có số liệu và không phải gõ tay lúc quay.
- Tắt thông báo hệ thống.

## 0. Giới thiệu nhanh (10-15s)

Nói ngắn gọn: tên hệ thống, stack công nghệ (ASP.NET Core 8 Web API + Blazor WebAssembly + MudBlazor + SQL Server/EF Core 8 Code First), kiến trúc (6 project theo Clean Architecture rút gọn, JWT + refresh token).

## 1. Đăng nhập & bảo mật phiên (60-75s)

1. Mở trình duyệt tại trang Login (giao diện 2 cột theo nhận diện CEP). Chỉ nhanh nút ẩn/hiện mật khẩu và ô "Ghi nhớ đăng nhập".
2. Thử đăng nhập sai mật khẩu — chỉ ra thông báo "Tên đăng nhập hoặc mật khẩu không đúng." (401), không lộ "sai username" hay "sai password" (quyết định bảo mật chủ động, chống dò tài khoản).
3. (Tuỳ chọn, nên quay cuối video vì sẽ khoá tài khoản 15 phút) Sai liên tiếp 5 lần — chỉ ra thông báo "Tài khoản tạm khoá do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút." (423 Locked). Lưu ý: rate limit cũng là 5 request/phút/IP, nếu vượt sẽ nhận 429 trước.
4. Đăng nhập đúng tài khoản admin đã seed — hệ thống chuyển sang Dashboard.
5. Mở DevTools > Application > Local Storage — chứng minh **không có JWT hay refresh token** nào bị lưu ở đó (chỉ có username "ghi nhớ" và theme). Token chỉ nằm trong bộ nhớ, giảm rủi ro XSS đọc token.
6. Chỉ badge "Phiên an toàn" trên AppBar và nói ngắn: access token chỉ sống 15 phút, Blazor tự gọi `POST /api/auth/refresh` khoảng 2 phút trước khi hết hạn nên người dùng không bị đăng xuất giữa chừng; refresh token được xoay vòng và DB chỉ lưu SHA-256 hash.

## 2. Dashboard (20-30s)

1. Chỉ các thẻ KPI: tổng số khách hàng, đang hoạt động, ngừng hoạt động — số liệu lấy thật từ API (không phải số giả).
2. Bật/tắt dark mode để thể hiện theme MudBlazor.
3. Bấm để chuyển sang trang Khách hàng.

## 3. Xem danh sách khách hàng (30-45s)

1. Chỉ ra bảng dữ liệu (`MudDataGrid`) với phân trang và sắp xếp server-side — đổi trang/sort, mở tab Network để chứng minh request gửi `pageNumber`/`pageSize`/`sortBy`/`sortDirection` lên server.
2. Chỉ ra các cột: Mã KH, Họ tên, Email, SĐT, Trạng thái hoạt động và các nút thao tác (Xem chi tiết, Sửa, Xoá).
3. (Tuỳ chọn) Thu hẹp cửa sổ trình duyệt/bật chế độ mobile trong DevTools — bảng tự chuyển sang dạng thẻ (card view).

## 4. Tìm kiếm & Lọc (30s)

1. Gõ vào ô tìm kiếm theo Họ và tên — chờ debounce (~400ms) rồi chỉ ra kết quả lọc.
2. Xoá ô tìm kiếm, gõ theo Số điện thoại — chỉ ra kết quả lọc theo SĐT.
3. Mở Filter Drawer (icon bộ lọc), chọn Trạng thái hoạt động → "Áp dụng" — danh sách cập nhật tương ứng; bấm "Đặt lại" để xoá bộ lọc.

## 5. Thêm mới khách hàng (45-60s)

1. Bấm nút "Thêm khách hàng" (icon dấu cộng trên thanh công cụ) — mở dialog.
2. Thử submit với dữ liệu thiếu (để trống SĐT) — chỉ ra lỗi validation hiển thị ngay dưới field.
3. Thử nhập Email sai định dạng, SĐT không đủ 10 số — chỉ ra lỗi tương ứng (SĐT phải gồm 10 chữ số và bắt đầu bằng 0).
4. Nhập đầy đủ dữ liệu hợp lệ, submit — Snackbar "Đã thêm khách hàng.", dialog đóng, danh sách tự làm mới, khách hàng mới có Mã KH do hệ thống tự sinh dạng `KH-0001` (không cho nhập tay — tránh trùng lặp).
5. (Tuỳ chọn) Thêm 1 khách hàng khác với Email trùng — chỉ ra lỗi 409 Conflict.

## 6. Cập nhật khách hàng (30-45s)

1. Bấm nút Sửa trên 1 dòng — dialog mở ở chế độ Edit, dữ liệu được tải sẵn.
2. Sửa 1 vài trường (ví dụ SĐT và trạng thái), submit — Snackbar "Đã cập nhật khách hàng.", dữ liệu trong bảng cập nhật ngay.

## 7. Xử lý xung đột cập nhật đồng thời — Optimistic Concurrency (45-60s, điểm kỹ thuật đáng nhấn mạnh)

1. Mở 2 tab trình duyệt, cùng đăng nhập, cùng mở dialog Sửa trên CÙNG 1 khách hàng.
2. Ở tab 1: sửa và lưu thành công.
3. Ở tab 2 (đang giữ dữ liệu cũ): sửa và lưu — hệ thống báo lỗi 409 "Dữ liệu khách hàng đã được người khác cập nhật. Vui lòng tải lại trang." thay vì âm thầm ghi đè dữ liệu của tab 1 (giải thích ngắn: cơ chế ROWVERSION của SQL Server).

## 8. Chi tiết khách hàng & Lịch sử thay đổi — Audit Log (45-60s)

1. Bấm nút Xem chi tiết trên khách hàng vừa sửa — mở trang `/customers/{id}`.
2. Chỉ thông tin người tạo/người sửa và thời điểm (CreatedBy/UpdatedBy).
3. Mở phần "Lịch sử thay đổi" — chỉ ra các bản ghi "Added" và "Modified", mỗi bản ghi hiển thị giá trị cũ/mới **chỉ của các field thực sự thay đổi**, kèm người thao tác (lấy từ JWT) và thời gian.
4. Nói ngắn: log được ghi tự động bởi `AuditLogInterceptor` (EF Core SaveChangesInterceptor) — Service không cần viết thêm dòng code nào, nên không thể "quên" ghi log; quan trọng với yêu cầu truy vết của hệ thống tài chính.

## 9. Xoá khách hàng — Soft Delete (45-60s)

1. Bấm nút Xoá trên 1 dòng — dialog xác nhận hiện ra.
2. Xác nhận xoá — Snackbar "Đã xóa khách hàng.", khách hàng biến mất khỏi danh sách.
3. (Tuỳ chọn) Tích chọn nhiều dòng — nút xoá hàng loạt xuất hiện — xoá cùng lúc nhiều khách hàng.
4. (Tuỳ chọn, chứng minh soft delete + audit) Mở SSMS/Azure Data Studio:
   - `SELECT * FROM Customers` — dòng dữ liệu vẫn còn, chỉ có `IsDeleted = 1`.
   - `SELECT * FROM AuditLogs ORDER BY Timestamp DESC` — có bản ghi Action = `Deleted`.

## 10. Command Palette (15-20s, tuỳ chọn)

Nhấn `Ctrl+K` — mở bảng lệnh nhanh: Dashboard, Khách hàng, Thêm khách hàng, Đổi chế độ sáng/tối, Đăng xuất. Dùng phím mũi tên + Enter để chọn "Thêm khách hàng".

## 11. Gọi thử API trực tiếp qua Postman (60-75s, khuyến khích)

1. Import `postman/CustomerManager.postman_collection.json`.
2. Chạy **Auth > Login** — chỉ ra `token` và `refreshToken` tự động lưu vào biến collection.
3. Chạy **Customers > Danh sách khách hàng** — request tự đính `Authorization: Bearer {{token}}`.
4. Chạy **Customers > Thêm mới khách hàng** với dữ liệu cố tình sai (SĐT rỗng) — response 400 kèm lỗi từng field theo chuẩn RFC 7807 ProblemDetails.
5. Chạy **Customers > Lịch sử thay đổi khách hàng** — JSON audit log của khách hàng vừa tạo.
6. Chạy **Auth > Refresh token** — nhận token mới. Sau đó sửa tay biến `refreshToken` về giá trị cũ và chạy lại — nhận 401, chứng minh token cũ đã bị thu hồi (rotation, chống replay).
7. Chạy **Auth > Logout** — 204, refresh token bị thu hồi phía server.
8. (Tuỳ chọn) Gọi `GET /api/customers` không có header Authorization — response 401.
9. (Tuỳ chọn) Mở tab Headers của một response — chỉ ra `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` và không có header `Server`.

## 12. Kết (15-20s)

Tóm tắt: CRUD đầy đủ, tìm kiếm/lọc, validation, xác thực JWT + refresh token + khoá tài khoản, xử lý xung đột cập nhật, soft delete, audit log đầy đủ, giao diện MudBlazor (Dashboard, dark mode, Command Palette), 34 unit test. Nhắc tới tài liệu đi kèm: `docs/System-Analysis-Phase1.md`, `docs/PHAN-TICH-VA-KE-HOACH.docx`, `docs/Estimation-WBS.xlsx`, `postman/CustomerManager.postman_collection.json`.

---

## Ghi chú kỹ thuật khi quay

- Chạy `dotnet build` và `dotnet test` một lần trước khi quay để chắc môi trường ổn định — không quay quá trình cài đặt/debug.
- Nếu demo khoá tài khoản (mục 1.3), quay đoạn đó **cuối cùng** hoặc chuẩn bị sẵn cách mở khoá: đợi 15 phút, hoặc tạm đặt `Security:LockoutDurationMinutes` = 1 bằng User Secrets rồi restart API.
- Muốn quay nhanh cảnh silent refresh: tạm đặt `Jwt:ExpiryMinutes` = 3 rồi restart API, mở tab Network và đợi request `refresh` tự xuất hiện. Nhớ trả lại giá trị 15 sau khi quay.
- Quay tối thiểu 1080p, có thuyết minh để người xem hiểu đang thao tác gì.
- Chỉ cần quay xen kẽ cửa sổ trình duyệt, Postman và SSMS theo đúng thứ tự trên, không cần dựng phức tạp.
