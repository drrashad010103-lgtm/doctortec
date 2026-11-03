-- ---------------------------------------------------------------------
-- V003: HR departments, sections, employees, payroll and commissions
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS hr_departments (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  name       VARCHAR(128) NOT NULL,
  notes      VARCHAR(255) NULL,
  is_active  TINYINT(1)   NOT NULL DEFAULT 1,
  created_at DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_hr_departments_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hr_sections (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  department_id BIGINT UNSIGNED NOT NULL,
  name          VARCHAR(128) NOT NULL,
  notes         VARCHAR(255) NULL,
  is_active     TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at    DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_hr_sections_department_name (department_id, name),
  KEY ix_hr_sections_department (department_id),
  CONSTRAINT fk_hr_sections_department FOREIGN KEY (department_id) REFERENCES hr_departments(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hr_employees (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uuid               CHAR(36)        NOT NULL,
  code               VARCHAR(32)     NOT NULL,
  name               VARCHAR(128)    NOT NULL,
  employee_type      ENUM('employee','seller','delivery') NOT NULL DEFAULT 'employee',
  department_id      BIGINT UNSIGNED NULL,
  section_id         BIGINT UNSIGNED NULL,
  mobile             VARCHAR(32)     NULL,
  phone              VARCHAR(32)     NULL,
  email              VARCHAR(128)    NULL,
  address            VARCHAR(255)    NULL,
  hire_date          DATE            NULL,
  base_salary        DECIMAL(18,2)   NOT NULL DEFAULT 0,
  commission_percent DECIMAL(5,2)    NOT NULL DEFAULT 0,
  delivery_fee       DECIMAL(18,2)   NOT NULL DEFAULT 0,
  notes              VARCHAR(255)    NULL,
  is_active          TINYINT(1)      NOT NULL DEFAULT 1,
  created_at         DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at         DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_hr_employees_uuid (uuid),
  UNIQUE KEY ux_hr_employees_code (code),
  KEY ix_hr_employees_department (department_id),
  KEY ix_hr_employees_section (section_id),
  KEY ix_hr_employees_type (employee_type),
  CONSTRAINT fk_hr_employees_department FOREIGN KEY (department_id) REFERENCES hr_departments(id) ON DELETE SET NULL,
  CONSTRAINT fk_hr_employees_section FOREIGN KEY (section_id) REFERENCES hr_sections(id) ON DELETE SET NULL,
  CONSTRAINT ck_hr_employees_salary CHECK (base_salary >= 0),
  CONSTRAINT ck_hr_employees_commission CHECK (commission_percent >= 0 AND commission_percent <= 100),
  CONSTRAINT ck_hr_employees_delivery_fee CHECK (delivery_fee >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hr_payroll_runs (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uuid              CHAR(36)        NOT NULL,
  period_year       SMALLINT UNSIGNED NOT NULL,
  period_month      TINYINT UNSIGNED  NOT NULL,
  status            ENUM('draft','paid','cancelled') NOT NULL DEFAULT 'draft',
  employee_count    INT             NOT NULL DEFAULT 0,
  total_base        DECIMAL(18,2)   NOT NULL DEFAULT 0,
  total_commission  DECIMAL(18,2)   NOT NULL DEFAULT 0,
  total_deductions  DECIMAL(18,2)   NOT NULL DEFAULT 0,
  total_bonus       DECIMAL(18,2)   NOT NULL DEFAULT 0,
  total_net         DECIMAL(18,2)   NOT NULL DEFAULT 0,
  notes             VARCHAR(255)    NULL,
  created_by        BIGINT UNSIGNED NOT NULL,
  created_at        DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  paid_at           DATETIME(6)     NULL,
  paid_by           BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_hr_payroll_runs_uuid (uuid),
  UNIQUE KEY ux_hr_payroll_runs_period (period_year, period_month),
  KEY ix_hr_payroll_runs_status (status),
  CONSTRAINT fk_hr_payroll_runs_created_by FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE RESTRICT,
  CONSTRAINT fk_hr_payroll_runs_paid_by FOREIGN KEY (paid_by) REFERENCES users(id) ON DELETE SET NULL,
  CONSTRAINT ck_hr_payroll_runs_period CHECK (period_month BETWEEN 1 AND 12)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS hr_payroll_lines (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  run_id            BIGINT UNSIGNED NOT NULL,
  employee_id       BIGINT UNSIGNED NOT NULL,
  employee_code     VARCHAR(32)     NOT NULL,
  employee_name     VARCHAR(128)    NOT NULL,
  employee_type     ENUM('employee','seller','delivery') NOT NULL,
  department_name   VARCHAR(128)    NULL,
  base_salary       DECIMAL(18,2)   NOT NULL DEFAULT 0,
  commission_amount DECIMAL(18,2)   NOT NULL DEFAULT 0,
  deductions        DECIMAL(18,2)   NOT NULL DEFAULT 0,
  bonus             DECIMAL(18,2)   NOT NULL DEFAULT 0,
  net_amount        DECIMAL(18,2)   NOT NULL DEFAULT 0,
  notes             VARCHAR(255)    NULL,
  updated_at        DATETIME(6)     NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  UNIQUE KEY ux_hr_payroll_lines_employee (run_id, employee_id),
  KEY ix_hr_payroll_lines_employee (employee_id),
  CONSTRAINT fk_hr_payroll_lines_run FOREIGN KEY (run_id) REFERENCES hr_payroll_runs(id) ON DELETE CASCADE,
  CONSTRAINT fk_hr_payroll_lines_employee FOREIGN KEY (employee_id) REFERENCES hr_employees(id) ON DELETE RESTRICT,
  CONSTRAINT ck_hr_payroll_lines_deductions CHECK (deductions >= 0),
  CONSTRAINT ck_hr_payroll_lines_bonus CHECK (bonus >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE invoices
  ADD COLUMN seller_id   BIGINT UNSIGNED NULL AFTER customer_id,
  ADD COLUMN delivery_id BIGINT UNSIGNED NULL AFTER seller_id,
  ADD KEY ix_invoices_seller (seller_id),
  ADD KEY ix_invoices_delivery (delivery_id),
  ADD CONSTRAINT fk_invoices_seller FOREIGN KEY (seller_id) REFERENCES hr_employees(id) ON DELETE SET NULL,
  ADD CONSTRAINT fk_invoices_delivery FOREIGN KEY (delivery_id) REFERENCES hr_employees(id) ON DELETE SET NULL;

INSERT INTO permissions(code, description)
VALUES('hr.manage', 'إدارة الموظفين والإدارات ومسيرات الرواتب')
ON DUPLICATE KEY UPDATE description=VALUES(description);

-- Grant the new permission to administrator-like roles that already manage users.
INSERT IGNORE INTO role_permissions(role_id, permission_id)
SELECT DISTINCT r.id, p.id
FROM roles r
JOIN permissions p ON p.code='hr.manage'
WHERE r.code IN ('admin','manager')
   OR EXISTS (SELECT 1 FROM role_permissions rp
              JOIN permissions up ON up.id=rp.permission_id AND up.code='users.manage'
              WHERE rp.role_id=r.id);
