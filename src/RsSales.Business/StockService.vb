Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports Dapper

''' <summary>Stock adjustments, counts and inter-branch transfers.</summary>
Public NotInheritable Class StockService
    Private Sub New()
    End Sub

    Public Shared Function Levels(session As AppSession, term As String, onlyLow As Boolean) As List(Of StockSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return StockRepository.ListForBranch(db.Connection, session.BranchId, term, onlyLow)
        End Using
    End Function

    Public Shared Function Adjust(session As AppSession, itemId As Long, qtyDelta As Decimal,
                                  unitCost As Decimal?, reason As String) As Decimal
        session.RequirePermission("stock.adjust")
        If qtyDelta = 0D Then Throw New BusinessException("أدخل كمية تعديل مختلفة عن صفر.")
        If String.IsNullOrWhiteSpace(reason) Then Throw New BusinessException("سبب التعديل مطلوب للتدقيق.")

        Dim occurredAt As Date = Date.UtcNow
        Dim businessDate As Date = occurredAt.ToLocalTime().Date

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim item = ItemsRepository.GetById(db.Connection, itemId, tx)
            If item Is Nothing Then Throw New BusinessException("الصنف غير موجود.")

            Dim current = StockRepository.GetBalanceForUpdate(db.Connection, tx, session.BranchId, itemId)
            Dim newQty = current + qtyDelta
            If newQty < 0D Then
                Throw New BusinessException("لا يمكن أن يصبح الرصيد سالبًا. المتاح: " & current.ToString("0.####"))
            End If

            Dim cost As Decimal = If(unitCost.HasValue, unitCost.Value, StockRepository.LastCostAsOf(db.Connection, session.BranchId, itemId, occurredAt, tx))
            If cost < 0D Then Throw New BusinessException("التكلفة لا يمكن أن تكون سالبة.")

            Dim eventId As String = Uuid7.NewId()
            StockRepository.AddMove(db.Connection, tx, eventId, session.BranchId, itemId, "adjustment",
                                    qtyDelta, cost, "manual", Nothing, Nothing, occurredAt, businessDate,
                                    session.UserId, session.DeviceId, reason, qtyDelta > 0D)

            Dim value As Decimal = Money.Round2(Math.Abs(qtyDelta) * cost)
            If value <> 0D Then
                Dim journalLines As New List(Of JournalLineInput)()
                Dim inventoryId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx)
                Dim adjustId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccStockAdjust, tx)
                If qtyDelta > 0D Then
                    journalLines.Add(New JournalLineInput With {.AccountId = inventoryId, .Debit = value, .Description = "زيادة مخزون: " & reason})
                    journalLines.Add(New JournalLineInput With {.AccountId = adjustId, .Credit = value, .Description = "تسوية مخزون: " & reason})
                Else
                    journalLines.Add(New JournalLineInput With {.AccountId = adjustId, .Debit = value, .Description = "تسوية مخزون: " & reason})
                    journalLines.Add(New JournalLineInput With {.AccountId = inventoryId, .Credit = value, .Description = "نقص مخزون: " & reason})
                End If
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "تسوية مخزون " & item.Code, "stock_adjustment", Nothing,
                                                 session.UserId, journalLines)
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "stock_adjust", "item", itemId.ToString(), current.ToString("0.####"),
                                newQty.ToString("0.####") & " | " & reason, session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "stock_adjustment",
                              itemId.ToString(), "stock.adjusted", 1,
                              PostingHelper.Serialize(New With {
                                  .branch_id = session.BranchId, .item_id = itemId,
                                  .qty_delta = qtyDelta, .unit_cost = cost, .reason = reason
                              }), occurredAt)

            db.Commit()
            Return newQty
        End Using
    End Function

    ''' <summary>Creates a transfer draft between two branches.</summary>
    Public Shared Function CreateTransfer(session As AppSession, toBranchId As Long, notes As String,
                                          lines As IList(Of TransferLineInput)) As Long
        session.RequirePermission("stock.transfer")
        If toBranchId = session.BranchId Then Throw New BusinessException("لا يمكن التحويل إلى نفس الفرع.")
        If lines Is Nothing OrElse lines.Count = 0 Then Throw New BusinessException("أضف أصناف التحويل.")
        For Each line In lines
            If line.Qty <= 0D Then Throw New BusinessException("كمية التحويل يجب أن تكون أكبر من صفر.")
        Next

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim target = BranchesRepository.GetById(db.Connection, toBranchId, tx)
            If target Is Nothing OrElse Not target.IsActive Then Throw New BusinessException("الفرع المستلم غير مفعّل.")

            Dim eventId As String = Uuid7.NewId()
            Dim period As String = Date.UtcNow.Year.ToString()
            Dim seq As Long = SequencesRepository.NextNo(db.Connection, tx, session.BranchId, "transfer", period)
            Dim transferNo As String = "TR-" & period & "-" & seq.ToString("000000")

            db.Connection.Execute(
                "INSERT INTO stock_transfers(event_id, transfer_no, from_branch, to_branch, status, created_by, notes) " &
                "VALUES(@eventId, @no, @from, @to, 'draft', @uid, @notes)",
                New With {.eventId = eventId, .no = transferNo, .from = session.BranchId,
                          .to = toBranchId, .uid = session.UserId, .notes = notes}, tx)
            Dim transferId As Long = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)

            For Each line In lines
                Dim item = ItemsRepository.GetById(db.Connection, line.ItemId, tx)
                If item Is Nothing Then Throw New BusinessException("الصنف غير موجود.")
                Dim available = StockRepository.GetBalanceForUpdate(db.Connection, tx, session.BranchId, line.ItemId)
                If line.Qty > available Then
                    Throw New BusinessException("الرصيد غير كافٍ للصنف «" & item.NameAr & "». المتاح: " & available.ToString("0.####"))
                End If
                Dim cost As Decimal = If(line.UnitCost.HasValue, line.UnitCost.Value,
                                         StockRepository.LastCostAsOf(db.Connection, session.BranchId, line.ItemId, Date.UtcNow, tx))

                db.Connection.Execute(
                    "INSERT INTO stock_transfer_lines(transfer_id, item_id, qty, unit_cost) VALUES(@t, @i, @q, @c)",
                    New With {.t = transferId, .i = line.ItemId, .q = line.Qty, .c = cost}, tx)
            Next

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "transfer_create", "stock_transfer", transferId.ToString(), Nothing, transferNo, session.DeviceId)

            db.Commit()
            Return transferId
        End Using
    End Function

    ''' <summary>Posts the outgoing side: stock leaves the source branch.</summary>
    Public Shared Sub SendTransfer(session As AppSession, transferId As Long)
        session.RequirePermission("stock.transfer")
        Dim occurredAt As Date = Date.UtcNow
        Dim businessDate As Date = occurredAt.ToLocalTime().Date

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim transfer = db.Connection.QueryFirstOrDefault(Of TransferRecord)(
                "SELECT id, event_id, transfer_no, from_branch, to_branch, status FROM stock_transfers WHERE id=@id FOR UPDATE",
                New With {.id = transferId}, tx)
            If transfer Is Nothing Then Throw New BusinessException("أمر التحويل غير موجود.")
            If transfer.FromBranch <> session.BranchId Then Throw New BusinessException("أمر التحويل من فرع آخر.")
            If transfer.Status <> "draft" Then Throw New BusinessException("أمر التحويل ليس في حالة مسودة.")

            Dim lines = db.Connection.Query(Of TransferLineRecord)(
                "SELECT id, item_id, qty, unit_cost FROM stock_transfer_lines WHERE transfer_id=@id",
                New With {.id = transferId}, tx).AsList()

            For Each line In lines
                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, line.ItemId,
                                        "transfer_out", -line.Qty, line.UnitCost, "transfer", transferId, line.Id,
                                        occurredAt, businessDate, session.UserId, session.DeviceId, transfer.TransferNo, False)
            Next

            ' Goods leave this branch's inventory and become a due from the receiving
            ' branch; the destination posts the matching due-to and inventory entry.
            Dim value As Decimal = Money.Round2(lines.Sum(Function(l) l.Qty * l.UnitCost))
            If value <> 0D Then
                Dim description As String = "تحويل مخزون صادر " & transfer.TransferNo
                Dim journalLines As New List(Of JournalLineInput)()
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccDueFromBranches, tx),
                    .Debit = value, .Description = description})
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Credit = value, .Description = description})
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 description, "stock_transfer", transferId, session.UserId, journalLines)
            End If

            db.Connection.Execute(
                "UPDATE stock_transfers SET status='sent', posted_at=UTC_TIMESTAMP(6) WHERE id=@id",
                New With {.id = transferId}, tx)

            AuditRepository.Log(db.Connection, tx, transfer.EventId, session.BranchId, session.UserId, session.Username,
                                "transfer_send", "stock_transfer", transferId.ToString(), Nothing, transfer.TransferNo, session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.DeviceId, "stock_transfer",
                              transferId.ToString(), "transfer.sent", 1,
                              PostingHelper.Serialize(New With {
                                  .id = transferId, .transfer_no = transfer.TransferNo,
                                  .from_branch = transfer.FromBranch, .to_branch = transfer.ToBranch,
                                  .lines = lines
                              }), occurredAt)

            db.Commit()
        End Using
    End Sub

    ''' <summary>Posts the incoming side at the destination branch.</summary>
    Public Shared Sub ReceiveTransfer(session As AppSession, transferId As Long)
        session.RequirePermission("stock.transfer")
        Dim occurredAt As Date = Date.UtcNow
        Dim businessDate As Date = occurredAt.ToLocalTime().Date

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim transfer = db.Connection.QueryFirstOrDefault(Of TransferRecord)(
                "SELECT id, event_id, transfer_no, from_branch, to_branch, status FROM stock_transfers WHERE id=@id FOR UPDATE",
                New With {.id = transferId}, tx)
            If transfer Is Nothing Then Throw New BusinessException("أمر التحويل غير موجود.")
            If transfer.ToBranch <> session.BranchId Then Throw New BusinessException("أمر التحويل ليس لهذا الفرع.")
            If transfer.Status <> "sent" Then Throw New BusinessException("أمر التحويل لم يُرسل بعد.")

            Dim lines = db.Connection.Query(Of TransferLineRecord)(
                "SELECT id, item_id, qty, unit_cost FROM stock_transfer_lines WHERE transfer_id=@id",
                New With {.id = transferId}, tx).AsList()

            For Each line In lines
                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, line.ItemId,
                                        "transfer_in", line.Qty, line.UnitCost, "transfer", transferId, line.Id,
                                        occurredAt, businessDate, session.UserId, session.DeviceId, transfer.TransferNo, True)
            Next

            Dim value As Decimal = Money.Round2(lines.Sum(Function(l) l.Qty * l.UnitCost))
            If value <> 0D Then
                Dim description As String = "تحويل مخزون وارد " & transfer.TransferNo
                Dim journalLines As New List(Of JournalLineInput)()
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Debit = value, .Description = description})
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccDueToBranches, tx),
                    .Credit = value, .Description = description})
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 description, "stock_transfer", transferId, session.UserId, journalLines)
            End If

            db.Connection.Execute(
                "UPDATE stock_transfers SET status='received', received_at=UTC_TIMESTAMP(6) WHERE id=@id",
                New With {.id = transferId}, tx)

            AuditRepository.Log(db.Connection, tx, transfer.EventId, session.BranchId, session.UserId, session.Username,
                                "transfer_receive", "stock_transfer", transferId.ToString(), Nothing, transfer.TransferNo, session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.DeviceId, "stock_transfer",
                              transferId.ToString(), "transfer.received", 1,
                              PostingHelper.Serialize(New With {.id = transferId, .transfer_no = transfer.TransferNo}), occurredAt)

            db.Commit()
        End Using
    End Sub

    Public Shared Function ListTransfers(session As AppSession, Optional limit As Integer = 100) As List(Of TransferRecord)
        Using db = DbSession.Open(session.ConnectionString)
            Return db.Connection.Query(Of TransferRecord)(
                "SELECT id, event_id, transfer_no, from_branch, to_branch, status, created_at, posted_at, " &
                "received_at, notes FROM stock_transfers " &
                "WHERE from_branch=@b OR to_branch=@b ORDER BY id DESC LIMIT @limit",
                New With {.b = session.BranchId, .limit = limit}).AsList()
        End Using
    End Function

    ''' <summary>تنبؤ المخزون الذكي وغرفة الطلبية (Smart Stock Forecast and Order Room).</summary>
    Public Shared Function ForecastOrderRoom(session As AppSession, Optional term As String = "",
                                             Optional lookbackDays As Integer = 30,
                                             Optional coverageDays As Integer = 14,
                                             Optional onlyActionable As Boolean = False) As List(Of StockForecastRow)
        Using db = DbSession.Open(session.ConnectionString)
            Return StockRepository.ForecastOrderRoom(db.Connection, session.BranchId, term, lookbackDays, coverageDays, onlyActionable)
        End Using
    End Function

    ' ---- Physical inventory count helpers used by the stock screen ----
    Public Shared Function ListInventoryCounts(session As AppSession) As List(Of InventoryCountSummary)
        Return InventoryCountService.ListCounts(session)
    End Function

    Public Shared Function GetInventoryCountLines(session As AppSession, countId As Long) As List(Of InventoryCountLineSummary)
        Return InventoryCountService.GetLines(session, countId)
    End Function

    Public Shared Function CreateInventoryCount(session As AppSession, notes As String) As Long
        Return InventoryCountService.CreateDraft(session, notes)
    End Function

    Public Shared Sub UpsertInventoryCountLine(session As AppSession, countId As Long, itemId As Long, countedQty As Decimal)
        InventoryCountService.SetCountedQty(session, countId, itemId, countedQty)
    End Sub

    Public Shared Function PostInventoryCount(session As AppSession, countId As Long) As Decimal
        Return InventoryCountService.Post(session, countId)
    End Function
End Class

''' <summary>
''' Physical inventory count sessions (inventory_counts &amp; inventory_count_lines).
''' Posts variance adjustments atomically with stock_moves and journal entries.
''' </summary>
Public NotInheritable Class InventoryCountService
    Private Sub New()
    End Sub

    Public Shared Function ListCounts(session As AppSession) As List(Of InventoryCountSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return InventoryCountRepository.ListForBranch(db.Connection, session.BranchId)
        End Using
    End Function

    Public Shared Function GetLines(session As AppSession, countId As Long,
                                    Optional term As String = "") As List(Of InventoryCountLineSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return InventoryCountRepository.GetLines(db.Connection, countId, term)
        End Using
    End Function

    Public Shared Function CreateDraft(session As AppSession, notes As String,
                                       Optional preloadMovedItems As Boolean = True) As Long
        session.RequirePermission("stock.adjust")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim eventId As String = Uuid7.NewId()
            Dim period As String = Date.UtcNow.Year.ToString()
            Dim seq As Long = SequencesRepository.NextNo(db.Connection, tx, session.BranchId, "inventory_count", period)
            Dim countNo As String = "CNT-" & period & "-" & seq.ToString("000000")

            Dim countId As Long = InventoryCountRepository.InsertHeader(
                db.Connection, tx, eventId, session.BranchId, countNo, session.UserId, notes)

            If preloadMovedItems Then
                Dim balances = db.Connection.Query(Of StockSummary)(
                    "SELECT sb.item_id, i.code, i.name_ar, sb.qty_on_hand, " &
                    "       COALESCE(NULLIF(sb.last_cost,0), i.cost_price) AS last_cost " &
                    "FROM stock_balances sb JOIN items i ON i.id=sb.item_id " &
                    "WHERE sb.branch_id=@b AND i.is_active=1 ORDER BY i.name_ar LIMIT 2000",
                    New With {.b = session.BranchId}, tx).AsList()
                For Each row In balances
                    InventoryCountRepository.UpsertLine(
                        db.Connection, tx, countId, row.ItemId, row.QtyOnHand, row.QtyOnHand, row.LastCost)
                Next
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "inventory_count_create", "inventory_count", countId.ToString(),
                                Nothing, countNo, session.DeviceId)
            db.Commit()
            Return countId
        End Using
    End Function

    Public Shared Sub SetCountedQty(session As AppSession, countId As Long, itemId As Long, countedQty As Decimal)
        session.RequirePermission("stock.adjust")
        If countedQty < 0D Then Throw New BusinessException("الكمية الفعلية لا يمكن أن تكون سالبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim header = InventoryCountRepository.GetById(db.Connection, countId, tx, True)
            If header Is Nothing Then Throw New BusinessException("محضر الجرد غير موجود.")
            If header.BranchId <> session.BranchId Then Throw New BusinessException("محضر الجرد يخص فرعًا آخر.")
            If header.Status <> "draft" Then Throw New BusinessException("لا يمكن تعديل محضر جرد تم اعتماده.")

            Dim item = ItemsRepository.GetById(db.Connection, itemId, tx)
            If item Is Nothing Then Throw New BusinessException("الصنف غير موجود.")

            Dim sysQty As Decimal = StockRepository.GetQty(db.Connection, session.BranchId, itemId, tx)
            Dim cost As Decimal = StockRepository.LastCostAsOf(db.Connection, session.BranchId, itemId, Date.UtcNow, tx)
            If cost <= 0D Then cost = item.CostPrice

            InventoryCountRepository.UpsertLine(db.Connection, tx, countId, itemId, sysQty, countedQty, cost)
            db.Commit()
        End Using
    End Sub

    Public Shared Function Post(session As AppSession, countId As Long) As Decimal
        session.RequirePermission("stock.adjust")
        Dim occurredAt As Date = Date.UtcNow
        Dim businessDate As Date = occurredAt.ToLocalTime().Date

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim header = InventoryCountRepository.GetById(db.Connection, countId, tx, True)
            If header Is Nothing Then Throw New BusinessException("محضر الجرد غير موجود.")
            If header.BranchId <> session.BranchId Then Throw New BusinessException("محضر الجرد يخص فرعًا آخر.")
            If header.Status <> "draft" Then Throw New BusinessException("تم اعتماد هذا الجرد بالفعل.")

            Dim lines = InventoryCountRepository.GetLines(db.Connection, countId, String.Empty, tx)
            If lines.Count = 0 Then Throw New BusinessException("محضر الجرد فارغ.")

            Dim orderedLines = lines.OrderBy(Function(l) l.ItemId).ToList()
            Dim netVarianceValue As Decimal = 0D
            Dim adjustedItemsCount As Integer = 0

            For Each line In orderedLines
                Dim currentSysQty As Decimal = StockRepository.GetBalanceForUpdate(db.Connection, tx, session.BranchId, line.ItemId)
                Dim cost As Decimal = If(line.UnitCost > 0D, line.UnitCost,
                                         StockRepository.LastCostAsOf(db.Connection, session.BranchId, line.ItemId, occurredAt, tx))
                InventoryCountRepository.UpsertLine(db.Connection, tx, countId, line.ItemId, currentSysQty, line.CountedQty, cost)

                Dim delta As Decimal = Money.Round4(line.CountedQty - currentSysQty)
                If delta = 0D Then Continue For

                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, line.ItemId,
                                        "count_adjust", delta, cost, "inventory_count", countId, line.Id,
                                        occurredAt, businessDate, session.UserId, session.DeviceId,
                                        "تسوية جرد " & header.CountNo, delta > 0D)

                netVarianceValue += Money.Round2(delta * cost)
                adjustedItemsCount += 1
            Next

            netVarianceValue = Money.Round2(netVarianceValue)
            If netVarianceValue <> 0D Then
                Dim absVal As Decimal = Math.Abs(netVarianceValue)
                Dim inventoryAccId As Long = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx)
                Dim adjustAccId As Long = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccStockAdjust, tx)
                Dim journalLines As New List(Of JournalLineInput)()

                If netVarianceValue > 0D Then
                    journalLines.Add(New JournalLineInput With {.AccountId = inventoryAccId, .Debit = absVal, .Description = "فائض جرد مخزني " & header.CountNo})
                    journalLines.Add(New JournalLineInput With {.AccountId = adjustAccId, .Credit = absVal, .Description = "فائض جرد مخزني " & header.CountNo})
                Else
                    journalLines.Add(New JournalLineInput With {.AccountId = adjustAccId, .Debit = absVal, .Description = "عجز جرد مخزني " & header.CountNo})
                    journalLines.Add(New JournalLineInput With {.AccountId = inventoryAccId, .Credit = absVal, .Description = "عجز جرد مخزني " & header.CountNo})
                End If

                AccountingRepository.InsertEntry(
                    db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                    "تسوية فروقات جرد " & header.CountNo, "inventory_count", countId, session.UserId, journalLines)
            End If

            InventoryCountRepository.MarkPosted(db.Connection, tx, countId)

            Dim payload As String = PostingHelper.Serialize(New With {
                .count_id = countId,
                .count_no = header.CountNo,
                .adjusted_items = adjustedItemsCount,
                .net_variance_value = netVarianceValue
            })

            AuditRepository.Log(db.Connection, tx, header.EventId, session.BranchId, session.UserId, session.Username,
                                "inventory_count_post", "inventory_count", countId.ToString(),
                                "draft", payload, session.DeviceId)

            SyncStore.Enqueue(db.Connection, tx, header.EventId, session.BranchId, session.DeviceId,
                              "inventory_count", countId.ToString(), "stock.count_posted", 1, payload, occurredAt)

            db.Commit()
            Return netVarianceValue
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, countId As Long) As InventoryCountSummary
        Using db = DbSession.Open(session.ConnectionString)
            Return InventoryCountRepository.GetById(db.Connection, countId)
        End Using
    End Function

    ''' <summary>Saves the counted quantities of an open inventory count in one transaction.</summary>
    Public Shared Sub SaveCounted(session As AppSession, countId As Long,
                                  lines As IList(Of InventoryCountLineInput))
        session.RequirePermission("stock.adjust")
        If lines Is Nothing OrElse lines.Count = 0 Then Return

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim header = InventoryCountRepository.GetById(db.Connection, countId, tx, True)
            If header Is Nothing Then Throw New BusinessException("محضر الجرد غير موجود.")
            If header.BranchId <> session.BranchId Then Throw New BusinessException("محضر الجرد يخص فرعًا آخر.")
            If header.Status <> "draft" Then Throw New BusinessException("لا يمكن تعديل محضر جرد تم اعتماده.")

            For Each line In lines
                If line Is Nothing Then Continue For
                If line.CountedQty < 0D Then Throw New BusinessException("الكمية الفعلية لا يمكن أن تكون سالبة.")
                InventoryCountRepository.UpdateCounted(db.Connection, tx, countId, line.LineId, line.CountedQty)
            Next
            db.Commit()
        End Using
    End Sub
End Class

Public Class TransferLineInput
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitCost As Decimal?
End Class

Public Class TransferRecord
    Public Property Id As Long
    Public Property EventId As String
    Public Property TransferNo As String
    Public Property FromBranch As Long
    Public Property ToBranch As Long
    Public Property Status As String
    Public Property CreatedAt As Date
    Public Property PostedAt As Date?
    Public Property ReceivedAt As Date?
    Public Property Notes As String
End Class

Public Class TransferLineRecord
    Public Property Id As Long
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitCost As Decimal
End Class

Public Class InventoryCountLineInput
    Public Property LineId As Long
    Public Property CountedQty As Decimal
End Class
