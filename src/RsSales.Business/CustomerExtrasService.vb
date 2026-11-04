Imports System
Imports System.Collections.Generic
Imports System.Linq

''' <summary>Customer groups used to classify customers and drive group discounts.</summary>
Public NotInheritable Class CustomerGroupService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String,
                                   Optional includeInactive As Boolean = False) As List(Of CustomerGroupSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomerGroupsRepository.Search(db.Connection, term, includeInactive)
        End Using
    End Function

    Public Shared Function Save(session As AppSession, id As Long?, name As String, discountPercent As Decimal,
                                notes As String, isActive As Boolean) As Long
        session.RequirePermission("customers.manage")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم المجموعة مطلوب.")
        If discountPercent < 0D OrElse discountPercent > 100D Then
            Throw New BusinessException("نسبة خصم المجموعة يجب أن تكون بين 0 و 100.")
        End If

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim duplicate = CustomerGroupsRepository.FindByName(db.Connection, name.Trim(), db.Transaction)
            If duplicate IsNot Nothing AndAlso (Not id.HasValue OrElse duplicate.Id <> id.Value) Then
                Throw New BusinessException("اسم المجموعة مستخدم بالفعل.")
            End If

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                If CustomerGroupsRepository.GetById(db.Connection, id.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("المجموعة غير موجودة.")
                End If
                CustomerGroupsRepository.Update(db.Connection, db.Transaction, id.Value, name.Trim(),
                                                discountPercent, notes, isActive)
                newId = id.Value
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "customer_group_update", "customer_group", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            Else
                newId = CustomerGroupsRepository.Insert(db.Connection, db.Transaction, name.Trim(),
                                                        discountPercent, notes)
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "customer_group_create", "customer_group", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            End If
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("customers.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            CustomerGroupsRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "customer_group_toggle", "customer_group", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>Loyalty points balances and manual adjustments.</summary>
Public NotInheritable Class LoyaltyService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional limit As Integer = 500) As List(Of LoyaltyAccountSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return LoyaltyRepository.Search(db.Connection, term, limit)
        End Using
    End Function

    Public Shared Function Points(session As AppSession, customerId As Long) As Decimal
        Using db = DbSession.Open(session.ConnectionString)
            Return LoyaltyRepository.GetPoints(db.Connection, Nothing, customerId)
        End Using
    End Function

    Public Shared Function Transactions(session As AppSession, customerId As Long) As List(Of LoyaltyTransactionSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return LoyaltyRepository.Transactions(db.Connection, customerId)
        End Using
    End Function

    ''' <summary>Applies a signed points movement and returns the new balance.</summary>
    Public Shared Function Adjust(session As AppSession, customerId As Long, pointsDelta As Decimal,
                                  movementType As String, note As String) As Decimal
        session.RequirePermission("customers.manage")
        If customerId <= 0 Then Throw New BusinessException("اختر العميل أولاً.")
        If pointsDelta = 0D Then Throw New BusinessException("قيمة النقاط يجب ألا تكون صفرًا.")
        Dim kind As String = If(movementType, "adjust").Trim().ToLowerInvariant()
        If kind <> "earn" AndAlso kind <> "redeem" AndAlso kind <> "adjust" Then
            Throw New BusinessException("نوع حركة النقاط غير صالح.")
        End If
        If kind = "earn" AndAlso pointsDelta < 0D Then Throw New BusinessException("نقاط الإضافة يجب أن تكون موجبة.")
        If kind = "redeem" AndAlso pointsDelta > 0D Then Throw New BusinessException("نقاط الاستبدال يجب أن تكون سالبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim current As Decimal = LoyaltyRepository.GetPoints(db.Connection, db.Transaction, customerId)
            Dim nextPoints As Decimal = current + pointsDelta
            If nextPoints < 0D Then
                Throw New BusinessException("رصيد النقاط الحالي (" & current.ToString("0.##") &
                                            ") لا يكفي لخصم " & Math.Abs(pointsDelta).ToString("0.##") & " نقطة.")
            End If

            Dim earned As Decimal = If(kind = "earn", pointsDelta, 0D)
            Dim redeemed As Decimal = If(kind = "redeem", -pointsDelta, 0D)
            LoyaltyRepository.ApplyPoints(db.Connection, db.Transaction, customerId, pointsDelta, earned, redeemed)
            LoyaltyRepository.AddTransaction(db.Connection, db.Transaction, customerId, Nothing, pointsDelta,
                                             kind, note, session.UserId)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "loyalty_adjust", "customer", customerId.ToString(),
                                current.ToString("0.##"), nextPoints.ToString("0.##") & " | " & kind, session.DeviceId)
            db.Commit()
            Return nextPoints
        End Using
    End Function
