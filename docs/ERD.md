# مخطط الكيانات والعلاقات — ERD

**المصدر الوحيد للحقيقة:** `db/migrations/V001__init.sql`.
**آخر تحقق:** 2026-11-02 — الهجرة تُنشئ **45 جدولًا + View واحد**، و**60 مفتاحًا
أجنبيًا**، وصفر حقول `float`/`double`.
**إضافة V009:** جدولا الخادم المركزي `server_sync_events` و`server_change_log`
يعيشان في قاعدة المركز فقط ولا يظهران في مخططات قاعدة الفرع أدناه.
**الاصطلاحات:** `PK` مفتاح أساسي، `FK` مفتاح أجنبي، وكل المبالغ `DECIMAL`.
الطوابع الزمنية UTC في `DATETIME(6)`، و`business_date` تاريخ محلي.

> الجداول مقسّمة إلى أربعة نطاقات لتكون المخططات قابلة للقراءة. العلاقات
> المرسومة بـ`||..o{` **علاقات منطقية بلا Foreign Key** (جداول المزامنة
> والتدقيق) — وهذا قرار مقصود مذكور في §5.

---

## 1. الهوية والصلاحيات والإعدادات

```mermaid
erDiagram
  branches {
    bigint id PK
    varchar code
    varchar name
    varchar timezone
    varchar tax_no
    tinyint is_active
  }
  users {
    bigint id PK
    varchar username
    varchar password_hash
    varchar full_name
    tinyint is_active
    int failed_attempts
    datetime locked_until
  }
  roles {
    bigint id PK
    varchar code
    varchar name
  }
  permissions {
    bigint id PK
    varchar code
  }
  user_roles {
    bigint user_id PK
    bigint role_id PK
  }
  role_permissions {
    bigint role_id PK
    bigint permission_id PK
  }
  user_branch_access {
    bigint user_id PK
    bigint branch_id PK
  }
  settings {
    varchar k PK
    text v
    varchar value_type
  }
  branch_settings {
    bigint branch_id PK
    varchar k PK
    text v
  }
  devices {
    bigint id PK
    bigint branch_id FK
    varchar name
    varchar device_key_hash
    tinyint is_active
  }
  document_sequences {
    bigint branch_id PK
    varchar doc_type PK
    char period PK
    bigint next_no
  }
  audit_log {
    bigint id PK
    char event_id
    bigint branch_id
    bigint user_id
    varchar action
    varchar entity_type
    varchar entity_id
    datetime created_at
  }

  branches ||--o{ user_branch_access : "يفتح"
  users ||--o{ user_branch_access : "يُسنَد"
  users ||--o{ user_roles : "يحمل"
  roles ||--o{ user_roles : "يُمنح"
  roles ||--o{ role_permissions : "يجمع"
  permissions ||--o{ role_permissions : "يدخل في"
  branches ||--o{ branch_settings : "تجاوزات"
  branches ||--o{ devices : "أجهزة"
  branches ||--o{ document_sequences : "مسلسلات"
  branches ||..o{ audit_log : "سجل"
  users ||..o{ audit_log : "فاعل"
```

**ملاحظات:**

- `users` ليست مرتبطة بفرع مباشرة؛ الربط عبر `user_branch_access` (M:N).
- `settings` عامة، و`branch_settings` تتجاوزها لكل فرع.
- `document_sequences` مفتاحها المركّب `(branch_id, doc_type, period)` وتمنع
  تصادم أرقام المستندات بين الفروع وعند العمل دون إنترنت.
- `devices` موجودة في المخطط لكن **لا كود يستخدمها بعد** (لا تسجيل جهاز ولا
  تحقق من `device_key_hash`).
- `audit_log` بلا FK على المستخدم/الفرع عمدًا: يجب أن يبقى السجل حتى لو حُذف
  المرجع، و`branch_id`/`user_id` فيه قد يكونان `NULL` لعمليات النظام.

---

## 2. الأصناف والمخزون

