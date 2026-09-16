# CustomerManager — Mini Customer Management System

Hệ thống quản lý khách hàng cơ bản cho môi trường tài chính/ngân hàng: **ASP.NET Core 8 Web API** + **Blazor WebAssembly (MudBlazor)** + **SQL Server / EF Core 8 Code First**. Thiết kế chi tiết nằm trong `docs/System-Analysis-Phase1.md` (v4 — đã đồng bộ với code).

## Tình trạng dự án

- Đã hoàn thành đầy đủ tính năng bắt buộc và cả 3 hạng mục Bonus của đề bài (MudBlazor, JWT, Git).

## Tính năng

**Bắt buộc**

- CRUD khách hàng: danh sách, thêm mới (Mã KH tự sinh `KH-0001`), cập nhật, xoá (soft delete).
- Tìm kiếm theo Họ và tên / Số điện thoại, lọc theo trạng thái hoạt động, phân trang + sắp xếp server-side.
- Validation (FluentValidation phía server + validate trên form): Email đúng định dạng và không trùng, SĐT bắt buộc và gồm 10 số bắt đầu bằng 0, ngày sinh hợp lệ.

**Bonus & mở rộng**

- **MudBlazor:** `MudDataGrid` server-side, dialog Thêm/Sửa/Xoá, Dashboard KPI, trang chi tiết khách hàng, Filter Drawer, xoá hàng loạt, card view trên mobile, dark mode, Command Palette (`Ctrl+K`).
- **Xác thực JWT:** 1 tài khoản admin seed từ User Secrets; access token 15 phút + refresh token 7 ngày (xoay vòng, DB chỉ lưu SHA-256 hash), tự làm mới phiên ở Blazor; khoá tài khoản 15 phút sau 5 lần đăng nhập sai; rate limit 5 request/phút/IP cho login/refresh.
- **Truy vết:** `CreatedBy/UpdatedBy` trên khách hàng + bảng `AuditLogs` lưu toàn bộ lịch sử thay đổi (giá trị cũ/mới dạng JSON), xem được trên UI.
- **An toàn dữ liệu:** optimistic concurrency bằng `ROWVERSION` (409 khi 2 người cùng sửa), ProblemDetails (RFC 7807), security headers, HSTS, CORS whitelist, Output Cache có invalidate theo tag.
- **Git:** GitFlow (`main` / `develop` / `feature/*` / `test/*`) + Conventional Commits.

## I. Yêu cầu môi trường