End Class

''' <summary>Read-only customer account balances and payment movements.</summary>
Public NotInheritable Class CustomerFinanceService
    Private Sub New()
    End Sub

    Public Shared Function Balances(session As AppSession, term As String,
                                    Optional filter As String = "all") As List(Of CustomerBalanceSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomerFinanceRepository.Balances(db.Connection, term, filter)
        End Using
    End Function

    Public Shared Function Payments(session As AppSession, fromDate As Date, toDate As Date,
                                    term As String, Optional limit As Integer = 2000) As List(Of CustomerPaymentSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomerFinanceRepository.Payments(db.Connection, session.BranchId, fromDate, toDate, term, limit)
        End Using
    End Function

    ''' <summary>Detailed statement of one party: the balance carried into the period, then every posted
    ''' document in date order with the running balance after it.</summary>
    Public Shared Function Statement(session As AppSession, customerId As Long,
                                     fromDate As Date, toDate As Date) As PartyStatement
        If customerId <= 0 Then Throw New BusinessException("اختر الحساب أولاً.")
        If toDate.Date < fromDate.Date Then Throw New BusinessException("تاريخ النهاية يجب ألا يسبق تاريخ البداية.")

        Using db = DbSession.Open(session.ConnectionString)
            Dim result = CustomerFinanceRepository.PartyHeader(db.Connection, customerId)
            If result Is Nothing Then Throw New BusinessException("الحساب غير موجود.")
            result.FromDate = fromDate.Date
            result.ToDate = toDate.Date
            Dim running As Decimal = Money.Round2(
                CustomerFinanceRepository.OpeningBalance(db.Connection, customerId, fromDate.Date))
            result.OpeningBalance = running
            For Each row In CustomerFinanceRepository.StatementRows(db.Connection, customerId,
                                                                    fromDate.Date, toDate.Date)
                row.Debit = Money.Round2(row.Debit)
                row.Credit = Money.Round2(row.Credit)
                running = Money.Round2(running + row.Debit - row.Credit)
                row.Balance = running
                result.Lines.Add(row)
            Next
            Return result
        End Using
    End Function
End Class

''' <summary>
''' تحصيل الآجل: money the customer hands over against their unpaid invoices. Each
''' payment settles the invoice it is allocated to, so the customer's balance — and
''' therefore the credit ceiling a later sale is checked against — goes down.
''' </summary>
Public NotInheritable Class CustomerCollectionService
    Private Sub New()
    End Sub

    Public Shared Function Outstanding(session As AppSession, customerId As Long) As List(Of CustomerDueInvoice)
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomerFinanceRepository.OutstandingInvoices(db.Connection, customerId)
        End Using
    End Function

    ''' <summary>Settles <paramref name="amount"/> against one invoice when <paramref name="invoiceId"/>
    ''' is given, otherwise against the oldest unpaid invoices first.</summary>
    Public Shared Function Collect(session As AppSession, customerId As Long, amount As Decimal,
                                   methodId As Long, invoiceId As Long?, note As String) As CustomerCollectionResult
        session.RequirePermission("customers.manage")
        If customerId <= 0 Then Throw New BusinessException("اختر العميل أولاً.")
        Dim wanted As Decimal = Money.Round2(amount)
        If wanted <= 0D Then Throw New BusinessException("مبلغ التحصيل يجب أن يكون أكبر من صفر.")

        Dim reference As String = If(note, String.Empty).Trim()
        If reference.Length > 128 Then reference = reference.Substring(0, 128)

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim c = db.Connection

            Dim party = CustomerFinanceRepository.PartyHeader(c, customerId, tx)
            If party Is Nothing Then Throw New BusinessException("العميل غير موجود.")

            Dim methodName As String = PostingHelper.MethodName(c, methodId, tx)
            If String.IsNullOrWhiteSpace(methodName) Then
                Throw New BusinessException("طريقة الدفع غير صالحة أو موقوفة.")
            End If

            Dim unpaid = CustomerFinanceRepository.OutstandingInvoices(c, customerId, True, tx)
            If unpaid.Count = 0 Then
                Throw New BusinessException("لا توجد فواتير غير مسددة على العميل «" & party.PartyName & "».")
            End If

            Dim targets As List(Of CustomerDueInvoice) = unpaid
            If invoiceId.HasValue Then
                targets = unpaid.Where(Function(row) row.InvoiceId = invoiceId.Value).ToList()
                If targets.Count = 0 Then
                    Throw New BusinessException("الفاتورة المختارة مسددة بالفعل أو لا تخص هذا العميل.")
                End If
            End If

            Dim allDue As Decimal = Money.Round2(unpaid.Sum(Function(row) row.Due))
            Dim available As Decimal = Money.Round2(targets.Sum(Function(row) row.Due))
            If wanted > available Then
                Throw New BusinessException("المبلغ أكبر من المتبقي (" & available.ToString("0.##") &
                                            "). المتبقي على العميل كله " & allDue.ToString("0.##") & ".")
            End If

            Dim occurredAt As Date = Date.UtcNow
            Dim businessDate As Date = PostingHelper.LocalBusinessDate(session, occurredAt)

            ' ---- settle the invoices --------------------------------------
            Dim left As Decimal = wanted
            Dim settledCount As Integer = 0
            For Each row In targets
                If left <= 0D Then Exit For
                Dim take As Decimal = Money.Round2(Math.Min(left, row.Due))
                InvoicesRepository.InsertPayment(c, tx, Uuid7.NewId(), row.InvoiceId, session.BranchId,
                                                 methodId, take, reference, occurredAt, session.UserId)
                Dim newDue As Decimal = Money.Round2(row.Due - take)
                InvoicesRepository.SetSettlement(c, tx, row.InvoiceId, Money.Round2(row.Paid + take), newDue,
                                                 If(newDue <= 0.005D, "paid", "partial"))
                left = Money.Round2(left - take)
                settledCount += 1
            Next

            ' ---- drawer ----------------------------------------------------
            If PostingHelper.IsCashMethod(c, methodId, tx) Then
                Dim cashboxId As Long? = ShiftsRepository.GetDefaultCashbox(c, session.BranchId, tx)
                If cashboxId.HasValue Then
                    Dim shift = ShiftsRepository.GetOpen(c, session.BranchId, session.UserId, tx)
                    Dim shiftId As Long? = If(shift IsNot Nothing, CType(shift.Id, Long?), Nothing)
                    ShiftsRepository.AddCashMove(c, tx, Uuid7.NewId(), cashboxId.Value, shiftId, "in", wanted,
                                                 "customer_collection", Nothing, session.UserId)
                End If
            End If

            ' ---- journal: cash in, receivable down -------------------------
            Dim journalLines As New List(Of JournalLineInput) From {
                New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(c, AccountingRepository.AccCash, tx),
                    .Debit = wanted, .Description = "تحصيل آجل من " & party.PartyName},
                New JournalLineInput With {
                    .AccountId = AccountingRepository.GetAccountId(c, AccountingRepository.AccReceivable, tx),
                    .Credit = wanted, .Description = "سداد آجل — " & party.PartyName}
            }
            AccountingRepository.InsertEntry(c, tx, Uuid7.NewId(), session.BranchId, businessDate,
                                             "تحصيل آجل من " & party.PartyName, "customer_collection",
                                             Nothing, session.UserId, journalLines)

            Dim result As New CustomerCollectionResult With {
                .CustomerId = customerId, .CustomerName = party.PartyName, .Amount = wanted,
                .MethodName = methodName, .SettledInvoiceCount = settledCount,
                .RemainingDue = Money.Round2(allDue - wanted),
                .BalanceAfter = CustomersRepository.Balance(c, customerId, tx), .PaidAt = occurredAt
            }

            Dim eventId As String = Uuid7.NewId()
            AuditRepository.Log(c, tx, eventId, session.BranchId, session.UserId, session.Username,
                                "customer_collection", "customer", customerId.ToString(),
                                allDue.ToString("0.##"),
                                wanted.ToString("0.##") & " | " & methodName, session.DeviceId)
            SyncStore.Enqueue(c, tx, eventId, session.BranchId, session.DeviceId, "customer",
                              customerId.ToString(), "customer.collected", 1,
                              PostingHelper.Serialize(result), occurredAt)

            db.Commit()
            Return result
        End Using
    End Function
