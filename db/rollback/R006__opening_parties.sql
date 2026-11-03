ALTER TABLE journal_lines
  DROP FOREIGN KEY fk_journal_lines_customer,
  DROP KEY ix_journal_lines_customer,
  DROP COLUMN customer_id;
