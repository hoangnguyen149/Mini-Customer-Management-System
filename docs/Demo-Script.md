# Kịch bản quay Demo — Mini Customer Management System

Mục tiêu: quay 1 video ngắn (khuyến nghị 4-7 phút) trình bày đầy đủ các chức năng đã làm được, theo đúng thứ tự dưới đây. Chuẩn bị trước khi quay: chạy sẵn `dotnet run` cho cả API và Blazor (xem README.md mục IV), mở sẵn 2 cửa sổ: trình duyệt (Blazor UI) và Swagger UI hoặc Postman ở cửa sổ phụ để show response khi cần. Tắt thông báo hệ thống, chuẩn bị sẵn 1-2 khách hàng mẫu để không phải gõ tay lúc quay.

## 0. Giới thiệu nhanh (10-15s)

Nói ngắn gọn: tên hệ thống, stack công nghệ (ASP.NET Core 8 Web API + Blazor WebAssembly + MudBlazor + SQL Server/EF Core 8), kiến trúc (Clean/Layered Architecture, JWT authentication).

## 1. Đăng nhập (30-45s)

1. Mở trình duyệt tại trang Login.
2. Thử đăng nhập sai mật khẩu trước — chỉ ra thông báo lỗi 401 rõ ràng, không lộ chi tiết "sai username" hay "sai password" (nói rõ đây là quyết định bảo mật chủ động, chống dò tài khoản).
3. Đăng nhập đúng với tài khoản admin đã seed — chỉ ra điều hướng tự động sang trang Danh sách khách hàng, JWT được lưu in-memory (mở DevTools > Application > Local Storage để chứng minh KHÔNG có token nào bị lưu ở đó — điểm cộng bảo mật đáng nhấn mạnh).

## 2. Xem danh sách khách hàng (30-45s)

1. Chỉ ra bảng dữ liệu (MudDataGrid) với phân trang server-side — đổi trang, chỉ ra URL/network request gửi `pageNumber`/`pageSize` lên server (mở tab Network trình duyệt nếu muốn chứng minh phân trang thật sự chạy ở server, không phải client tự cắt mảng).
2. Chỉ ra cột dữ liệu: Mã KH, Họ tên, Email, SĐT, Ngày sinh, Trạng thái hoạt động.

## 3. Tìm kiếm & Lọc (30s)

1. Gõ vào ô tìm kiếm theo Họ và tên — chờ debounce (~400ms) rồi chỉ ra kết quả lọc.
2. Xóa ô tìm kiếm, gõ theo Số điện thoại — chỉ ra kết quả lọc theo SĐT.
3. Dùng bộ lọc Trạng thái hoạt động (Active/Inactive) — chỉ ra danh sách cập nhật tương ứng.

## 4. Thêm mới khách hàng (45-60s)

1. Bấm nút "Thêm mới" — mở dialog.
2. Thử submit với dữ liệu thiếu (để trống SĐT) — chỉ ra lỗi validation client-side hiển thị ngay dưới field, chưa gọi API.
3. Thử nhập Email sai định dạng — chỉ ra lỗi validation tương ứng.
4. Nhập đầy đủ dữ liệu hợp lệ, submit — chỉ ra Snackbar báo thành công, dialog đóng, danh sách tự refresh, khách hàng mới xuất hiện với Mã KH do hệ thống tự sinh (nhấn mạnh: KHÔNG cho nhập tay Mã KH — tránh trùng lặp).
5. (Tùy chọn, nếu muốn chứng minh chặn trùng) Thử thêm 1 khách hàng khác với Email trùng khách hàng vừa tạo — chỉ ra lỗi 409 Conflict.

## 5. Cập nhật khách hàng (30-45s)

1. Bấm vào 1 dòng khách hàng (hoặc nút Sửa) — dialog mở ở chế độ Edit, dữ liệu preload sẵn.
2. Sửa 1 vài trường, submit — chỉ ra Snackbar thành công, dữ liệu trong bảng cập nhật ngay.

