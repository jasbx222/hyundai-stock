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
- طلبات تعديل الكمية انشالت من اللوحة والتطبيق. جداولها وسجلها القديم باقية بقاعدة البيانات بدون واجهة.

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
- ورقة `Parts`: `Quantity | Description | Location | Serial | BranchNo.`
- ورقة `Vins`: `VIN | WarehouseNo. | VehicleType | Dealer | ManufactureYear`. الـ VIN هو رقم الشاصي. الوكيل وسنة الصنع اختياريان، وتُقبل العناوين العربية «رقم الشاصي» و«الوكيل» و«سنة الصنع».
- استخدم أرقام الفرع والمخزن من اللوحة، مو ids. القالب من صفحة جلسات الجرد ← «تحميل القالب».
- جلسة جرد وحدة قيد التنفيذ بنفس الوقت. النوع إما جرد سنوي (واحد لكل سنة) أو جرد شهري (واحد لكل شهر). الشهري يشتغل مثل السنوي بالضبط: قطع الغيار والسيارات، والفروقات تروح للحسابات، لكن له جلسته واسمه («جرد شباط 2026») وأرقامه منفصلة عن السنوي. يبدأ الجلسة المسؤول الرئيسي أو السوبر أدمن أو مدير الحسابات. الإنهاء للمسؤول الرئيسي والسوبر أدمن فقط.

### قواعد الجرد
- القطعة تنجرد مرة وحدة. إذا جردها موظف، غيره يشوف عند المسح «هذه القطعة معدودة من قبل الموظف <الاسم>» وما يكدر يجردها، وما تظهر له الكمية المعدودة ولا موقعها. نفس الموظف اللي جردها يكدر يعيد جردها مباشرة، والتغيير ينكتب بسجل القطعة. مهمة إعادة العد اللي ترسلها الإدارة تبقى الطريق الثاني لإعادة العد.
- كل سطر يخص فرعه ومخزنه. نفس رقم القطعة بأكثر من مخزن (مثلاً بغداد وأربيل) سطور منفصلة، وكل سطر ينجرد بمخزنه.
- أي عدد، أعلى أو أقل من كمية النظام، ينحفظ مباشرة بدون طلب موافقة. الفرق يظهر بصفحة «فروقات المخزون المالية»: الأعلى «زيادة» والأقل «نقص»، والتصدير فيه عمود `Variance` بنفس التسمية.
- نوع الجرد لكل مستخدم تطبيق: قطع الغيار فقط، السيارات فقط، أو الاثنين (الافتراضي). المشرف يحدده عند الإضافة أو التعديل. التطبيق يعرض الأيقونة المسموحة فقط، والـ API يرفض (403) جرد القطع أو طلب إضافة قطعة أو مسح VIN غير المسموح.
- «مهام إعادة العد» (`/Requests`): قطع أرسلتها الإدارة لموظف حتى يعيد عدّها، مع الحالة والنتيجة. «طلبات إعادة الجرد» (`/RecountAsks`): طلبات الموظفين لإعادة جرد قطعة، والموافقة تنشئ مهمة إعادة عد.
- طلب إضافة قطعة غير موجودة يمر بمرحلتين: مدير قطع الغيار يوافق أو يرجّع الطلب للموظف مع السبب. بعد موافقته يتحول الطلب «بانتظار الحسابات»، والمحاسب أو مدير الحسابات يوافق أو يرجّعه. بعد موافقة الحسابات فقط تنضاف القطعة للجرد (كمية نظام 0 وسعر 0) وتظهر بصفحة «القطع الجديدة» باسم الموافقَين الاثنين. المدير الرئيسي والسوبر أدمن يقدرون يسوون المرحلتين. الفرع والمخزن من مخزن الموظف اللي أرسل الطلب. صفحة «طلبات إضافة قطعة» فيها تصدير Excel حسب الفلتر.
- رقم قطعة مكرر بنفس المخزن: المستخدم اللي عنده صلاحية «اختيار السطر عند تكرار رقم القطعة» يشوف كل الأسطر (سطر 1 من 2...) ويختار. بدونها يُجرد السطر الافتراضي: أول سطر غير معدود. إعادة العد تبقى على السطر المحدد بالطلب.
- كل صفحات القوائم فيها فلتر سنة الجرد. أزرار «تصدير حسب الفلتر» تصدّر نفس السنة والبحث والعرض الظاهر بالصفحة.

