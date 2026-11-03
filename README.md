# RS Sales — نظام مبيعات الصيدلية (VB.NET / Windows Forms)

تحويل نظام المبيعات القديم (PHP) إلى تطبيق سطح مكتب **VB.NET + Windows Forms**
على MySQL/MariaDB، مع دعم عدة فروع والعمل دون إنترنت ثم المزامنة عبر HTTPS API
مركزي.

---

## حالة المشروع

> **آخر تحقق: 2026-11-03.**
> - شجرة العمل المدموجة في `D:\DOCTOR TECH` (دمج `DOCTOR TECH` + `rs_sales_vbnet`)
>   تبني كاملًا: `Build succeeded` — 0 تحذيرات، 0 أخطاء (Release).
> - الاختبارات: **51/57 ناجحة** على MariaDB حقيقية. الستة الفاشلة معروفة وموجودة
>   قبل الدمج: `ClosedShiftCannotBeUsedForANewSale`، `ExpiredStockCannotBeSold`،
>   `CreditLimitBlocksThenManagerOverrideAllows`، `BranchesKeepStockLedgerCashAndTaxSeparate`،
>   `InventoryCountPostsVarianceStockMovementAndJournal`،
>   `InventoryCountRepostsAgainstLiveBalanceNotStaleSnapshot`.
> - البيانات: `import-legacy` نقل **29,576 صنفًا** + 29,576 باركود + 7 مجموعات من
>   `rs_pharmacy.currencies` إلى مخطط `items`. الكميات ما زالت صفرًا (`Moves : 0`)
>   حتى يُنفَّذ المخزون الافتتاحي.
> - شاشة البيع: شريط الدفع والخصم والإجماليات عريض أسفل الأصناف، خانة كود الصنف في
>   وسط الأعلى، «إدارة الأصناف» و«كارت الصنف» داخل الفاتورة، وعرض الدروب ليست تلقائي.
> - **ليس جاهزًا للإنتاج**: خادم المزامنة المركزي غير منفّذ، والطباعة غير
>   مختبرة، وثغرات TLS/DeviceKey ما زالت مفتوحة (انظر §8).
>
> **آخر تحقق: 2026-10-31.**
> - نسخة `HEAD` تبني بنجاح: `Build succeeded` — 6 تحذيرات، 0 أخطاء.
> - الاختبارات: **40/40 ناجحة** على MariaDB حقيقية (17 وحدة + 23 تكامل).
> - الكود: 7 مشاريع إنتاجية + مشروع اختبارات (≈8,559 سطر إنتاجي).
> - **ليس جاهزًا للإنتاج**: خادم المزامنة المركزي غير منفّذ، والطباعة غير
>   مختبرة، وثغرات TLS/DeviceKey ما زالت مفتوحة (انظر §8).
>
> **شجرة العمل (2026-10-30):** أُصلح كسر البناء، وأُلغي الدخول التلقائي،
> وأُزيلت كلمة المرور المسبقة والافتراضية. التطبيق يفتح الآن على شاشة الدخول،
> و`seed` يرفض إنشاء أول مدير بلا كلمة مرور قوية. أُضيف `appsettings.json`
> إلى `.gitignore`. التزمت إصلاحات الدخول في `42a441b`، وإصلاح الترحيل M-13
> في `5f42547`. الترحيل الكامل والمخزون الأول تحققا على قاعدة مؤقتة
> (`VERIFY OK`)، وأُصلح M-14 (إجمالي رأس فاتورة المخزون الأول). وأُصلح M-15…M-21:
> تقييم حركة البيع، ومطابقة «كاش» في الصندوق، وفحص طريقة الدفع في المرتجعات،
> وأسماء SQL المحجوزة، ووردية فرع آخر في الفاتورة وإغلاقها، وميزان المراجعة
> الذي كان يجمع كل الفروع، والتحويل بين الفروع الذي كان يحرّك المخزون بلا قيد.
> عزل الفروع والتحويل مُتحقَّقان بفرعين وصندوقين، و`verify` يقارن حساب المخزون
> بقيمة الحركات في كل فرع (40 اختبارًا). من يرقّي قاعدة
> قديمة يُعيد `seed` لإضافة الحسابين `1250`/`2150`.

