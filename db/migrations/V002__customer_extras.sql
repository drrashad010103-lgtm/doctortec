-- ---------------------------------------------------------------------
-- V002: customer groups, purchase vouchers and redemption log
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS customer_groups (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name             VARCHAR(128) NOT NULL,
  discount_percent DECIMAL(5,2) NOT NULL DEFAULT 0,
  notes            VARCHAR(255) NULL,
  is_active        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_customer_groups_name (name),
  CONSTRAINT ck_customer_groups_discount CHECK (discount_percent >= 0 AND discount_percent <= 100)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE customers
  ADD COLUMN group_id BIGINT UNSIGNED NULL AFTER type,
  ADD KEY ix_customers_group (group_id),
  ADD CONSTRAINT fk_customers_group FOREIGN KEY (group_id) REFERENCES customer_groups(id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS purchase_vouchers (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uuid             CHAR(36)     NOT NULL,
  code             VARCHAR(32)  NOT NULL,
  customer_id      BIGINT UNSIGNED NULL,
  amount           DECIMAL(18,2) NOT NULL,
  remaining_amount DECIMAL(18,2) NOT NULL,
  issued_at        DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  expires_at       DATETIME(6)  NULL,
  is_active        TINYINT(1)   NOT NULL DEFAULT 1,
  notes            VARCHAR(255) NULL,
  created_by       BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_purchase_vouchers_uuid (uuid),
  UNIQUE KEY ux_purchase_vouchers_code (code),
  KEY ix_purchase_vouchers_customer (customer_id),
  CONSTRAINT fk_purchase_vouchers_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE SET NULL,
  CONSTRAINT fk_purchase_vouchers_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT,
  CONSTRAINT ck_purchase_vouchers_amount CHECK (amount > 0 AND remaining_amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS purchase_voucher_redemptions (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  voucher_id  BIGINT UNSIGNED NOT NULL,
  invoice_id  BIGINT UNSIGNED NULL,
  amount      DECIMAL(18,2) NOT NULL,
  note        VARCHAR(255) NULL,
  redeemed_at DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  created_by  BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  KEY ix_purchase_voucher_redemptions_voucher (voucher_id, redeemed_at),
  CONSTRAINT fk_purchase_voucher_redemptions_voucher FOREIGN KEY (voucher_id) REFERENCES purchase_vouchers(id) ON DELETE CASCADE,
  CONSTRAINT fk_purchase_voucher_redemptions_invoice FOREIGN KEY (invoice_id) REFERENCES invoices(id) ON DELETE SET NULL,
  CONSTRAINT fk_purchase_voucher_redemptions_user FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT,
  CONSTRAINT ck_purchase_voucher_redemptions_amount CHECK (amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
