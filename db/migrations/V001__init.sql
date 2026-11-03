-- =====================================================================
-- RS Sales — V001 initial schema
-- Target: MariaDB 10.6+ / MySQL 8.0+
-- Charset: utf8mb4 / utf8mb4_unicode_ci
-- All money: DECIMAL. All timestamps: UTC in DATETIME(6).
-- =====================================================================

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS schema_migrations (
  version     VARCHAR(32)  NOT NULL,
  checksum    CHAR(64)     NOT NULL,
  applied_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Identity and configuration
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS branches (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(16)  NOT NULL,
  name        VARCHAR(128) NOT NULL,
  timezone    VARCHAR(64)  NOT NULL DEFAULT 'Africa/Cairo',
  address     VARCHAR(255) NULL,
  phone       VARCHAR(32)  NULL,
  tax_no      VARCHAR(64)  NULL,
  is_active   TINYINT(1)   NOT NULL DEFAULT 1,
  created_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_branches_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS settings (
  k           VARCHAR(100) NOT NULL,
  v           TEXT         NULL,
  value_type  VARCHAR(16)  NOT NULL DEFAULT 'string',
  updated_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  updated_by  BIGINT UNSIGNED NULL,
  PRIMARY KEY (k)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS branch_settings (
  branch_id   BIGINT UNSIGNED NOT NULL,
  k           VARCHAR(100) NOT NULL,
  v           TEXT         NULL,
  updated_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (branch_id, k),
  CONSTRAINT fk_branch_settings_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS roles (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(32)  NOT NULL,
  name        VARCHAR(128) NOT NULL,
  is_active   TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY ux_roles_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS permissions (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(64)  NOT NULL,
  description VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_permissions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS role_permissions (
  role_id       BIGINT UNSIGNED NOT NULL,
  permission_id BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (role_id, permission_id),
  CONSTRAINT fk_rp_role FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE,
  CONSTRAINT fk_rp_perm FOREIGN KEY (permission_id) REFERENCES permissions(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users (
  id                   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username             VARCHAR(64)  NOT NULL,
  password_hash        VARCHAR(255) NOT NULL,
  full_name            VARCHAR(128) NOT NULL,
  is_active            TINYINT(1)   NOT NULL DEFAULT 1,
  must_change_password TINYINT(1)   NOT NULL DEFAULT 0,
  failed_attempts      INT          NOT NULL DEFAULT 0,
  locked_until         DATETIME(6)  NULL,
  created_at           DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at           DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_users_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_roles (
  user_id  BIGINT UNSIGNED NOT NULL,
  role_id  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (user_id, role_id),
  CONSTRAINT fk_ur_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
  CONSTRAINT fk_ur_role FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_branch_access (
  user_id    BIGINT UNSIGNED NOT NULL,
  branch_id  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (user_id, branch_id),
  CONSTRAINT fk_uba_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
  CONSTRAINT fk_uba_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS devices (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  branch_id     BIGINT UNSIGNED NOT NULL,
  name          VARCHAR(128) NOT NULL,
  device_key_hash VARCHAR(255) NOT NULL,
  last_seen_at  DATETIME(6)  NULL,
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_devices_branch (branch_id),
  CONSTRAINT fk_devices_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS document_sequences (
  branch_id  BIGINT UNSIGNED NOT NULL,
  doc_type   VARCHAR(32)  NOT NULL,
  period     CHAR(4)      NOT NULL,
  next_no    BIGINT UNSIGNED NOT NULL DEFAULT 1,
  PRIMARY KEY (branch_id, doc_type, period),
  CONSTRAINT fk_docseq_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Tax
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tax_categories (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code           VARCHAR(32)  NOT NULL,
  name_ar        VARCHAR(128) NOT NULL,
  name_en        VARCHAR(128) NULL,
  rate           DECIMAL(6,3) NOT NULL DEFAULT 0,
  is_exempt      TINYINT(1)   NOT NULL DEFAULT 0,
  is_active      TINYINT(1)   NOT NULL DEFAULT 1,
  effective_from DATE         NOT NULL,
  effective_to   DATE         NULL,
  created_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  updated_by     BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_tax_categories_code (code),
  CONSTRAINT ck_tax_rate CHECK (rate >= 0 AND rate <= 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Items
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_groups (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name       VARCHAR(128) NOT NULL,
  parent_id  BIGINT UNSIGNED NULL,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  KEY ix_item_groups_parent (parent_id),
  CONSTRAINT fk_item_groups_parent FOREIGN KEY (parent_id) REFERENCES item_groups(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS units (
  id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name      VARCHAR(64) NOT NULL,
  is_active TINYINT(1)  NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY ux_units_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS items (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uuid              CHAR(36)     NOT NULL,
  code              VARCHAR(64)  NOT NULL,
  name_ar           VARCHAR(255) NOT NULL,
  name_en           VARCHAR(255) NULL,
  group_id          BIGINT UNSIGNED NULL,
  base_unit_id      BIGINT UNSIGNED NULL,
  cost_price        DECIMAL(18,4) NOT NULL DEFAULT 0,
  sale_price        DECIMAL(18,4) NOT NULL DEFAULT 0,
  wholesale_price   DECIMAL(18,4) NOT NULL DEFAULT 0,
  min_price         DECIMAL(18,4) NOT NULL DEFAULT 0,
  reorder_level     DECIMAL(18,4) NOT NULL DEFAULT 0,
  tax_category_id   BIGINT UNSIGNED NOT NULL,
  is_active         TINYINT(1)   NOT NULL DEFAULT 1,
  row_version       INT          NOT NULL DEFAULT 1,
  created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_items_uuid (uuid),
  UNIQUE KEY ux_items_code (code),
  KEY ix_items_name_ar (name_ar),
  KEY ix_items_group (group_id),
  KEY ix_items_tax (tax_category_id),
  CONSTRAINT fk_items_group FOREIGN KEY (group_id) REFERENCES item_groups(id) ON DELETE RESTRICT,
  CONSTRAINT fk_items_unit FOREIGN KEY (base_unit_id) REFERENCES units(id) ON DELETE RESTRICT,
  CONSTRAINT fk_items_tax FOREIGN KEY (tax_category_id) REFERENCES tax_categories(id) ON DELETE RESTRICT,
  CONSTRAINT ck_items_prices CHECK (cost_price >= 0 AND sale_price >= 0 AND wholesale_price >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS item_barcodes (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  item_id    BIGINT UNSIGNED NOT NULL,
  barcode    VARCHAR(64)  NOT NULL,
  is_primary TINYINT(1)   NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY ux_item_barcodes_barcode (barcode),
  KEY ix_item_barcodes_item (item_id),
  CONSTRAINT fk_item_barcodes_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS item_units (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  item_id    BIGINT UNSIGNED NOT NULL,
  unit_id    BIGINT UNSIGNED NOT NULL,
  factor     DECIMAL(18,4) NOT NULL DEFAULT 1,
  barcode    VARCHAR(64)  NULL,
  sale_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  cost_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY ux_item_units (item_id, unit_id),
  CONSTRAINT fk_item_units_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE,
  CONSTRAINT fk_item_units_unit FOREIGN KEY (unit_id) REFERENCES units(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS item_price_history (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  item_id     BIGINT UNSIGNED NOT NULL,
  price_type  VARCHAR(16)  NOT NULL,
  old_value   DECIMAL(18,4) NOT NULL,
  new_value   DECIMAL(18,4) NOT NULL,
  changed_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  changed_by  BIGINT UNSIGNED NULL,
  reason      VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY ix_item_price_history_item (item_id, changed_at),
  CONSTRAINT fk_item_price_history_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Customers / suppliers
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS customers (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uuid           CHAR(36)     NOT NULL,
  code           VARCHAR(32)  NULL,
  name           VARCHAR(255) NOT NULL,
  type           ENUM('customer','supplier','both') NOT NULL DEFAULT 'customer',
  mobile         VARCHAR(32)  NULL,
  phone          VARCHAR(32)  NULL,
  email          VARCHAR(128) NULL,
  address        VARCHAR(255) NULL,
  tax_no         VARCHAR(64)  NULL,
  credit_limit   DECIMAL(18,2) NOT NULL DEFAULT 0,
  opening_balance DECIMAL(18,2) NOT NULL DEFAULT 0,
  is_active      TINYINT(1)   NOT NULL DEFAULT 1,
  row_version    INT          NOT NULL DEFAULT 1,
  created_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_customers_uuid (uuid),
  KEY ix_customers_name (name),
  KEY ix_customers_mobile (mobile)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Payments / cash / shifts
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payment_methods (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name               VARCHAR(64) NOT NULL,
  requires_reference TINYINT(1)  NOT NULL DEFAULT 0,
  is_active          TINYINT(1)  NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY ux_payment_methods_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cashboxes (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  branch_id  BIGINT UNSIGNED NOT NULL,
  name       VARCHAR(128) NOT NULL,
  account_id BIGINT UNSIGNED NULL,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  KEY ix_cashboxes_branch (branch_id),
  CONSTRAINT fk_cashboxes_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS shifts (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  branch_id      BIGINT UNSIGNED NOT NULL,
  shift_no       VARCHAR(32)  NOT NULL,
  user_id        BIGINT UNSIGNED NOT NULL,
  opened_at      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  closed_at      DATETIME(6)  NULL,
  opening_cash   DECIMAL(18,2) NOT NULL DEFAULT 0,
  expected_cash  DECIMAL(18,2) NOT NULL DEFAULT 0,
  counted_cash   DECIMAL(18,2) NULL,
  variance       DECIMAL(18,2) NULL,
  status         ENUM('open','closed') NOT NULL DEFAULT 'open',
  notes          VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_shifts_no (branch_id, shift_no),
  KEY ix_shifts_branch (branch_id, status),
  CONSTRAINT fk_shifts_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_shifts_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS cash_moves (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NOT NULL,
  cashbox_id  BIGINT UNSIGNED NOT NULL,
  shift_id    BIGINT UNSIGNED NULL,
  direction   ENUM('in','out') NOT NULL,
  amount      DECIMAL(18,2) NOT NULL,
  ref_type    VARCHAR(32)  NULL,
  ref_id      BIGINT UNSIGNED NULL,
  occurred_at DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_cash_moves_event (event_id),
  KEY ix_cash_moves_cashbox (cashbox_id, occurred_at),
  CONSTRAINT fk_cash_moves_cashbox FOREIGN KEY (cashbox_id) REFERENCES cashboxes(id) ON DELETE RESTRICT,
  CONSTRAINT fk_cash_moves_shift FOREIGN KEY (shift_id) REFERENCES shifts(id) ON DELETE RESTRICT,
  CONSTRAINT fk_cash_moves_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Stock
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS stock_moves (
  id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id            CHAR(36)     NOT NULL,
  branch_id           BIGINT UNSIGNED NOT NULL,
  item_id             BIGINT UNSIGNED NOT NULL,
  move_type           ENUM('purchase','purchase_return','sale','sale_return',
                           'opening','adjustment','transfer_in','transfer_out',
                           'count_gain','count_loss') NOT NULL,
  qty_delta           DECIMAL(18,4) NOT NULL,
  unit_cost           DECIMAL(18,4) NOT NULL DEFAULT 0,
  ref_type            VARCHAR(32)  NULL,
  ref_id              BIGINT UNSIGNED NULL,
  line_id             BIGINT UNSIGNED NULL,
  occurred_at         DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  business_date       DATE         NOT NULL,
  created_by          BIGINT UNSIGNED NOT NULL,
  device_id           BIGINT UNSIGNED NULL,
  reversal_of_event_id CHAR(36)    NULL,
  notes               VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_stock_moves_event (event_id),
  KEY ix_stock_moves_branch_item (branch_id, item_id, occurred_at),
  KEY ix_stock_moves_ref (ref_type, ref_id),
  KEY ix_stock_moves_date (business_date),
  CONSTRAINT fk_stock_moves_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_stock_moves_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT,
  CONSTRAINT fk_stock_moves_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stock_balances (
  branch_id    BIGINT UNSIGNED NOT NULL,
  item_id      BIGINT UNSIGNED NOT NULL,
  qty_on_hand  DECIMAL(18,4) NOT NULL DEFAULT 0,
  last_cost    DECIMAL(18,4) NOT NULL DEFAULT 0,
  updated_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (branch_id, item_id),
  KEY ix_stock_balances_item (item_id),
  CONSTRAINT fk_stock_balances_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_stock_balances_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stock_transfers (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NOT NULL,
  transfer_no VARCHAR(40)  NOT NULL,
  from_branch BIGINT UNSIGNED NOT NULL,
  to_branch   BIGINT UNSIGNED NOT NULL,
  status      ENUM('draft','sent','received','cancelled') NOT NULL DEFAULT 'draft',
  created_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  posted_at   DATETIME(6)  NULL,
  received_at DATETIME(6)  NULL,
  created_by  BIGINT UNSIGNED NOT NULL,
  notes       VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_stock_transfers_event (event_id),
  UNIQUE KEY ux_stock_transfers_no (transfer_no),
  CONSTRAINT fk_transfers_from FOREIGN KEY (from_branch) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_transfers_to FOREIGN KEY (to_branch) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_transfers_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS stock_transfer_lines (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  transfer_id BIGINT UNSIGNED NOT NULL,
  item_id     BIGINT UNSIGNED NOT NULL,
  qty         DECIMAL(18,4) NOT NULL,
  unit_cost   DECIMAL(18,4) NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  KEY ix_transfer_lines_transfer (transfer_id),
  CONSTRAINT fk_transfer_lines_transfer FOREIGN KEY (transfer_id) REFERENCES stock_transfers(id) ON DELETE CASCADE,
  CONSTRAINT fk_transfer_lines_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT,
  CONSTRAINT ck_transfer_qty CHECK (qty > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Invoices
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS invoices (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id         CHAR(36)     NOT NULL,
  branch_id        BIGINT UNSIGNED NOT NULL,
  invoice_no       VARCHAR(40)  NOT NULL,
  invoice_type     ENUM('sale','sale_return','purchase','purchase_return','opening') NOT NULL,
  status           ENUM('draft','posted','voided') NOT NULL DEFAULT 'posted',
  customer_id      BIGINT UNSIGNED NULL,
  original_invoice_id BIGINT UNSIGNED NULL,
  shift_id         BIGINT UNSIGNED NULL,
  subtotal         DECIMAL(18,2) NOT NULL DEFAULT 0,
  line_discount    DECIMAL(18,2) NOT NULL DEFAULT 0,
  header_discount  DECIMAL(18,2) NOT NULL DEFAULT 0,
  tax_base_total   DECIMAL(18,2) NOT NULL DEFAULT 0,
  tax_total        DECIMAL(18,2) NOT NULL DEFAULT 0,
  total            DECIMAL(18,2) NOT NULL DEFAULT 0,
  paid             DECIMAL(18,2) NOT NULL DEFAULT 0,
  due              DECIMAL(18,2) NOT NULL DEFAULT 0,
  payment_status   ENUM('unpaid','partial','paid') NOT NULL DEFAULT 'unpaid',
  price_mode       ENUM('inclusive','exclusive') NOT NULL DEFAULT 'inclusive',
  seller_tax_no    VARCHAR(64)  NULL,
  buyer_tax_no     VARCHAR(64)  NULL,
  buyer_name       VARCHAR(255) NULL,
  buyer_address    VARCHAR(255) NULL,
  eta_uuid         CHAR(36)     NULL,
  eta_status       ENUM('not_required','pending','submitted','accepted','rejected') NOT NULL DEFAULT 'not_required',
  eta_submitted_at DATETIME(6)  NULL,
  qr_payload       TEXT         NULL,
  occurred_at      DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  business_date    DATE         NOT NULL,
  created_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  posted_at        DATETIME(6)  NULL,
  voided_at        DATETIME(6)  NULL,
  void_reason      VARCHAR(255) NULL,
  voided_by        BIGINT UNSIGNED NULL,
  created_by       BIGINT UNSIGNED NOT NULL,
  device_id        BIGINT UNSIGNED NULL,
  notes            VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_invoices_event (event_id),
  UNIQUE KEY ux_invoices_no (branch_id, invoice_type, invoice_no),
  KEY ix_invoices_branch_date (branch_id, business_date),
  KEY ix_invoices_customer (customer_id),
  KEY ix_invoices_status (status, invoice_type),
  KEY ix_invoices_original (original_invoice_id),
  CONSTRAINT fk_invoices_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoices_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoices_original FOREIGN KEY (original_invoice_id) REFERENCES invoices(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoices_shift FOREIGN KEY (shift_id) REFERENCES shifts(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoices_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS invoice_lines (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  invoice_id         BIGINT UNSIGNED NOT NULL,
  item_id            BIGINT UNSIGNED NOT NULL,
  unit_id            BIGINT UNSIGNED NULL,
  qty                DECIMAL(18,4) NOT NULL,
  bonus_qty          DECIMAL(18,4) NOT NULL DEFAULT 0,
  unit_price         DECIMAL(18,4) NOT NULL,
  discount           DECIMAL(18,2) NOT NULL DEFAULT 0,
  tax_category_id    BIGINT UNSIGNED NOT NULL,
  tax_rate           DECIMAL(6,3) NOT NULL DEFAULT 0,
  tax_base           DECIMAL(18,2) NOT NULL DEFAULT 0,
  tax_amount         DECIMAL(18,2) NOT NULL DEFAULT 0,
  price_mode         ENUM('inclusive','exclusive') NOT NULL DEFAULT 'inclusive',
  line_total         DECIMAL(18,2) NOT NULL,
  cost_at_sale       DECIMAL(18,4) NOT NULL DEFAULT 0,
  cost_at_sale_incl_tax DECIMAL(18,4) NOT NULL DEFAULT 0,
  original_line_id   BIGINT UNSIGNED NULL,
  batch_no           VARCHAR(64)  NULL,
  expiry_date        DATE         NULL,
  notes              VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY ix_invoice_lines_invoice (invoice_id),
  KEY ix_invoice_lines_item (item_id),
  CONSTRAINT fk_invoice_lines_invoice FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE,
  CONSTRAINT fk_invoice_lines_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoice_lines_tax FOREIGN KEY (tax_category_id) REFERENCES tax_categories(id) ON DELETE RESTRICT,
  CONSTRAINT fk_invoice_lines_orig FOREIGN KEY (original_line_id) REFERENCES invoice_lines(id) ON DELETE RESTRICT,
  CONSTRAINT ck_invoice_lines_qty CHECK (qty <> 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payments (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NOT NULL,
  invoice_id  BIGINT UNSIGNED NULL,
  branch_id   BIGINT UNSIGNED NOT NULL,
  method_id   BIGINT UNSIGNED NOT NULL,
  amount      DECIMAL(18,2) NOT NULL,
  reference   VARCHAR(128) NULL,
  bank_id     BIGINT UNSIGNED NULL,
  paid_at     DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_payments_event (event_id),
  KEY ix_payments_invoice (invoice_id),
  KEY ix_payments_branch_date (branch_id, paid_at),
  CONSTRAINT fk_payments_invoice FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE RESTRICT,
  CONSTRAINT fk_payments_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_payments_method FOREIGN KEY (method_id) REFERENCES payment_methods(id) ON DELETE RESTRICT,
  CONSTRAINT fk_payments_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Accounting
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS accounts (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code        VARCHAR(32)  NOT NULL,
  name        VARCHAR(128) NOT NULL,
  parent_id   BIGINT UNSIGNED NULL,
  nature      ENUM('asset','liability','equity','revenue','expense') NOT NULL,
  level       INT          NOT NULL DEFAULT 1,
  is_postable TINYINT(1)   NOT NULL DEFAULT 1,
  is_active   TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY ux_accounts_code (code),
  KEY ix_accounts_parent (parent_id),
  CONSTRAINT fk_accounts_parent FOREIGN KEY (parent_id) REFERENCES accounts(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS journal_entries (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NOT NULL,
  branch_id   BIGINT UNSIGNED NOT NULL,
  entry_no    VARCHAR(40)  NOT NULL,
  entry_date  DATE         NOT NULL,
  description VARCHAR(255) NOT NULL,
  source_type VARCHAR(32)  NULL,
  source_id   BIGINT UNSIGNED NULL,
  status      ENUM('draft','posted','voided') NOT NULL DEFAULT 'posted',
  created_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  posted_at   DATETIME(6)  NULL,
  created_by  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_journal_entries_event (event_id),
  UNIQUE KEY ux_journal_entries_no (branch_id, entry_no),
  KEY ix_journal_entries_date (branch_id, entry_date),
  KEY ix_journal_entries_source (source_type, source_id),
  CONSTRAINT fk_journal_entries_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_journal_entries_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS journal_lines (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  entry_id    BIGINT UNSIGNED NOT NULL,
  account_id  BIGINT UNSIGNED NOT NULL,
  debit       DECIMAL(18,2) NOT NULL DEFAULT 0,
  credit      DECIMAL(18,2) NOT NULL DEFAULT 0,
  description VARCHAR(255) NULL,
  PRIMARY KEY (id),
  KEY ix_journal_lines_entry (entry_id),
  KEY ix_journal_lines_account (account_id),
  CONSTRAINT fk_journal_lines_entry FOREIGN KEY (entry_id) REFERENCES journal_entries(id) ON DELETE CASCADE,
  CONSTRAINT fk_journal_lines_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
  CONSTRAINT ck_journal_line_sign CHECK ((debit >= 0 AND credit >= 0) AND (debit > 0 OR credit > 0))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Loyalty / coupons (kept for phase 2, empty initially)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS loyalty_accounts (
  customer_id    BIGINT UNSIGNED NOT NULL,
  points         DECIMAL(18,2) NOT NULL DEFAULT 0,
  total_earned   DECIMAL(18,2) NOT NULL DEFAULT 0,
  total_redeemed DECIMAL(18,2) NOT NULL DEFAULT 0,
  updated_at     DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (customer_id),
  CONSTRAINT fk_loyalty_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS loyalty_transactions (
  id           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id     CHAR(36)     NOT NULL,
  customer_id  BIGINT UNSIGNED NOT NULL,
  invoice_id   BIGINT UNSIGNED NULL,
  points_delta DECIMAL(18,2) NOT NULL,
  type         ENUM('earn','redeem','adjust','expire') NOT NULL,
  note         VARCHAR(255) NULL,
  created_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by   BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_loyalty_tx_event (event_id),
  KEY ix_loyalty_tx_customer (customer_id, created_at),
  CONSTRAINT fk_loyalty_tx_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE CASCADE,
  CONSTRAINT fk_loyalty_tx_invoice FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS coupons (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  code           VARCHAR(64)  NOT NULL,
  name           VARCHAR(128) NOT NULL,
  discount_type  ENUM('percent','amount') NOT NULL,
  discount_value DECIMAL(18,2) NOT NULL,
  starts_at      DATETIME(6)  NULL,
  ends_at        DATETIME(6)  NULL,
  max_uses       INT          NOT NULL DEFAULT 0,
  used_count     INT          NOT NULL DEFAULT 0,
  is_active      TINYINT(1)   NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY ux_coupons_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS coupon_redemptions (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  coupon_id   BIGINT UNSIGNED NOT NULL,
  invoice_id  BIGINT UNSIGNED NOT NULL,
  customer_id BIGINT UNSIGNED NULL,
  amount      DECIMAL(18,2) NOT NULL,
  redeemed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_coupon_redemption_invoice (invoice_id),
  CONSTRAINT fk_coupon_redemption_coupon FOREIGN KEY (coupon_id) REFERENCES coupons(id) ON DELETE RESTRICT,
  CONSTRAINT fk_coupon_redemption_invoice FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Inventory counts
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS inventory_counts (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NOT NULL,
  branch_id   BIGINT UNSIGNED NOT NULL,
  count_no    VARCHAR(40)  NOT NULL,
  status      ENUM('draft','posted','cancelled') NOT NULL DEFAULT 'draft',
  started_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  posted_at   DATETIME(6)  NULL,
  created_by  BIGINT UNSIGNED NOT NULL,
  notes       VARCHAR(255) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_counts_event (event_id),
  UNIQUE KEY ux_counts_no (branch_id, count_no),
  CONSTRAINT fk_counts_branch FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE RESTRICT,
  CONSTRAINT fk_counts_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS inventory_count_lines (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  count_id    BIGINT UNSIGNED NOT NULL,
  item_id     BIGINT UNSIGNED NOT NULL,
  system_qty  DECIMAL(18,4) NOT NULL,
  counted_qty DECIMAL(18,4) NOT NULL,
  variance_qty DECIMAL(18,4) NOT NULL,
  unit_cost   DECIMAL(18,4) NOT NULL DEFAULT 0,
  variance_value DECIMAL(18,2) NOT NULL DEFAULT 0,
  PRIMARY KEY (id),
  UNIQUE KEY ux_count_lines (count_id, item_id),
  CONSTRAINT fk_count_lines_count FOREIGN KEY (count_id) REFERENCES inventory_counts(id) ON DELETE CASCADE,
  CONSTRAINT fk_count_lines_item FOREIGN KEY (item_id) REFERENCES items(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Audit and sync
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit_log (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event_id    CHAR(36)     NULL,
  branch_id   BIGINT UNSIGNED NULL,
  user_id     BIGINT UNSIGNED NULL,
  username    VARCHAR(64)  NULL,
  action      VARCHAR(64)  NOT NULL,
  entity_type VARCHAR(64)  NOT NULL,
  entity_id   VARCHAR(64)  NULL,
  before_json LONGTEXT     NULL,
  after_json  LONGTEXT     NULL,
  ip          VARCHAR(64)  NULL,
  device_id   BIGINT UNSIGNED NULL,
  created_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_audit_created (created_at),
  KEY ix_audit_entity (entity_type, entity_id),
  KEY ix_audit_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_outbox (
  event_id          CHAR(36)     NOT NULL,
  branch_id         BIGINT UNSIGNED NOT NULL,
  device_id         BIGINT UNSIGNED NULL,
  entity_type       VARCHAR(64)  NOT NULL,
  entity_id         VARCHAR(64)  NOT NULL,
  event_type        VARCHAR(64)  NOT NULL,
  aggregate_version INT          NOT NULL DEFAULT 1,
  payload           LONGTEXT     NOT NULL,
  occurred_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  status            ENUM('pending','sending','sent','acked','failed','dead') NOT NULL DEFAULT 'pending',
  attempts          INT          NOT NULL DEFAULT 0,
  next_attempt_at   DATETIME(6)  NULL,
  last_error        TEXT         NULL,
  created_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  sent_at           DATETIME(6)  NULL,
  acked_at          DATETIME(6)  NULL,
  PRIMARY KEY (event_id),
  KEY ix_outbox_pending (status, next_attempt_at),
  KEY ix_outbox_branch (branch_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_inbox (
  event_id    CHAR(36)     NOT NULL,
  branch_id   BIGINT UNSIGNED NOT NULL,
  entity_type VARCHAR(64)  NOT NULL,
  entity_id   VARCHAR(64)  NOT NULL,
  event_type  VARCHAR(64)  NOT NULL,
  payload     LONGTEXT     NOT NULL,
  received_at DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  applied_at  DATETIME(6)  NULL,
  result      ENUM('applied','duplicate','conflict','rejected') NOT NULL,
  error       TEXT         NULL,
  PRIMARY KEY (event_id),
  KEY ix_inbox_branch (branch_id, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_cursors (
  branch_id    BIGINT UNSIGNED NOT NULL,
  stream       VARCHAR(64)  NOT NULL,
  last_seq     BIGINT UNSIGNED NOT NULL DEFAULT 0,
  updated_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (branch_id, stream)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS change_log (
  seq         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  entity_type VARCHAR(64)  NOT NULL,
  entity_id   VARCHAR(64)  NOT NULL,
  op          ENUM('upsert','delete') NOT NULL,
  payload     LONGTEXT     NULL,
  branch_id   BIGINT UNSIGNED NULL,
  changed_at  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (seq),
  KEY ix_change_log_entity (entity_type, entity_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Helper view: stock with item data
-- ---------------------------------------------------------------------
CREATE OR REPLACE VIEW v_stock AS
SELECT b.id            AS branch_id,
       i.id            AS item_id,
       i.code,
       i.name_ar,
       i.name_en,
       i.sale_price,
       i.cost_price,
       i.reorder_level,
       COALESCE(sb.qty_on_hand, 0) AS qty_on_hand,
       COALESCE(sb.last_cost, 0)   AS last_cost
FROM items i
CROSS JOIN branches b
LEFT JOIN stock_balances sb ON sb.item_id = i.id AND sb.branch_id = b.id
WHERE i.is_active = 1 AND b.is_active = 1;