- .NET 8 SDK
- SQL Server (LocalDB, Developer Edition, hoặc Docker `mcr.microsoft.com/mssql/server`)
- Công cụ `dotnet-ef`: `dotnet tool install --global dotnet-ef`
- (Tuỳ chọn) Redis — chỉ cần nếu muốn Output Cache dùng Redis thay vì in-memory mặc định
- (Tuỳ chọn) [Postman](https://www.postman.com/) — import `postman/CustomerManager.postman_collection.json` để gọi thử API

### Không có SQL Server cài sẵn? Chạy bằng Docker

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name customermanager-sql \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

Kiểm tra bằng `docker ps` — nếu container không lên (crash/restart loop), thường do mật khẩu SA chưa đủ mạnh (tối thiểu 8 ký tự, có chữ hoa/thường/số/ký tự đặc biệt).

## II. Cấu trúc solution

```
CustomerManager.sln
src/
├── CustomerManager.Domain          # Entities: Customer, User, AuditLog, RefreshToken (không phụ thuộc gì)
├── CustomerManager.Contracts       # DTO dùng chung WebApi ↔ Blazor (0 package, không kéo EF Core vào bundle WASM)
├── CustomerManager.Application     # CustomerService, AuthService, FluentValidation, IApplicationDbContext (thay Repository)
├── CustomerManager.Infrastructure  # AppDbContext, Configurations, Interceptors (soft delete, audit log), Migrations, JWT, AdminUserSeeder
├── CustomerManager.WebApi          # AuthController, CustomersController, GlobalExceptionHandler, Program.cs
└── CustomerManager.Blazor          # Blazor WebAssembly + MudBlazor (Pages, Components, Services, Theme)
tests/
└── CustomerManager.UnitTests       # xUnit + FluentAssertions + EF Core InMemory
docs/                               # Tài liệu phân tích, ước lượng, kịch bản demo
postman/                            # Postman Collection
.vscode/                            # Cấu hình F5 debug + tasks cho VS Code
```

Vì sao không có Repository generic hay AutoMapper — xem comment trong `CustomerManager.Application/Interfaces/IApplicationDbContext.cs` và `CustomerManager.Application/Mappings/CustomerMappingExtensions.cs`.

## III. Thiết lập lần đầu

Mọi secret dùng **User Secrets** (dev) hoặc biến môi trường (prod). `appsettings.json` cố ý để trống connection string, `Jwt:Secret` và `AdminSeed`.

```bash
cd src/CustomerManager.WebApi
dotnet user-secrets init
```

### 1. Connection string

**LocalDB** (Windows, kèm Visual Studio):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=CustomerManagerDb;Trusted_Connection=True;TrustServerCertificate=True"
```

**Docker** (khuyến nghị cho macOS/Linux):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=CustomerManagerDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True"
```

**SQL Server Developer/Express** cài trên máy: thay `Server=` bằng tên instance (thường là `Server=localhost\\SQLEXPRESS;...` hoặc `Server=.;...`).

### 2. JWT secret (bắt buộc — API dừng ngay khi start nếu thiếu hoặc ngắn hơn 32 ký tự)

```bash
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
```

### 3. Tài khoản admin (bắt buộc để đăng nhập)

```bash
dotnet user-secrets set "AdminSeed:Username" "admin"
dotnet user-secrets set "AdminSeed:Password" "<mật khẩu đủ mạnh>"
```

`AdminUserSeeder` tạo tài khoản khi API khởi động nếu bảng `Users` còn trống. Nếu bỏ qua bước này, API vẫn chạy nhưng log warning "no admin user was seeded" — set lại rồi restart API.

### 4. Tạo database từ migration có sẵn

Repo đã có sẵn 3 migration trong `src/CustomerManager.Infrastructure/Persistence/Migrations`:

| Migration | Nội dung |
|---|---|
| `InitialCreate` | Bảng `Users`, `Customers` (index, RowVersion, soft delete, CreatedBy/UpdatedBy) |
| `AddAuditLogs` | Bảng `AuditLogs` |
| `AddRefreshTokenAndLockout` | Bảng `RefreshTokens`, cột `Users.FailedLoginAttempts` / `LockedOutUntil` |

Chỉ cần áp dụng vào database (chạy từ thư mục gốc solution):

```bash
dotnet ef database update \
  --project src/CustomerManager.Infrastructure \
  --startup-project src/CustomerManager.WebApi
```

> Không chạy lại `dotnet ef migrations add InitialCreate` (hay task VS Code `ef-migrations-add-InitialCreate`) — migration này đã tồn tại. Chỉ dùng `migrations add <TênMới>` khi bạn thay đổi model.

### 5. Cấu hình tuỳ chọn

| Key | Mặc định | Ý nghĩa |
|---|---|---|
| `Jwt:ExpiryMinutes` | 15 | Thời hạn access token |
| `Jwt:RefreshTokenExpiryDays` | 7 | Thời hạn refresh token |
| `Security:MaxFailedLoginAttempts` | 5 | Số lần sai trước khi khoá tài khoản |
| `Security:LockoutDurationMinutes` | 15 | Thời gian khoá |
| `ConnectionStrings:Redis` | (trống) | Có giá trị thì Output Cache dùng Redis, trống thì dùng in-memory |
| `Cors:AllowedOrigins` | `http://localhost:5100`, `https://localhost:7100` (Development) | Origin của Blazor được phép gọi API |

## IV. Chạy ứng dụng

| Thành phần | Địa chỉ mặc định |
|---|---|
| API (profile `http`, mặc định) | `http://localhost:5050` — Swagger: `http://localhost:5050/swagger` |
| API (profile `https`) | `https://localhost:7050` |
| Blazor | `http://localhost:5100` (hoặc `https://localhost:7100`) |

Blazor gọi API qua `ApiBaseUrl` trong `src/CustomerManager.Blazor/wwwroot/appsettings.json` (mặc định `http://localhost:5050/`) — nếu đổi port hoặc chạy API bằng profile `https`, sửa lại giá trị này cho khớp.

### Bằng VS Code (khuyến nghị)

Mở thư mục gốc repo (thư mục chứa `CustomerManager.sln`). VS Code đọc cấu hình trong `.vscode/`:

1. Cài extension **C# Dev Kit** khi được đề xuất, đợi solution tải xong.
2. `Ctrl+Shift+B` → task `build`.
3. `Ctrl+Shift+D` → chọn **API + Blazor (chạy song song)** → `F5`.
4. `Ctrl+Shift+P` → **Tasks: Run Task** → `ef-database-update` hoặc `test`.

### Bằng dòng lệnh

```bash
# Terminal 1 — API
dotnet run --project src/CustomerManager.WebApi
# hoặc chạy HTTPS: dotnet run --project src/CustomerManager.WebApi --launch-profile https

# Terminal 2 — Blazor WebAssembly
dotnet run --project src/CustomerManager.Blazor
```

Mở `http://localhost:5100`, đăng nhập bằng tài khoản ở bước III.3.

### Giao diện

| Route | Màn hình |
|---|---|
| `/login` | Đăng nhập (ẩn/hiện mật khẩu, ghi nhớ username, thông báo khoá tài khoản) |
| `/` | Dashboard — tổng số / đang hoạt động / ngừng hoạt động |
| `/customers` | Danh sách khách hàng — tìm kiếm, lọc, phân trang, Thêm/Sửa/Xoá, xoá hàng loạt |
| `/customers/{id}` | Chi tiết khách hàng + lịch sử thay đổi |
| `Ctrl+K` | Command Palette — điều hướng nhanh, thêm khách hàng, đổi sáng/tối, đăng xuất |

### Lỗi thường gặp

- **`dotnet` không nhận / không có SDK:** cài .NET 8 SDK, mở lại VS Code, kiểm tra `dotnet --version` trả về `8.x`.
- **API dừng ngay khi start với lỗi `Jwt:Secret is missing`:** chưa làm bước III.2 (User Secrets gắn với project `CustomerManager.WebApi`).
- **Đăng nhập báo sai dù đúng mật khẩu:** kiểm tra bước III.3 đã set trước khi API chạy lần đầu; seeder chỉ tạo admin khi bảng `Users` trống.
- **Blazor báo lỗi gọi API / CORS:** kiểm tra API đang chạy đúng địa chỉ trong `ApiBaseUrl`, và origin của Blazor có trong `Cors:AllowedOrigins`.
- **Lỗi HTTPS certificate:** `dotnet dev-certs https --trust`, rồi khởi động lại API.
- **Port đã được sử dụng:** đóng tiến trình đang dùng port `5050`/`7050`/`5100`/`7100`, hoặc cập nhật đồng thời `launchSettings.json`, `.vscode/launch.json`, `ApiBaseUrl` và `Cors:AllowedOrigins`.
- **Nhận 423 khi đăng nhập:** tài khoản đang bị khoá do sai quá 5 lần — đợi hết thời gian khoá (hoặc giảm `Security:LockoutDurationMinutes` khi dev).
- **IntelliSense không nhận project:** mở đúng thư mục chứa `CustomerManager.sln`, chạy `C# Dev Kit: Restart Language Server`.

## V. API

| Method | Endpoint | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | Đăng nhập — 200 / 401 / 423 (bị khoá) / 429 (rate limit) |
| POST | `/api/auth/refresh` | Anonymous | Đổi refresh token lấy access token mới (token cũ bị thu hồi) |
| POST | `/api/auth/logout` | Anonymous | Thu hồi refresh token phía server — 204 |
| GET | `/api/customers` | Bearer | Danh sách: `fullName`, `phoneNumber`, `isActive`, `pageNumber`, `pageSize` (≤100), `sortBy` (`fullName`/`customerCode`/`createdAt`), `sortDirection` |
| GET | `/api/customers/{id}` | Bearer | Chi tiết (kèm `rowVersion`) — 200 / 404 |
| GET | `/api/customers/{id}/audit-logs` | Bearer | Lịch sử thay đổi, mới nhất trước |
| POST | `/api/customers` | Bearer | Thêm mới — 201 / 400 / 409 (trùng Email) |
| PUT | `/api/customers/{id}` | Bearer | Cập nhật, gửi kèm `rowVersion` — 200 / 400 / 404 / 409 (xung đột) |
| DELETE | `/api/customers/{id}` | Bearer | Xoá mềm — 204 / 404 |

Lỗi trả về dạng `application/problem+json` (RFC 7807). Swagger UI (chỉ bật ở Development) có sẵn nút **Authorize** để nhập Bearer token.

## VI. Chạy test

```bash
dotnet test
```

**34 test** (xUnit + FluentAssertions + EF Core InMemory, không mock Repository — xem comment trong `TestDoubles.cs` về lý do và giới hạn):

| File | Nội dung |
|---|---|
| `CustomerServiceTests` | Tạo + sinh Mã KH tuần tự, trùng Email, lấy theo Id (kể cả đã xoá mềm), xoá bản ghi không tồn tại, cập nhật + xung đột RowVersion, lọc theo Họ tên |
| `AuthServiceTests` | Đăng nhập đúng/sai, khoá tài khoản, refresh token rotation, replay bị từ chối, logout |
| `CreateCustomerRequestValidatorTests` | Email, SĐT, ngày sinh |
| `AuditLogInterceptorTests` | Ghi Added / Modified (chỉ field thay đổi) / Deleted, thứ tự mới nhất trước |

## VII. Các quyết định kỹ thuật đáng chú ý (giải thích chi tiết bằng comment trong code)

| Quyết định | Xem tại |
|---|---|
| Không Repository generic, dùng `IApplicationDbContext` mỏng | `Application/Interfaces/IApplicationDbContext.cs` |
| Soft delete + CreatedBy/UpdatedBy tự động, không set thủ công trong Service | `Infrastructure/Persistence/Interceptors/SoftDeleteAndAuditInterceptor.cs` |
| AuditLog riêng (Old/New JSON), tách khỏi soft-delete interceptor | `Infrastructure/Persistence/Interceptors/AuditLogInterceptor.cs` |
| `CustomerCode` là clustered index, `Id` (GUID) không clustered | `Infrastructure/Persistence/Configurations/CustomerConfiguration.cs` |
| Seed admin qua `IHostedService`, không qua migration `HasData()` | `Infrastructure/Authentication/AdminUserSeeder.cs` |
| ProblemDetails (RFC 7807) thay vì envelope tự chế | `WebApi/ExceptionHandling/GlobalExceptionHandler.cs` |
| Output Cache optional/pluggable Redis + invalidate theo tag | `WebApi/Program.cs`, `EvictByTagAsync` trong `CustomersController` |
| Access + refresh token chỉ lưu in-memory ở Blazor, không localStorage | `Blazor/Services/TokenProvider.cs` |
| Tự làm mới phiên trước khi access token hết hạn | `Blazor/Services/SilentRefreshScheduler.cs` |
| Refresh token: chỉ lưu SHA-256 hash, rotation mỗi lần refresh | `Application/Services/AuthService.cs` |
| Account lockout sau N lần sai (cấu hình qua `Security:*`) | `Domain/Entities/User.cs`, `Application/Services/AuthService.cs` |
| Security headers (CSP / X-Frame-Options / nosniff), ẩn `Server` header, HSTS ngoài Development | `WebApi/Program.cs` |
| Không AutoMapper | `Application/Mappings/CustomerMappingExtensions.cs` |

## VIII. Git workflow

- Nhánh: `main` (ổn định) ← `develop` (tích hợp) ← `feature/*`, `test/*`. Các nhánh đã dùng: `feature/core-domain`, `feature/customer-crud-api`, `feature/mudblazor-datagrid`, `test/customer-service`, `feature/audit-log`, `feature/security-hardening-phase1`.
- Commit theo Conventional Commits có scope, ví dụ `feat(api): ...`, `feat(ui): ...`, `test(application): ...`, `fix: ...`, `docs: ...`, `chore(vscode): ...`.

## IX. Việc còn lại / Possible Improvements

- `CustomerManager.IntegrationTests` với SQL Server thật (Testcontainers) — kiểm tra unique constraint `CustomerCode`/`Email` và ROWVERSION ở mức database (InMemory provider không tái hiện chính xác 2 hành vi này).
- CI (GitHub Actions) chạy `dotnet build` + `dotnet test` cho mỗi PR.
- API xoá hàng loạt phía backend (UI hiện gọi lần lượt từng `DELETE`).
- Export Excel/CSV, Full-Text Search khi dữ liệu lớn, Microsoft Entra ID.

### Đánh đổi bảo mật có chủ đích

- **Multi-user / RBAC (Admin/Staff/Viewer):** giữ đúng yêu cầu đề bài — 1 tài khoản admin duy nhất, không có endpoint đăng ký (xem `AdminUserSeeder.cs`). Đây là quyết định giảm attack surface, không phải thiếu sót. Nếu cần nhiều người dùng/phân quyền, cần thiết kế lại `Users` + `Roles` và áp dụng `[Authorize(Roles = ...)]` theo endpoint.
- **Mã hoá SĐT/CCCD trong DB (AES field-level):** chưa triển khai, vì `CustomerService.GetPagedAsync` tìm SĐT bằng `PhoneNumber.Contains(...)` (`LIKE '%...%'`) — mã hoá sẽ làm mất khả năng tìm theo chuỗi con. Hướng cải tiến: mã hoá cột (EF Core Value Converter hoặc SQL Server Always Encrypted) + cột "blind index" (hash tất định của số đã chuẩn hoá) để tìm exact-match, chấp nhận không tìm được theo một phần số.

## X. Tài liệu & tài nguyên bổ sung

| Tài liệu | Vị trí | Mô tả |
|---|---|---|
| Phân tích & Thiết kế (Markdown) | `docs/System-Analysis-Phase1.md` | Requirement, UI, ERD, API, security, estimation, risk; mục 0.2 (v4) ghi các thay đổi khi triển khai |
| Phân tích chức năng/UI/DB (Word) | `docs/PHAN-TICH-VA-KE-HOACH.docx` | Bản 2.0: 16 chức năng, 7 màn hình, 4 bảng dữ liệu, tóm tắt ước lượng — dùng để nộp/trình bày |
| Bảng ước lượng thời gian (Excel) | `docs/Estimation-WBS.xlsx` | WBS chi tiết 10 hạng mục (67.5h), có công thức tổng |
| Postman Collection | `postman/CustomerManager.postman_collection.json` | Login / Refresh / Logout, CRUD, lịch sử thay đổi, kèm response mẫu |
| Kịch bản quay demo | `docs/Demo-Script.md` | Các bước quay video demo theo thứ tự nên trình bày |

### Import Postman Collection

1. Postman → **Import** → chọn `postman/CustomerManager.postman_collection.json`.
2. Kiểm tra biến collection `baseUrl` khớp với API đang chạy — `http://localhost:5050` (profile mặc định) hoặc `https://localhost:7050` (profile `https`).
3. Chạy **Auth → Login** trước — script tự lưu `token` và `refreshToken`; mọi request Customers tự đính kèm `Authorization: Bearer {{token}}`.
4. Khi access token hết hạn (15 phút), chạy **Auth → Refresh token** để lấy token mới.
