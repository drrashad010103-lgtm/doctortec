Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>
''' Stock movements are the source of truth; stock_balances is a cache updated
''' inside the same transaction as the movement.
''' </summary>
Public NotInheritable Class StockRepository
    Private Sub New()
    End Sub

    Public Shared Function GetBalance(conn As IDbConnection, branchId As Long, itemId As Long,
                                      Optional tx As IDbTransaction = Nothing) As StockSummary
        Return conn.QueryFirstOrDefault(Of StockSummary)(
            "SELECT sb.item_id, i.code, i.name_ar, sb.qty_on_hand, sb.last_cost, i.sale_price, i.reorder_level " &
            "FROM stock_balances sb JOIN items i ON i.id=sb.item_id " &
            "WHERE sb.branch_id=@b AND sb.item_id=@i",
            New With {.b = branchId, .i = itemId}, tx)
    End Function

    ''' <summary>Locks the balance row for the duration of the transaction.</summary>
    Public Shared Function GetBalanceForUpdate(conn As IDbConnection, tx As IDbTransaction,
                                                branchId As Long, itemId As Long) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT qty_on_hand FROM stock_balances WHERE branch_id=@b AND item_id=@i FOR UPDATE",
            New With {.b = branchId, .i = itemId}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    ''' <summary>Balance can be missing (item never moved) — treat as zero.</summary>
    Public Shared Function GetQty(conn As IDbConnection, branchId As Long, itemId As Long,
                                  Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT qty_on_hand FROM stock_balances WHERE branch_id=@b AND item_id=@i",
            New With {.b = branchId, .i = itemId}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    Public Shared Function ListForBranch(conn As IDbConnection, branchId As Long, term As String,
                                         onlyLow As Boolean, Optional limit As Integer = 500,
                                         Optional tx As IDbTransaction = Nothing) As List(Of StockSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT i.id AS item_id, i.code, i.name_ar, COALESCE(sb.qty_on_hand,0) AS qty_on_hand, " &
            "       COALESCE(sb.last_cost, i.cost_price) AS last_cost, i.sale_price, i.reorder_level " &
            "FROM items i LEFT JOIN stock_balances sb ON sb.item_id=i.id AND sb.branch_id=@b " &
            "WHERE i.is_active=1 AND (@term='' OR i.code LIKE @like OR i.name_ar LIKE @like) "
        If onlyLow Then sql &= " AND COALESCE(sb.qty_on_hand,0) <= i.reorder_level "
        sql &= "ORDER BY i.name_ar LIMIT @limit"

        Return conn.Query(Of StockSummary)(sql,
            New With {.b = branchId, .term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    ''' <summary>Adds a movement row and applies it to the cached balance.</summary>
    Public Shared Sub AddMove(conn As IDbConnection, tx As IDbTransaction,
                              eventId As String, branchId As Long, itemId As Long, moveType As String,
                              qtyDelta As Decimal, unitCost As Decimal, refType As String, refId As Long?,
                              lineId As Long?, occurredAt As Date, businessDate As Date,
                              userId As Long, deviceId As Long?, notes As String,
                              Optional updateLastCost As Boolean = True)
        conn.Execute(
            "INSERT INTO stock_moves(event_id, branch_id, item_id, move_type, qty_delta, unit_cost, " &
            "ref_type, ref_id, line_id, occurred_at, business_date, created_by, device_id, notes) " &
            "VALUES(@eventId, @branchId, @itemId, @moveType, @qty, @cost, @refType, @refId, @lineId, " &
            "@occurredAt, @businessDate, @userId, @deviceId, @notes)",
            New With {
                .eventId = eventId, .branchId = branchId, .itemId = itemId, .moveType = moveType,
                .qty = qtyDelta, .cost = unitCost, .refType = refType, .refId = refId, .lineId = lineId,
                .occurredAt = occurredAt, .businessDate = businessDate, .userId = userId,
                .deviceId = deviceId, .notes = notes
            }, tx)

        ApplyToBalance(conn, tx, branchId, itemId, qtyDelta, unitCost, updateLastCost)
    End Sub

    Public Shared Sub ApplyToBalance(conn As IDbConnection, tx As IDbTransaction,
                                     branchId As Long, itemId As Long, qtyDelta As Decimal,
                                     unitCost As Decimal, Optional updateLastCost As Boolean = True)
        Dim newLastCost As Decimal? = Nothing
        If updateLastCost AndAlso qtyDelta > 0D AndAlso unitCost > 0D Then newLastCost = unitCost

        conn.Execute(
            "INSERT INTO stock_balances(branch_id, item_id, qty_on_hand, last_cost) " &
            "VALUES(@b, @i, @qty, COALESCE(@cost, 0)) " &
            "ON DUPLICATE KEY UPDATE qty_on_hand=qty_on_hand+VALUES(qty_on_hand), " &
            "last_cost=IF(@hasCost=1, @cost, last_cost)",
            New With {.b = branchId, .i = itemId, .qty = qtyDelta, .cost = If(newLastCost.HasValue, newLastCost.Value, 0D),
                      .hasCost = If(newLastCost.HasValue, 1, 0)}, tx)
    End Sub

    ''' <summary>
    ''' LASTCOST rule (D-10): unit cost of the most recent inbound movement on or
    ''' before the given instant. Falls back to the item's stored cost_price.
    ''' </summary>
    Public Shared Function LastCostAsOf(conn As IDbConnection, branchId As Long, itemId As Long,
                                        asOf As Date, Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT unit_cost FROM stock_moves " &
            "WHERE branch_id=@b AND item_id=@i AND qty_delta > 0 AND unit_cost > 0 AND occurred_at <= @asOf " &
            "ORDER BY occurred_at DESC, id DESC LIMIT 1",
            New With {.b = branchId, .i = itemId, .asOf = asOf}, tx)

        If value IsNot Nothing AndAlso value IsNot DBNull.Value Then
            Return Convert.ToDecimal(value)
        End If

        Dim fallback As Object = conn.ExecuteScalar(
            "SELECT cost_price FROM items WHERE id=@i", New With {.i = itemId}, tx)
        If fallback Is Nothing OrElse fallback Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(fallback)
    End Function

    ''' <summary>Recomputes the cached balance from movements (verification tool).</summary>
    Public Shared Function RecomputeBalance(conn As IDbConnection, tx As IDbTransaction,
                                            branchId As Long, itemId As Long) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT COALESCE(SUM(qty_delta),0) FROM stock_moves WHERE branch_id=@b AND item_id=@i",
            New With {.b = branchId, .i = itemId}, tx)
        Dim expected As Decimal = If(value Is Nothing OrElse value Is DBNull.Value, 0D, Convert.ToDecimal(value))

        conn.Execute(
            "INSERT INTO stock_balances(branch_id, item_id, qty_on_hand) VALUES(@b, @i, @qty) " &
            "ON DUPLICATE KEY UPDATE qty_on_hand=VALUES(qty_on_hand)",
            New With {.b = branchId, .i = itemId, .qty = expected}, tx)

        Return expected
    End Function

    Public Shared Function ListBalances(conn As IDbConnection, branchId As Long,
                                        Optional tx As IDbTransaction = Nothing) As List(Of StockSummary)
        Return conn.Query(Of StockSummary)(
            "SELECT item_id, code, name_ar, qty_on_hand, last_cost, sale_price, reorder_level " &
            "FROM v_stock WHERE branch_id=@b", New With {.b = branchId}, tx).AsList()
    End Function

    ''' <summary>
    ''' Smart Stock Forecast & Order Room (تنبؤ المخزون الذكي وغرفة الطلبية):
    ''' Calculates real 30-day sales velocity from posted invoices, daily burn rate,
    ''' remaining days of supply, and suggested purchase order quantities.
    ''' </summary>
    Public Shared Function ForecastOrderRoom(conn As IDbConnection, branchId As Long,
                                             term As String, lookbackDays As Integer,
                                             coverageDays As Integer,
                                             Optional onlyActionable As Boolean = False) As List(Of StockForecastRow)
        Dim days As Integer = Math.Max(1, lookbackDays)
        Dim cov As Integer = Math.Max(1, coverageDays)
        Dim fromDate As Date = Date.Today.AddDays(-days)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"

        Dim sql As String =
            "SELECT i.id AS ItemId, i.code AS Code, i.name_ar AS NameAr, " &
            "       COALESCE(sb.qty_on_hand, 0) AS QtyOnHand, " &
            "       i.reorder_level AS ReorderLevel, " &
            "       COALESCE(NULLIF(sb.last_cost, 0), i.cost_price) AS LastCost, " &
            "       COALESCE((SELECT SUM(CASE WHEN v.invoice_type='sale' THEN l.qty ELSE -l.qty END) " &
            "                 FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id " &
            "                 WHERE l.item_id=i.id AND v.branch_id=@b AND v.status='posted' " &
            "                   AND v.invoice_type IN ('sale','sale_return') " &
            "                   AND v.business_date >= @fromDate), 0) AS SoldLast30Days " &
            "FROM items i " &
            "LEFT JOIN stock_balances sb ON sb.item_id=i.id AND sb.branch_id=@b " &
            "WHERE i.is_active=1 " &
            "  AND (@term='' OR i.code LIKE @like OR i.name_ar LIKE @like) " &
            "ORDER BY i.name_ar LIMIT 500"

        Dim raw = conn.Query(Of StockForecastRow)(sql, New With {
            .b = branchId, .fromDate = fromDate,
            .term = If(term, String.Empty).Trim(), .like = likeTerm
        }).AsList()

        Dim result As New List(Of StockForecastRow)()
        For Each r In raw
            If r.SoldLast30Days < 0D Then r.SoldLast30Days = 0D
            r.DailyBurnRate = Money.Round4(r.SoldLast30Days / CDec(days))
            If r.DailyBurnRate > 0D Then
                r.DaysOfSupply = Money.Round2(Math.Max(0D, r.QtyOnHand) / r.DailyBurnRate)
            Else
                r.DaysOfSupply = If(r.QtyOnHand > 0D, 999D, 0D)
            End If

            Dim targetStock As Decimal = Money.Round2((r.DailyBurnRate * CDec(cov)) + r.ReorderLevel)
            r.SuggestedOrderQty = Math.Max(0D, Money.Round2(targetStock - r.QtyOnHand))
            r.EstimatedOrderCost = Money.Round2(r.SuggestedOrderQty * r.LastCost)

            If r.QtyOnHand <= 0D Then
                r.StatusText = "🔴 نافد تماماً — أولوية قصوى"
            ElseIf r.QtyOnHand <= r.ReorderLevel OrElse r.DaysOfSupply <= 7D Then
                r.StatusText = "🟠 حرج (أقل من 7 أيام / تحت حد الطلب)"
            ElseIf r.SuggestedOrderQty > 0D Then
                r.StatusText = "🟡 يحتاج تعزيز للطلبية"
            Else
                r.StatusText = "🟢 رصيد آمن وكافٍ"
            End If

            If Not onlyActionable OrElse r.SuggestedOrderQty > 0D OrElse r.QtyOnHand <= r.ReorderLevel Then
                result.Add(r)
            End If
        Next

        Return result
    End Function
