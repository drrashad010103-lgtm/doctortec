Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Security.Cryptography
Imports System.Text
Imports Dapper

''' <summary>
''' Outbox/inbox persistence. Nothing is deleted from the outbox before the
''' central server acknowledges it (sync protocol requirement).
''' </summary>
Public NotInheritable Class SyncStore
    Private Sub New()
    End Sub

    Public Shared Sub Enqueue(conn As IDbConnection, tx As IDbTransaction,
                              eventId As String, branchId As Long, deviceId As Long?,
                              entityType As String, entityId As String, eventType As String,
                              aggregateVersion As Integer, payload As String, occurredAt As Date)
        conn.Execute(
            "INSERT INTO sync_outbox(event_id, branch_id, device_id, entity_type, entity_id, event_type, " &
            "aggregate_version, payload, occurred_at, status, next_attempt_at) " &
            "VALUES(@eventId, @branchId, @deviceId, @entityType, @entityId, @eventType, @version, " &
            "@payload, @occurredAt, 'pending', @occurredAt) " &
            "ON DUPLICATE KEY UPDATE payload=VALUES(payload)",
            New With {
                .eventId = eventId, .branchId = branchId, .deviceId = deviceId, .entityType = entityType,
                .entityId = entityId, .eventType = eventType, .version = aggregateVersion,
                .payload = payload, .occurredAt = occurredAt
            }, tx)
    End Sub

    ''' <summary>Claims a batch for sending. Call inside a transaction.</summary>
    Public Shared Function ClaimBatch(conn As IDbConnection, tx As IDbTransaction,
                                      Optional batchSize As Integer = 50) As List(Of OutboxRecord)
        conn.Execute(
            "UPDATE sync_outbox SET status='sending', attempts=attempts+1 " &
            "WHERE status IN ('pending','failed') " &
            "  AND (next_attempt_at IS NULL OR next_attempt_at <= UTC_TIMESTAMP(6)) " &
            "ORDER BY occurred_at, event_id LIMIT @limit",
            New With {.limit = batchSize}, tx)

        Return conn.Query(Of OutboxRecord)(
            "SELECT event_id, branch_id, entity_type, entity_id, event_type, payload, attempts " &
            "FROM sync_outbox WHERE status='sending' ORDER BY occurred_at, event_id LIMIT @limit",
            New With {.limit = batchSize}, tx).AsList()
    End Function

    Public Shared Sub MarkAcked(conn As IDbConnection, tx As IDbTransaction, eventId As String)
        conn.Execute(
            "UPDATE sync_outbox SET status='acked', acked_at=UTC_TIMESTAMP(6), last_error=NULL WHERE event_id=@id",
            New With {.id = eventId}, tx)
    End Sub

    Public Shared Sub MarkSent(conn As IDbConnection, tx As IDbTransaction, eventId As String)
        conn.Execute(
            "UPDATE sync_outbox SET status='sent', sent_at=UTC_TIMESTAMP(6) WHERE event_id=@id",
            New With {.id = eventId}, tx)
    End Sub

    ''' <summary>
    ''' Failure is not dropped: it becomes 'failed' with a backoff, or 'dead'
    ''' after the maximum number of attempts, for human inspection.
    ''' </summary>
    Public Shared Sub MarkFailed(conn As IDbConnection, tx As IDbTransaction, eventId As String,
                                 errorMessage As String, backoffSeconds As Integer, maxAttempts As Integer)
        conn.Execute(
            "UPDATE sync_outbox SET " &
            "  status=CASE WHEN attempts>=@maxAttempts THEN 'dead' ELSE 'failed' END, " &
            "  last_error=@err, " &
            "  next_attempt_at=DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @backoff SECOND) " &
            "WHERE event_id=@id",
            New With {.id = eventId, .err = errorMessage, .backoff = backoffSeconds, .maxAttempts = maxAttempts}, tx)
    End Sub

    ''' <summary>Returns rows stuck in 'sending' (crash recovery) to the queue.</summary>
    Public Shared Function RequeueStuck(conn As IDbConnection, tx As IDbTransaction,
                                        olderThanMinutes As Integer) As Integer
        Return conn.Execute(
            "UPDATE sync_outbox SET status='failed', " &
            "next_attempt_at=UTC_TIMESTAMP(6) " &
            "WHERE status='sending' AND (sent_at IS NULL) " &
            "  AND occurred_at <= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @mins MINUTE)",
            New With {.mins = olderThanMinutes}, tx)
    End Function

    ''' <summary>Idempotent inbox write: duplicate events are recorded, not applied twice.</summary>
    Public Shared Function InsertInbox(conn As IDbConnection, tx As IDbTransaction,
                                       eventId As String, branchId As Long, entityType As String,
                                       entityId As String, eventType As String, payload As String,
                                       result As String, errorMessage As String) As Boolean
        Dim affected As Integer = conn.Execute(
            "INSERT INTO sync_inbox(event_id, branch_id, entity_type, entity_id, event_type, payload, " &
            "applied_at, result, error) VALUES(@eventId, @branchId, @entityType, @entityId, @eventType, " &
            "@payload, UTC_TIMESTAMP(6), @result, @error) " &
            "ON DUPLICATE KEY UPDATE event_id=event_id",
            New With {
                .eventId = eventId, .branchId = branchId, .entityType = entityType, .entityId = entityId,
                .eventType = eventType, .payload = payload, .result = result, .error = errorMessage
            }, tx)
        Return affected = 1
    End Function

    Public Shared Function InboxExists(conn As IDbConnection, eventId As String,
                                       Optional tx As IDbTransaction = Nothing) As Boolean
        Return conn.ExecuteScalar(Of Long)(
            "SELECT COUNT(*) FROM sync_inbox WHERE event_id=@id", New With {.id = eventId}, tx) > 0
    End Function

    Public Shared Function GetCursor(conn As IDbConnection, branchId As Long, stream As String,
                                     Optional tx As IDbTransaction = Nothing) As Long
        Dim value As Object = conn.ExecuteScalar(
            "SELECT last_seq FROM sync_cursors WHERE branch_id=@b AND stream=@s",
            New With {.b = branchId, .s = stream}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0L
        Return Convert.ToInt64(value)
    End Function

    Public Shared Sub SetCursor(conn As IDbConnection, tx As IDbTransaction, branchId As Long,
                                stream As String, lastSeq As Long)
        conn.Execute(
            "INSERT INTO sync_cursors(branch_id, stream, last_seq) VALUES(@b, @s, @seq) " &
            "ON DUPLICATE KEY UPDATE last_seq=GREATEST(last_seq, VALUES(last_seq))",
            New With {.b = branchId, .s = stream, .seq = lastSeq}, tx)
    End Sub

    ''' <summary>Appends to the change log used by downstream branch pull.</summary>
    Public Shared Sub AppendChange(conn As IDbConnection, tx As IDbTransaction, entityType As String,
                                   entityId As String, op As String, payload As String, branchId As Long?)
        conn.Execute(
            "INSERT INTO change_log(entity_type, entity_id, op, payload, branch_id) VALUES(@t, @id, @op, @p, @b)",
            New With {.t = entityType, .id = entityId, .op = op, .p = payload, .b = branchId}, tx)
    End Sub

    Public Shared Function GetChanges(conn As IDbConnection, afterSeq As Long, limit As Integer,
                                      Optional tx As IDbTransaction = Nothing) As List(Of String)
        Return conn.Query(Of String)(
            "SELECT CONCAT(seq, '|', entity_type, '|', entity_id, '|', op, '|', COALESCE(payload,'')) " &
            "FROM change_log WHERE seq > @seq ORDER BY seq LIMIT @limit",
            New With {.seq = afterSeq, .limit = limit}, tx).AsList()
    End Function

    ''' <summary>Structured change-log query used by the central sync server Pull endpoint.</summary>
    Public Shared Function QueryChanges(conn As IDbConnection, afterSeq As Long, limit As Integer,
                                        Optional excludeBranchId As Long? = Nothing,
                                        Optional tx As IDbTransaction = Nothing) As List(Of ChangeLogRecord)
        Dim safeLimit As Integer = Math.Max(1, Math.Min(limit, 500))
        Dim sql As String =
            "SELECT seq, entity_type, entity_id, op, COALESCE(payload,'') AS payload, branch_id " &
            "FROM change_log WHERE seq > @seq "
        If excludeBranchId.HasValue AndAlso excludeBranchId.Value > 0 Then
            sql &= "AND (branch_id IS NULL OR branch_id <> @branch) "
        End If
        sql &= "ORDER BY seq LIMIT @limit"

        Return conn.Query(Of ChangeLogRecord)(
            sql,
            New With {.seq = afterSeq, .branch = excludeBranchId, .limit = safeLimit},
            tx).AsList()
    End Function

    ''' <summary>
    ''' Validates a terminal's Bearer DeviceKey against active rows in the devices table
    ''' (supporting SHA-256 hex or direct hash match) and updates last_seen_at.
    ''' Returns the authenticated branch_id, or Nothing when rejected.
    ''' </summary>
    Public Shared Function ValidateDeviceToken(conn As IDbConnection, bearerToken As String,
                                               Optional tx As IDbTransaction = Nothing) As Long?
        Dim activeCount As Long = conn.ExecuteScalar(Of Long)(
            "SELECT COUNT(*) FROM devices WHERE is_active=1", Nothing, tx)

        If String.IsNullOrWhiteSpace(bearerToken) Then
            Return Nothing
        End If

        Dim raw As String = bearerToken.Trim()
        Dim shaHex As String = ComputeSha256Hex(raw)

        Dim devices = conn.Query(Of DeviceAuthRecord)(
            "SELECT id, branch_id, name, device_key_hash, is_active FROM devices WHERE is_active=1",
            Nothing, tx).AsList()

        For Each dev In devices
            If String.IsNullOrWhiteSpace(dev.DeviceKeyHash) Then Continue For
            Dim stored As String = dev.DeviceKeyHash.Trim()
            Dim matched As Boolean =
                String.Equals(stored, shaHex, StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(stored, raw, StringComparison.Ordinal)

            If matched Then
                conn.Execute(
                    "UPDATE devices SET last_seen_at=UTC_TIMESTAMP(6) WHERE id=@id",
                    New With {.id = dev.Id}, tx)
                Return dev.BranchId
            End If
        Next

        ' When no devices have been registered in the table yet, allow a non-empty token
        ' if it matches settings sync.master_key (or default to branch 1 in dev bootstrap).
        If activeCount = 0 Then
            Dim masterKey As String = conn.ExecuteScalar(Of String)(
                "SELECT v FROM settings WHERE k='sync.server_key'", Nothing, tx)
            If String.IsNullOrWhiteSpace(masterKey) OrElse
               String.Equals(masterKey.Trim(), raw, StringComparison.Ordinal) OrElse
               String.Equals(masterKey.Trim(), shaHex, StringComparison.OrdinalIgnoreCase) Then
                Dim defaultBranch As Long = conn.ExecuteScalar(Of Long)(
                    "SELECT id FROM branches WHERE is_active=1 ORDER BY id LIMIT 1", Nothing, tx)
                If defaultBranch > 0 Then Return defaultBranch
            End If
        End If

        Return Nothing
    End Function

    Private Shared Function ComputeSha256Hex(input As String) As String
        Using sha = SHA256.Create()
            Dim bytes As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(input))
            Dim sb As New StringBuilder(bytes.Length * 2)
            For Each b In bytes
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString()
        End Using
    End Function

    Public Shared Sub PurgeAcked(conn As IDbConnection, tx As IDbTransaction, olderThanDays As Integer)
        conn.Execute(
            "DELETE FROM sync_outbox WHERE status='acked' AND acked_at <= DATE_SUB(UTC_TIMESTAMP(6), INTERVAL @d DAY)",
            New With {.d = olderThanDays}, tx)
    End Sub

    Public Shared Function Stats(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As SyncStats
        Dim row = conn.QueryFirstOrDefault(Of SyncStats)(
            "SELECT " &
            "SUM(CASE WHEN status IN ('pending','sending','failed') THEN 1 ELSE 0 END) AS pending, " &
            "SUM(CASE WHEN status='failed' THEN 1 ELSE 0 END) AS failed, " &
            "SUM(CASE WHEN status='dead' THEN 1 ELSE 0 END) AS dead, " &
            "SUM(CASE WHEN status='acked' AND acked_at >= UTC_DATE() THEN 1 ELSE 0 END) AS acked_today, " &
            "MAX(acked_at) AS last_ack_at FROM sync_outbox", Nothing, tx)
        If row Is Nothing Then Return New SyncStats()
        Return row
    End Function
End Class