End Class

''' <summary>Discount coupons master data.</summary>
Public NotInheritable Class CouponService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional includeInactive As Boolean = True) As List(Of CouponSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CouponsRepository.Search(db.Connection, term, includeInactive)
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, id As Long) As CouponSummary
        Using db = DbSession.Open(session.ConnectionString)
            Return CouponsRepository.GetById(db.Connection, id)
        End Using
    End Function

    Public Shared Function Redemptions(session As AppSession, couponId As Long) As List(Of CouponRedemptionSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CouponsRepository.Redemptions(db.Connection, couponId)
        End Using
    End Function

    ''' <summary>Validates a coupon code against a given gross invoice amount and returns the discount value.</summary>
    Public Shared Function ValidateForSale(session As AppSession, code As String, grossAmount As Decimal) As Decimal
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("أدخل كود الكوبون أولاً.")
        If grossAmount <= 0D Then Throw New BusinessException("أضف أصنافًا للفاتورة قبل تطبيق الكوبون.")
        Using db = DbSession.Open(session.ConnectionString)
            Dim coupon = CouponsRepository.FindByCode(db.Connection, code.Trim())
            If coupon Is Nothing Then Throw New BusinessException("كود الكوبون «" & code.Trim() & "» غير موجود.")
            If Not coupon.IsActive Then Throw New BusinessException("الكوبون «" & coupon.Code & "» غير مفعّل.")
            Dim today As Date = Date.Today
            If coupon.StartsAt.HasValue AndAlso coupon.StartsAt.Value.Date > today Then
                Throw New BusinessException("لم يبدأ سريان هذا الكوبون بعد.")
            End If
            If coupon.EndsAt.HasValue AndAlso coupon.EndsAt.Value.Date < today Then
                Throw New BusinessException("انتهت صلاحية هذا الكوبون.")
            End If
            If coupon.MaxUses > 0 AndAlso coupon.UsedCount >= coupon.MaxUses Then
                Throw New BusinessException("استُنفد الحد الأقصى لاستخدام هذا الكوبون.")
            End If

            Dim discount As Decimal = If(coupon.DiscountType = "percent",
                                         Money.Round2(grossAmount * (coupon.DiscountValue / 100D)),
                                         Money.Round2(coupon.DiscountValue))
            Return Math.Min(discount, grossAmount)
        End Using
    End Function

    Public Shared Function Save(session As AppSession, id As Long?, code As String, name As String,
                                discountType As String, discountValue As Decimal, startsAt As Date?,
                                endsAt As Date?, maxUses As Integer, isActive As Boolean) As Long
        session.RequirePermission("customers.manage")
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("كود الكوبون مطلوب.")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم الكوبون مطلوب.")
        If discountType <> "percent" AndAlso discountType <> "amount" Then
            Throw New BusinessException("نوع الخصم غير صالح.")
        End If
        If discountValue <= 0D Then Throw New BusinessException("قيمة الخصم يجب أن تكون أكبر من صفر.")
        If discountType = "percent" AndAlso discountValue > 100D Then
            Throw New BusinessException("نسبة الخصم يجب ألا تتجاوز 100%.")
        End If
        If maxUses < 0 Then Throw New BusinessException("عدد الاستخدامات لا يمكن أن يكون سالبًا.")
        If startsAt.HasValue AndAlso endsAt.HasValue AndAlso endsAt.Value.Date < startsAt.Value.Date Then
            Throw New BusinessException("تاريخ النهاية يجب ألا يسبق تاريخ البداية.")
        End If

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim duplicate = CouponsRepository.FindByCode(db.Connection, code.Trim(), db.Transaction)
            If duplicate IsNot Nothing AndAlso (Not id.HasValue OrElse duplicate.Id <> id.Value) Then
                Throw New BusinessException("كود الكوبون مستخدم بالفعل.")
            End If

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                If CouponsRepository.GetById(db.Connection, id.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("الكوبون غير موجود.")
                End If
                CouponsRepository.Update(db.Connection, db.Transaction, id.Value, code.Trim(), name.Trim(),
                                         discountType, discountValue, startsAt, endsAt, maxUses, isActive)
                newId = id.Value
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "coupon_update", "coupon", newId.ToString(),
                                    Nothing, code.Trim(), session.DeviceId)
            Else
                newId = CouponsRepository.Insert(db.Connection, db.Transaction, code.Trim(), name.Trim(),
                                                 discountType, discountValue, startsAt, endsAt, maxUses)
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "coupon_create", "coupon", newId.ToString(),
                                    Nothing, code.Trim(), session.DeviceId)
            End If
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("customers.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            CouponsRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "coupon_toggle", "coupon", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>Prepaid purchase vouchers (gift vouchers) issued for customers.</summary>
Public NotInheritable Class PurchaseVoucherService
    Private Sub New()
    End Sub

    Public Const AccPrepaidVouchers As String = "2160"

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional includeInactive As Boolean = True) As List(Of PurchaseVoucherSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return PurchaseVouchersRepository.Search(db.Connection, term, includeInactive)
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, id As Long) As PurchaseVoucherSummary
        Using db = DbSession.Open(session.ConnectionString)
            Return PurchaseVouchersRepository.GetById(db.Connection, id)
        End Using
    End Function

    Public Shared Function FindActiveByCode(session As AppSession, code As String) As PurchaseVoucherSummary
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("أدخل كود القسيمة أولاً.")
        Using db = DbSession.Open(session.ConnectionString)
            Dim voucher = PurchaseVouchersRepository.FindByCode(db.Connection, code.Trim())
            If voucher Is Nothing Then Throw New BusinessException("كود القسيمة «" & code.Trim() & "» غير موجود.")
            If Not voucher.IsActive Then Throw New BusinessException("القسيمة «" & voucher.Code & "» موقوفة.")
            If voucher.ExpiresAt.HasValue AndAlso voucher.ExpiresAt.Value.Date < Date.Today Then
                Throw New BusinessException("القسيمة «" & voucher.Code & "» منتهية الصلاحية.")
            End If
            If voucher.RemainingAmount <= 0D Then
                Throw New BusinessException("رصيد القسيمة «" & voucher.Code & "» مستنفد بالكامل.")
            End If
            Return voucher
        End Using
    End Function

    Public Shared Function Redemptions(session As AppSession, voucherId As Long) As List(Of PurchaseVoucherRedemptionSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return PurchaseVouchersRepository.Redemptions(db.Connection, voucherId)
        End Using
    End Function

    Public Shared Function Issue(session As AppSession, customerId As Long?, amount As Decimal,
                                 expiresAt As Date?, notes As String) As Long
        session.RequirePermission("customers.manage")
        If amount <= 0D Then Throw New BusinessException("قيمة القسيمة يجب أن تكون أكبر من صفر.")
        If expiresAt.HasValue AndAlso expiresAt.Value.Date < Date.Today Then
            Throw New BusinessException("تاريخ انتهاء القسيمة لا يمكن أن يكون في الماضي.")
        End If

        Dim roundedAmount As Decimal = Money.Round2(amount)
        Dim code As String = "PV-" & Date.Now.ToString("yyMMdd") & "-" &
                             Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim newId As Long = PurchaseVouchersRepository.Insert(db.Connection, tx, code, customerId,
                                                                  roundedAmount, expiresAt, notes, session.UserId)

            Dim cashAccId As Long = AccountingRepository.GetAccountId(db.Connection, AccountingRepository.AccCash, tx)
            Dim voucherLiabAccId As Long = AccountingRepository.GetOrCreateAccountId(
                db.Connection, AccPrepaidVouchers, "قسائم مشتريات مقدمة", "liability", tx)

            Dim journalLines As New List(Of JournalLineInput) From {
                New JournalLineInput With {
                    .AccountId = cashAccId,
                    .Debit = roundedAmount,
                    .Description = "تحصيل إصدار قسيمة شراء " & code
                },
                New JournalLineInput With {
                    .AccountId = voucherLiabAccId,
                    .Credit = roundedAmount,
                    .Description = "التزام قسيمة شراء " & code
                }
            }
            AccountingRepository.InsertEntry(db.Connection, tx, Uuid7.NewId(), session.BranchId, Date.UtcNow.Date,
                                             "إصدار قسيمة شراء " & code, "purchase_voucher", newId, session.UserId, journalLines)

            Dim cashboxId As Long? = ShiftsRepository.GetDefaultCashbox(db.Connection, session.BranchId, tx)
            If cashboxId.HasValue Then
                Dim openShift = ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId, tx)
                Dim shiftId As Long? = If(openShift IsNot Nothing, CType(openShift.Id, Long?), Nothing)
                ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(), cashboxId.Value, shiftId,
                                             "in", roundedAmount, "purchase_voucher", newId, session.UserId)
            End If

            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId,
                                session.Username, "purchase_voucher_issue", "purchase_voucher", newId.ToString(),
                                Nothing, code & " | " & roundedAmount.ToString("0.00"), session.DeviceId)
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub Redeem(session As AppSession, voucherId As Long, amount As Decimal, note As String)
        session.RequirePermission("customers.manage")
        If amount <= 0D Then Throw New BusinessException("مبلغ الاسترداد يجب أن يكون أكبر من صفر.")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim voucher = PurchaseVouchersRepository.GetById(db.Connection, voucherId, db.Transaction)
            If voucher Is Nothing Then Throw New BusinessException("القسيمة غير موجودة.")
            If Not voucher.IsActive Then Throw New BusinessException("القسيمة موقوفة.")
            If voucher.ExpiresAt.HasValue AndAlso voucher.ExpiresAt.Value.Date < Date.Today Then
                Throw New BusinessException("القسيمة منتهية الصلاحية.")
            End If
            If amount > voucher.RemainingAmount Then
                Throw New BusinessException("المبلغ المطلوب أكبر من الرصيد المتاح (" &
                                            voucher.RemainingAmount.ToString("0.00") & ").")
            End If
            If PurchaseVouchersRepository.Redeem(db.Connection, db.Transaction, voucherId, amount, note, session.UserId) = 0 Then
                Throw New BusinessException("تعذّر استرداد القسيمة. حدّث البيانات وحاول مرة أخرى.")
            End If
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "purchase_voucher_redeem", "purchase_voucher", voucherId.ToString(),
                                voucher.RemainingAmount.ToString("0.00"),
                                (voucher.RemainingAmount - amount).ToString("0.00"), session.DeviceId)
            db.Commit()
        End Using
    End Sub

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("customers.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            PurchaseVouchersRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "purchase_voucher_toggle", "purchase_voucher", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class
