-- ---------------------------------------------------------------------
-- V006: opening entries can post a line to a customer or supplier.
-- journal_lines.customer_id links the line to customers(id), which also
-- holds suppliers (customers.type = 'supplier').
-- ---------------------------------------------------------------------

ALTER TABLE journal_lines
  ADD COLUMN customer_id BIGINT UNSIGNED NULL AFTER account_id,
  ADD KEY ix_journal_lines_customer (customer_id),
  ADD CONSTRAINT fk_journal_lines_customer FOREIGN KEY (customer_id) REFERENCES customers(id) ON DELETE RESTRICT;