```mermaid
erDiagram
  tax_categories {
    bigint id PK
    varchar code
    decimal rate
    tinyint is_exempt
    date effective_from
    date effective_to
  }
  item_groups {
    bigint id PK
    varchar name
    bigint parent_id FK
  }
  units {
    bigint id PK
    varchar name
  }
  items {
    bigint id PK
    char uuid
    varchar code
    varchar name_ar
    bigint group_id FK
    bigint base_unit_id FK
    bigint tax_category_id FK
    decimal cost_price
    decimal sale_price
    decimal min_price
    decimal reorder_level
    tinyint is_active
  }
  item_barcodes {
    bigint id PK
    bigint item_id FK
    varchar barcode
    tinyint is_primary
  }
  item_units {
    bigint id PK
    bigint item_id FK
    bigint unit_id FK
    decimal factor
    decimal sale_price
  }
  item_price_history {
    bigint id PK
    bigint item_id FK
    varchar price_type
    decimal old_value
    decimal new_value
    datetime changed_at
  }
  stock_moves {
    bigint id PK
    char event_id
    bigint branch_id FK
    bigint item_id FK
    enum move_type
    decimal qty_delta
    decimal unit_cost
    datetime occurred_at
    date business_date
    bigint created_by FK
  }
  stock_balances {
    bigint branch_id PK
    bigint item_id PK
    decimal qty_on_hand
    decimal last_cost
  }
  stock_transfers {
    bigint id PK
    char event_id
    varchar transfer_no
    bigint from_branch FK
    bigint to_branch FK
    enum status
  }
  stock_transfer_lines {
    bigint id PK
    bigint transfer_id FK
    bigint item_id FK
    decimal qty
    decimal unit_cost
  }

  tax_categories ||--o{ items : "تصنيف"
  item_groups ||--o{ items : "مجموعة"
  item_groups ||--o{ item_groups : "أب"
  units ||--o{ items : "وحدة أساس"
  items ||--o{ item_barcodes : "باركودات"
  items ||--o{ item_units : "وحدات"
  units ||--o{ item_units : "وحدة"
  items ||--o{ item_price_history : "تاريخ سعر"
  branches ||--o{ stock_moves : "حركات"
  items ||--o{ stock_moves : "حركات"
  branches ||--o{ stock_balances : "رصيد"
  items ||--o{ stock_balances : "رصيد"
  branches ||--o{ stock_transfers : "من/إلى فرع"
  stock_transfers ||--o{ stock_transfer_lines : "بنود"
  items ||--o{ stock_transfer_lines : "صنف"
```

**ملاحظات:**

- `stock_moves` هو **مصدر الحقيقة**؛ `stock_balances` مخزن مؤقت مفتاحه
  `(branch_id, item_id)` ويُحدَّث في نفس معاملة الحركة.
- `move_type`: `purchase`, `purchase_return`, `sale`, `sale_return`, `opening`,
  `adjustment`, `transfer_in`, `transfer_out`, `count_gain`, `count_loss`.
- `item_units` و`item_groups.parent_id` موجودان لكن **لا كود يستخدمهما بعد**؛
  الاستيراد يسطّح شجرة المجموعات.
- `item_price_history` يُكتب عند تغيير سعر البيع من `ItemService`.
- التحويل حالة على مستوى الوثيقة (`draft → sent → received/cancelled`)،
  وحركتا `transfer_out`/`transfer_in` هما الأثر المخزني.

---

## 3. البيع والشراء والمدفوعات والمحاسبة

