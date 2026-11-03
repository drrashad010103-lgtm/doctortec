DROP TABLE IF EXISTS purchase_voucher_redemptions;
DROP TABLE IF EXISTS purchase_vouchers;
ALTER TABLE customers DROP FOREIGN KEY fk_customers_group;
ALTER TABLE customers DROP KEY ix_customers_group;
ALTER TABLE customers DROP COLUMN group_id;
DROP TABLE IF EXISTS customer_groups;
