ALTER TABLE invoices DROP FOREIGN KEY fk_invoices_delivery;
ALTER TABLE invoices DROP FOREIGN KEY fk_invoices_seller;
ALTER TABLE invoices DROP KEY ix_invoices_delivery;
ALTER TABLE invoices DROP KEY ix_invoices_seller;
ALTER TABLE invoices DROP COLUMN delivery_id;
ALTER TABLE invoices DROP COLUMN seller_id;
DROP TABLE IF EXISTS hr_payroll_lines;
DROP TABLE IF EXISTS hr_payroll_runs;
DROP TABLE IF EXISTS hr_employees;
DROP TABLE IF EXISTS hr_sections;
DROP TABLE IF EXISTS hr_departments;
DELETE rp FROM role_permissions rp
  JOIN permissions p ON p.id=rp.permission_id
  WHERE p.code='hr.manage';
DELETE FROM permissions WHERE code='hr.manage';