```mermaid
erDiagram
  customers {
    bigint id PK
    char uuid
    varchar code
    varchar name
    enum type
    varchar mobile
    varchar tax_no
    decimal credit_limit
    decimal opening_balance
  }
  invoices {
    bigint id PK
    char event_id
    bigint branch_id FK
    varchar invoice_no
    enum invoice_type
    enum status
    bigint customer_id FK
    bigint original_invoice_id FK
    bigint shift_id FK
    decimal subtotal
    decimal header_discount
    decimal tax_total
    decimal total
    decimal paid
    decimal due
    enum payment_status
    enum price_mode
    date business_date
    datetime occurred_at
    bigint created_by FK
  }
  invoice_lines {
    bigint id PK
    bigint invoice_id FK
    bigint item_id FK
    decimal qty
    decimal unit_price
    decimal discount
    bigint tax_category_id FK
    decimal tax_rate
    decimal tax_base
    decimal tax_amount
    decimal line_total
    decimal cost_at_sale
    bigint original_line_id FK
  }
  payments {
    bigint id PK
    char event_id
    bigint invoice_id FK
    bigint branch_id FK
    bigint method_id FK
    decimal amount
    datetime paid_at
  }
  payment_methods {
    bigint id PK
    varchar name
    tinyint requires_reference
  }
  shifts {
    bigint id PK
    bigint branch_id FK
    varchar shift_no
    bigint user_id FK
    decimal opening_cash
    decimal expected_cash
    decimal counted_cash
    decimal variance
    enum status
  }
  cashboxes {
    bigint id PK
    bigint branch_id FK
    varchar name
    bigint account_id
  }
  cash_moves {
    bigint id PK
    char event_id
    bigint cashbox_id FK
    bigint shift_id FK
    enum direction
    decimal amount
  }
  accounts {
    bigint id PK
    varchar code
    varchar name
    bigint parent_id FK
    enum nature
    tinyint is_postable
  }
  journal_entries {
    bigint id PK
    char event_id
    bigint branch_id FK
    varchar entry_no
    date entry_date
    enum status
  }
  journal_lines {
    bigint id PK
    bigint entry_id FK
    bigint account_id FK
    decimal debit
    decimal credit
  }

  customers ||--o{ invoices : "فاتورة"
  branches ||--o{ invoices : "فرع"
  shifts ||--o{ invoices : "وردية"
  invoices ||--o{ invoice_lines : "بنود"
  items ||--o{ invoice_lines : "صنف"
  tax_categories ||--o{ invoice_lines : "لقطة ضريبية"
  invoices ||--o{ invoices : "مرتجع"
  invoice_lines ||--o{ invoice_lines : "بند مرتجع"
  invoices ||--o{ payments : "دفعات"
  payment_methods ||--o{ payments : "طريقة"
  branches ||--o{ payments : "فرع"
  branches ||--o{ shifts : "وردية"
  users ||--o{ shifts : "يفتح"
  branches ||--o{ cashboxes : "صناديق"
  cashboxes ||--o{ cash_moves : "حركة نقدية"
  shifts ||--o{ cash_moves : "حركة نقدية"
  branches ||--o{ journal_entries : "قيد"
  journal_entries ||--o{ journal_lines : "أسطر"
  accounts ||--o{ journal_lines : "حساب"
  accounts ||--o{ accounts : "أب"
```

**ملاحظات:**

- `invoices.invoice_type`: `sale`, `sale_return`, `purchase`, `purchase_return`,
  `opening`. و`status`: `draft`, `posted`, `voided`.
- `invoice_no` فريد لكل `(branch_id, invoice_type, invoice_no)`، والمسلسل من
  `document_sequences`.
- لقطة الضريبة على البند: `tax_category_id`, `tax_rate`, `tax_base`,
  `tax_amount`, `price_mode` — التقارير التاريخية لا تتأثر بتغيّر التصنيف.
- `original_invoice_id` و`original_line_id` يربطان المرتجع بأصله ويمنعان
  إرجاع أكثر من المتاح (مُختبَر).
- `cost_at_sale` (+ `cost_at_sale_incl_tax`) لقطة تكلفة LASTCOST وقت البيع
  (D-10)؛ تقرير الأرباح يعتمد عليها.
- `payments.amount` سالب في حالات رد النقد (مرتجع بيع).
- `accounts` شجرة مبسّطة بـ13 حسابًا من `seed`؛ لا نقل لشجرة الـ177 القديمة.

---