## 6. Xử lý xung đột cập nhật đồng thời — Optimistic Concurrency (45-60s, điểm cộng kỹ thuật đáng nhấn mạnh nhất)

Đây là phần thể hiện rõ nhất chiều sâu kỹ thuật, nên dành thời lượng xứng đáng:

1. Mở 2 tab trình duyệt, cùng đăng nhập, cùng mở dialog Edit trên CÙNG 1 khách hàng.
2. Ở tab 1: sửa và lưu thành công.
3. Ở tab 2 (đang giữ dữ liệu cũ): sửa và lưu — chỉ ra hệ thống trả về lỗi 409 "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang." thay vì âm thầm ghi đè mất dữ liệu của tab 1 (giải thích ngắn gọn: cơ chế RowVersion/ROWVERSION của SQL Server).

## 7. Xóa khách hàng — Soft Delete (30-45s)

1. Bấm nút Xóa trên 1 dòng — dialog xác nhận hiện ra (nhấn mạnh: không xóa nhầm do có bước xác nhận).
2. Xác nhận xóa — chỉ ra khách hàng biến mất khỏi danh sách.
3. (Tùy chọn, nếu muốn chứng minh soft delete) Mở SQL Server Management Studio/Azure Data Studio, query trực tiếp bảng `Customers` — chỉ ra dòng dữ liệu vẫn còn, chỉ có `IsDeleted = 1`, không bị xóa vật lý (quan trọng với hệ thống tài chính — không mất dấu vết dữ liệu).

## 8. Gọi thử API trực tiếp qua Postman (45-60s, tùy chọn nhưng khuyến khích)

1. Mở Postman, import `postman/CustomerManager.postman_collection.json` (xem README.md mục VIII).
2. Chạy request **Auth > Login** — chỉ ra token tự động lưu vào biến `{{token}}`.
3. Chạy request **Customers > Danh sách khách hàng** — chỉ ra request tự động đính `Authorization: Bearer {{token}}` mà không cần set tay.
4. Chạy thử request **Customers > Thêm mới khách hàng** với dữ liệu cố tình sai (ví dụ SĐT rỗng) — chỉ ra response 400 kèm chi tiết lỗi từng field theo chuẩn RFC 7807 ProblemDetails.
5. (Nếu còn thời gian) Gọi trực tiếp `GET /api/customers` mà KHÔNG có header Authorization (xóa tạm token) — chỉ ra response 401, chứng minh toàn bộ API Customers đều yêu cầu xác thực JWT.

## 9. Kết (10-15s)

Tóm tắt nhanh những gì đã demo: CRUD đầy đủ, tìm kiếm/lọc, validation 2 lớp, xác thực JWT, xử lý xung đột cập nhật, soft delete. Nhắc tới tài liệu đi kèm: `docs/System-Analysis-Phase1.md` (phân tích kỹ thuật đầy đủ), `docs/PHAN-TICH-VA-KE-HOACH.docx` + `docs/Estimation-WBS.xlsx` (tài liệu phân tích & ước lượng), `postman/CustomerManager.postman_collection.json` (Postman collection).

---

## Ghi chú kỹ thuật khi quay

- Nếu app chưa build được ngay trên máy bạn (xem README.md mục "Tình trạng build"), hãy chạy `dotnet restore && dotnet build` trước và sửa các lỗi biên dịch nếu phát sinh (nhiều khả năng chỉ là version package lệch nhẹ) — nên làm bước này *trước* khi quay, không quay luôn quá trình debug build.
- Nên quay ở độ phân giải tối thiểu 1080p, ưu tiên công cụ có thu cả tiếng nói (thuyết minh) để giám khảo/người xem hiểu rõ bạn đang thao tác gì mà không cần dừng video đọc chữ.
- Nếu dùng OBS Studio / công cụ quay màn hình khác: chỉ cần quay 1 cửa sổ trình duyệt + 1 cửa sổ Postman/SSMS xen kẽ theo đúng thứ tự trên, không cần dựng, cắt ghép phức tạp.
