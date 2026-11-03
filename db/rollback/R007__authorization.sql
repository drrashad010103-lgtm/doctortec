DELETE rp FROM role_permissions rp
  JOIN permissions p ON p.id = rp.permission_id
 WHERE p.code IN ('definitions.view', 'definitions.manage', 'company.manage');

DELETE FROM permissions
 WHERE code IN ('definitions.view', 'definitions.manage', 'company.manage');
