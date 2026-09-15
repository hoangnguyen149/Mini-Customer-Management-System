# CustomerManager — Mini Customer Management System

ASP.NET Core 8 Web API + Blazor WebAssembly + SQL Server/EF Core 8 (Code First), theo thiết kế trong `docs/System-Analysis-Phase1.md` (Phase 1 Analysis & Design, v3).

## ⚠️ Tình trạng build — đọc trước khi mở project

Toàn bộ source code trong repo này được viết trong một môi trường sandbox **không có quyền truy cập NuGet** (chính sách egress của tổ chức chặn `nuget.org`). Vì vậy:

- **Đã build & verify thật** (biên dịch thành công, 0 lỗi): `CustomerManager.Domain`, `CustomerManager.Contracts` — hai project này không có package ngoài nào.
- **Chưa build được**: `CustomerManager.Application`, `CustomerManager.Infrastructure`, `CustomerManager.WebApi`, `CustomerManager.Blazor`, `CustomerManager.UnitTests` — các project này cần EF Core, FluentValidation, MudBlazor, JWT Bearer... Code được viết cẩn thận, đúng API mà tôi biết ở thời điểm viết, nhưng **chưa được compiler xác nhận**. Việc đầu tiên bạn nên làm sau khi tải về là:

  ```bash
 dotnet restore 
  dotnet build
  ```

  Nếu có lỗi biên dịch (sai tên package, version không tồn tại, API signature lệch phiên bản...), đó nhiều khả năng là do tôi không có compiler để tự sửa trong lúc viết — hãy paste lỗi lại, tôi sẽ sửa ngay.

## I. Yêu cầu môi trường

