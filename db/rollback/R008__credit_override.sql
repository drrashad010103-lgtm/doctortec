DELETE rp FROM role_permissions rp
  JOIN permissions p ON p.id = rp.permission_id
 WHERE p.code = 'sales.credit_override';

DELETE FROM permissions WHERE code = 'sales.credit_override';