## 4. الولاء والكوبونات والجرد والمزامنة

```mermaid
erDiagram
  loyalty_accounts {
    bigint customer_id PK
    decimal points
    decimal total_earned
    decimal total_redeemed
  }
  loyalty_transactions {
    bigint id PK
    char event_id
    bigint customer_id FK
    bigint invoice_id FK
    decimal points_delta
    enum type
  }
  coupons {
    bigint id PK
    varchar code
    enum discount_type
    decimal discount_value
    int max_uses
    int used_count
  }
  coupon_redemptions {
    bigint id PK
    bigint coupon_id FK
    bigint invoice_id FK
    decimal amount
  }
  inventory_counts {
    bigint id PK
    char event_id
    bigint branch_id FK
    varchar count_no
    enum status
  }
  inventory_count_lines {
    bigint id PK
    bigint count_id FK
    bigint item_id FK
    decimal system_qty
    decimal counted_qty
    decimal variance_qty
    decimal variance_value
  }
  sync_outbox {
    char event_id PK
    bigint branch_id
    varchar entity_type
    varchar entity_id
    varchar event_type
    longtext payload
    enum status
    int attempts
    datetime next_attempt_at
  }
  sync_inbox {
    char event_id PK
    bigint branch_id
    varchar entity_type
    varchar entity_id
    enum result
    datetime applied_at
  }
  sync_cursors {
    bigint branch_id PK
    varchar stream PK
    bigint last_seq
  }
  change_log {
    bigint seq PK
    varchar entity_type
    varchar entity_id
    enum op
    longtext payload
    bigint branch_id
  }
  schema_migrations {
    varchar version PK
    char checksum
    datetime applied_at
  }

  customers ||--o| loyalty_accounts : "رصيد نقاط"
  customers ||--o{ loyalty_transactions : "حركات نقاط"
  invoices ||--o{ loyalty_transactions : "سبب"
  coupons ||--o{ coupon_redemptions : "استخدام"
  invoices ||--o{ coupon_redemptions : "فاتورة"
  branches ||--o{ inventory_counts : "جرد"
  inventory_counts ||--o{ inventory_count_lines : "بنود"
  items ||--o{ inventory_count_lines : "صنف"
  branches ||..o{ sync_outbox : "أحداث صادرة"
  branches ||..o{ sync_inbox : "أحداث واردة"
  branches ||..o{ sync_cursors : "مؤشرات"
  branches ||..o{ change_log : "تغييرات"
```

**ملاحظات:**

- جداول الولاء والكوبونات والجرد **موجودة بلا كود تطبيقي** (0 مراجع في `src/`).
- `sync_outbox.status`: `pending`, `sending`, `sent`, `acked`, `failed`, `dead`.
  لا حذف قبل الإقرار. `attempts` يزيد عند المطالبة، و`next_attempt_at` بوابة
  إعادة المحاولة.
- `sync_inbox.result`: `applied`, `duplicate`, `conflict`, `rejected`.
- `change_log.op`: `upsert`, `delete`.
- جداول المزامنة والتدقيق **بلا Foreign Keys** عمدًا (حجم كتابة عالٍ، وقابلية
  ترحيل/تفريغ دون كسر مراجع). العلاقات فيها منطقية فقط.

---

## 5. قرارات وقيود مهمة