- .NET 8 SDK
- SQL Server (LocalDB, Developer Edition, hoặc Docker `mcr.microsoft.com/mssql/server`)
- (Tùy chọn) Redis — chỉ cần nếu bạn muốn Output Cache dùng Redis thay vì in-memory mặc định
- (Tùy chọn) [Postman](https://www.postman.com/) — để import `postman/CustomerManager.postman_collection.json` và gọi thử API mà không cần UI

### Không có SQL Server cài sẵn? Chạy bằng Docker (khuyến nghị, nhanh nhất)

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name customermanager-sql \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

Kiểm tra container đã chạy: `docker ps` — nếu container không lên (crash/restart loop), thường là do mật khẩu SA chưa đủ mạnh (tối thiểu 8 ký tự, có chữ hoa/thường/số/ký tự đặc biệt). Connection string tương ứng ở bước III.1 bên dưới sẽ dùng `Server=localhost,1433;...;User Id=sa;Password=YourStrong!Passw0rd`.

## II. Cấu trúc solution

```
CustomerManager.sln
src/
├── CustomerManager.Domain          # Entities thuần C#, không phụ thuộc gì
├── CustomerManager.Contracts       # DTO dùng chung giữa WebApi và Blazor (không kéo EF Core vào bundle WASM)
├── CustomerManager.Application     # Services, FluentValidation, IApplicationDbContext (thay Repository)
├── CustomerManager.Infrastructure  # EF Core DbContext, Interceptor (soft delete + audit), JWT, seeder
├── CustomerManager.WebApi          # Controllers, Program.cs, ProblemDetails, rate limiting, output cache
└── CustomerManager.Blazor          # Blazor WebAssembly + MudBlazor
tests/
└── CustomerManager.UnitTests       # xUnit + FluentAssertions + EF Core InMemory
docs/
└── System-Analysis-Phase1.md       # Toàn bộ tài liệu phân tích/thiết kế (Phase 1, v3)
```

Vì sao không có Repository generic hay AutoMapper — xem `CustomerManager.Application/Interfaces/IApplicationDbContext.cs` và `CustomerManager.Application/Mappings/CustomerMappingExtensions.cs` (comment giải thích trade-off ngay trong code).

## III. Thiết lập lần đầu

### 1. Connection string

Dùng **User Secrets** (không commit connection string/mật khẩu vào `appsettings.json` — xem lý do bảo mật trong `docs/PHAN-TICH-VA-KE-HOACH.docx`, mục Security Design).

```bash
cd src/CustomerManager.WebApi
dotnet user-secrets init
```

Nếu dùng **LocalDB** (Windows, kèm theo Visual Studio):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=CustomerManagerDb;Trusted_Connection=True;TrustServerCertificate=True"
```

Nếu dùng **Docker** (bước ở mục I, khuyến nghị cho macOS/Linux hoặc CI):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=CustomerManagerDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True"
```

Nếu dùng **SQL Server Developer Edition** cài đặt đầy đủ trên máy: thay `Server=` bằng tên instance của bạn (thường là `Server=localhost\\SQLEXPRESS;...` hoặc `Server=.;...`).

### 2. JWT secret (bắt buộc — API sẽ throw ngay khi start nếu thiếu, xem `Program.cs`)

```bash
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
```

### 3. Tài khoản admin seed (bắt buộc để đăng nhập được — xem `AdminUserSeeder.cs`)

```bash
dotnet user-secrets set "AdminSeed:Username" "admin"
dotnet user-secrets set "AdminSeed:Password" "<mật khẩu bạn chọn, đủ mạnh>"
```

Nếu bỏ qua bước này, API vẫn chạy nhưng sẽ log warning "no admin user was seeded" và không ai đăng nhập được — chạy `dotnet user-secrets set` rồi restart API để seed.

### 4. Tạo migration đầu tiên (chưa có sẵn migration nào trong repo — xem lý do bên dưới)

```bash
# từ thư mục gốc solution
dotnet tool install --global dotnet-ef   # nếu chưa có
dotnet ef migrations add InitialCreate \
  --project src/CustomerManager.Infrastructure \
  --startup-project src/CustomerManager.WebApi \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/CustomerManager.Infrastructure \
  --startup-project src/CustomerManager.WebApi
```

**Vì sao chưa có migration sẵn:** một EF Core migration là code sinh ra bởi công cụ `dotnet-ef` sau khi compiler đã xác nhận model hợp lệ — tôi không có quyền chạy công cụ đó trong môi trường viết code này (không có NuGet). Viết tay một migration là rủi ro cao (dễ sai với schema thật), nên bước này cố ý để bạn tự sinh bằng tooling chính chủ, đúng thực hành chuẩn (migration luôn nên do tool sinh, không nên viết tay).

### 5. CORS cho Blazor (dev)

`src/CustomerManager.WebApi/appsettings.Development.json` đã có sẵn `https://localhost:7100` / `http://localhost:5100` — chỉnh nếu Blazor chạy ở port khác trên máy bạn.

## IV. Chạy ứng dụng

### Cách chạy bằng VS Code (khuyến nghị)

Mở đúng thư mục chứa `CustomerManager.sln` (`CustomerManager/CustomerManager`). VS Code sẽ đọc cấu hình trong `.vscode/`:

1. Cài extension **C# Dev Kit** khi VS Code đề xuất, sau đó đợi solution tải xong.
2. Nhấn `Ctrl+Shift+B` và chọn task `build` để restore và build solution.
3. Nhấn `Ctrl+Shift+D`, chọn `API + Blazor (chạy song song)` rồi nhấn `F5`. API chạy tại `https://localhost:7050/swagger`, Blazor tại `http://localhost:5100`.
4. Nhấn `Ctrl+Shift+P` → **Tasks: Run Task** để chạy `ef-migrations-add-InitialCreate`, `ef-database-update` hoặc `test`.

Lần đầu cần cài `dotnet-ef` và hoàn tất User Secrets ở mục III trước khi chạy migration. Có thể đặt breakpoint trong Controller, Service và file `.razor` khi debug.

```bash
# Terminal 1 — API (Swagger tự mở ở https://localhost:7050/swagger)
dotnet run --project src/CustomerManager.WebApi

# Terminal 2 — Blazor WebAssembly
dotnet run --project src/CustomerManager.Blazor
```

Đăng nhập bằng tài khoản đã set ở `AdminSeed:Username`/`AdminSeed:Password` bước III.3.

### Lỗi thường gặp

- **`dotnet` không nhận hoặc không có SDK:** cài .NET 8 SDK, mở lại VS Code và kiểm tra `dotnet --version` trả về `8.x`.
- **Lỗi HTTPS certificate:** chạy `dotnet dev-certs https --trust`, sau đó khởi động lại API.
- **Port đã được sử dụng:** đóng tiến trình đang dùng port `7050` hoặc `5100`, hoặc cập nhật đồng thời `launchSettings.json`, `.vscode/launch.json` và `Cors:AllowedOrigins`.
- **IntelliSense không nhận project:** mở thư mục chứa `CustomerManager.sln`, cài **C# Dev Kit**, rồi chạy lệnh `C# Dev Kit: Restart Language Server` từ Command Palette.

## V. Chạy test

```bash
dotnet test
```

12 test case cho `CustomerService`/`AuthService` (EF Core InMemory, không mock Repository — xem comment trong `TestDoubles.cs` về lý do và giới hạn của cách tiếp cận này), cộng thêm bộ test cho `CreateCustomerRequestValidator`.

## VI. Các quyết định kỹ thuật đáng chú ý (đã giải thích chi tiết bằng comment trong code)

| Quyết định | Xem tại |
|---|---|
| Không Repository generic, dùng `IApplicationDbContext` mỏng | `Application/Interfaces/IApplicationDbContext.cs` |
| Soft delete + audit tự động, không set thủ công trong Service | `Infrastructure/Persistence/Interceptors/SoftDeleteAndAuditInterceptor.cs` |
| `CustomerCode` là clustered index, `Id` (GUID) không clustered | `Infrastructure/Persistence/Configurations/CustomerConfiguration.cs` |
| Seed admin qua `IHostedService`, không qua migration `HasData()` | `Infrastructure/Authentication/AdminUserSeeder.cs` |
| ProblemDetails (RFC 7807) thay vì envelope tự chế | `WebApi/ExceptionHandling/GlobalExceptionHandler.cs` |
| Output Cache optional/pluggable Redis + invalidate theo tag | `WebApi/Program.cs` (`ConfigureOutputCache`, `EvictByTagAsync` trong `CustomersController`) |
| JWT lưu in-memory ở Blazor, không localStorage | `Blazor/Services/TokenProvider.cs` |
| Không AutoMapper | `Application/Mappings/CustomerMappingExtensions.cs` |
| AuditLog riêng (Old/New JSON), tách khỏi soft-delete interceptor | `Infrastructure/Persistence/Interceptors/AuditLogInterceptor.cs` |
| Refresh token: chỉ lưu SHA-256 hash trong DB, rotation mỗi lần refresh | `Application/Services/AuthService.cs` |
| Account lockout sau N lần sai (mặc định 5 lần/15 phút, cấu hình qua `Security:*`) | `Domain/Entities/User.cs`, `Application/Services/AuthService.cs` |
| Security response headers (CSP/X-Frame-Options/nosniff), ẩn `Server` header, HSTS khi không phải Development | `WebApi/Program.cs` |

Toàn bộ phân tích đầy đủ (requirement, UI, ERD, API design, estimation, risk...) nằm ở `docs/System-Analysis-Phase1.md`.

## VII. Việc còn lại / Possible Improvements

- `CustomerManager.IntegrationTests` (đã đề cập trong thiết kế, chưa scaffold trong lần commit này) — nơi phù hợp để test hành vi phụ thuộc SQL Server thật: unique constraint trên `CustomerCode`/`Email`, ROWVERSION auto-generation.
- CI (GitHub Actions) chạy `dotnet build` + `dotnet test` trên mỗi PR.
- Export Excel/CSV, Entra ID, bảng `Users` mở rộng role — xem mục "Possible Improvements" trong tài liệu Phase 1.

### Đánh đổi bảo mật có chủ đích (chưa làm, có lý do)

- **Multi-user / RBAC (Admin/Staff/Viewer):** hệ thống vẫn giữ đúng thiết kế ban đầu — 1 tài khoản admin duy nhất, không có endpoint đăng ký (xem `AdminUserSeeder.cs`). Đây là quyết định giảm attack surface có chủ đích, không phải thiếu sót. Nếu cần nhiều người dùng/phân quyền thật, cần thiết kế lại bảng `Users` + `Roles` và áp dụng `[Authorize(Roles = ...)]` theo từng endpoint.
- **Mã hoá SĐT/CCCD trong DB (AES field-level):** chưa triển khai. Lý do: `CustomerService.GetPagedAsync` hiện tìm kiếm theo số điện thoại bằng `PhoneNumber.Contains(...)` (dịch sang `LIKE '%...%'` ở SQL Server) — mã hoá AES chuẩn sẽ làm mất khả năng này (mỗi lần mã hoá cùng giá trị ra ciphertext khác nhau, hoặc nếu dùng mã hoá tất định thì chỉ tìm được chính xác, không tìm được theo chuỗi con). Hướng cải tiến trong tương lai nếu cần: mã hoá cột thật (EF Core Value Converter hoặc SQL Server Always Encrypted) + thêm cột "blind index" (hash tất định của số đã chuẩn hoá) để tìm kiếm exact-match, chấp nhận đánh đổi không tìm được theo số điện thoại một phần.

## VIII. Tài liệu & tài nguyên bổ sung

| Tài liệu | Vị trí | Mô tả |
|---|---|---|
| Phân tích & Thiết kế đầy đủ (Markdown) | `docs/System-Analysis-Phase1.md` | Toàn bộ Phase 1: requirement, UI, ERD, API design, security, estimation, risk (v3) |
| Phân tích chức năng/UI/DB (Word) | `docs/PHAN-TICH-VA-KE-HOACH.docx` | Bản rút gọn theo đúng 3 mục yêu cầu: chức năng & UI, thiết kế DB, tóm tắt estimation — dùng để nộp/trình bày |
| Bảng ước lượng thời gian (Excel) | `docs/Estimation-WBS.xlsx` | WBS chi tiết theo giờ cho từng hạng mục công việc, có công thức tổng |
| Postman Collection | `postman/CustomerManager.postman_collection.json` | Import vào Postman để gọi thử toàn bộ API (login, CRUD customers) kèm ví dụ response |
| Kịch bản quay demo | `docs/Demo-Script.md` | Các bước cụ thể để tự quay video demo tính năng, đúng thứ tự nên trình bày |

### Import Postman Collection

1. Mở Postman → **Import** → chọn `postman/CustomerManager.postman_collection.json`.
2. Tạo Environment mới với 2 biến: `baseUrl` (ví dụ `https://localhost:7050`) và `token` (để trống — request "Login" sẽ tự động set biến này qua Postman test script sau khi đăng nhập thành công).
3. Chạy request **Auth → Login** trước tiên, sau đó mọi request Customers khác sẽ tự động đính kèm `Authorization: Bearer {{token}}`.
