Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Text.Json
Imports Dapper

''' <summary>
''' Push/pull orchestration. Rules: an event is never removed before the server
''' acknowledges it; retries use exponential backoff; each batch is idempotent
''' on event_id.
''' </summary>
Public Class SyncService
    Private ReadOnly _connectionString As String
    Private ReadOnly _branchId As Long
    Private ReadOnly _deviceId As Long?
    Private ReadOnly _transport As ISyncTransport
    Private ReadOnly _maxAttempts As Integer

    Public Const StreamMasterData As String = "master_data"

    Public Sub New(connectionString As String, branchId As Long, deviceId As Long?,
                   transport As ISyncTransport, Optional maxAttempts As Integer = 10)
        _connectionString = connectionString
        _branchId = branchId
        _deviceId = deviceId
        _transport = transport
        _maxAttempts = maxAttempts
    End Sub

    Public Function PushOnce(Optional batchSize As Integer = 50) As SyncRunResult
        Dim result As New SyncRunResult()

        Dim batch As New SyncBatch With {.DeviceId = _deviceId}

        Using db = DbSession.Open(_connectionString)
            db.BeginTransaction()
            SyncStore.RequeueStuck(db.Connection, db.Transaction, 10)
            Dim claimed = SyncStore.ClaimBatch(db.Connection, db.Transaction, batchSize)
            db.Commit()

            For Each row In claimed
                batch.Events.Add(New SyncEvent With {
                    .EventId = row.EventId, .BranchId = row.BranchId, .EntityType = row.EntityType,
                    .EntityId = row.EntityId, .EventType = row.EventType, .Attempts = row.Attempts,
                    .Payload = row.Payload
                })
            Next
        End Using

        result.Claimed = batch.Events.Count
        If batch.Events.Count = 0 Then
            result.Message = "لا توجد أحداث معلّقة."
            Return result
        End If

        Dim ack As SyncAck
        Try
            ack = _transport.Push(batch)
        Catch ex As Exception
            AppLog.Warn("Sync push failed: " & ex.Message)
            MarkAllFailed(batch, ex.Message, result)
            Return result
        End Try

        Dim byId = batch.Events.ToDictionary(Function(e) e.EventId, StringComparer.OrdinalIgnoreCase)
        Dim handled As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Using db = DbSession.Open(_connectionString)
            If ack IsNot Nothing AndAlso ack.Results IsNot Nothing Then
                For Each item In ack.Results
                    If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.EventId) Then Continue For
                    If Not byId.ContainsKey(item.EventId) Then Continue For

                    db.BeginTransaction()
                    If IsAccepted(item.Status) Then
                        SyncStore.MarkAcked(db.Connection, db.Transaction, item.EventId)
                        result.Acked += 1
                    Else
                        Dim attempts = byId(item.EventId).Attempts
                        If attempts >= _maxAttempts Then result.Dead += 1 Else result.Failed += 1
                        SyncStore.MarkFailed(db.Connection, db.Transaction, item.EventId,
                                             If(item.Error, "rejected by server"), BackoffSeconds(attempts), _maxAttempts)
                    End If
                    db.Commit()
                    handled.Add(item.EventId)
                Next
            End If

            ' A batch accepted without per-event results still means everything is safe.
            Dim remaining = batch.Events.Where(Function(e) Not handled.Contains(e.EventId)).ToList()
            For Each eventRow In remaining
                db.BeginTransaction()
                If ack IsNot Nothing AndAlso ack.Accepted Then
                    SyncStore.MarkAcked(db.Connection, db.Transaction, eventRow.EventId)
                    result.Acked += 1
                Else
                    If eventRow.Attempts >= _maxAttempts Then result.Dead += 1 Else result.Failed += 1
                    SyncStore.MarkFailed(db.Connection, db.Transaction, eventRow.EventId,
                                         "no acknowledgement for event", BackoffSeconds(eventRow.Attempts), _maxAttempts)
                End If
                db.Commit()
            Next
        End Using

        result.Message = $"أُرسل {result.Claimed}، تأكد {result.Acked}، فشل {result.Failed}، متوقف {result.Dead}."
        Return result
    End Function

    ''' <summary>Pulls master-data changes and records them idempotently in the inbox.</summary>
    Public Function PullOnce(Optional limit As Integer = 200) As Integer
        Using db = DbSession.Open(_connectionString)
            Dim cursor As Long = SyncStore.GetCursor(db.Connection, _branchId, StreamMasterData)
            Dim response = _transport.Pull(StreamMasterData, cursor, limit)
            If response Is Nothing OrElse response.Changes Is Nothing OrElse response.Changes.Count = 0 Then Return 0

            Dim applied As Integer = 0
            For Each change In response.Changes
                Dim eventId As String = "pull:" & change.EntityType & ":" & change.EntityId & ":" & change.Seq.ToString()
                db.BeginTransaction()
                Dim inserted = SyncStore.InsertInbox(db.Connection, db.Transaction, eventId, _branchId,
                                                     change.EntityType, change.EntityId, change.Op,
                                                     If(change.Payload, String.Empty), "applied", Nothing)
                If inserted Then
                    ApplyChange(db.Connection, db.Transaction, change)
                    applied += 1
                End If
                db.Commit()
            Next

            db.BeginTransaction()
            SyncStore.SetCursor(db.Connection, db.Transaction, _branchId, StreamMasterData, response.NextSeq)
            db.Commit()

            Return applied
        End Using
    End Function

    Public Shared Function Status(connectionString As String) As SyncStats
        Using db = DbSession.Open(connectionString)
            Return SyncStore.Stats(db.Connection)
        End Using
    End Function

    ''' <summary>
    ''' Used by the central sync server when an event is pushed from a branch:
    ''' applies master-data mutations and returns the normalized change-log op.
    ''' </summary>
    Public Shared Function ApplyIncomingEvent(conn As IDbConnection, tx As IDbTransaction, ev As SyncEvent) As String
        If ev Is Nothing OrElse String.IsNullOrWhiteSpace(ev.EntityType) Then Return "upsert"
        Dim evType As String = If(ev.EventType, String.Empty).ToLowerInvariant()
        Dim op As String = If(evType.EndsWith(".deleted") OrElse evType = "delete", "delete", "upsert")
        Dim change As New PullChange With {
            .EntityType = ev.EntityType,
            .EntityId = ev.EntityId,
            .Op = op,
            .Payload = ev.Payload
        }
        ApplyChange(conn, tx, change)
        Return op
    End Function

    Private Shared Function IsAccepted(status As String) As Boolean
        If String.IsNullOrWhiteSpace(status) Then Return False
        Select Case status.Trim().ToLowerInvariant()
            Case "acked", "accepted", "ok", "duplicate", "applied"
                Return True
        End Select
        Return False
    End Function

    Private Shared Function BackoffSeconds(attempts As Integer) As Integer
        Dim exponent As Integer = Math.Min(Math.Max(attempts - 1, 0), 10)
        Return Math.Min(3600, 10 * CInt(Math.Pow(2, exponent)))
    End Function

    Private Sub MarkAllFailed(batch As SyncBatch, message As String, result As SyncRunResult)
        Using db = DbSession.Open(_connectionString)
            For Each eventRow In batch.Events
                db.BeginTransaction()
                If eventRow.Attempts >= _maxAttempts Then result.Dead += 1 Else result.Failed += 1
                SyncStore.MarkFailed(db.Connection, db.Transaction, eventRow.EventId, message,
                                     BackoffSeconds(eventRow.Attempts), _maxAttempts)
                db.Commit()
            Next
        End Using
        result.Message = "تعذر الاتصال بالخادم المركزي: " & message
    End Sub

    ''' <summary>Applies a pulled change to local data (idempotent upsert/delete).</summary>
    Public Shared Sub ApplyChange(conn As IDbConnection, tx As IDbTransaction, change As PullChange)
        If change Is Nothing OrElse String.IsNullOrWhiteSpace(change.EntityType) Then Return
        Dim payload As String = If(change.Payload, String.Empty)
        Dim op As String = If(change.Op, "upsert").ToLowerInvariant()
        Try
            Select Case change.EntityType.ToLowerInvariant()
                Case "item", "items"
                    ApplyItem(conn, tx, payload)
                Case "customer", "customers"
                    ApplyCustomer(conn, tx, payload)
                Case "customer_group", "customer_groups"
                    ApplyCustomerGroup(conn, tx, change.EntityId, op, payload)
                Case "unit", "units"
                    ApplySimpleNamedRow(conn, tx, "units", change.EntityId, op, payload)
                Case "item_group", "item_groups"
                    ApplySimpleNamedRow(conn, tx, "item_groups", change.EntityId, op, payload)
                Case "area", "areas"
                    ApplyArea(conn, tx, change.EntityId, op, payload)
                Case "coupon", "coupons"
                    ApplyCoupon(conn, tx, change.EntityId, op, payload)
                Case Else
                    ' Transactional documents (invoices, payroll, journal) remain recorded in sync_inbox.
            End Select
        Catch
            ' Application failures must not break the pull loop; the inbox keeps the record.
        End Try
    End Sub

    Private Shared Sub ApplyItem(conn As IDbConnection, tx As IDbTransaction, payload As String)
        If String.IsNullOrWhiteSpace(payload) Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            Dim id As Long = 0
            If root.TryGetProperty("id", Nothing) Then Long.TryParse(root.GetProperty("id").ToString(), id)
            If id <= 0 Then Return
            Dim code As String = Nothing
            If root.TryGetProperty("code", Nothing) Then code = root.GetProperty("code").GetString()
            Dim nameAr As String = Nothing
            If root.TryGetProperty("name_ar", Nothing) Then nameAr = root.GetProperty("name_ar").GetString()
            Dim salePrice As Decimal = 0D
            If root.TryGetProperty("sale_price", Nothing) Then Decimal.TryParse(root.GetProperty("sale_price").ToString(), salePrice)
            Dim costPrice As Decimal = 0D
            If root.TryGetProperty("cost_price", Nothing) Then Decimal.TryParse(root.GetProperty("cost_price").ToString(), costPrice)
            Dim taxCategoryId As Long = 0
            If root.TryGetProperty("tax_category_id", Nothing) Then Long.TryParse(root.GetProperty("tax_category_id").ToString(), taxCategoryId)
            Dim isActive As Boolean = True
            If root.TryGetProperty("is_active", Nothing) Then Boolean.TryParse(root.GetProperty("is_active").ToString(), isActive)

            Dim exists As Long = conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM items WHERE id=@id", New With {.id = id}, tx)
            If exists = 0 Then
                conn.Execute("INSERT INTO items(id, uuid, code, name_ar, tax_category_id, cost_price, sale_price, is_active) " &
                             "VALUES(@id, @uuid, @code, @name, @tax, @cost, @sale, @active) " &
                             "ON DUPLICATE KEY UPDATE name_ar=VALUES(name_ar), sale_price=VALUES(sale_price), cost_price=VALUES(cost_price), is_active=VALUES(is_active)",
                             New With {.id = id, .uuid = Uuid7.NewId(), .code = If(code, "L" & id.ToString("000000")),
                                       .name = If(nameAr, "item " & id.ToString()), .tax = If(taxCategoryId > 0, taxCategoryId, 1L),
                                       .cost = costPrice, .sale = salePrice, .active = isActive}, tx)
            Else
                conn.Execute("UPDATE items SET name_ar=@name, sale_price=@sale, cost_price=@cost, is_active=@active WHERE id=@id",
                             New With {.name = If(nameAr, "item " & id.ToString()), .sale = salePrice, .cost = costPrice,
                                       .active = isActive, .id = id}, tx)
            End If
        End Using
    End Sub

    Private Shared Sub ApplyCustomer(conn As IDbConnection, tx As IDbTransaction, payload As String)
        If String.IsNullOrWhiteSpace(payload) Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            Dim id As Long = 0
            If root.TryGetProperty("id", Nothing) Then Long.TryParse(root.GetProperty("id").ToString(), id)
            If id <= 0 Then Return
            Dim name As String = Nothing
            If root.TryGetProperty("name", Nothing) Then name = root.GetProperty("name").GetString()
            Dim mobile As String = Nothing
            If root.TryGetProperty("mobile", Nothing) Then mobile = root.GetProperty("mobile").GetString()
            Dim taxNo As String = Nothing
            If root.TryGetProperty("tax_no", Nothing) Then taxNo = root.GetProperty("tax_no").GetString()
            Dim creditLimit As Decimal = 0D
            If root.TryGetProperty("credit_limit", Nothing) Then Decimal.TryParse(root.GetProperty("credit_limit").ToString(), creditLimit)
            Dim isActive As Boolean = True
            If root.TryGetProperty("is_active", Nothing) Then Boolean.TryParse(root.GetProperty("is_active").ToString(), isActive)

            Dim exists As Long = conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM customers WHERE id=@id", New With {.id = id}, tx)
            If exists = 0 Then
                conn.Execute("INSERT INTO customers(id, uuid, name, mobile, tax_no, credit_limit, is_active) " &
                             "VALUES(@id, @uuid, @name, @mobile, @tax, @credit, @active)",
                             New With {.id = id, .uuid = Uuid7.NewId(), .name = If(name, "customer " & id.ToString()),
                                       .mobile = mobile, .tax = taxNo, .credit = creditLimit, .active = isActive}, tx)
            Else
                conn.Execute("UPDATE customers SET name=@name, mobile=@mobile, tax_no=@tax, credit_limit=@credit, is_active=@active WHERE id=@id",
                             New With {.name = If(name, "customer " & id.ToString()), .mobile = mobile, .tax = taxNo,
                                       .credit = creditLimit, .active = isActive, .id = id}, tx)
            End If
        End Using
    End Sub

    Private Shared Sub ApplyCustomerGroup(conn As IDbConnection, tx As IDbTransaction, entityId As String, op As String, payload As String)
        Dim id As Long = 0
        Long.TryParse(entityId, id)
        If op = "delete" AndAlso id > 0 Then
            conn.Execute("DELETE FROM customer_groups WHERE id=@id", New With {.id = id}, tx)
            Return
        End If
        If String.IsNullOrWhiteSpace(payload) Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            If id <= 0 AndAlso root.TryGetProperty("id", Nothing) Then Long.TryParse(root.GetProperty("id").ToString(), id)
            If id <= 0 Then Return
            Dim name As String = If(root.TryGetProperty("name", Nothing), root.GetProperty("name").GetString(), "Group " & id.ToString())
            Dim discountPct As Decimal = 0D
            If root.TryGetProperty("discount_percent", Nothing) Then Decimal.TryParse(root.GetProperty("discount_percent").ToString(), discountPct)
            conn.Execute(
                "INSERT INTO customer_groups(id, name, discount_percent, is_active) VALUES(@id, @name, @disc, 1) " &
                "ON DUPLICATE KEY UPDATE name=VALUES(name), discount_percent=VALUES(discount_percent)",
                New With {.id = id, .name = name, .disc = discountPct}, tx)
        End Using
    End Sub

    Private Shared Sub ApplySimpleNamedRow(conn As IDbConnection, tx As IDbTransaction, table As String,
                                           entityId As String, op As String, payload As String)
        Dim id As Long = 0
        Long.TryParse(entityId, id)
        If op = "delete" AndAlso id > 0 Then
            conn.Execute("DELETE FROM " & table & " WHERE id=@id", New With {.id = id}, tx)
            Return
        End If
        If String.IsNullOrWhiteSpace(payload) OrElse id <= 0 Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            Dim name As String = If(root.TryGetProperty("name", Nothing), root.GetProperty("name").GetString(), Nothing)
            If String.IsNullOrWhiteSpace(name) Then Return
            conn.Execute(
                "INSERT INTO " & table & "(id, name, is_active) VALUES(@id, @name, 1) " &
                "ON DUPLICATE KEY UPDATE name=VALUES(name)",
                New With {.id = id, .name = name}, tx)
        End Using
    End Sub

    Private Shared Sub ApplyArea(conn As IDbConnection, tx As IDbTransaction, entityId As String, op As String, payload As String)
        Dim id As Long = 0
        Long.TryParse(entityId, id)
        If op = "delete" AndAlso id > 0 Then
            conn.Execute("DELETE FROM areas WHERE id=@id", New With {.id = id}, tx)
            Return
        End If
        If String.IsNullOrWhiteSpace(payload) OrElse id <= 0 Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            Dim name As String = If(root.TryGetProperty("name", Nothing), root.GetProperty("name").GetString(), Nothing)
            If String.IsNullOrWhiteSpace(name) Then Return
            Dim city As String = If(root.TryGetProperty("city", Nothing), root.GetProperty("city").GetString(), Nothing)
            conn.Execute(
                "INSERT INTO areas(id, name, city, is_active) VALUES(@id, @name, @city, 1) " &
                "ON DUPLICATE KEY UPDATE name=VALUES(name), city=VALUES(city)",
                New With {.id = id, .name = name, .city = city}, tx)
        End Using
    End Sub

    Private Shared Sub ApplyCoupon(conn As IDbConnection, tx As IDbTransaction, entityId As String, op As String, payload As String)
        Dim id As Long = 0
        Long.TryParse(entityId, id)
        If op = "delete" AndAlso id > 0 Then
            conn.Execute("DELETE FROM coupons WHERE id=@id", New With {.id = id}, tx)
            Return
        End If
        If String.IsNullOrWhiteSpace(payload) OrElse id <= 0 Then Return
        Using doc = JsonDocument.Parse(payload)
            Dim root = doc.RootElement
            Dim code As String = If(root.TryGetProperty("code", Nothing), root.GetProperty("code").GetString(), Nothing)
            Dim name As String = If(root.TryGetProperty("name", Nothing), root.GetProperty("name").GetString(), code)
            If String.IsNullOrWhiteSpace(code) Then Return
            Dim discType As String = If(root.TryGetProperty("discount_type", Nothing), root.GetProperty("discount_type").GetString(), "amount")
            Dim discVal As Decimal = 0D
            If root.TryGetProperty("discount_value", Nothing) Then Decimal.TryParse(root.GetProperty("discount_value").ToString(), discVal)
            conn.Execute(
                "INSERT INTO coupons(id, code, name, discount_type, discount_value, is_active) " &
                "VALUES(@id, @code, @name, @dtype, @dval, 1) " &
                "ON DUPLICATE KEY UPDATE code=VALUES(code), name=VALUES(name), discount_type=VALUES(discount_type), discount_value=VALUES(discount_value)",
                New With {.id = id, .code = code, .name = name, .dtype = discType, .dval = discVal}, tx)
        End Using
    End Sub
End Class
