Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports Dapper

''' <summary>Purchases, purchase returns and opening stock.</summary>
Public NotInheritable Class PurchaseService
    Private Sub New()
    End Sub

    Public Shared Function PostPurchase(session As AppSession, request As InvoiceRequest) As InvoiceResult
        session.RequirePermission("purchases.create")
        If request Is Nothing OrElse request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("فاتورة الشراء لا تحتوي على أصناف.")
        End If

        Dim occurredAt As Date = PostingHelper.AsUtc(If(request.OccurredAt.HasValue, request.OccurredAt.Value, Date.UtcNow))
        Dim businessDate As Date = If(request.BusinessDate.HasValue, request.BusinessDate.Value,
                                      PostingHelper.LocalBusinessDate(session, occurredAt))

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            PostingHelper.EnsureShiftInBranch(db.Connection, tx, request.ShiftId, session.BranchId)

            Dim recoverable As Boolean = SettingsRepository.GetBool(db.Connection, "tax.recoverable", True, tx)

            Dim prepared As New List(Of PreparedPurchaseLine)()
            For Each line In request.Lines
                If line Is Nothing OrElse line.Qty <= 0D Then Throw New BusinessException("كمية الشراء يجب أن تكون أكبر من صفر.")
                Dim item = ItemsRepository.GetById(db.Connection, line.ItemId, tx)
                If item Is Nothing Then Throw New BusinessException("الصنف رقم " & line.ItemId.ToString() & " غير موجود.")

                Dim unitCost As Decimal = If(line.UnitPrice.HasValue, line.UnitPrice.Value, item.CostPrice)
                If unitCost < 0D Then Throw New BusinessException("سعر الشراء لا يمكن أن يكون سالبًا.")

                Dim taxCategoryId As Long = If(line.TaxCategoryId.HasValue AndAlso line.TaxCategoryId.Value > 0,
                                               line.TaxCategoryId.Value, item.TaxCategoryId)
                Dim taxCategory = TaxRepository.GetById(db.Connection, taxCategoryId, tx)
                If taxCategory Is Nothing Then Throw New BusinessException("التصنيف الضريبي غير موجود.")

                prepared.Add(New PreparedPurchaseLine With {
                    .Item = item, .Request = line, .UnitCost = unitCost,
                    .TaxCategoryId = taxCategoryId, .TaxRate = taxCategory.Rate
                })
            Next

            Dim calcLines As New List(Of InvoiceLineInput)()
            For Each p In prepared
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = p.Request.Qty, .UnitPrice = p.UnitCost,
                    .LineDiscount = p.Request.LineDiscount, .TaxRate = p.TaxRate
                })
            Next

            Dim totals As InvoiceTotals
            Try
                totals = InvoiceCalculator.Calculate(calcLines, request.HeaderDiscountAmount, session.PricesIncludeTax)
            Catch ex As ArgumentException
                Throw New BusinessException(ex.Message)
            End Try

            Dim paid As Decimal = Money.Round2(request.Paid)
            If paid < 0D OrElse paid > totals.Total Then Throw New BusinessException("المدفوع أكبر من إجمالي الفاتورة.")
            Dim due As Decimal = Money.Round2(totals.Total - paid)

            Dim eventId As String = Uuid7.NewId()
            Dim invoiceNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId, "purchase", "P", businessDate)
            Dim priceMode As String = If(session.PricesIncludeTax, "inclusive", "exclusive")

            Dim invoiceId As Long = InvoicesRepository.Insert(
                db.Connection, tx, eventId, session.BranchId, invoiceNo, "purchase", "posted",
                request.CustomerId, Nothing, Nothing, Nothing, request.ShiftId,
                totals.Subtotal, totals.LineDiscount, totals.HeaderDiscount, totals.TaxBaseTotal,
                totals.TaxTotal, totals.Total, paid, due, PaymentStatusFor(paid, due), priceMode,
                Nothing, request.BuyerTaxNo, request.BuyerName, request.BuyerAddress, Nothing,
                occurredAt, businessDate, session.UserId, session.DeviceId, request.Notes)

            Dim result As New InvoiceResult With {
                .InvoiceId = invoiceId, .InvoiceNo = invoiceNo, .EventId = eventId, .Subtotal = totals.Subtotal,
                .LineDiscount = totals.LineDiscount, .HeaderDiscount = totals.HeaderDiscount,
                .TaxBaseTotal = totals.TaxBaseTotal, .TaxTotal = totals.TaxTotal, .Total = totals.Total,
                .Paid = paid, .Due = due
            }

            Dim inventoryValue As Decimal = 0D
            For i As Integer = 0 To prepared.Count - 1
                Dim p = prepared(i)
                Dim r = totals.Lines(i)

                ' Cost stored on the movement is the unit cost excluding recoverable input tax.
                Dim netUnit As Decimal = r.NetAfterDiscounts / p.Request.Qty
                Dim unitStockCost As Decimal = If(recoverable AndAlso p.TaxRate > 0D, r.TaxBase / p.Request.Qty, netUnit)

                Dim lineId As Long = InvoicesRepository.InsertLine(
                    db.Connection, tx, invoiceId, p.Item.Id, p.Item.BaseUnitId, p.Request.Qty, 0D,
                    p.UnitCost, p.Request.LineDiscount, p.TaxCategoryId, p.TaxRate, r.TaxBase, r.TaxAmount,
                    priceMode, r.LineTotal, unitStockCost, netUnit, Nothing,
                    p.Request.BatchNo, p.Request.ExpiryDate, Nothing)

                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, p.Item.Id, "purchase",
                                        p.Request.Qty, unitStockCost, "invoice", invoiceId, lineId,
                                        occurredAt, businessDate, session.UserId, session.DeviceId, invoiceNo, True)

                inventoryValue += Money.Round2(unitStockCost * p.Request.Qty)
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = lineId, .ItemId = p.Item.Id, .Qty = p.Request.Qty, .UnitPrice = p.UnitCost,
                    .Discount = p.Request.LineDiscount, .TaxRate = p.TaxRate, .TaxBase = r.TaxBase,
                    .TaxAmount = r.TaxAmount, .LineTotal = r.LineTotal, .CostAtSale = unitStockCost
                })
            Next

            ' Ledger value must match the invoice totals exactly (single rounding point).
            inventoryValue = If(recoverable, totals.TaxBaseTotal, totals.Total)

            If paid > 0D Then
                Dim methodId As Long = If(request.PaymentMethodId > 0, request.PaymentMethodId,
                                          PostingHelper.DefaultPaymentMethod(db.Connection, tx))
                InvoicesRepository.InsertPayment(db.Connection, tx, Uuid7.NewId(), invoiceId, session.BranchId,
                                                 methodId, -paid, Nothing, occurredAt, session.UserId)
                If PostingHelper.IsCashMethod(db.Connection, methodId, tx) Then
                    ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(),
                        PostingHelper.CashboxOrThrow(db.Connection, session.BranchId, tx),
                        request.ShiftId, "out", paid, "invoice", invoiceId, session.UserId)
                End If
            End If

            Dim journalLines As New List(Of JournalLineInput)()
            If inventoryValue <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Debit = inventoryValue, .Description = "مخزون " & invoiceNo
                })
            End If
            If recoverable AndAlso totals.TaxTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInputTax, tx),
                    .Debit = totals.TaxTotal, .Description = "ضريبة مدخلات " & invoiceNo
                })
            End If
            If paid > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCash, tx),
                    .Credit = paid, .Description = "سداد شراء " & invoiceNo
                })
            End If
            Dim payable As Decimal = due
            If Not recoverable AndAlso totals.TaxTotal <> 0D Then
                ' Input tax is not recoverable: it is part of the payable but not of inventory ledger
                ' only when it was already included in inventoryValue. Keep payable equal to total.
                payable = Money.Round2(totals.Total - paid)
            End If
            If payable > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccPayable, tx),
                    .Credit = payable, .Description = "مستحق لمورد " & invoiceNo
                })
            End If

            If journalLines.Count > 0 Then
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "فاتورة شراء " & invoiceNo, "invoice", invoiceId, session.UserId, journalLines)
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "purchase_post", "invoice", invoiceId.ToString(), Nothing,
                                PostingHelper.Serialize(result), session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "invoice",
                              invoiceId.ToString(), "purchase.posted", 1, PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function

    Public Shared Function PostPurchaseReturn(session As AppSession, originalInvoiceId As Long,
                                              request As InvoiceRequest) As InvoiceResult
        session.RequirePermission("purchases.return")
        If request Is Nothing OrElse request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("اختر الأصناف المرتجعة للمورد.")
        End If

        Dim occurredAt As Date = PostingHelper.AsUtc(If(request.OccurredAt.HasValue, request.OccurredAt.Value, Date.UtcNow))
        Dim businessDate As Date = If(request.BusinessDate.HasValue, request.BusinessDate.Value,
                                      PostingHelper.LocalBusinessDate(session, occurredAt))

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            PostingHelper.EnsureShiftInBranch(db.Connection, tx, request.ShiftId, session.BranchId)

            Dim original = InvoicesRepository.GetById(db.Connection, originalInvoiceId, tx)
            If original Is Nothing OrElse original.InvoiceType <> "purchase" Then
                Throw New BusinessException("الفاتورة الأصلية ليست فاتورة شراء.")
            End If
            If original.Status <> "posted" Then Throw New BusinessException("الفاتورة الأصلية غير مرحّلة.")
            If original.BranchId <> session.BranchId Then Throw New BusinessException("الفاتورة الأصلية لفرع آخر.")

            Dim returnable = InvoicesRepository.GetReturnableLines(db.Connection, originalInvoiceId, tx)
            Dim byLine As New Dictionary(Of Long, ReturnableLine)()
            For Each r In returnable
                byLine(r.LineId) = r
            Next

            Dim prepared As New List(Of PreparedReturnLine)()
            For Each line In request.Lines
                If line.Qty <= 0D Then Throw New BusinessException("كمية الإرجاع يجب أن تكون أكبر من صفر.")
                If Not line.OriginalLineId.HasValue Then Throw New BusinessException("بند الإرجاع غير مرتبط بالأصل.")
                Dim source As ReturnableLine = Nothing
                If Not byLine.TryGetValue(line.OriginalLineId.Value, source) Then
                    Throw New BusinessException("بند الإرجاع لا ينتمي إلى الفاتورة الأصلية.")
                End If
                If line.Qty > source.Qty - source.ReturnedQty Then
                    Throw New BusinessException("كمية الإرجاع أكبر من المتاح للصنف «" & source.ItemName & "».")
                End If

                Dim available = StockRepository.GetBalanceForUpdate(db.Connection, tx, session.BranchId, source.ItemId)
                If line.Qty > available Then
                    Throw New BusinessException("لا يوجد رصيد كافٍ لإرجاع الصنف «" & source.ItemName & "». المتاح: " &
                                                available.ToString("0.####"))
                End If

                prepared.Add(New PreparedReturnLine With {
                    .Original = source, .Qty = line.Qty, .UnitCost = source.UnitPrice,
                    .TaxCategoryId = source.TaxCategoryId, .TaxRate = source.TaxRate, .OriginalLineId = source.LineId
                })
            Next

            Dim calcLines As New List(Of InvoiceLineInput)()
            For Each p In prepared
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = p.Qty, .UnitPrice = p.UnitCost, .LineDiscount = 0D, .TaxRate = p.TaxRate
                })
            Next

            Dim totals As InvoiceTotals
            Try
                totals = InvoiceCalculator.Calculate(calcLines, 0D, session.PricesIncludeTax)
            Catch ex As ArgumentException
                Throw New BusinessException(ex.Message)
            End Try

            Dim received As Decimal = Money.Round2(request.Paid)
            If received < 0D OrElse received > totals.Total Then
                Throw New BusinessException("المبلغ المستلم أكبر من قيمة المرتجع.")
            End If
            Dim supplierCredit As Decimal = Money.Round2(totals.Total - received)

            Dim eventId As String = Uuid7.NewId()
            Dim invoiceNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId, "purchase_return", "PR", businessDate)
            Dim priceMode As String = If(session.PricesIncludeTax, "inclusive", "exclusive")

            Dim invoiceId As Long = InvoicesRepository.Insert(
                db.Connection, tx, eventId, session.BranchId, invoiceNo, "purchase_return", "posted",
                original.CustomerId, Nothing, Nothing, originalInvoiceId, request.ShiftId,
                -totals.Subtotal, -totals.LineDiscount, 0D, -totals.TaxBaseTotal, -totals.TaxTotal,
                -totals.Total, -received, -supplierCredit, PaymentStatusFor(received, supplierCredit),
                priceMode, Nothing, Nothing, Nothing, Nothing, Nothing, occurredAt, businessDate,
                session.UserId, session.DeviceId, request.Notes)

            Dim result As New InvoiceResult With {
                .InvoiceId = invoiceId, .InvoiceNo = invoiceNo, .EventId = eventId, .Subtotal = -totals.Subtotal,
                .TaxBaseTotal = -totals.TaxBaseTotal, .TaxTotal = -totals.TaxTotal, .Total = -totals.Total,
                .Paid = -received, .Due = -supplierCredit
            }

            Dim recoverable As Boolean = SettingsRepository.GetBool(db.Connection, "tax.recoverable", True, tx)
            Dim inventoryValue As Decimal = 0D

            For i As Integer = 0 To prepared.Count - 1
                Dim p = prepared(i)
                Dim r = totals.Lines(i)
                Dim netUnit As Decimal = r.NetAfterDiscounts / p.Qty
                Dim unitCost As Decimal = If(recoverable AndAlso p.TaxRate > 0D, r.TaxBase / p.Qty, netUnit)

                Dim lineId As Long = InvoicesRepository.InsertLine(
                    db.Connection, tx, invoiceId, p.Original.ItemId, Nothing, -p.Qty, 0D,
                    p.UnitCost, 0D, p.TaxCategoryId, p.TaxRate, -r.TaxBase, -r.TaxAmount,
                    priceMode, -r.LineTotal, unitCost, netUnit, p.OriginalLineId,
                    Nothing, Nothing, "مرتجع شراء")

                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, p.Original.ItemId,
                                        "purchase_return", -p.Qty, unitCost, "invoice", invoiceId, lineId,
                                        occurredAt, businessDate, session.UserId, session.DeviceId, invoiceNo, False)

                inventoryValue += Money.Round2(unitCost * p.Qty)
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = lineId, .ItemId = p.Original.ItemId, .Qty = -p.Qty, .UnitPrice = p.UnitCost,
                    .TaxRate = p.TaxRate, .TaxBase = -r.TaxBase, .TaxAmount = -r.TaxAmount,
                    .LineTotal = -r.LineTotal, .CostAtSale = unitCost
                })
            Next

            inventoryValue = If(recoverable, totals.TaxBaseTotal, totals.Total)

            If received > 0D Then
                Dim refundMethodId As Long = If(request.PaymentMethodId > 0, request.PaymentMethodId,
                                                 PostingHelper.DefaultPaymentMethod(db.Connection, tx))
                InvoicesRepository.InsertPayment(db.Connection, tx, Uuid7.NewId(), invoiceId, session.BranchId,
                                                 refundMethodId, received, Nothing, occurredAt, session.UserId)
                If PostingHelper.IsCashMethod(db.Connection, refundMethodId, tx) Then
                    ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(),
                        PostingHelper.CashboxOrThrow(db.Connection, session.BranchId, tx),
                        request.ShiftId, "in", received, "invoice", invoiceId, session.UserId)
                End If
            End If

            Dim journalLines As New List(Of JournalLineInput)()
            If inventoryValue <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Credit = inventoryValue, .Description = "إرجاع مخزون للمورد " & invoiceNo
                })
            End If
            If recoverable AndAlso totals.TaxTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInputTax, tx),
                    .Credit = totals.TaxTotal, .Description = "عكس ضريبة مدخلات " & invoiceNo
                })
            End If
            If received > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCash, tx),
                    .Debit = received, .Description = "تحصيل من مورد " & invoiceNo
                })
            End If
            If supplierCredit > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccPayable, tx),
                    .Debit = supplierCredit, .Description = "تخفيض مستحق مورد " & invoiceNo
                })
            End If
            If journalLines.Count > 0 Then
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "مرتجع شراء " & invoiceNo, "invoice", invoiceId, session.UserId, journalLines)
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "purchase_return_post", "invoice", invoiceId.ToString(), Nothing,
                                PostingHelper.Serialize(result), session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "invoice",
                              invoiceId.ToString(), "purchase_return.posted", 1, PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function

    ''' <summary>Opening stock for a new branch: no supplier, credited to capital.</summary>
    Public Shared Function PostOpeningStock(session As AppSession, request As InvoiceRequest) As InvoiceResult
        session.RequirePermission("stock.opening")
        If request Is Nothing OrElse request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("أدخل أصناف المخزون الأول على الأقل.")
        End If

        Dim occurredAt As Date = PostingHelper.AsUtc(If(request.OccurredAt.HasValue, request.OccurredAt.Value, Date.UtcNow))
        Dim businessDate As Date = If(request.BusinessDate.HasValue, request.BusinessDate.Value,
                                      PostingHelper.LocalBusinessDate(session, occurredAt))

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim eventId As String = Uuid7.NewId()
            Dim invoiceNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId, "opening", "OP", businessDate)
            Dim invoiceId As Long = InvoicesRepository.Insert(
                db.Connection, tx, eventId, session.BranchId, invoiceNo, "opening", "posted", Nothing, Nothing, Nothing, Nothing, Nothing,
                0D, 0D, 0D, 0D, 0D, 0D, 0D, 0D, "paid", "exclusive", Nothing, Nothing, Nothing, Nothing,
                Nothing, occurredAt, businessDate, session.UserId, session.DeviceId, request.Notes)

            Dim result As New InvoiceResult With {.InvoiceId = invoiceId, .InvoiceNo = invoiceNo, .EventId = eventId}
            Dim inventoryValue As Decimal = 0D
            Dim totalCost As Decimal = 0D

            For Each line In request.Lines
                If line.Qty <= 0D Then Throw New BusinessException("الكمية يجب أن تكون أكبر من صفر.")
                Dim item = ItemsRepository.GetById(db.Connection, line.ItemId, tx)
                If item Is Nothing Then Throw New BusinessException("الصنف غير موجود.")

                Dim unitCost As Decimal = If(line.UnitPrice.HasValue, line.UnitPrice.Value, item.CostPrice)
                If unitCost < 0D Then Throw New BusinessException("التكلفة لا يمكن أن تكون سالبة.")

                Dim lineId As Long = InvoicesRepository.InsertLine(
                    db.Connection, tx, invoiceId, item.Id, item.BaseUnitId, line.Qty, 0D, unitCost, 0D,
                    item.TaxCategoryId, 0D, 0D, 0D, "exclusive", Money.Round2(unitCost * line.Qty),
                    unitCost, unitCost, Nothing, line.BatchNo, line.ExpiryDate, "مخزون أول")

                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, item.Id, "opening",
                                        line.Qty, unitCost, "invoice", invoiceId, lineId, occurredAt, businessDate,
                                        session.UserId, session.DeviceId, invoiceNo, True)

                inventoryValue += Money.Round2(unitCost * line.Qty)
                totalCost += Money.Round2(unitCost * line.Qty)
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = lineId, .ItemId = item.Id, .Qty = line.Qty, .UnitPrice = unitCost,
                    .LineTotal = Money.Round2(unitCost * line.Qty), .CostAtSale = unitCost
                })
            Next

            ' The header is inserted before the lines, so its totals must be written afterwards.
            result.Subtotal = inventoryValue
            result.TaxBaseTotal = inventoryValue
            result.TaxTotal = 0D
            result.Total = inventoryValue
            result.Paid = inventoryValue
            result.Due = 0D
            InvoicesRepository.UpdateTotals(db.Connection, tx, invoiceId, inventoryValue, inventoryValue, 0D,
                                            inventoryValue, inventoryValue, 0D, "paid")

            If inventoryValue <> 0D Then
                Dim journalLines As New List(Of JournalLineInput)()
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Debit = inventoryValue, .Description = "مخزون أول " & invoiceNo
                })
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCapital, tx),
                    .Credit = inventoryValue, .Description = "مخزون أول " & invoiceNo
                })
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "مخزون أول " & invoiceNo, "invoice", invoiceId, session.UserId, journalLines)
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "opening_stock_post", "invoice", invoiceId.ToString(), Nothing,
                                PostingHelper.Serialize(result), session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "invoice",
                              invoiceId.ToString(), "opening.posted", 1, PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function

    Private Shared Function PaymentStatusFor(paid As Decimal, due As Decimal) As String
        If due <= 0.004D Then Return "paid"
        If paid > 0.004D Then Return "partial"
        Return "unpaid"
    End Function

    Private Class PreparedPurchaseLine
        Public Property Item As ItemRecord
        Public Property Request As InvoiceLineRequest
        Public Property UnitCost As Decimal
        Public Property TaxCategoryId As Long
        Public Property TaxRate As Decimal
    End Class

    Private Class PreparedReturnLine
        Public Property Original As ReturnableLine
        Public Property Qty As Decimal
        Public Property UnitCost As Decimal
        Public Property TaxCategoryId As Long
        Public Property TaxRate As Decimal
        Public Property OriginalLineId As Long
    End Class
End Class
