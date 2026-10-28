# نظام جرد هيونداي — Hyundai Iraq Stock Count

ASP.NET Core MVC (.NET 8) — لوحة تحكم عربية (RTL) بتصميم هيونداي + REST API لتطبيق الموبايل.

## العربية

### المكونات
| المشروع | الوصف |
|---|---|
| `src/HyundaiStock.Domain` | الكيانات والـ enums والتسميات العربية |
| `src/HyundaiStock.Infrastructure` | EF Core (SQL Server 2014+ / SQLite)، الخدمات، استيراد وتصدير Excel (ClosedXML)، المايكريشن |
| `src/HyundaiStock.Web` | لوحة التحكم (Razor + Cookie auth) و API الموبايل `/api/v1` (JWT + Swagger) |

### الأدوار
- **0 مسؤول رئيسي**: كل شيء، ويقدر ينشئ سوبر أدمن.
- **1 سوبر أدمن**: كل شيء عدا إنشاء سوبر أدمن.
- **2 أدمن**: بيانات فرعه فقط، يشوف طلبات تصحيح VIN، ما يشوف المشرفين وجلسات الجرد، وما يكدر يضيف/يعدل/يحذف فروع ومخازن.
- طلبات تعديل الكمية: «قبول» (أي مشرف) ← «قبول نهائي» (0/1 فقط) يحدّث الكمية المعدودة ويكتب سجل التغيير.

### التشغيل محلياً (SQLite)
```bash
cp src/HyundaiStock.Web/appsettings.Development.example.json src/HyundaiStock.Web/appsettings.Development.json
# عدّل Jwt:Key (32 بايت على الأقل) وبيانات Seed للمسؤول الرئيسي
cd src/HyundaiStock.Web && dotnet run
```
- لوحة التحكم: `http://localhost:5169` — Swagger: `/swagger`

### الإنتاج (SQL Server)
لا توجد أسرار في `appsettings.json`. استخدم متغيرات البيئة:
```
Database__Provider=SqlServer
ConnectionStrings__Default=Server=...;Database=HyundaiStock;User Id=...;Password=...;TrustServerCertificate=True
Jwt__Key=<random 32+ bytes>
Seed__MainAdminName=...  Seed__MainAdminPhone=07XXXXXXXXX  Seed__MainAdminPassword=...
Storage__UploadsPath=D:\HyundaiStock\uploads
```
التطبيق يطبّق المايكريشن تلقائياً عند التشغيل، أو شغّل `db/initial-sqlserver.sql` يدوياً. متوافق مع SQL Server 2014 (Compatibility level 120، بدون JSON/OPENJSON).

### ملف جلسة الجرد (Excel)
- ورقة القطع: `رقم القطعة | الوصف | السعر | الكمية | الموقع | المخزن`
- ورقة المركبات: `VIN | الموديل | المخزن`
- اسم المخزن لازم يطابق اسمه بالنظام. القالب: صفحة جلسات الجرد ← «تحميل القالب».
- جلسة وحدة قيد التنفيذ، وجلسة وحدة لكل سنة.

### الاختبارات
```bash
python3 tests/smoke.py     # 131 فحص للصفحات والـ API والتصدير والصلاحيات (على قاعدة فارغة)
python3 tests/screens.py   # لقطات 1440px إلى screens/
```

## English

### Mobile API (`/api/v1`, Bearer JWT)
All responses: `{ success, message, data, errors }`; lists: `data = { items, page, pageSize, totalCount, totalPages }`.

| Method | Path | Purpose |
|---|---|---|
| POST | `auth/login` | phone + password → JWT (rate limited) |
| GET | `me` | profile, branches, warehouses |
| GET | `sessions/current` | in-progress stocktaking session |
| GET | `parts?search=&warehouseId=&page=&pageSize=` | parts in the user's warehouses |
| GET | `parts/{id}` | part details |
| POST | `counts` | submit a count (`partId` or new `partNumber`+`description`+`warehouseId`, `quantity`, `location`, `latitude`, `longitude`) |
| POST | `vins/scan` | scan a VIN (17 chars, GPS required) |
| GET | `vins` | my scanned VINs |
| POST/GET | `vin-corrections` | request / list VIN corrections |
| GET | `recounts` | my recount tasks |
| POST | `recounts/{id}/submit` | submit a recount |
| GET | `history?type=part\|vin` | my history in the current session |

Disabled users are rejected at login and on every token validation (JWT `OnTokenValidated`). Errors are JSON (`401`, `403`, `404`, `500`).

### Configuration
No secrets in `appsettings.json`. Required at runtime: `ConnectionStrings:Default`, `Jwt:Key` (≥ 32 bytes). The main admin is seeded once from `Seed:*` (never hard-coded). `appsettings.Development.json` is git-ignored; copy the `.example` file.

### Migrations
```bash
dotnet ef migrations add <Name> --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web --output-dir Data/Migrations
dotnet ef migrations script --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web -o db/initial-sqlserver.sql
```
SQLite (dev only) uses `EnsureCreated`.
