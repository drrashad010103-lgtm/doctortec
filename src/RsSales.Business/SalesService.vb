Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports Dapper

''' <summary>
''' Sales and sales returns. A sale is one transaction: invoice, lines, stock
''' movements, payment, journal entry, audit and outbox. Any failure rolls back
''' everything.
''' </summary>
Public NotInheritable Class SalesService
    Private Sub New()
    End Sub

    Public Shared Function PostSale(session As AppSession, request As InvoiceRequest) As InvoiceResult
        session.RequirePermission("sales.create")
        If request Is Nothing Then Throw New BusinessException("بيانات الفاتورة مطلوبة.")
        If request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("الفاتورة لا تحتوي على أصناف.")
        End If

        Dim occurredAt As Date = PostingHelper.AsUtc(If(request.OccurredAt.HasValue, request.OccurredAt.Value, Date.UtcNow))
        Dim businessDate As Date = If(request.BusinessDate.HasValue, request.BusinessDate.Value,
                                      PostingHelper.LocalBusinessDate(session, occurredAt))

        If request.HeaderDiscountAmount > 0D AndAlso Not session.HasPermission("sales.discount") Then
            Throw New BusinessException("ليس لديك صلاحية منح خصم على الفاتورة.")
        End If

        Using db = DbSession.Open(session.ConnectionString)
            ' Idempotency guard: if ClientEventId was already posted, return existing invoice without duplicating
            If Not String.IsNullOrWhiteSpace(request.ClientEventId) Then
                Dim existingId As Long = db.Connection.ExecuteScalar(Of Long)(
                    "SELECT COALESCE((SELECT id FROM invoices WHERE event_id=@ev LIMIT 1), 0)",
                    New With {.ev = request.ClientEventId.Trim()})
                If existingId > 0 Then
                    Return GetInvoice(session, existingId)
                End If
            End If

            db.BeginTransaction()
            Dim tx = db.Transaction
            PostingHelper.EnsureShiftInBranch(db.Connection, tx, request.ShiftId, session.BranchId)
            Dim sellerId As Long? = ValidateHrAttribution(db, tx, request.SellerId, "seller", "البائع")
            Dim deliveryId As Long? = ValidateHrAttribution(db, tx, request.DeliveryId, "delivery", "الدليفري")

            ' ---- 1. validate items, prices, tax categories -------------------
            Dim prepared As New List(Of PreparedLine)()
            For Each line In request.Lines
                If line Is Nothing Then Throw New BusinessException("بند غير صالح.")
                If line.Qty <= 0D Then Throw New BusinessException("الكمية يجب أن تكون أكبر من صفر.")

                Dim item = ItemsRepository.GetById(db.Connection, line.ItemId, tx)
                If item Is Nothing OrElse Not item.IsActive Then
                    Throw New BusinessException("الصنف رقم " & line.ItemId.ToString() & " غير موجود أو غير مفعّل.")
                End If

                Dim unitPrice As Decimal = If(line.UnitPrice.HasValue, line.UnitPrice.Value, item.SalePrice)
                If unitPrice < 0D Then Throw New BusinessException("سعر البيع لا يمكن أن يكون سالبًا.")
                If unitPrice < item.MinPrice AndAlso Not session.HasPermission("sales.below_min_price") Then
                    Throw New BusinessException("سعر الصنف " & item.NameAr & " أقل من الحد الأدنى المسموح.")
                End If
                If line.LineDiscount < 0D Then Throw New BusinessException("خصم البند لا يمكن أن يكون سالبًا.")
                If line.LineDiscount > Money.Round2(line.Qty * unitPrice) Then
                    Throw New BusinessException("خصم البند أكبر من قيمة البند للصنف " & item.NameAr & ".")
                End If
                If line.ExpiryDate.HasValue AndAlso line.ExpiryDate.Value.Date < businessDate Then
                    Throw New BusinessException("الصنف «" & item.NameAr & "» منتهي الصلاحية بتاريخ " &
                                                line.ExpiryDate.Value.ToString("yyyy-MM-dd") & ".")
                End If

                Dim taxCategoryId As Long = If(line.TaxCategoryId.HasValue AndAlso line.TaxCategoryId.Value > 0,
                                               line.TaxCategoryId.Value, item.TaxCategoryId)
                Dim taxCategory = TaxRepository.GetById(db.Connection, taxCategoryId, tx)
                If taxCategory Is Nothing Then Throw New BusinessException("التصنيف الضريبي للصنف غير موجود.")

                prepared.Add(New PreparedLine With {
                    .Item = item, .Request = line, .UnitPrice = unitPrice,
                    .TaxCategoryId = taxCategoryId, .TaxRate = taxCategory.Rate, .QtyBase = line.Qty
                })
            Next

            ' ---- 2. stock: lock in a stable order and reject overselling ------
            Dim requiredQty As New Dictionary(Of Long, Decimal)()
            For Each p In prepared
                If requiredQty.ContainsKey(p.Item.Id) Then
                    requiredQty(p.Item.Id) += p.QtyBase
                Else
                    requiredQty(p.Item.Id) = p.QtyBase
                End If
            Next

            Dim allowNegativeStock As Boolean =
                SettingsRepository.GetBool(db.Connection, "company.allow_sale_without_stock",
                    SettingsRepository.GetBool(db.Connection, "stock.allow_negative", False, tx), tx)

            For Each itemId In requiredQty.Keys.OrderBy(Function(k) k)
                Dim available = StockRepository.GetBalanceForUpdate(db.Connection, tx, session.BranchId, itemId)
                Dim needed = requiredQty(itemId)
                If Not allowNegativeStock AndAlso needed > available Then
                    Dim item = prepared.First(Function(p) p.Item.Id = itemId).Item
                    Throw New BusinessException(
                        "الرصيد غير كافٍ للصنف «" & item.NameAr & "». المتاح: " &
                        available.ToString("0.####") & " والمطلوب: " & needed.ToString("0.####") & ".")
                End If
            Next

            ' ---- 2b. coupon & loyalty point redemption ------------------------
            Dim grossAfterLineDiscount As Decimal = 0D
            For Each p In prepared
                grossAfterLineDiscount += Money.Round2(p.QtyBase * p.UnitPrice) - Money.Round2(p.Request.LineDiscount)
            Next

            Dim effectiveHeaderDiscount As Decimal = Money.Round2(request.HeaderDiscountAmount)
            Dim matchedCoupon As CouponSummary = Nothing
            Dim couponDiscountAmount As Decimal = 0D

            If Not String.IsNullOrWhiteSpace(request.CouponCode) Then
                matchedCoupon = CouponsRepository.FindByCodeForUpdate(db.Connection, tx, request.CouponCode.Trim())
                If matchedCoupon Is Nothing Then
                    Throw New BusinessException("كود الكوبون «" & request.CouponCode.Trim() & "» غير موجود.")
                End If
                If Not matchedCoupon.IsActive Then
                    Throw New BusinessException("الكوبون «" & matchedCoupon.Code & "» غير مفعّل.")
                End If
                If matchedCoupon.StartsAt.HasValue AndAlso matchedCoupon.StartsAt.Value.Date > businessDate Then
                    Throw New BusinessException("لم يبدأ سريان الكوبون «" & matchedCoupon.Code & "» بعد.")
                End If
                If matchedCoupon.EndsAt.HasValue AndAlso matchedCoupon.EndsAt.Value.Date < businessDate Then
                    Throw New BusinessException("انتهت صلاحية الكوبون «" & matchedCoupon.Code & "».")
                End If
                If matchedCoupon.MaxUses > 0 AndAlso matchedCoupon.UsedCount >= matchedCoupon.MaxUses Then
                    Throw New BusinessException("استُنفد الحد الأقصى لاستخدام الكوبون «" & matchedCoupon.Code & "».")
                End If

                Dim baseForCoupon As Decimal = Math.Max(0D, grossAfterLineDiscount - effectiveHeaderDiscount)
                If matchedCoupon.DiscountType = "percent" Then
                    couponDiscountAmount = Money.Round2(baseForCoupon * (matchedCoupon.DiscountValue / 100D))
                Else
                    couponDiscountAmount = Math.Min(Money.Round2(matchedCoupon.DiscountValue), baseForCoupon)
                End If
                effectiveHeaderDiscount = Money.Round2(effectiveHeaderDiscount + couponDiscountAmount)
            End If

            Dim redeemedPoints As Decimal = Money.Round2(request.RedeemLoyaltyPoints)
            If redeemedPoints < 0D Then Throw New BusinessException("نقاط الولاء المستبدلة لا يمكن أن تكون سالبة.")
            If redeemedPoints > 0D Then
                If Not request.CustomerId.HasValue OrElse request.CustomerId.Value <= 0 Then
                    Throw New BusinessException("اختر العميل أولاً لاستبدال نقاط الولاء.")
                End If
                Dim currentPoints As Decimal = LoyaltyRepository.GetPoints(db.Connection, tx, request.CustomerId.Value)
                If redeemedPoints > currentPoints Then
                    Throw New BusinessException("رصيد نقاط العميل (" & currentPoints.ToString("0.##") &
                                                ") لا يكفي لاستبدال " & redeemedPoints.ToString("0.##") & " نقطة.")
                End If
                Dim pointValue As Decimal = SettingsRepository.GetDecimal(db.Connection, "loyalty.point_value", 1D, tx)
                If pointValue <= 0D Then pointValue = 1D
                Dim loyaltyDiscount As Decimal = Money.Round2(redeemedPoints * pointValue)
                Dim maxRemaining As Decimal = Math.Max(0D, grossAfterLineDiscount - effectiveHeaderDiscount)
                If loyaltyDiscount > maxRemaining Then loyaltyDiscount = maxRemaining
                effectiveHeaderDiscount = Money.Round2(effectiveHeaderDiscount + loyaltyDiscount)
            End If

            ' ---- 3. totals -----------------------------------------------------
            Dim calcLines As New List(Of InvoiceLineInput)()
            For Each p In prepared
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = p.QtyBase, .UnitPrice = p.UnitPrice, .LineDiscount = p.Request.LineDiscount,
                    .TaxRate = p.TaxRate
                })
            Next

            Dim totals As InvoiceTotals
            Try
                totals = InvoiceCalculator.Calculate(calcLines, effectiveHeaderDiscount, session.PricesIncludeTax)
            Catch ex As ArgumentException
                Throw New BusinessException(ex.Message)
            End Try

            ' ---- 3b. purchase voucher settlement ------------------------------
            Dim matchedVoucher As PurchaseVoucherSummary = Nothing
            Dim voucherPaid As Decimal = 0D
            If Not String.IsNullOrWhiteSpace(request.VoucherCode) Then
                matchedVoucher = PurchaseVouchersRepository.FindByCode(db.Connection, request.VoucherCode.Trim(), tx)
                If matchedVoucher Is Nothing Then
                    Throw New BusinessException("قسيمة الشراء «" & request.VoucherCode.Trim() & "» غير موجودة.")
                End If
                If Not matchedVoucher.IsActive Then
                    Throw New BusinessException("قسيمة الشراء «" & matchedVoucher.Code & "» موقوفة.")
                End If
                If matchedVoucher.ExpiresAt.HasValue AndAlso matchedVoucher.ExpiresAt.Value.Date < businessDate Then
                    Throw New BusinessException("قسيمة الشراء «" & matchedVoucher.Code & "» منتهية الصلاحية.")
                End If
                If matchedVoucher.RemainingAmount <= 0D Then
                    Throw New BusinessException("رصيد قسيمة الشراء «" & matchedVoucher.Code & "» مستنفد.")
                End If

                If request.VoucherAmount > 0D Then
                    voucherPaid = Money.Round2(request.VoucherAmount)
                    If voucherPaid > matchedVoucher.RemainingAmount Then
                        Throw New BusinessException("مبلغ القسيمة المطلوب أكبر من رصيدها المتاح (" &
                                                    matchedVoucher.RemainingAmount.ToString("0.00") & ").")
                    End If
                Else
                    voucherPaid = Math.Min(matchedVoucher.RemainingAmount, totals.Total)
                End If
                If voucherPaid > totals.Total Then voucherPaid = totals.Total
            End If

            Dim cashOrMethodPaid As Decimal = Money.Round2(request.Paid)
            If cashOrMethodPaid < 0D Then Throw New BusinessException("المدفوع لا يمكن أن يكون سالبًا.")
            If cashOrMethodPaid + voucherPaid > totals.Total Then
                Throw New BusinessException("إجمالي المدفوع (نقد + قسيمة) أكبر من إجمالي الفاتورة.")
            End If
            Dim totalSettled As Decimal = Money.Round2(cashOrMethodPaid + voucherPaid)
            Dim due As Decimal = Money.Round2(totals.Total - totalSettled)

            ' ---- 3c. credit limit ----------------------------------------------
            If due > 0D AndAlso request.CustomerId.HasValue AndAlso request.CustomerId.Value > 0 AndAlso
               Not AuthorizationService.Can(session, Permissions.SalesCreditOverride) Then
                Dim creditLimit As Decimal = db.Connection.ExecuteScalar(Of Decimal)(
                    "SELECT COALESCE(credit_limit,0) FROM customers WHERE id=@c",
                    New With {.c = request.CustomerId.Value}, tx)
                ' عميل بلا سقف مسجّل = السقف الافتراضي للمحل، مش «بلا حدود».
                If creditLimit <= 0D Then
                    creditLimit = SettingsRepository.GetDecimal(db.Connection, "sales.default_credit_limit", 500D, tx)
                End If
                If creditLimit > 0D Then
                    Dim currentBalance As Decimal = CustomersRepository.Balance(db.Connection,
                                                                                request.CustomerId.Value, tx)
                    If currentBalance + due > creditLimit Then
                        Throw New BusinessException(
                            "سقف الائتمان للعميل (" & creditLimit.ToString("0.##") & ") لا يكفي: رصيد العميل " &
                            currentBalance.ToString("0.##") & " والمطلوب آجلًا " & due.ToString("0.##") &
                            ". حصّل من العميل أو استخدم صلاحية تجاوز سقف الائتمان.")
                    End If
                End If
            End If

            Dim paymentMethodId As Long = request.PaymentMethodId
            If paymentMethodId <= 0 Then paymentMethodId = PostingHelper.DefaultPaymentMethod(db.Connection, tx)

            ' ---- 4. header -----------------------------------------------------
            Dim eventId As String = If(Not String.IsNullOrWhiteSpace(request.ClientEventId), request.ClientEventId.Trim(), Uuid7.NewId())
            Dim invoiceNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId, "sale", "S", businessDate)
            Dim qr As String = PostingHelper.BuildQr(session, session.BranchName, session.BranchTaxNo,
                                                     occurredAt, totals.Total, totals.TaxTotal)
            Dim priceMode As String = If(session.PricesIncludeTax, "inclusive", "exclusive")

            Dim invoiceId As Long = InvoicesRepository.Insert(
                db.Connection, tx, eventId, session.BranchId, invoiceNo, "sale", "posted",
                request.CustomerId, sellerId, deliveryId, Nothing, request.ShiftId,
                totals.Subtotal, totals.LineDiscount, totals.HeaderDiscount,
                totals.TaxBaseTotal, totals.TaxTotal, totals.Total, totalSettled, due,
                PaymentStatusFor(totalSettled, due), priceMode, session.BranchTaxNo, request.BuyerTaxNo,
                request.BuyerName, request.BuyerAddress, qr, occurredAt, businessDate,
                session.UserId, session.DeviceId, request.Notes)

            ' ---- 5. lines, stock and costing ----------------------------------
            Dim result As New InvoiceResult With {
                .InvoiceId = invoiceId, .InvoiceNo = invoiceNo, .EventId = eventId,
                .Subtotal = totals.Subtotal, .LineDiscount = totals.LineDiscount,
                .HeaderDiscount = totals.HeaderDiscount, .TaxBaseTotal = totals.TaxBaseTotal,
                .TaxTotal = totals.TaxTotal, .Total = totals.Total, .Paid = totalSettled, .Due = due,
                .QrPayload = qr, .CouponDiscount = couponDiscountAmount,
                .LoyaltyRedeemedPoints = redeemedPoints, .VoucherPaidAmount = voucherPaid
            }

            Dim totalCost As Decimal = 0D
            For i As Integer = 0 To prepared.Count - 1
                Dim p = prepared(i)
                Dim r = totals.Lines(i)

                Dim costExcl As Decimal = StockRepository.LastCostAsOf(db.Connection, session.BranchId, p.Item.Id, occurredAt, tx)
                Dim costIncl As Decimal = CostWithTax(costExcl, p.TaxRate, session)

                Dim lineEventId As String = Uuid7.NewId()
                Dim lineId As Long = InvoicesRepository.InsertLine(
                    db.Connection, tx, invoiceId, p.Item.Id, p.Item.BaseUnitId, p.QtyBase, 0D,
                    p.UnitPrice, p.Request.LineDiscount, p.TaxCategoryId, p.TaxRate,
                    r.TaxBase, r.TaxAmount, priceMode, r.LineTotal, costExcl, costIncl,
                    Nothing, p.Request.BatchNo, p.Request.ExpiryDate, Nothing)

                StockRepository.AddMove(db.Connection, tx, lineEventId, session.BranchId, p.Item.Id, "sale",
                                        -p.QtyBase, costExcl, "invoice", invoiceId, lineId, occurredAt, businessDate,
                                        session.UserId, session.DeviceId, invoiceNo, True)

                totalCost += p.QtyBase * costExcl
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = lineId, .ItemId = p.Item.Id, .Qty = p.QtyBase, .UnitPrice = p.UnitPrice,
                    .Discount = p.Request.LineDiscount, .TaxRate = p.TaxRate, .TaxBase = r.TaxBase,
                    .TaxAmount = r.TaxAmount, .LineTotal = r.LineTotal, .CostAtSale = costExcl
                })
            Next

            ' ---- 5b. finalize coupon, voucher & loyalty movements -------------
            If matchedCoupon IsNot Nothing AndAlso couponDiscountAmount > 0D Then
                If CouponsRepository.RecordRedemption(db.Connection, tx, matchedCoupon.Id, invoiceId,
                                                      request.CustomerId, couponDiscountAmount) = 0 Then
                    Throw New BusinessException("تعذّر استخدام الكوبون «" & matchedCoupon.Code & "».")
                End If
            End If

            If matchedVoucher IsNot Nothing AndAlso voucherPaid > 0D Then
                If PurchaseVouchersRepository.Redeem(db.Connection, tx, matchedVoucher.Id, voucherPaid,
                                                     "سداد فاتورة بيع " & invoiceNo, session.UserId, invoiceId) = 0 Then
                    Throw New BusinessException("تعذّر خصم قيمة قسيمة الشراء «" & matchedVoucher.Code & "».")
                End If
            End If

            If request.CustomerId.HasValue AndAlso request.CustomerId.Value > 0 Then
                If redeemedPoints > 0D Then
                    LoyaltyRepository.ApplyPoints(db.Connection, tx, request.CustomerId.Value,
                                                  -redeemedPoints, 0D, redeemedPoints)
                    LoyaltyRepository.AddTransaction(db.Connection, tx, request.CustomerId.Value, invoiceId,
                                                     -redeemedPoints, "redeem",
                                                     "استبدال نقاط في فاتورة " & invoiceNo, session.UserId)
                End If

                If totals.Total > 0D Then
                    Dim earnRatePct As Decimal = SettingsRepository.GetDecimal(
                        db.Connection, "loyalty.earn_rate_pct",
                        SettingsRepository.GetDecimal(db.Connection, "loy_cash_rate", 1D, tx), tx)
                    If earnRatePct > 0D Then
                        Dim earnedPoints As Decimal = Money.Round2(totals.Total * (earnRatePct / 100D))
                        If earnedPoints > 0D Then
                            LoyaltyRepository.ApplyPoints(db.Connection, tx, request.CustomerId.Value,
                                                          earnedPoints, earnedPoints, 0D)
                            LoyaltyRepository.AddTransaction(db.Connection, tx, request.CustomerId.Value, invoiceId,
                                                             earnedPoints, "earn",
                                                             "نقاط مكتسبة من فاتورة " & invoiceNo, session.UserId)
                            result.LoyaltyEarnedPoints = earnedPoints
                        End If
                    End If
                End If
            End If

            ' ---- 6. payment ----------------------------------------------------
            If cashOrMethodPaid > 0D Then
                InvoicesRepository.InsertPayment(db.Connection, tx, Uuid7.NewId(), invoiceId, session.BranchId,
                                                 paymentMethodId, cashOrMethodPaid, Nothing, occurredAt, session.UserId)
                If PostingHelper.IsCashMethod(db.Connection, paymentMethodId, tx) Then
                    ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(),
                        PostingHelper.CashboxOrThrow(db.Connection, session.BranchId, tx),
                        request.ShiftId, "in", cashOrMethodPaid, "invoice", invoiceId, session.UserId)
                End If
            End If

            ' ---- 7. journal ----------------------------------------------------
            Dim journalLines As New List(Of JournalLineInput)()
            If cashOrMethodPaid > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCash, tx),
                    .Debit = cashOrMethodPaid, .Description = "تحصيل فاتورة " & invoiceNo
                })
            End If
            If voucherPaid > 0D Then
                Dim voucherAccId As Long = AccountingRepository.GetOrCreateAccountId(
                    db.Connection, PurchaseVoucherService.AccPrepaidVouchers, "قسائم مشتريات مقدمة", "liability", tx)
                journalLines.Add(New JournalLineInput With {
                    .AccountId = voucherAccId,
                    .Debit = voucherPaid, .Description = "سداد بقسيمة شراء لفاتورة " & invoiceNo
                })
            End If
            If due > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccReceivable, tx),
                    .Debit = due, .Description = "آجل فاتورة " & invoiceNo
                })
            End If
            If totals.TaxBaseTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccSales, tx),
                    .Credit = totals.TaxBaseTotal, .Description = "مبيعات " & invoiceNo
                })
            End If
            If totals.TaxTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccOutputTax, tx),
                    .Credit = totals.TaxTotal, .Description = "ضريبة مبيعات " & invoiceNo
                })
            End If

            If totalCost > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCogs, tx),
                    .Debit = Money.Round2(totalCost), .Description = "تكلفة مبيعات " & invoiceNo
                })
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Credit = Money.Round2(totalCost), .Description = "صرف مخزون " & invoiceNo
                })
            End If

            If journalLines.Count > 0 Then
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "فاتورة بيع " & invoiceNo, "invoice", invoiceId, session.UserId, journalLines)
            End If

            ' ---- 8. audit + outbox ---------------------------------------------
            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "sale_post", "invoice", invoiceId.ToString(), Nothing,
                                PostingHelper.Serialize(result), session.DeviceId)

            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "invoice",
                              invoiceId.ToString(), "sale.posted", 1, PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function

    Public Shared Function PostSaleReturn(session As AppSession, originalInvoiceId As Long,
                                          request As InvoiceRequest) As InvoiceResult
        session.RequirePermission("sales.return")
        If request Is Nothing OrElse request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("اختر الأصناف المرتجعة وكمياتها.")
        End If

        Dim occurredAt As Date = PostingHelper.AsUtc(If(request.OccurredAt.HasValue, request.OccurredAt.Value, Date.UtcNow))
        Dim businessDate As Date = If(request.BusinessDate.HasValue, request.BusinessDate.Value,
                                      PostingHelper.LocalBusinessDate(session, occurredAt))

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            PostingHelper.EnsureShiftInBranch(db.Connection, tx, request.ShiftId, session.BranchId)

            Dim original = InvoicesRepository.GetById(db.Connection, originalInvoiceId, tx)
            If original Is Nothing Then Throw New BusinessException("الفاتورة الأصلية غير موجودة.")
            If original.InvoiceType <> "sale" Then Throw New BusinessException("لا يمكن إرجاع إلا فاتورة بيع.")
            If original.Status <> "posted" Then Throw New BusinessException("الفاتورة الأصلية ملغاة أو غير مرحّلة.")
            If original.BranchId <> session.BranchId Then Throw New BusinessException("الفاتورة الأصلية لفرع آخر.")

            Dim returnable = InvoicesRepository.GetReturnableLines(db.Connection, originalInvoiceId, tx)
            Dim byLine As New Dictionary(Of Long, ReturnableLine)()
            For Each r In returnable
                byLine(r.LineId) = r
            Next

            Dim prepared As New List(Of PreparedReturnLine)()
            For Each line In request.Lines
                If line.Qty <= 0D Then Throw New BusinessException("كمية الإرجاع يجب أن تكون أكبر من صفر.")
                If Not line.OriginalLineId.HasValue Then Throw New BusinessException("بند الإرجاع غير مرتبط بالفاتورة الأصلية.")
                Dim source As ReturnableLine = Nothing
                If Not byLine.TryGetValue(line.OriginalLineId.Value, source) Then
                    Throw New BusinessException("بند الإرجاع لا ينتمي إلى الفاتورة الأصلية.")
                End If
                Dim remaining As Decimal = source.Qty - source.ReturnedQty
                If line.Qty > remaining Then
                    Throw New BusinessException("كمية الإرجاع للصنف «" & source.ItemName & "» أكبر من المتاح (" &
                                                remaining.ToString("0.####") & ").")
                End If

                prepared.Add(New PreparedReturnLine With {
                    .Original = source, .Qty = line.Qty,
                    .UnitPrice = source.UnitPrice, .TaxCategoryId = source.TaxCategoryId,
                    .TaxRate = source.TaxRate, .OriginalLineId = source.LineId
                })
            Next

            Dim calcLines As New List(Of InvoiceLineInput)()
            For Each p In prepared
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = p.Qty, .UnitPrice = p.UnitPrice, .LineDiscount = 0D, .TaxRate = p.TaxRate
                })
            Next

            Dim totals As InvoiceTotals
            Try
                totals = InvoiceCalculator.Calculate(calcLines, 0D, session.PricesIncludeTax)
            Catch ex As ArgumentException
                Throw New BusinessException(ex.Message)
            End Try

            Dim refund As Decimal = Money.Round2(request.Paid)
            If refund < 0D OrElse refund > totals.Total Then
                Throw New BusinessException("المبلغ المرتجع أكبر من قيمة المرتجع.")
            End If
            Dim customerCredit As Decimal = Money.Round2(totals.Total - refund)

            Dim eventId As String = Uuid7.NewId()
            Dim invoiceNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId, "sale_return", "SR", businessDate)
            Dim qr As String = PostingHelper.BuildQr(session, session.BranchName, session.BranchTaxNo,
                                                     occurredAt, -totals.Total, -totals.TaxTotal)
            Dim priceMode As String = If(session.PricesIncludeTax, "inclusive", "exclusive")

            Dim invoiceId As Long = InvoicesRepository.Insert(
                db.Connection, tx, eventId, session.BranchId, invoiceNo, "sale_return", "posted",
                original.CustomerId, Nothing, Nothing, originalInvoiceId, request.ShiftId,
                -totals.Subtotal, -totals.LineDiscount, 0D, -totals.TaxBaseTotal, -totals.TaxTotal,
                -totals.Total, -refund, -customerCredit, PaymentStatusFor(refund, customerCredit),
                priceMode, session.BranchTaxNo, Nothing, Nothing, Nothing, qr,
                occurredAt, businessDate, session.UserId, session.DeviceId, request.Notes)

            Dim result As New InvoiceResult With {
                .InvoiceId = invoiceId, .InvoiceNo = invoiceNo, .EventId = eventId,
                .Subtotal = -totals.Subtotal, .LineDiscount = -totals.LineDiscount,
                .TaxBaseTotal = -totals.TaxBaseTotal, .TaxTotal = -totals.TaxTotal,
                .Total = -totals.Total, .Paid = -refund, .Due = -customerCredit, .QrPayload = qr
            }

            Dim totalCost As Decimal = 0D
            For i As Integer = 0 To prepared.Count - 1
                Dim p = prepared(i)
                Dim r = totals.Lines(i)
                Dim costExcl As Decimal = StockRepository.LastCostAsOf(db.Connection, session.BranchId,
                                                                       p.Original.ItemId, occurredAt, tx)

                Dim lineId As Long = InvoicesRepository.InsertLine(
                    db.Connection, tx, invoiceId, p.Original.ItemId, Nothing, -p.Qty, 0D,
                    p.UnitPrice, 0D, p.TaxCategoryId, p.TaxRate, -r.TaxBase, -r.TaxAmount,
                    priceMode, -r.LineTotal, costExcl, CostWithTax(costExcl, p.TaxRate, session),
                    p.OriginalLineId, Nothing, Nothing, "مرتجع")

                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), session.BranchId, p.Original.ItemId,
                                        "sale_return", p.Qty, costExcl, "invoice", invoiceId, lineId,
                                        occurredAt, businessDate, session.UserId, session.DeviceId, invoiceNo, False)

                totalCost += p.Qty * costExcl
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = lineId, .ItemId = p.Original.ItemId, .Qty = -p.Qty, .UnitPrice = p.UnitPrice,
                    .TaxRate = p.TaxRate, .TaxBase = -r.TaxBase, .TaxAmount = -r.TaxAmount,
                    .LineTotal = -r.LineTotal, .CostAtSale = costExcl
                })
            Next

            If refund > 0D Then
                Dim refundMethodId As Long = If(request.PaymentMethodId > 0, request.PaymentMethodId,
                                                PostingHelper.DefaultPaymentMethod(db.Connection, tx))
                InvoicesRepository.InsertPayment(db.Connection, tx, Uuid7.NewId(), invoiceId, session.BranchId,
                                                 refundMethodId, -refund, Nothing, occurredAt, session.UserId)
                If PostingHelper.IsCashMethod(db.Connection, refundMethodId, tx) Then
                    ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(),
                        PostingHelper.CashboxOrThrow(db.Connection, session.BranchId, tx),
                        request.ShiftId, "out", refund, "invoice", invoiceId, session.UserId)
                End If
            End If

            Dim journalLines As New List(Of JournalLineInput)()
            If totals.TaxBaseTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccSalesReturn, tx),
                    .Debit = Money.Round2(totals.TaxBaseTotal), .Description = "مرتجع بيع " & invoiceNo
                })
            End If
            If totals.TaxTotal <> 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccOutputTax, tx),
                    .Debit = totals.TaxTotal, .Description = "تخفيض ضريبة مبيعات " & invoiceNo
                })
            End If
            If refund > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCash, tx),
                    .Credit = refund, .Description = "رد نقدي " & invoiceNo
                })
            End If
            If customerCredit > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccReceivable, tx),
                    .Credit = customerCredit, .Description = "رصيد دائن للعميل " & invoiceNo
                })
            End If
            If totalCost > 0D Then
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccInventory, tx),
                    .Debit = Money.Round2(totalCost), .Description = "إعادة مخزون " & invoiceNo
                })
                journalLines.Add(New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCogs, tx),
                    .Credit = Money.Round2(totalCost), .Description = "عكس تكلفة مبيعات " & invoiceNo
                })
            End If

            If journalLines.Count > 0 Then
                AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                                 "مرتجع بيع " & invoiceNo, "invoice", invoiceId, session.UserId, journalLines)
            End If

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "sale_return_post", "invoice", invoiceId.ToString(), Nothing,
                                PostingHelper.Serialize(result), session.DeviceId)
            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId, "invoice",
                              invoiceId.ToString(), "sale_return.posted", 1, PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function

    ''' <summary>Loads an invoice and its lines for display or reprint.</summary>
    Public Shared Function GetInvoice(session As AppSession, invoiceId As Long) As InvoiceResult
        Using db = DbSession.Open(session.ConnectionString)
            Dim header = InvoicesRepository.GetById(db.Connection, invoiceId)
            If header Is Nothing Then Throw New BusinessException("الفاتورة غير موجودة.")
            If header.BranchId <> session.BranchId AndAlso Not session.HasPermission("reports.all_branches") Then
                Throw New BusinessException("الفاتورة تخص فرعًا آخر.")
            End If

            Dim result As New InvoiceResult With {
                .InvoiceId = header.Id, .InvoiceNo = header.InvoiceNo, .EventId = header.EventId,
                .Subtotal = header.Subtotal, .LineDiscount = header.LineDiscount,
                .HeaderDiscount = header.HeaderDiscount, .TaxBaseTotal = header.TaxBaseTotal,
                .TaxTotal = header.TaxTotal, .Total = header.Total, .Paid = header.Paid,
                .Due = header.Due, .QrPayload = header.QrPayload
            }
            For Each line In InvoicesRepository.GetLines(db.Connection, invoiceId)
                result.Lines.Add(New InvoiceLineResult With {
                    .LineId = line.Id, .ItemId = line.ItemId, .Qty = line.Qty, .UnitPrice = line.UnitPrice,
                    .Discount = line.Discount, .TaxRate = line.TaxRate, .TaxBase = line.TaxBase,
                    .TaxAmount = line.TaxAmount, .LineTotal = line.LineTotal, .CostAtSale = line.CostAtSale
                })
            Next
            Return result
        End Using
    End Function

    Public Shared Function ListInvoices(session As AppSession, fromDate As Date, toDate As Date,
                                        invoiceType As String) As List(Of InvoiceListRow)
        Using db = DbSession.Open(session.ConnectionString)
            Return InvoicesRepository.List(db.Connection, session.BranchId, fromDate, toDate, invoiceType)
        End Using
    End Function

    Private Shared Function PaymentStatusFor(paid As Decimal, due As Decimal) As String
        If due <= 0.004D Then Return "paid"
        If paid > 0.004D Then Return "partial"
        Return "unpaid"
    End Function

    Private Shared Function CostWithTax(costExcl As Decimal, rate As Decimal, session As AppSession) As Decimal
        If rate <= 0D Then Return costExcl
        Return Money.Round4(costExcl * (1D + rate / 100D))
    End Function

    Private Shared Function ValidateHrAttribution(db As DbSession, tx As IDbTransaction,
                                                   id As Long?, expectedType As String,
                                                   label As String) As Long?
        If Not id.HasValue OrElse id.Value <= 0 Then Return Nothing
        Dim employee = HrEmployeesRepository.GetById(db.Connection, id.Value, tx)
        If employee Is Nothing OrElse Not employee.IsActive OrElse employee.EmployeeType <> expectedType Then
            Throw New BusinessException(label & " المختار غير موجود أو غير مفعّل.")
        End If
        Return employee.Id
    End Function

    Private Class PreparedLine
        Public Property Item As ItemRecord
        Public Property Request As InvoiceLineRequest
        Public Property UnitPrice As Decimal
        Public Property TaxCategoryId As Long
        Public Property TaxRate As Decimal
        Public Property QtyBase As Decimal
    End Class

    Private Class PreparedReturnLine
        Public Property Original As ReturnableLine
        Public Property Qty As Decimal
        Public Property UnitPrice As Decimal
        Public Property TaxCategoryId As Long
        Public Property TaxRate As Decimal
        Public Property OriginalLineId As Long
    End Class

    ''' <summary>
    ''' Voids a posted sale invoice: posts the full reversal return AND marks the original invoice
    ''' status='voided' via InvoicesRepository.Void so it can never be voided or returned again.
    ''' </summary>
    Public Shared Function VoidSaleInvoice(session As AppSession, originalInvoiceId As Long,
                                           reason As String, Optional shiftId As Long? = Nothing) As InvoiceResult
        session.RequirePermission("sales.return")
        Dim returnable As List(Of ReturnableLine)
        Dim origHeader As InvoiceHeaderRecord
        Using db = DbSession.Open(session.ConnectionString)
            origHeader = InvoicesRepository.GetById(db.Connection, originalInvoiceId)
            If origHeader Is Nothing Then Throw New BusinessException("الفاتورة الأصلية غير موجودة.")
            If origHeader.Status <> "posted" Then Throw New BusinessException("الفاتورة ملغاة مسبقاً أو غير مرحّلة.")
            returnable = InvoicesRepository.GetReturnableLines(db.Connection, originalInvoiceId)
        End Using

        Dim linesToReturn As New List(Of InvoiceLineRequest)()
        For Each r In returnable
            Dim remQty As Decimal = r.Qty - r.ReturnedQty
            If remQty > 0D Then
                linesToReturn.Add(New InvoiceLineRequest With {
                    .ItemId = r.ItemId,
                    .OriginalLineId = r.LineId,
                    .Qty = remQty,
                    .UnitPrice = r.UnitPrice,
                    .TaxCategoryId = r.TaxCategoryId
                })
            End If
        Next

        Dim result As InvoiceResult = Nothing
        If linesToReturn.Count > 0 Then
            Dim req As New InvoiceRequest With {
                .CustomerId = origHeader.CustomerId,
                .ShiftId = shiftId,
                .Paid = origHeader.Paid,
                .Notes = "إلغاء فاتورة (Void): " & If(reason, "إلغاء") & " — أصل: " & origHeader.InvoiceNo,
                .Lines = linesToReturn
            }
            result = PostSaleReturn(session, originalInvoiceId, req)
        End If

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            InvoicesRepository.Void(db.Connection, db.Transaction, originalInvoiceId,
                                    If(String.IsNullOrWhiteSpace(reason), "Post Void", reason.Trim()),
                                    session.UserId)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "invoice_void", "invoice", originalInvoiceId.ToString(),
                                "posted", "voided: " & reason, session.DeviceId)
            db.Commit()
        End Using

        Return If(result, GetInvoice(session, originalInvoiceId))
    End Function

    ' ==========================================================================
    ' DB-Backed Held Invoices (تعليق واستئناف الفواتير في قاعدة البيانات)
    ' ==========================================================================
    Private Shared Sub EnsureHeldTable(conn As IDbConnection, Optional tx As IDbTransaction = Nothing)
        conn.Execute(
            "CREATE TABLE IF NOT EXISTS held_invoices (" &
            "  id BIGINT AUTO_INCREMENT PRIMARY KEY," &
            "  slot_title VARCHAR(255) NOT NULL," &
            "  branch_id BIGINT NOT NULL," &
            "  user_id BIGINT NOT NULL," &
            "  customer_id BIGINT NULL," &
            "  header_discount DECIMAL(18,4) NOT NULL DEFAULT 0," &
            "  coupon_code VARCHAR(64) NULL," &
            "  voucher_code VARCHAR(64) NULL," &
            "  redeem_points DECIMAL(18,4) NOT NULL DEFAULT 0," &
            "  notes VARCHAR(500) NULL," &
            "  lines_json LONGTEXT NOT NULL," &
            "  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP" &
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", Nothing, tx)
    End Sub

    Public Shared Function SaveHeldInvoice(session As AppSession, slot As HeldInvoiceRecord) As Long
        Using db = DbSession.Open(session.ConnectionString)
            EnsureHeldTable(db.Connection)
            db.BeginTransaction()
            Dim tx = db.Transaction
            db.Connection.Execute(
                "INSERT INTO held_invoices(slot_title, branch_id, user_id, customer_id, header_discount, " &
                "coupon_code, voucher_code, redeem_points, notes, lines_json, created_at) " &
                "VALUES(@title, @b, @u, @c, @disc, @cpn, @vch, @pts, @notes, @json, UTC_TIMESTAMP())",
                New With {
                    .title = slot.SlotTitle, .b = session.BranchId, .u = session.UserId,
                    .c = slot.CustomerId, .disc = slot.HeaderDiscount,
                    .cpn = slot.CouponCode, .vch = slot.VoucherCode,
                    .pts = slot.RedeemPoints, .notes = slot.Notes, .json = slot.LinesJson
                }, tx)
            Dim newId As Long = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                "invoice_hold", "held_invoice", newId.ToString(), Nothing, slot.SlotTitle, session.DeviceId)
            db.Commit()
            slot.Id = newId
            Return newId
        End Using
    End Function

    Public Shared Function ListHeldInvoices(session As AppSession) As List(Of HeldInvoiceRecord)
        Using db = DbSession.Open(session.ConnectionString)
            EnsureHeldTable(db.Connection)
            Return db.Connection.Query(Of HeldInvoiceRecord)(
                "SELECT id AS Id, slot_title AS SlotTitle, branch_id AS BranchId, user_id AS UserId, " &
                "       customer_id AS CustomerId, header_discount AS HeaderDiscount, " &
                "       coupon_code AS CouponCode, voucher_code AS VoucherCode, " &
                "       redeem_points AS RedeemPoints, notes AS Notes, lines_json AS LinesJson, " &
                "       created_at AS CreatedAt " &
                "FROM held_invoices WHERE branch_id=@b ORDER BY id ASC",
                New With {.b = session.BranchId}).AsList()
        End Using
    End Function

    Public Shared Sub DeleteHeldInvoice(session As AppSession, heldId As Long)
        If heldId <= 0 Then Return
        Using db = DbSession.Open(session.ConnectionString)
            EnsureHeldTable(db.Connection)
            db.BeginTransaction()
            db.Connection.Execute("DELETE FROM held_invoices WHERE id=@id AND branch_id=@b",
                                  New With {.id = heldId, .b = session.BranchId}, db.Transaction)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "invoice_resume", "held_invoice", heldId.ToString(),
                                Nothing, Nothing, session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class