| القرار | التفصيل |
|---|---|
| المحرك والترميز | InnoDB، `utf8mb4_unicode_ci` في كل الجداول |
| المال | `DECIMAL(18,2)` للمبالغ، `DECIMAL(18,4)` للأسعار والكميات والتكاليف؛ معدل الضريبة `DECIMAL(6,3)` |
| الزمن | `DATETIME(6)` UTC، و`business_date DATE` محلي |
| المعرّفات | `BIGINT UNSIGNED AUTO_INCREMENT` + `uuid CHAR(36)` (UUIDv7) للصفوف القابلة للمزامنة |
| `event_id` | `CHAR(36)` فريد في جداول الأحداث: `invoices`, `stock_moves`, `cash_moves`, `payments`, `journal_entries`, `stock_transfers`, `inventory_counts`, `loyalty_transactions`, `sync_outbox`, `sync_inbox` |
| الحذف | `ON DELETE RESTRICT` افتراضيًا للبيانات المالية؛ `CASCADE` للبنود التابعة (بنود الفاتورة، أسطر القيد، بنود التحويل/الجرد) |
| الفواتير | غير قابلة للتعديل بعد الترحيل؛ الإلغاء يضبط `status='voided'` ويُبقي الصف |
| لا FK | `sync_outbox`, `sync_inbox`, `sync_cursors`, `change_log`, `audit_log` |
| CHECK | معدل ضريبة 0–100، كميات غير صفرية، أسعار غير سالبة، أسطر قيد موجبة باتجاه واحد |
| View | `v_stock` = `items × branches` مع رصيد وأساس تكلفة (تُظهر كل صنف مفعّل حتى بلا صف رصيد) |

---

## 6. كتالوج الجداول الـ45

| # | الجدول | الغرض | الكاتب | الحالة في الكود |
|---:|---|---|---|---|
| 1 | `schema_migrations` | إصدارات الهجرات وبصماتها | أداة CLI | مستخدم |
| 2 | `branches` | الفروع | المركز | مستخدم |
| 3 | `settings` | إعدادات عامة | المركز/الفرع | مستخدم |
| 4 | `branch_settings` | تجاوزات إعداد لكل فرع | الفرع | **مخطط فقط** |
| 5 | `roles` | الأدوار | المركز | مستخدم |
| 6 | `permissions` | الصلاحيات | المركز | مستخدم |
| 7 | `role_permissions` | ربط الأدوار بالصلاحيات | المركز | مستخدم |
| 8 | `users` | المستخدمون | المركز | مستخدم |
| 9 | `user_roles` | أدوار المستخدم | المركز | مستخدم |
| 10 | `user_branch_access` | فروع المستخدم | المركز | مستخدم |
| 11 | `devices` | أجهزة الفروع ومفاتيحها | المركز | **مخطط فقط** |
| 12 | `document_sequences` | مسلسلات المستندات | الفرع | مستخدم |
| 13 | `tax_categories` | التصنيفات الضريبية | المركز (الفرع حاليًا) | مستخدم |
| 14 | `item_groups` | مجموعات الأصناف | المركز | مستخدم (بلا أب) |
| 15 | `units` | الوحدات | المركز | مستخدم |
| 16 | `items` | الأصناف والأسعار | المركز (الفرع حاليًا) | مستخدم |
| 17 | `item_barcodes` | الباركودات | المركز | مستخدم |
| 18 | `item_units` | وحدات متعددة للصنف | المركز | **مخطط فقط** |
| 19 | `item_price_history` | تاريخ تغيّر الأسعار | الفرع | مستخدم |
| 20 | `customers` | العملاء والموردون | الفرع | مستخدم |
| 21 | `payment_methods` | طرق الدفع | المركز | مستخدم |
| 22 | `cashboxes` | الصناديق | الفرع | مستخدم |
| 23 | `shifts` | الورديات | الفرع | مستخدم |
| 24 | `cash_moves` | حركات الصندوق | الفرع | مستخدم |
| 25 | `stock_moves` | حركات المخزون (مصدر الحقيقة) | الفرع | مستخدم |
| 26 | `stock_balances` | الرصيد المخزني (Cache) | الفرع | مستخدم |
| 27 | `stock_transfers` | تحويلات بين الفروع | الفرع | مستخدم |
| 28 | `stock_transfer_lines` | بنود التحويل | الفرع | مستخدم |
| 29 | `invoices` | رأس الفاتورة | الفرع | مستخدم |
| 30 | `invoice_lines` | بنود الفاتورة | الفرع | مستخدم |
| 31 | `payments` | الدفعات | الفرع | مستخدم |
| 32 | `accounts` | دليل الحسابات | المركز | مستخدم |
| 33 | `journal_entries` | القيود | الفرع | مستخدم |
| 34 | `journal_lines` | أسطر القيود | الفرع | مستخدم |
| 35 | `loyalty_accounts` | أرصدة نقاط الولاء | الفرع | **مخطط فقط** |
| 36 | `loyalty_transactions` | حركات النقاط | الفرع | **مخطط فقط** |
| 37 | `coupons` | الكوبونات | المركز | **مخطط فقط** |
| 38 | `coupon_redemptions` | استخدام الكوبونات | الفرع | **مخطط فقط** |
| 39 | `inventory_counts` | وثائق الجرد | الفرع | **مخطط فقط** |
| 40 | `inventory_count_lines` | بنود الجرد | الفرع | **مخطط فقط** |
| 41 | `audit_log` | سجل التدقيق | الطرفان | مستخدم |
| 42 | `sync_outbox` | أحداث صادرة | الفرع | مستخدم |
| 43 | `sync_inbox` | أحداث واردة | الطرفان | مستخدم (تسجيل فقط) |
| 44 | `sync_cursors` | مؤشرات السحب | الفرع | مستخدم |
| 45 | `change_log` | تغييرات الأساسيات | الطرفان | مستخدم (محليًا فقط) |

