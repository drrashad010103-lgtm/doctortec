-- V009: durable, idempotent storage for the central sync API.
-- server_sync_events records every pushed event exactly once (event_id is the
-- idempotency key). server_change_log is the stream terminals pull from; the
-- origin branch is stored so a terminal never receives its own changes back.

CREATE TABLE IF NOT EXISTS server_sync_events (
  event_id      CHAR(36)     NOT NULL,
  branch_id     BIGINT UNSIGNED NOT NULL,
  device_id     BIGINT UNSIGNED NULL,
  entity_type   VARCHAR(64)  NOT NULL,
  entity_id     VARCHAR(64)  NOT NULL,
  event_type    VARCHAR(64)  NOT NULL,
  payload       LONGTEXT     NOT NULL,
  result        ENUM('acked','duplicate','rejected','conflict') NOT NULL DEFAULT 'acked',
  error         TEXT         NULL,
  received_at   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (event_id),
  KEY ix_server_events_branch (branch_id, received_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS server_change_log (
  seq              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  origin_branch_id BIGINT UNSIGNED NULL,
  entity_type      VARCHAR(64)  NOT NULL,
  entity_id        VARCHAR(64)  NOT NULL,
  op               ENUM('upsert','delete') NOT NULL,
  payload          LONGTEXT     NULL,
  changed_at       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (seq),
  KEY ix_server_changes_entity (entity_type, entity_id),
  KEY ix_server_changes_origin (origin_branch_id, seq)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
