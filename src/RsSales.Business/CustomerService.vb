Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Text.Json
Imports Dapper

Public Class CustomerHistoryRow
    Public Property Id As Long
    Public Property InvoiceNo As String
    Public Property InvoiceType As String
    Public Property BusinessDate As Date
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
    Public Property Status As String

    Public ReadOnly Property TypeArabic As String
        Get
            Select Case InvoiceType
                Case "sale" : Return "فاتورة بيع"
                Case "sale_return" : Return "مرتجع بيع"
                Case "purchase" : Return "فاتورة شراء"
                Case "purchase_return" : Return "مرتجع شراء"
                Case Else : Return InvoiceType
            End Select
        End Get
    End Property
End Class

Public NotInheritable Class CustomerService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String, customerType As String) As List(Of CustomerSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomersRepository.Search(db.Connection, term, customerType)
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, id As Long) As CustomerRecord
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomersRepository.GetById(db.Connection, id)
        End Using
    End Function

    ''' <summary>DOCTOR TECH POS Figure 4-2: Customer transaction history from database.</summary>
    Public Shared Function ListHistory(session As AppSession, customerId As Long, Optional limit As Integer = 100) As List(Of CustomerHistoryRow)
        Using db = DbSession.Open(session.ConnectionString)
            Const sql As String =
                "SELECT id AS Id, invoice_no AS InvoiceNo, invoice_type AS InvoiceType, " &
                "       business_date AS BusinessDate, total AS Total, paid AS Paid, due AS Due, status AS Status " &
                "FROM invoices " &
                "WHERE customer_id = @cid AND status = 'posted' " &
                "ORDER BY id DESC LIMIT @lim"
            Return db.Connection.Query(Of CustomerHistoryRow)(sql, New With {.cid = customerId, .lim = limit}).AsList()
        End Using
    End Function

    Public Shared Function Save(session As AppSession, customerId As Long?, name As String, customerType As String,
                                mobile As String, phone As String, email As String, address As String,
                                taxNo As String, creditLimit As Decimal, openingBalance As Decimal,
                                isActive As Boolean, Optional groupId As Long? = Nothing) As Long
        session.RequirePermission("customers.manage")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم العميل/المورد مطلوب.")
        If creditLimit < 0D Then Throw New BusinessException("حد الائتمان لا يمكن أن يكون سالبًا.")
        If customerType <> "customer" AndAlso customerType <> "supplier" AndAlso customerType <> "both" Then
            Throw New BusinessException("نوع الحساب غير صالح.")
        End If

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim newId As Long
            If customerId.HasValue AndAlso customerId.Value > 0 Then
                Dim before = CustomersRepository.GetById(db.Connection, customerId.Value, tx)
                If before Is Nothing Then Throw New BusinessException("الحساب غير موجود.")
                CustomersRepository.Update(db.Connection, tx, customerId.Value, name.Trim(), customerType,
                                           mobile, phone, email, address, taxNo, creditLimit, isActive, groupId)
                AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                    "customer_update", "customer", customerId.Value.ToString(),
                                    JsonSerializer.Serialize(before), Nothing, session.DeviceId)
                newId = customerId.Value
            Else
                newId = CustomersRepository.Insert(db.Connection, tx, Uuid7.NewId(), Nothing, name.Trim(),
                                                   customerType, mobile, phone, email, address, taxNo,
                                                   creditLimit, openingBalance, groupId)
                AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                    "customer_create", "customer", newId.ToString(), Nothing, Nothing, session.DeviceId)
            End If

            Dim payload As String = JsonSerializer.Serialize(New With {
                .id = newId, .name = name.Trim(), .type = customerType, .group_id = groupId,
                .mobile = mobile, .phone = phone, .address = address,
                .tax_no = taxNo, .credit_limit = creditLimit, .is_active = isActive
            })
            SyncStore.AppendChange(db.Connection, tx, "customer", newId.ToString(), "upsert", payload, session.BranchId)
            SyncStore.Enqueue(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.DeviceId,
                              "customer", newId.ToString(), "customer.upserted", 1, payload, Date.UtcNow)

            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Function Balance(session As AppSession, customerId As Long) As Decimal
        Using db = DbSession.Open(session.ConnectionString)
            Return CustomersRepository.Balance(db.Connection, customerId)
        End Using
    End Function
End Class

Public NotInheritable Class ShiftService
    Private Sub New()
    End Sub

    Public Shared Function Current(session As AppSession) As ShiftRecord
        Using db = DbSession.Open(session.ConnectionString)
            Return ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId)
        End Using
    End Function

    Public Shared Function List(session As AppSession, fromDate As Date, toDate As Date,
                                Optional onlyMine As Boolean = False) As List(Of ShiftRecord)
        If toDate < fromDate Then
            Dim swap = fromDate
            fromDate = toDate
            toDate = swap
        End If
        Using db = DbSession.Open(session.ConnectionString)
            Dim userId As Long? = If(onlyMine, session.UserId, CType(Nothing, Long?))
            Return ShiftsRepository.List(db.Connection, session.BranchId, fromDate, toDate, userId)
        End Using
    End Function

    Public Shared Function ExpectedCash(session As AppSession, shiftId As Long) As Decimal
        Using db = DbSession.Open(session.ConnectionString)
            Dim branch = db.Connection.ExecuteScalar(Of Long)(
                "SELECT branch_id FROM shifts WHERE id=@id", New With {.id = shiftId})
            If branch <> session.BranchId Then Throw New BusinessException("الوردية تخص فرعًا آخر.")
            Return ShiftsRepository.GetExpectedCash(db.Connection, shiftId)
        End Using
    End Function

    Public Shared Function Open(session As AppSession, openingCash As Decimal) As ShiftRecord
        session.RequirePermission("shifts.open")
        If openingCash < 0D Then Throw New BusinessException("النقدية الافتتاحية لا يمكن أن تكون سالبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim existing = ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId, tx)
            If existing IsNot Nothing Then Throw New BusinessException("لديك وردية مفتوحة بالفعل: " & existing.ShiftNo)

            Dim shift = ShiftsRepository.Open(db.Connection, tx, session.BranchId, session.UserId, openingCash)
            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                "shift_open", "shift", shift.Id.ToString(), Nothing, shift.ShiftNo, session.DeviceId)
            db.Commit()
            Return shift
        End Using
    End Function

    Public Shared Function Close(session As AppSession, shiftId As Long, countedCash As Decimal, notes As String) As Decimal
        session.RequirePermission("shifts.close")
        If countedCash < 0D Then Throw New BusinessException("النقدية المعدودة لا يمكن أن تكون سالبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim owner As Object = db.Connection.ExecuteScalar(
                "SELECT branch_id FROM shifts WHERE id=@id", New With {.id = shiftId}, tx)
            If owner Is Nothing OrElse IsDBNull(owner) Then Throw New BusinessException("الوردية غير موجودة.")
            If CLng(owner) <> session.BranchId Then
                Throw New BusinessException("الوردية تخص فرعًا آخر ولا يمكن إغلاقها من هذا الفرع.")
            End If
            Dim status As String = db.Connection.ExecuteScalar(Of String)(
                "SELECT status FROM shifts WHERE id=@id", New With {.id = shiftId}, tx)
            If status <> "open" Then Throw New BusinessException("هذه الوردية مغلقة بالفعل.")

            ShiftsRepository.Close(db.Connection, tx, shiftId, countedCash, notes)
            Dim variance As Decimal = db.Connection.ExecuteScalar(Of Decimal)(
                "SELECT variance FROM shifts WHERE id=@id", New With {.id = shiftId}, tx)

            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                "shift_close", "shift", shiftId.ToString(), Nothing,
                                $"counted={countedCash}; variance={variance}", session.DeviceId)
            db.Commit()
            Return variance
        End Using
    End Function

    ''' <summary>
    ''' DOCTOR TECH Retail POS Figure 3-33 Till Functions (Pay-In, Pay-Out, Till Loan, Till Pickup):
    ''' Writes a real cash movement into cash_moves linked to the current open shift and cashbox.
    ''' </summary>
    Public Shared Function RecordTillOperation(session As AppSession, operationType As String,
                                               amount As Decimal, reason As String) As Decimal
        If amount <= 0D Then Throw New BusinessException("المبلغ يجب أن يكون أكبر من صفر.")
        Dim op As String = If(operationType, "").Trim().ToLowerInvariant()
        Dim direction As String
        Dim refType As String
        Select Case op
            Case "payin", "pay-in"
                direction = "in"
                refType = "till_payin"
            Case "loan"
                direction = "in"
                refType = "till_loan"
            Case "payout", "pay-out"
                direction = "out"
                refType = "till_payout"
            Case "pickup"
                direction = "out"
                refType = "till_pickup"
            Case Else
                Throw New BusinessException("نوع حركة الخزينة غير معروف: " & operationType)
        End Select

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim shift = ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId, tx)
            If shift Is Nothing Then
                Throw New BusinessException("يجب فتح وردية أولاً (F2 Daily Ops / فتح وردية) قبل تسجيل حركات الخزينة.")
            End If

            Dim cashboxId = ShiftsRepository.GetDefaultCashbox(db.Connection, session.BranchId, tx)
            If Not cashboxId.HasValue Then
                Throw New BusinessException("لا توجد خزينة نشطة معرفة لهذا الفرع.")
            End If

            If direction = "out" Then
                Dim currentExpected As Decimal = ShiftsRepository.GetExpectedCash(db.Connection, shift.Id, tx)
                If amount > currentExpected Then
                    Throw New BusinessException("المبلغ المطلوب صرفه/سحبه (" & amount.ToString("N2") &
                                                ") أكبر من النقدية المتاحة بالدرج (" & currentExpected.ToString("N2") & ").")
                End If
            End If

            ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(), cashboxId.Value, shift.Id,
                                         direction, Money.Round2(amount), refType, shift.Id, session.UserId)

            Dim newExpected As Decimal = ShiftsRepository.GetExpectedCash(db.Connection, shift.Id, tx)
            db.Connection.Execute(
                "UPDATE shifts SET expected_cash = @exp WHERE id = @id",
                New With {.exp = newExpected, .id = shift.Id}, tx)

            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                refType, "shift", shift.Id.ToString(), Nothing,
                                $"amount={amount:N2}; dir={direction}; reason={reason}", session.DeviceId)

            db.Commit()
            Return newExpected
        End Using
    End Function

    Public Class SafeBalanceRow
        Public Property Id As Long
        Public Property Code As String
        Public Property Name As String
        Public Property BranchId As Long
        Public Property TotalIn As Decimal
        Public Property TotalOut As Decimal
        Public Property CurrentBalance As Decimal
    End Class

    ''' <summary>Returns all active cashboxes with their live net balances from cash_moves.</summary>
    Public Shared Function ListSafeBalances(session As AppSession) As List(Of SafeBalanceRow)
        Using db = DbSession.Open(session.ConnectionString)
            Const sql As String =
                "SELECT cb.id AS Id, cb.name AS Name, cb.branch_id AS BranchId, " &
                "       COALESCE(SUM(CASE WHEN cm.direction='in' THEN cm.amount ELSE 0 END), 0) AS TotalIn, " &
                "       COALESCE(SUM(CASE WHEN cm.direction='out' THEN cm.amount ELSE 0 END), 0) AS TotalOut, " &
                "       COALESCE(SUM(CASE WHEN cm.direction='in' THEN cm.amount WHEN cm.direction='out' THEN -cm.amount ELSE 0 END), 0) AS CurrentBalance " &
                "FROM cashboxes cb " &
                "LEFT JOIN cash_moves cm ON cm.cashbox_id = cb.id " &
                "WHERE cb.is_active = 1 AND (cb.branch_id = @b OR @b > 0) " &
                "GROUP BY cb.id, cb.name, cb.branch_id " &
                "ORDER BY cb.id"
            Return db.Connection.Query(Of SafeBalanceRow)(sql, New With {.b = session.BranchId}).AsList()
        End Using
    End Function

    ''' <summary>Transfers cash between two cashboxes inside a real DB transaction + audit_log.</summary>
    Public Shared Sub TransferBetweenSafes(session As AppSession, fromCashboxId As Long, toCashboxId As Long,
                                           amount As Decimal, notes As String)
        If fromCashboxId <= 0 OrElse toCashboxId <= 0 Then Throw New BusinessException("اختر خزينة المصدر وخزينة الوجهة.")
        If fromCashboxId = toCashboxId Then Throw New BusinessException("لا يمكن التحويل من الخزينة إلى نفسها.")
        If amount <= 0D Then Throw New BusinessException("مبلغ التحويل يجب أن يكون أكبر من صفر.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim shift = ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId, tx)
            Dim shiftId As Long? = If(shift IsNot Nothing, CType(shift.Id, Long?), Nothing)

            Dim available As Decimal = db.Connection.ExecuteScalar(Of Decimal)(
                "SELECT COALESCE(SUM(CASE WHEN direction='in' THEN amount ELSE -amount END), 0) FROM cash_moves WHERE cashbox_id=@id",
                New With {.id = fromCashboxId}, tx)
            If amount > available AndAlso available > 0D Then
                Throw New BusinessException("رصيد الخزينة المحوّل منها (" & available.ToString("N2") & ") أقل من المبلغ المطلوب (" & amount.ToString("N2") & ").")
            End If

            ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(), fromCashboxId, shiftId,
                                         "out", Money.Round2(amount), "safe_transfer_out", toCashboxId, session.UserId)
            ShiftsRepository.AddCashMove(db.Connection, tx, Uuid7.NewId(), toCashboxId, shiftId,
                                         "in", Money.Round2(amount), "safe_transfer_in", fromCashboxId, session.UserId)

            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId, session.Username,
                                "safe_transfer", "cashbox", fromCashboxId.ToString() & "->" & toCashboxId.ToString(),
                                Nothing, $"amount={amount:N2};notes={notes}", session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class