---

## 1. حدود المشروع (غير قابلة للتفاوض)

| القاعدة | التفصيل |
|---|---|
| المصدر الأصلي للقراءة فقط | `D:\rs_pharmacy` — لا تعديل ولا حذف ولا تشغيل هجرات عليه |
| قاعدة الإنتاج | `rs_pharmacy` — لا تُلمس. الاختبار على `rs_pharmacy_test` / `rs_sales_test_it` فقط |
| التقنية | VB.NET + Windows Forms، .NET 10 (`net10.0` / `net10.0-windows`) |
| قاعدة البيانات | MySQL 8.x أو MariaDB 10.6+، InnoDB، `utf8mb4` |
| المبالغ | `DECIMAL` فقط — لا `FLOAT`/`DOUBLE` في أي حقل مالي |
| المزامنة | HTTPS API مركزي + `event_id` فريد + idempotent + outbox/inbox |
| العمليات الحساسة | لا `last-write-wins`؛ تُطبَّق كأحداث أعمال مرتبة وقابلة للتتبع |
| الأسرار | خارج المستودع — متغيرات بيئة أو ملف محمي أو Windows Credential Manager/DPAPI |

---

## 2. حالة المراحل

| # | المرحلة | الحالة | الملاحظة |
|---|---|---|---|
| 0 | تهيئة البيئة والجرد | ✅ مكتملة | `docs/PHASE_0_INVENTORY.md` |
| 1 | تحليل النظام القديم | ✅ مكتملة | 4 وثائق تحليلية + مخاطر/أسئلة |
| 2 | التصميم المعماري وقاعدة البيانات | ✅ مكتملة الوثائق | المخطط + الهجرات + `ARCHITECTURE` + `SYNC_PROTOCOL` + `DATA_MIGRATION_PLAN` + `ERD` + `PHASE_2_REPORT` |
| 3 | حل VB.NET + الدخول + الإعدادات | ✅ مكتملة | 7 مشاريع، دخول بصلاحيات، إعدادات، أداة قاعدة بيانات |
| 4 | نقل الوظائف | 🟡 معظمها | بيع/مرتجع/شراء/مرتجع شراء/مخزون أول/تسوية/تحويل/وردية/ضريبة/تقارير. ناقص: الولاء والكوبونات وجرد المخزون (جداول بلا كود) |
| 5 | المزامنة والعمل دون إنترنت | 🟡 العميل فقط | outbox/inbox/backoff مُختبَرة؛ **الخادم المركزي غير موجود** |
| 6 | التقارير والطباعة والأجهزة | 🔴 | طباعة غير مختبرة، لا باركود/طابعة مُتحقَّق منها |
| 7 | الترحيل والتشغيل التجريبي | 🟡 | ترحيل كامل (29,576 صنفًا) + مخزون أول + دورة أعمال كاملة + دورة نقدية ضريبية على وردية + `verify OK`؛ القطع الإنتاجي لم يُنفَّذ |
| 8 | التثبيت والتسليم | 🔴 | لا مثبّت ولا CI |

---

## 3. المعمارية باختصار

```text
RsSales.UI (WinForms)
   ├─ RsSales.Business  (خدمات + AppSession + صلاحيات)
   │     ├─ RsSales.Data (Dapper + MySqlConnector + هجرات)
   │     └─ RsSales.Domain (حسابات المال والضريبة — بلا I/O)
   ├─ RsSales.Sync (Push/Pull + outbox/inbox)
   └─ RsSales.Infrastructure (إعدادات، سجلات، BCrypt)
RsSales.DbTool (CLI: migrate/rollback/seed/create-admin/import-legacy/verify/status)
```

كل عملية مالية = **معاملة واحدة** تشمل الفاتورة والبنود وحركات المخزون والدفع
والقيد المحاسبي والتدقيق وحدث المزامنة. أي فشل ⇒ تراجع كلي.
التفاصيل الكاملة: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## 4. هيكل المستودع

