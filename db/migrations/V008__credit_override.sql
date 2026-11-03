-- V008: explicit permission to override a customer's credit limit.
-- Keeping this separate from sales.discount makes the audit trail clearer.
INSERT INTO permissions(code, description)
VALUES ('sales.credit_override', 'تجاوز سقف ائتمان العميل بموافقة إدارية')
ON DUPLICATE KEY UPDATE description=VALUES(description);

INSERT IGNORE INTO role_permissions(role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.code='sales.credit_override'
WHERE r.code IN ('admin', 'manager');
