using MySqlConnector;

namespace RsSales.Server.Sync;

/// <summary>
/// Durable, idempotent server-side sync store. Every pushed event is written
/// once keyed by event_id; master-data events are appended to a change log
/// that terminals pull from. Each event is committed in its own transaction so
/// one bad row cannot roll back the whole batch.
/// </summary>
public sealed class SyncServerStore
{
    private const int MaxPayloadChars = 1_000_000;

    private readonly string _connectionString;

    public SyncServerStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A sync connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public SyncEventAck Record(SyncEvent e, long? deviceId)
    {
        var ack = new SyncEventAck { EventId = e.EventId };

        var problem = Validate(e);
        if (problem is not null)
        {
            ack.Status = "rejected";
            ack.Error = problem;
            return ack;
        }

        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var inserted = InsertEvent(connection, transaction, e, deviceId);
        if (!inserted)
        {
            transaction.Rollback();
            ack.Status = "duplicate";
            return ack;
        }

        if (IsMasterData(e.EntityType))
        {
            InsertChange(connection, transaction, e);
        }

        transaction.Commit();
        ack.Status = "acked";
        return ack;
    }

    public PullResponse Pull(long afterSeq, int limit, long branchId)
    {
        var response = new PullResponse { NextSeq = afterSeq };
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT seq, entity_type, entity_id, op, payload " +
            "FROM server_change_log " +
            "WHERE seq > @after AND (origin_branch_id IS NULL OR origin_branch_id <> @branch) " +
            "ORDER BY seq LIMIT @limit";
        command.Parameters.AddWithValue("@after", afterSeq);
        command.Parameters.AddWithValue("@branch", branchId);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var seq = reader.GetInt64(0);
            response.Changes.Add(new PullChange
            {
                Seq = seq,
                EntityType = reader.GetString(1),
                EntityId = reader.GetString(2),
                Op = reader.GetString(3),
                Payload = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
            response.NextSeq = seq;
        }

        return response;
    }

    public static bool IsMasterData(string entityType) =>
        entityType.Equals("item", StringComparison.OrdinalIgnoreCase) ||
        entityType.Equals("customer", StringComparison.OrdinalIgnoreCase);

    private static string? Validate(SyncEvent e)
    {
        if (string.IsNullOrWhiteSpace(e.EventId)) return "event_id is required.";
        if (e.EventId.Length > 36) return "event_id must be at most 36 characters.";
        if (e.BranchId <= 0) return "branch_id must be a positive branch id.";
        if (string.IsNullOrWhiteSpace(e.EntityType)) return "entity_type is required.";
        if (e.EntityType.Length > 64) return "entity_type is too long.";
        if (string.IsNullOrWhiteSpace(e.EntityId)) return "entity_id is required.";
        if (e.EntityId.Length > 64) return "entity_id is too long.";
        if (string.IsNullOrWhiteSpace(e.EventType)) return "event_type is required.";
        if (e.EventType.Length > 64) return "event_type is too long.";
        if (e.Payload is null) return "payload is required.";
        if (e.Payload.Length > MaxPayloadChars) return "payload is too large.";
        return null;
    }

    private static bool InsertEvent(MySqlConnection connection, MySqlTransaction transaction,
                                    SyncEvent e, long? deviceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT IGNORE INTO server_sync_events(event_id, branch_id, device_id, entity_type, entity_id, " +
            "event_type, payload, result) VALUES(@id, @branch, @device, @type, @entity, @event, @payload, 'acked')";
        command.Parameters.AddWithValue("@id", e.EventId);
        command.Parameters.AddWithValue("@branch", e.BranchId);
        command.Parameters.AddWithValue("@device", (object?)deviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@type", e.EntityType);
        command.Parameters.AddWithValue("@entity", e.EntityId);
        command.Parameters.AddWithValue("@event", e.EventType);
        command.Parameters.AddWithValue("@payload", e.Payload);
        return command.ExecuteNonQuery() > 0;
    }

    private static void InsertChange(MySqlConnection connection, MySqlTransaction transaction,
                                     SyncEvent e)
    {
        var op = IsDelete(e.EventType) ? "delete" : "upsert";

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO server_change_log(origin_branch_id, entity_type, entity_id, op, payload) " +
            "VALUES(@branch, @type, @entity, @op, @payload)";
        command.Parameters.AddWithValue("@branch", e.BranchId);
        command.Parameters.AddWithValue("@type", e.EntityType);
        command.Parameters.AddWithValue("@entity", e.EntityId);
        command.Parameters.AddWithValue("@op", op);
        command.Parameters.AddWithValue("@payload", e.Payload);
        command.ExecuteNonQuery();
    }

    private static bool IsDelete(string eventType) =>
        eventType.EndsWith(".deleted", StringComparison.OrdinalIgnoreCase) ||
        eventType.EndsWith(".removed", StringComparison.OrdinalIgnoreCase) ||
        eventType.Equals("delete", StringComparison.OrdinalIgnoreCase) ||
        eventType.Equals("item.deleted", StringComparison.OrdinalIgnoreCase);
}