```text
RsSales.slnx               الحل (7 مشاريع + الاختبارات)
Directory.Build.props      Option Strict/Explicit On لكل المشاريع
src/
  RsSales.Domain/          Money, TaxCalculator, DiscountAllocator, InvoiceCalculator, Uuid7
  RsSales.Data/            DbSession, مستودعات Dapper, SchemaMigrator, SyncStore
  RsSales.Business/        Auth/Sales/Purchase/Stock/Item/Customer/Tax/Report + PostingHelper
  RsSales.Sync/            SyncContracts, SyncService, HttpSyncTransport
  RsSales.Infrastructure/  AppConfig, AppLog, PasswordHasher
  RsSales.UI/              24 نموذج WinForms عربي RTL
  RsSales.DbTool/          أداة CLI للقاعدة
tests/RsSales.Tests/       DomainTests (17) + IntegrationTests (19) + DbFixture
db/
  migrations/V001__init.sql   45 جدولًا + 60 FK + View
  rollback/R001__init.sql
docs/                      التحليل والتصميم والبروتوكول (انظر §7)
```

`_archive/` و`*.sql` و`*.log` و`bin/obj` مستثناة من Git.

---

## 5. التشغيل السريع

### المتطلبات

| العنصر | المتحقَّق عليه |
|---|---|
| نظام التشغيل | Windows 10/11 |
| .NET SDK | **10.0.403** — مثبّت محليًا في `D:\dotnet` (غير مضاف إلى PATH) |
| قاعدة البيانات | MariaDB 13.0.2 / MySQL 8 على `127.0.0.1:3306` |

### البناء

```powershell
D:\dotnet\dotnet.exe build RsSales.slnx
```

### تهيئة القاعدة (تُنشئ الجداول والبيانات المرجعية)

```powershell
$cs = "Server=localhost;Port=3306;Database=rs_sales;User ID=root;Password=;SslMode=None;Allow User Variables=True"
D:\dotnet\dotnet.exe run --project src/RsSales.DbTool -- migrate --conn $cs
D:\dotnet\dotnet.exe run --project src/RsSales.DbTool -- seed    --conn $cs --admin admin --password "<كلمة-قوية>"
D:\dotnet\dotnet.exe run --project src/RsSales.DbTool -- verify  --conn $cs
```

> `seed` **يرفض** إنشاء أول مدير بلا `--password` (8 أحرف على الأقل، مع حرف
> ورقم). إن كان المدير موجودًا بالفعل فلا حاجة لتمريرها. لا توجد أي كلمة مرور
> افتراضية في الكود.

### تشغيل الواجهة

```powershell
D:\dotnet\dotnet.exe run --project src/RsSales.UI
```

### الاختبارات

```powershell
D:\dotnet\dotnet.exe test tests/RsSales.Tests
# أو تحديد خادم اختبار مختلف:
$env:RSSALES_TEST_SERVER = "Server=localhost;Port=3306;User ID=root;Password=;SslMode=None"
```

الاختبارات تُسقط قاعدة `rs_sales_test_it` وتُنشئها من جديد في بداية كل تشغيل،
ثم تطبّق **الهجرات الحقيقية**؛ ولا تلمس قاعدة التطبيق أو الإنتاج.

---

## 6. الإعدادات

`AppConfig.Load` يحل الإعدادات بالترتيب:

1. `appsettings.json` بجوار التنفيذي، ثم `%LOCALAPPDATA%\RsSales\appsettings.json`.
2. متغيرات البيئة (لها الأولوية):
   - `RSSALES_CONNECTION` — سلسلة اتصال MariaDB.
   - `RSSALES_SERVER_URL` — عنوان API المزامنة المركزي.
3. القيم الافتراضية (`Database=rs_sales`, `root` بلا كلمة مرور) — **للتطوير فقط**.