### بيانات وتجربة شاملة
- [سيناريو اللوحة والتطبيق باللهجة العراقية](docs/TEST-SCENARIO.ar.md): أوامر وندوز، الحسابات، النتائج المتوقعة والحالات السلبية.
- [باركودات وستيكرات قابلة للطباعة](docs/test-barcodes.html): تشتغل بدون إنترنت.
- [تفاصيل HTTPS والاعتمادية وحدود التحقق](docs/LIVE-READINESS.md).
- بيانات التجربة اختيارية: `Seed__Demo=true` حصراً في `Development`، وكلمة سر الحسابات الجديدة من `Seed__DemoPassword` (افتراضياً `Test1234`). لا تغيّر حسابات أو بيانات موجودة.

### الاختبارات
```bash
dotnet build HyundaiStock.sln -c Release
dotnet test HyundaiStock.sln -c Release
# Smoke HTTP: على سيرفر محلي منفصل وقاعدة فارغة مخصصة للاختبار فقط.
# يحتاج requests وopenpyxl؛ لا تشغله على بيانات الشغل أو مع Demo seed.
BASE=http://127.0.0.1:5080 HYUNDAI_SMOKE_ADMIN_PHONE=<test-admin-phone> HYUNDAI_SMOKE_ADMIN_PASSWORD=<test-admin-password> python3 tests/smoke.py
```
الـ smoke ينشئ ويعدّل ويحذف بيانات اختبار، ويشغّل 150 فحص للصفحات والـ API والتصدير والصلاحيات. `SMOKE_RESULTS` يحدد مسار تقرير JSON اختياري. اختبارات .NET تستخدم قواعد مؤقتة مستقلة؛ فحص الكاميرا وOCR وأذونات الهاتف يدوي حسب السيناريو.

## English

### Mobile API (`/api/v1`, Bearer JWT)
All responses: `{ success, message, data, errors }`; lists: `data = { items, page, pageSize, totalCount, totalPages }`.

| Method | Path | Purpose |
|---|---|---|
| POST | `auth/login` | phone + password → JWT (rate limited) |
| GET | `me` | profile, branches, warehouses, `countType` (`parts` \| `vehicles` \| `both`), `canCountParts`, `canScanVins` |
| GET | `sessions/current` | in-progress stocktaking session |
| GET | `parts?search=&warehouseId=&page=&pageSize=` | parts in the user's warehouses. `isCounted` and `countedBy` (name) for every row; the counted quantity, location and time only for the user who counted it |
| GET | `parts/{id}` | part details |
| POST | `counts` | submit a count (`partId` or `partNumber`+`warehouseId`, `quantity`, `location`, `latitude`, `longitude`). Saved directly whatever the variance. A row counted by another user returns 409 with `data.countedBy`; the user who counted it may count it again. 403 if the user's count type excludes parts |
| POST | `vins/scan` | scan a VIN (17 chars, GPS required). 403 if the user's count type excludes vehicles |
| GET | `vins` | my scanned VINs |
| POST/GET | `vin-corrections` | request / list VIN corrections |
| GET | `recounts` | my recount tasks |
| POST | `recounts/{id}/submit` | submit a recount |
| GET | `history?type=part\|vin` | my history in the current session |
| POST | `parts/{id}/recount-ask` | the user who counted a part asks an admin to recount it |

`qty-requests` endpoints were removed (404).

Disabled users are rejected at login and on every token validation (JWT `OnTokenValidated`). Errors are JSON (`401`, `403`, `404`, `500`).

### Configuration
No secrets in `appsettings.json`. Required at runtime: `ConnectionStrings:Default`, `Jwt:Key` (≥ 32 bytes). The main admin is seeded once from `Seed:*` (never hard-coded). `appsettings.Development.json` is git-ignored; copy the `.example` file.

### Migrations
```bash
dotnet ef migrations add <Name> --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web --output-dir Data/Migrations
dotnet ef migrations script --project src/HyundaiStock.Infrastructure --startup-project src/HyundaiStock.Web -o db/initial-sqlserver.sql
```
SQLite (dev only) uses `EnsureCreated`.
