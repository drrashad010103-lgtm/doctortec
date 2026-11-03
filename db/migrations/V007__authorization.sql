-- V007: unified authorization codes used by the desktop application.
INSERT INTO permissions(code, description) VALUES
  ('definitions.view',   'عرض البيانات الأساسية والتعريفات'),
  ('definitions.manage', 'إضافة وتعديل وحذف التعريفات'),
  ('company.manage',     'تعديل بيانات الشركة والإعدادات المؤسسية')
ON DUPLICATE KEY UPDATE description=VALUES(description);

-- The system administrator receives every new permission.
INSERT IGNORE INTO role_permissions(role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.code IN ('definitions.view', 'definitions.manage', 'company.manage')
WHERE r.code = 'admin';

-- Branch managers may maintain definitions, but not company identity data.
INSERT IGNORE INTO role_permissions(role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.code IN ('definitions.view', 'definitions.manage')
WHERE r.code = 'manager';

-- Backwards compatibility: whoever could manage settings keeps definition access.
INSERT IGNORE INTO role_permissions(role_id, permission_id)
SELECT rp.role_id, p.id
FROM role_permissions rp
JOIN permissions existing ON existing.id = rp.permission_id AND existing.code = 'settings.manage'
JOIN permissions p ON p.code IN ('definitions.view', 'definitions.manage', 'company.manage');