| المفتاح | المعنى | الافتراضي |
|---|---|---|
| `ConnectionString` | اتصال MariaDB | `rs_sales` محلي |
| `ServerUrl` | جذر API المزامنة | فارغ (المزامنة معطلة) |
| `DeviceKey` | توكن الجهاز (Bearer) | فارغ |
| `SyncBatchSize` | أحداث لكل دفعة | 50 |
| `SyncMaxAttempts` | حد المحاولات قبل `dead` | 10 |
| `LogDirectory` | مجلد السجلات | `%LOCALAPPDATA%\RsSales\logs` |

> **ملاحظة:** `appsettings.json` مُستثنى الآن من Git صراحةً. أما
> `appsettings.example.json` فما زال **فارغًا** ويحتاج قالبًا بلا أسرار.

---

## 7. الوثائق

| الملف | الحالة |
|---|---|
| `docs/PHASE_0_INVENTORY.md` | ✅ جرد البيئة والمصدر |
| `docs/LEGACY_SYSTEM_AUDIT.md` | ✅ تحليل النظام القديم |
| `docs/DATABASE_SCHEMA.md` | ✅ مخطط النظام **القديم** (68 جدولًا) — مرجع ترحيل |
| `docs/BUSINESS_RULES.md` | ✅ قواعد العمل المستخرجة |
| `docs/FEATURE_MAPPING.md` | ✅ خريطة 100 صفحة PHP → وحدات |
| `docs/RISKS_AND_QUESTIONS.md` | ✅ المخاطر والأسئلة المفتوحة |
| `docs/ROADMAP.md` | ✅ خطة المراحل وبوابات القبول |
| `docs/PHASE_1_REPORT.md` | ✅ تقرير المرحلتين 0 و1 |
| `docs/BRANCH_MODEL_PROPOSAL.md` | ✅ نموذج الفروع (بانتظار B-01…B-08) |
| `docs/TAX_DESIGN.md` | ✅ تصميم الضريبة |
| **`docs/ARCHITECTURE.md`** | ✅ **جديد** — المعمارية المنفّذة فعليًا |
| **`docs/SYNC_PROTOCOL.md`** | ✅ **جديد** — عقد المزامنة الفعلي |
| **`docs/DATA_MIGRATION_PLAN.md`** | ✅ **جديد** — تحويل الحقول وRunbook وفجوات الاستيراد |
| **`docs/ERD.md`** | ✅ **جديد** — 4 مخططات Mermaid + كتالوج 45 جدولًا |
| **`docs/PHASE_2_REPORT.md`** | ✅ **جديد** — أدلة المرحلة 2 وبوابتها |

---

## 8. ما هو غير مكتمل (بصراحة)

| # | النقص | الخطورة |
|---|---|---|
| 1 | خادم المزامنة المركزي غير موجود (العميل فقط) | 🔴 وظيفي |
| 2 | TLS غير مفروض في `HttpSyncTransport`، و`DeviceKey` بنص صريح | 🔴 أمني |
| 3 | `ReportService` لا يفرض صلاحيات `reports.*` | 🟠 أمني |
| 4 | كل استدعاءات DB متزامنة على خيط الواجهة | 🟠 أداء |
| 5 | الأصناف/العملاء لا تُرسَل في outbox (فقط `change_log` محلي) | 🟠 وظيفي |
| 6 | السحب يُسجّل التغييرات ولا يطبّقها على البيانات | 🟠 وظيفي |
| 7 | الولاء والكوبونات وجرد المخزون: جداول بلا كود | 🟡 نطاق |
| 8 | لا CI، لا مثبّت، لا اختبار طباعة/باركود، لا اختبار تزامن | 🟡 تسليم |

---

## 9. قواعد العمل اليومية

- لا تعديل على `D:\rs_pharmacy`.
- لا تشغيل أي أمر كتابة على `rs_pharmacy`.
- كل تغيير = Commit واضح بالعربية أو الإنجليزية.
- كل مرحلة تُسلَّم بتقرير: ما فُحص، ما أُنشئ، القرارات، الاختبارات الفعلية،
  الفروق، المخاطر، الخطوة التالية، بوابة القبول.
- **لا توثّق ما لم تُنفّذه.** هذه الوثيقة حُدّثت لأن النسخة السابقة كانت تقول
  «لا يوجد كود» بينما الحل كامل.