End Class


Public Class InventoryCountSummary
    Public Property Id As Long
    Public Property EventId As String
    Public Property BranchId As Long
    Public Property CountNo As String
    Public Property Status As String
    Public Property StartedAt As Date
    Public Property PostedAt As Date?
    Public Property Notes As String

    Public ReadOnly Property StatusName As String
        Get
            Select Case If(Status, String.Empty)
                Case "draft" : Return "مسودة جرد"
                Case "posted" : Return "معتمد ومرحّل"
                Case "cancelled" : Return "ملغى"
                Case Else : Return Status
            End Select
        End Get
    End Property

    Public ReadOnly Property DisplayTitle As String
        Get
            Return ToString()
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return CountNo & " — " & StatusName & " (" & StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") & ")"
    End Function
End Class

Public Class InventoryCountLineSummary
    Public Property Id As Long
    Public Property CountId As Long
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property SystemQty As Decimal
    Public Property CountedQty As Decimal
    Public Property VarianceQty As Decimal
    Public Property UnitCost As Decimal
    Public Property VarianceValue As Decimal
End Class

Public NotInheritable Class InventoryCountRepository
    Private Sub New()
    End Sub

    Public Shared Function ListForBranch(conn As IDbConnection, branchId As Long,
                                         Optional limit As Integer = 100,
                                         Optional tx As IDbTransaction = Nothing) As List(Of InventoryCountSummary)
        Return conn.Query(Of InventoryCountSummary)(
            "SELECT id, event_id, branch_id, count_no, status, started_at, posted_at, notes " &
            "FROM inventory_counts WHERE branch_id=@b ORDER BY id DESC LIMIT @limit",
            New With {.b = branchId, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing,
                                   Optional forUpdate As Boolean = False) As InventoryCountSummary
        Dim sql As String =
            "SELECT id, event_id, branch_id, count_no, status, started_at, posted_at, notes " &
            "FROM inventory_counts WHERE id=@id"
        If forUpdate AndAlso tx IsNot Nothing Then sql &= " FOR UPDATE"
        Return conn.QueryFirstOrDefault(Of InventoryCountSummary)(sql, New With {.id = id}, tx)
    End Function

    Public Shared Function GetLines(conn As IDbConnection, countId As Long,
                                    Optional term As String = "",
                                    Optional tx As IDbTransaction = Nothing) As List(Of InventoryCountLineSummary)
        Dim cleanTerm As String = If(term, String.Empty).Trim()
        Dim likeTerm As String = "%" & cleanTerm & "%"
        Return conn.Query(Of InventoryCountLineSummary)(
            "SELECT l.id, l.count_id, l.item_id, i.code, i.name_ar, l.system_qty, l.counted_qty, " &
            "       l.variance_qty, l.unit_cost, l.variance_value " &
            "FROM inventory_count_lines l JOIN items i ON i.id=l.item_id " &
            "WHERE l.count_id=@cid AND (@term='' OR i.code LIKE @like OR i.name_ar LIKE @like) " &
            "ORDER BY i.name_ar LIMIT 2000",
            New With {.cid = countId, .term = cleanTerm, .like = likeTerm}, tx).AsList()
    End Function

    Public Shared Function InsertHeader(conn As IDbConnection, tx As IDbTransaction,
                                        eventId As String, branchId As Long, countNo As String,
                                        userId As Long, notes As String) As Long
        conn.Execute(
            "INSERT INTO inventory_counts(event_id, branch_id, count_no, status, created_by, notes) " &
            "VALUES(@eventId, @branchId, @countNo, 'draft', @userId, @notes)",
            New With {.eventId = eventId, .branchId = branchId, .countNo = countNo, .userId = userId, .notes = notes}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub UpsertLine(conn As IDbConnection, tx As IDbTransaction,
                                 countId As Long, itemId As Long, systemQty As Decimal,
                                 countedQty As Decimal, unitCost As Decimal)
        Dim varianceQty As Decimal = Money.Round4(countedQty - systemQty)
        Dim varianceVal As Decimal = Money.Round2(varianceQty * unitCost)
        conn.Execute(
            "INSERT INTO inventory_count_lines(count_id, item_id, system_qty, counted_qty, variance_qty, unit_cost, variance_value) " &
            "VALUES(@cid, @iid, @sys, @cnt, @vqty, @cost, @vval) " &
            "ON DUPLICATE KEY UPDATE system_qty=VALUES(system_qty), counted_qty=VALUES(counted_qty), " &
            "variance_qty=VALUES(variance_qty), " &
            "unit_cost=VALUES(unit_cost), variance_value=VALUES(variance_value)",
            New With {
                .cid = countId, .iid = itemId, .sys = systemQty, .cnt = countedQty,
                .vqty = varianceQty, .cost = unitCost, .vval = varianceVal
            }, tx)
    End Sub

    Public Shared Sub UpdateCounted(conn As IDbConnection, tx As IDbTransaction, countId As Long,
                                    lineId As Long, countedQty As Decimal)
        conn.Execute(
            "UPDATE inventory_count_lines SET counted_qty=@qty, variance_qty=@qty - system_qty, " &
            "variance_value=ROUND((@qty - system_qty) * unit_cost, 2) " &
            "WHERE id=@id AND count_id=@countId",
            New With {.id = lineId, .countId = countId, .qty = countedQty}, tx)
    End Sub

    Public Shared Sub MarkPosted(conn As IDbConnection, tx As IDbTransaction, countId As Long)
        conn.Execute(
            "UPDATE inventory_counts SET status='posted', posted_at=UTC_TIMESTAMP(6) WHERE id=@id AND status='draft'",
            New With {.id = countId}, tx)
    End Sub

End Class