**إحصاء:** 45 جدولًا، منها **36 مستخدمًا في الكود**، و**9 جداول مخطط فقط**
(`branch_settings`, `devices`, `item_units`, `loyalty_accounts`,
`loyalty_transactions`, `coupons`, `coupon_redemptions`,
`inventory_counts`, `inventory_count_lines`).

---

## 7. تعارضات واجب حسمها

| # | التعارض | الأثر |
|---|---|---|
| E-1 | `devices` مُعرَّف لكن المزامنة ترسل `device_id` من الجلسة ولا تُسجّل الجهاز ولا تتحقق من مفتاحه | لا يمكن إبطال جهاز مخترق |
| E-2 | `branch_settings` غير مستخدم؛ الإعدادات تُقرأ من `settings` فقط | لا تجاوزات لكل فرع رغم وجود الجدول |
| E-3 | `items` و`tax_categories` قابلة للتعديل محليًا في الكود، بينما نموذج الفروع يجعل المركز مالكها | تعارض أساسيات بين الفروع |
| E-4 | `change_log` يُكتب محليًا ولا يُرسَل للمركز | الأصناف/العملاء لا تُزامَن صعودًا |
| E-5 | `sync_inbox` يُكتب بلا تطبيق فعلي على الجداول | سحب الأساسيات لا يحدّث البيانات |
| E-6 | `item_units` بلا كود مع أن الاستيراد يقرأ `curr_units` | الوحدات المتعددة غير مدعومة فعليًا |
| E-7 | `invoice_lines` بلا `event_id` مستقل | لا يمكن تتبع بند منفرد في المزامنة |

---

## 8. كيف تُحدَّث هذه الوثيقة

1. أي هجرة جديدة `V002__*.sql` تُضاف كملف مستقل، ولا تُعدَّل `V001` (البصمة
   تمنع ذلك).
2. تُحدَّث المخططات الأربعة والكتالوج في نفس الالتزام الذي يضيف الهجرة.
3. يمكن استخراج العلاقات آليًا من MariaDB:

```sql
SELECT table_name, column_name, referenced_table_name, referenced_column_name
FROM information_schema.key_column_usage
WHERE table_schema = DATABASE() AND referenced_table_name IS NOT NULL
ORDER BY table_name, ordinal_position;
```

---

## 9. المراجع

- `db/migrations/V001__init.sql` — التعريف الكامل.
- `db/rollback/R001__init.sql` — التراجع.
- `docs/ARCHITECTURE.md` §7 — مبادئ المخطط.
- `docs/DATA_MIGRATION_PLAN.md` — مصدر البيانات القديمة.
- `docs/SYNC_PROTOCOL.md` §5 — جداول المزامنة وسلوكها.
