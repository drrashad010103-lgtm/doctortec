Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>Customer groups master data.</summary>
Public NotInheritable Class CustomerGroupsRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String, includeInactive As Boolean,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of CustomerGroupSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT g.id, g.name, g.discount_percent, g.notes, g.is_active, " &
            "       (SELECT COUNT(*) FROM customers c WHERE c.group_id=g.id) AS customer_count, " &
            "       COALESCE((SELECT SUM(c.opening_balance + COALESCE((SELECT SUM(v.due) FROM invoices v " &
            "            WHERE v.customer_id=c.id AND v.status='posted'),0)) FROM customers c WHERE c.group_id=g.id),0) AS total_balance " &
            "FROM customer_groups g " &
            "WHERE (@includeInactive=1 OR g.is_active=1) " &
            "  AND (@term='' OR g.name LIKE @like) " &
            "ORDER BY g.name LIMIT @limit"
        Return conn.Query(Of CustomerGroupSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As CustomerGroupSummary
        Return conn.QueryFirstOrDefault(Of CustomerGroupSummary)(
            "SELECT id, name, discount_percent, notes, is_active FROM customer_groups WHERE id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function FindByName(conn As IDbConnection, name As String,
                                      Optional tx As IDbTransaction = Nothing) As CustomerGroupSummary
        Return conn.QueryFirstOrDefault(Of CustomerGroupSummary)(
            "SELECT id, name, discount_percent, notes, is_active FROM customer_groups WHERE name=@name",
            New With {.name = name}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, name As String,
                                  discountPercent As Decimal, notes As String) As Long
        conn.Execute(
            "INSERT INTO customer_groups(name, discount_percent, notes) VALUES(@name, @discount, @notes)",
            New With {.name = name, .discount = discountPercent, .notes = notes}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, name As String,
                             discountPercent As Decimal, notes As String, isActive As Boolean)
        conn.Execute(
            "UPDATE customer_groups SET name=@name, discount_percent=@discount, notes=@notes, is_active=@active " &
            "WHERE id=@id",
            New With {.id = id, .name = name, .discount = discountPercent, .notes = notes,
                      .active = isActive}, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE customer_groups SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub
End Class

''' <summary>Loyalty points accounts and movements.</summary>
Public NotInheritable Class LoyaltyRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of LoyaltyAccountSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT c.id AS customer_id, c.name AS customer_name, c.mobile, " &
            "       COALESCE(a.points,0) AS points, COALESCE(a.total_earned,0) AS total_earned, " &
            "       COALESCE(a.total_redeemed,0) AS total_redeemed, a.updated_at " &
            "FROM customers c LEFT JOIN loyalty_accounts a ON a.customer_id=c.id " &
            "WHERE c.is_active=1 AND (c.type='customer' OR c.type='both') " &
            "  AND (@term='' OR c.name LIKE @like OR c.mobile LIKE @like) " &
            "ORDER BY points DESC, c.name LIMIT @limit"
        Return conn.Query(Of LoyaltyAccountSummary)(sql,
            New With {.term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetPoints(conn As IDbConnection, tx As IDbTransaction, customerId As Long) As Decimal
        Dim sql As String = "SELECT points FROM loyalty_accounts WHERE customer_id=@id"
        If tx IsNot Nothing Then sql &= " FOR UPDATE"
        Dim value As Object = conn.ExecuteScalar(sql, New With {.id = customerId}, tx)
        If value Is Nothing OrElse Convert.IsDBNull(value) Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    Public Shared Sub ApplyPoints(conn As IDbConnection, tx As IDbTransaction, customerId As Long,
                                  pointsDelta As Decimal, earned As Decimal, redeemed As Decimal)
        conn.Execute(
            "INSERT INTO loyalty_accounts(customer_id, points, total_earned, total_redeemed) " &
            "VALUES(@id, @delta, @earned, @redeemed) " &
            "ON DUPLICATE KEY UPDATE points=points+@delta, total_earned=total_earned+@earned, " &
            "total_redeemed=total_redeemed+@redeemed",
            New With {.id = customerId, .delta = pointsDelta, .earned = earned, .redeemed = redeemed}, tx)
    End Sub

    Public Shared Sub AddTransaction(conn As IDbConnection, tx As IDbTransaction, customerId As Long,
                                     invoiceId As Long?, pointsDelta As Decimal, type As String,
                                     note As String, userId As Long)
        conn.Execute(
            "INSERT INTO loyalty_transactions(event_id, customer_id, invoice_id, points_delta, type, note, created_by) " &
            "VALUES(@eventId, @customerId, @invoiceId, @delta, @type, @note, @userId)",
            New With {.eventId = Uuid7.NewId(), .customerId = customerId, .invoiceId = invoiceId,
                      .delta = pointsDelta, .type = type, .note = note, .userId = userId}, tx)
    End Sub

    Public Shared Function Transactions(conn As IDbConnection, customerId As Long,
                                        Optional limit As Integer = 300,
                                        Optional tx As IDbTransaction = Nothing) As List(Of LoyaltyTransactionSummary)
        Return conn.Query(Of LoyaltyTransactionSummary)(
            "SELECT t.id, t.customer_id, v.invoice_no, t.points_delta, t.type, t.note, t.created_at, " &
            "       COALESCE(u.full_name,'') AS created_by_name " &
            "FROM loyalty_transactions t " &
            "LEFT JOIN invoices v ON v.id=t.invoice_id " &
            "LEFT JOIN users u ON u.id=t.created_by " &
            "WHERE t.customer_id=@id ORDER BY t.id DESC LIMIT @limit",
            New With {.id = customerId, .limit = limit}, tx).AsList()
    End Function
End Class

''' <summary>Read-only customer balances and payment movements.</summary>
Public NotInheritable Class CustomerFinanceRepository
    Private Sub New()
    End Sub

    Public Shared Function Balances(conn As IDbConnection, term As String, filter As String,
                                    Optional limit As Integer = 1000,
                                    Optional tx As IDbTransaction = Nothing) As List(Of CustomerBalanceSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim safeFilter As String = If(String.IsNullOrWhiteSpace(filter), "all", filter)
        Dim sql As String =
            "SELECT c.id, c.name, c.mobile, c.opening_balance, c.credit_limit, " &
            "       COALESCE(i.total_sum,0) AS invoiced_total, COALESCE(i.paid_sum,0) AS paid_total, " &
            "       COALESCE(i.due_sum,0) AS due, " &
            "       c.opening_balance + COALESCE(i.due_sum,0) AS balance " &
            "FROM customers c " &
            "LEFT JOIN (SELECT customer_id, SUM(total) AS total_sum, SUM(paid) AS paid_sum, SUM(due) AS due_sum " &
            "           FROM invoices WHERE status='posted' GROUP BY customer_id) i ON i.customer_id=c.id " &
            "WHERE c.is_active=1 AND (c.type='customer' OR c.type='both') " &
            "  AND (@term='' OR c.name LIKE @like OR c.mobile LIKE @like) " &
            "  AND (@filter='all' " &
            "       OR (@filter='debit' AND c.opening_balance + COALESCE(i.due_sum,0) > 0) " &
            "       OR (@filter='credit' AND c.opening_balance + COALESCE(i.due_sum,0) < 0) " &
            "       OR (@filter='over' AND c.credit_limit > 0 AND c.opening_balance + COALESCE(i.due_sum,0) > c.credit_limit)) " &
            "ORDER BY balance DESC, c.name LIMIT @limit"
        Return conn.Query(Of CustomerBalanceSummary)(sql,
            New With {.term = If(term, String.Empty).Trim(), .like = likeTerm,
                      .filter = safeFilter, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function Payments(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                    term As String, Optional limit As Integer = 2000,
                                    Optional tx As IDbTransaction = Nothing) As List(Of CustomerPaymentSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim toExclusive As Date = toDate.Date.AddDays(1)
        Dim sql As String =
            "SELECT p.id, p.paid_at, v.invoice_no, v.invoice_type, " &
            "       COALESCE(c.name, 'عميل نقدي') AS customer_name, pm.name AS method_name, " &
            "       p.amount, p.reference, COALESCE(u.full_name,'') AS user_name " &
            "FROM payments p " &
            "JOIN invoices v ON v.id=p.invoice_id " &
            "LEFT JOIN customers c ON c.id=v.customer_id " &
            "JOIN payment_methods pm ON pm.id=p.method_id " &
            "LEFT JOIN users u ON u.id=p.created_by " &
            "WHERE p.branch_id=@branch AND p.paid_at >= @from AND p.paid_at < @to " &
            "  AND (@term='' OR c.name LIKE @like OR v.invoice_no LIKE @like OR p.reference LIKE @like) " &
            "ORDER BY p.paid_at DESC LIMIT @limit"
        Return conn.Query(Of CustomerPaymentSummary)(sql,
            New With {.branch = branchId, .from = fromDate.Date, .to = toExclusive,
                      .term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    ''' <summary>Identity line used to head a statement (Nothing when the party does not exist).</summary>
    Public Shared Function PartyHeader(conn As IDbConnection, customerId As Long,
                                       Optional tx As IDbTransaction = Nothing) As PartyStatement
        Dim sql As String =
            "SELECT c.id AS party_id, c.name AS party_name, " &
            "       CASE c.type WHEN 'supplier' THEN 'مورد' WHEN 'both' THEN 'عميل ومورد' " &
            "            ELSE 'عميل' END AS party_type_name " &
            "FROM customers c WHERE c.id=@id"
        Return conn.QueryFirstOrDefault(Of PartyStatement)(sql, New With {.id = customerId}, tx)
    End Function

    ''' <summary>
    ''' Balance carried into a period. Same formula as Balances, restricted to documents dated
    ''' before the period so the statement's running balance ends on the figure the balances screen shows.
    ''' </summary>
    Public Shared Function OpeningBalance(conn As IDbConnection, customerId As Long, fromDate As Date,
                                          Optional tx As IDbTransaction = Nothing) As Decimal
        Dim sql As String =
            "SELECT COALESCE(c.opening_balance,0) + " &
            "       COALESCE((SELECT SUM(v.due) FROM invoices v " &
            "                 WHERE v.customer_id=c.id AND v.status='posted' AND v.business_date < @from),0) " &
            "FROM customers c WHERE c.id=@id"
        Dim value As Object = conn.ExecuteScalar(sql, New With {.id = customerId, .from = fromDate.Date}, tx)
        If value Is Nothing OrElse Convert.IsDBNull(value) Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    ''' <summary>
    ''' One row per posted document in the period, oldest first, without a running balance.
    ''' Returns are stored as negative invoice rows, so their charge is moved to the credit side and
    ''' their settlement to the debit side; either way debit minus credit equals that row's due.
    ''' </summary>
    Public Shared Function StatementRows(conn As IDbConnection, customerId As Long, fromDate As Date,
                                         toDate As Date, Optional limit As Integer = 5000,
                                         Optional tx As IDbTransaction = Nothing) As List(Of PartyStatementLine)
        Dim sql As String =
            "SELECT v.business_date AS entry_date, 'document' AS kind, " &
            "       CASE v.invoice_type " &
            "            WHEN 'sale' THEN 'فاتورة مبيعات' " &
            "            WHEN 'sale_return' THEN 'مرتجع مبيعات' " &
            "            WHEN 'purchase' THEN 'فاتورة مشتريات' " &
            "            WHEN 'purchase_return' THEN 'مرتجع مشتريات' " &
            "            WHEN 'opening' THEN 'رصيد افتتاحي' " &
            "            ELSE v.invoice_type END AS document_name, " &
            "       v.invoice_no AS reference, " &
            "       COALESCE(NULLIF(TRIM(v.notes),''), CONCAT('فرع: ', b.name)) AS description, " &
            "       CASE WHEN v.total >= 0 THEN v.total ELSE -v.paid END AS debit, " &
            "       CASE WHEN v.total >= 0 THEN v.paid ELSE -v.total END AS credit " &
            "FROM invoices v " &
            "JOIN branches b ON b.id=v.branch_id " &
            "WHERE v.customer_id=@id AND v.status='posted' " &
            "  AND v.business_date >= @from AND v.business_date <= @to " &
            "ORDER BY v.business_date, v.id LIMIT @limit"
        Return conn.Query(Of PartyStatementLine)(sql,
            New With {.id = customerId, .from = fromDate.Date, .to = toDate.Date, .limit = limit}, tx).AsList()
    End Function
End Class

''' <summary>Discount coupons and their redemptions.</summary>
Public NotInheritable Class CouponsRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String, includeInactive As Boolean,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of CouponSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT id, code, name, discount_type, discount_value, starts_at, ends_at, max_uses, used_count, is_active " &
            "FROM coupons " &
            "WHERE (@includeInactive=1 OR is_active=1) " &
            "  AND (@term='' OR code LIKE @like OR name LIKE @like) " &
            "ORDER BY is_active DESC, code LIMIT @limit"
        Return conn.Query(Of CouponSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As CouponSummary
        Return conn.QueryFirstOrDefault(Of CouponSummary)(
            "SELECT id, code, name, discount_type, discount_value, starts_at, ends_at, max_uses, used_count, is_active " &
            "FROM coupons WHERE id=@id", New With {.id = id}, tx)
    End Function

    Public Shared Function FindByCode(conn As IDbConnection, code As String,
                                      Optional tx As IDbTransaction = Nothing) As CouponSummary
        Return conn.QueryFirstOrDefault(Of CouponSummary)(
            "SELECT id, code, name, discount_type, discount_value, starts_at, ends_at, max_uses, used_count, is_active " &
            "FROM coupons WHERE code=@code", New With {.code = code}, tx)
    End Function

    Public Shared Function FindByCodeForUpdate(conn As IDbConnection, tx As IDbTransaction, code As String) As CouponSummary
        Return conn.QueryFirstOrDefault(Of CouponSummary)(
            "SELECT id, code, name, discount_type, discount_value, starts_at, ends_at, max_uses, used_count, is_active " &
            "FROM coupons WHERE code=@code FOR UPDATE", New With {.code = code}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, code As String, name As String,
                                  discountType As String, discountValue As Decimal, startsAt As Date?,
                                  endsAt As Date?, maxUses As Integer) As Long
        conn.Execute(
            "INSERT INTO coupons(code, name, discount_type, discount_value, starts_at, ends_at, max_uses) " &
            "VALUES(@code, @name, @type, @value, @starts, @ends, @maxUses)",
            New With {.code = code, .name = name, .type = discountType, .value = discountValue,
                      .starts = startsAt, .ends = endsAt, .maxUses = maxUses}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, code As String,
                             name As String, discountType As String, discountValue As Decimal,
                             startsAt As Date?, endsAt As Date?, maxUses As Integer, isActive As Boolean)
        conn.Execute(
            "UPDATE coupons SET code=@code, name=@name, discount_type=@type, discount_value=@value, " &
            "starts_at=@starts, ends_at=@ends, max_uses=@maxUses, is_active=@active WHERE id=@id",
            New With {.id = id, .code = code, .name = name, .type = discountType, .value = discountValue,
                      .starts = startsAt, .ends = endsAt, .maxUses = maxUses, .active = isActive}, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE coupons SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub

    Public Shared Function RecordRedemption(conn As IDbConnection, tx As IDbTransaction, couponId As Long,
                                            invoiceId As Long, customerId As Long?, amount As Decimal) As Integer
        Dim affected As Integer = conn.Execute(
            "UPDATE coupons SET used_count=used_count+1 " &
            "WHERE id=@id AND is_active=1 AND (max_uses=0 OR used_count<max_uses)",
            New With {.id = couponId}, tx)
        If affected = 0 Then Return 0

        conn.Execute(
            "INSERT INTO coupon_redemptions(coupon_id, invoice_id, customer_id, amount) " &
            "VALUES(@cid, @invId, @custId, @amount)",
            New With {.cid = couponId, .invId = invoiceId, .custId = customerId, .amount = amount}, tx)
        Return affected
    End Function

    Public Shared Function Redemptions(conn As IDbConnection, couponId As Long,
                                       Optional limit As Integer = 500,
                                       Optional tx As IDbTransaction = Nothing) As List(Of CouponRedemptionSummary)
        Return conn.Query(Of CouponRedemptionSummary)(
            "SELECT r.id, v.invoice_no, COALESCE(c.name,'عميل نقدي') AS customer_name, r.amount, r.redeemed_at " &
            "FROM coupon_redemptions r " &
            "JOIN invoices v ON v.id=r.invoice_id " &
            "LEFT JOIN customers c ON c.id=r.customer_id " &
            "WHERE r.coupon_id=@id ORDER BY r.redeemed_at DESC LIMIT @limit",
            New With {.id = couponId, .limit = limit}, tx).AsList()
    End Function
End Class

''' <summary>Prepaid purchase vouchers (gift vouchers) and their redemptions.</summary>
Public NotInheritable Class PurchaseVouchersRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String, includeInactive As Boolean,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of PurchaseVoucherSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT pv.id, pv.code, COALESCE(c.name,'بدون عميل') AS customer_name, pv.amount, " &
            "       pv.remaining_amount, pv.issued_at, pv.expires_at, pv.is_active, pv.notes " &
            "FROM purchase_vouchers pv LEFT JOIN customers c ON c.id=pv.customer_id " &
            "WHERE (@includeInactive=1 OR pv.is_active=1) " &
            "  AND (@term='' OR pv.code LIKE @like OR c.name LIKE @like OR c.mobile LIKE @like) " &
            "ORDER BY pv.issued_at DESC LIMIT @limit"
        Return conn.Query(Of PurchaseVoucherSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As PurchaseVoucherSummary
        Return conn.QueryFirstOrDefault(Of PurchaseVoucherSummary)(
            "SELECT pv.id, pv.code, COALESCE(c.name,'بدون عميل') AS customer_name, pv.amount, " &
            "       pv.remaining_amount, pv.issued_at, pv.expires_at, pv.is_active, pv.notes " &
            "FROM purchase_vouchers pv LEFT JOIN customers c ON c.id=pv.customer_id WHERE pv.id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function FindByCode(conn As IDbConnection, code As String,
                                      Optional tx As IDbTransaction = Nothing) As PurchaseVoucherSummary
        Dim sql As String =
            "SELECT pv.id, pv.code, COALESCE(c.name,'بدون عميل') AS customer_name, pv.amount, " &
            "       pv.remaining_amount, pv.issued_at, pv.expires_at, pv.is_active, pv.notes " &
            "FROM purchase_vouchers pv LEFT JOIN customers c ON c.id=pv.customer_id WHERE pv.code=@code"
        If tx IsNot Nothing Then sql &= " FOR UPDATE"
        Return conn.QueryFirstOrDefault(Of PurchaseVoucherSummary)(sql, New With {.code = code}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, code As String,
                                  customerId As Long?, amount As Decimal, expiresAt As Date?,
                                  notes As String, userId As Long) As Long
        conn.Execute(
            "INSERT INTO purchase_vouchers(uuid, code, customer_id, amount, remaining_amount, expires_at, notes, created_by) " &
            "VALUES(@uuid, @code, @customerId, @amount, @amount, @expires, @notes, @userId)",
            New With {.uuid = Uuid7.NewId(), .code = code, .customerId = customerId, .amount = amount,
                      .expires = expiresAt, .notes = notes, .userId = userId}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Function Redeem(conn As IDbConnection, tx As IDbTransaction, voucherId As Long,
                                  amount As Decimal, note As String, userId As Long,
                                  Optional invoiceId As Long? = Nothing) As Integer
        Dim affected As Integer = conn.Execute(
            "UPDATE purchase_vouchers SET remaining_amount=remaining_amount-@amount " &
            "WHERE id=@id AND is_active=1 AND remaining_amount>=@amount",
            New With {.id = voucherId, .amount = amount}, tx)
        If affected = 0 Then Return 0
        conn.Execute(
            "INSERT INTO purchase_voucher_redemptions(voucher_id, invoice_id, amount, note, created_by) " &
            "VALUES(@id, @invId, @amount, @note, @userId)",
            New With {.id = voucherId, .invId = invoiceId, .amount = amount, .note = note, .userId = userId}, tx)
        Return affected
    End Function

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE purchase_vouchers SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub

    Public Shared Function Redemptions(conn As IDbConnection, voucherId As Long,
                                       Optional limit As Integer = 300,
                                       Optional tx As IDbTransaction = Nothing) As List(Of PurchaseVoucherRedemptionSummary)
        Return conn.Query(Of PurchaseVoucherRedemptionSummary)(
            "SELECT r.id, v.invoice_no, r.amount, r.note, r.redeemed_at " &
            "FROM purchase_voucher_redemptions r " &
            "LEFT JOIN invoices v ON v.id=r.invoice_id " &
            "WHERE r.voucher_id=@id ORDER BY r.redeemed_at DESC LIMIT @limit",
            New With {.id = voucherId, .limit = limit}, tx).AsList()
    End Function
End Class
