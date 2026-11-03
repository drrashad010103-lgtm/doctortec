Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public Class SalesDayRow
    Public Property BusinessDate As Date
    Public Property InvoiceCount As Long
    Public Property Subtotal As Decimal
    Public Property Discounts As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
End Class

Public Class ProfitRow
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property QtySold As Decimal
    Public Property RevenueExTax As Decimal
    Public Property Cost As Decimal
    Public Property Profit As Decimal
End Class

Public Class TaxReportRow
    Public Property TaxRate As Decimal
    Public Property TaxCode As String
    Public Property TaxName As String
    Public Property OutputBase As Decimal
    Public Property OutputTax As Decimal
    Public Property InputBase As Decimal
    Public Property InputTax As Decimal
End Class

Public Class TaxReportTotals
    Public Property OutputBase As Decimal
    Public Property OutputTax As Decimal
    Public Property InputBase As Decimal
    Public Property InputTax As Decimal
    Public Property NetPayable As Decimal
    Public Property LedgerTaxMovement As Decimal
    Public Property Rows As New List(Of TaxReportRow)()
End Class

Public Class StockValueRow
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property QtyOnHand As Decimal
    Public Property LastCost As Decimal
    Public Property Value As Decimal
End Class

Public Class CashMoveReportRow
    Public Property Id As Long
    Public Property CashboxName As String
    Public Property ShiftNo As String
    Public Property Direction As String
    Public Property Amount As Decimal
    Public Property RefType As String
    Public Property CreatedAt As Date
    Public Property UserName As String

    Public ReadOnly Property DirectionArabic As String
        Get
            Return If(Direction = "in", "وارد (قبض)", "صادر (صرف)")
        End Get
    End Property
End Class

Public Class AuditReportRow
    Public Property Id As Long
    Public Property Action As String
    Public Property EntityType As String
    Public Property EntityId As String
    Public Property Username As String
    Public Property CreatedAt As Date
    Public Property AfterJson As String
End Class

Public NotInheritable Class ReportsRepository
    Private Sub New()
    End Sub

    Public Shared Function SalesByDay(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                      Optional tx As IDbTransaction = Nothing) As List(Of SalesDayRow)
        Return conn.Query(Of SalesDayRow)(
            "SELECT business_date, COUNT(*) AS invoice_count, SUM(subtotal) AS subtotal, " &
            "       SUM(line_discount + header_discount) AS discounts, SUM(tax_total) AS tax_total, " &
            "       SUM(total) AS total, SUM(paid) AS paid, SUM(due) AS due " &
            "FROM invoices WHERE branch_id=@b AND status='posted' " &
            "  AND invoice_type IN ('sale','sale_return') AND business_date BETWEEN @from AND @to " &
            "GROUP BY business_date ORDER BY business_date DESC",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function SalesByType(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                       Optional tx As IDbTransaction = Nothing) As List(Of KeyValuePair(Of String, Decimal))
        Return conn.Query(Of KeyValuePair(Of String, Decimal))(
            "SELECT invoice_type AS `Key`, SUM(total) AS `Value` FROM invoices " &
            "WHERE branch_id=@b AND status='posted' AND business_date BETWEEN @from AND @to " &
            "GROUP BY invoice_type ORDER BY invoice_type",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function Profit(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                  Optional tx As IDbTransaction = Nothing) As List(Of ProfitRow)
        Return conn.Query(Of ProfitRow)(
            "SELECT l.item_id, i.code, i.name_ar, " &
            "       SUM(l.qty) AS qty_sold, " &
            "       SUM((l.line_total - l.tax_amount)) AS revenue_ex_tax, " &
            "       SUM(l.qty * l.cost_at_sale) AS cost, " &
            "       SUM((l.line_total - l.tax_amount) - (l.qty * l.cost_at_sale)) AS profit " &
            "FROM invoice_lines l " &
            "JOIN invoices v ON v.id=l.invoice_id " &
            "JOIN items i ON i.id=l.item_id " &
            "WHERE v.branch_id=@b AND v.status='posted' " &
            "  AND v.invoice_type IN ('sale','sale_return') " &
            "  AND v.business_date BETWEEN @from AND @to " &
            "GROUP BY l.item_id, i.code, i.name_ar " &
            "ORDER BY profit DESC",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function ProfitByGroup(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                         Optional tx As IDbTransaction = Nothing) As List(Of ProfitRow)
        Return conn.Query(Of ProfitRow)(
            "SELECT COALESCE(g.id, 0) AS item_id, COALESCE(g.code, 'GEN') AS code, " &
            "       COALESCE(g.name, 'عام / بدون مجموعة') AS name_ar, " &
            "       SUM(l.qty) AS qty_sold, " &
            "       SUM((l.line_total - l.tax_amount)) AS revenue_ex_tax, " &
            "       SUM(l.qty * l.cost_at_sale) AS cost, " &
            "       SUM((l.line_total - l.tax_amount) - (l.qty * l.cost_at_sale)) AS profit " &
            "FROM invoice_lines l " &
            "JOIN invoices v ON v.id=l.invoice_id " &
            "JOIN items i ON i.id=l.item_id " &
            "LEFT JOIN item_groups g ON g.id=i.group_id " &
            "WHERE v.branch_id=@b AND v.status='posted' " &
            "  AND v.invoice_type IN ('sale','sale_return') " &
            "  AND v.business_date BETWEEN @from AND @to " &
            "GROUP BY COALESCE(g.id, 0), COALESCE(g.code, 'GEN'), COALESCE(g.name, 'عام / بدون مجموعة') " &
            "ORDER BY profit DESC",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function TaxReport(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                     Optional tx As IDbTransaction = Nothing) As TaxReportTotals
        Dim rows = conn.Query(Of TaxReportRow)(
            "SELECT l.tax_rate, COALESCE(t.code,'') AS tax_code, COALESCE(t.name_ar,'') AS tax_name, " &
            "       SUM(CASE WHEN v.invoice_type IN ('sale','sale_return') THEN l.tax_base ELSE 0 END) AS output_base, " &
            "       SUM(CASE WHEN v.invoice_type IN ('sale','sale_return') THEN l.tax_amount ELSE 0 END) AS output_tax, " &
            "       SUM(CASE WHEN v.invoice_type IN ('purchase','purchase_return') THEN l.tax_base ELSE 0 END) AS input_base, " &
            "       SUM(CASE WHEN v.invoice_type IN ('purchase','purchase_return') THEN l.tax_amount ELSE 0 END) AS input_tax " &
            "FROM invoice_lines l " &
            "JOIN invoices v ON v.id=l.invoice_id " &
            "LEFT JOIN tax_categories t ON t.id=l.tax_category_id " &
            "WHERE v.branch_id=@b AND v.status='posted' AND v.business_date BETWEEN @from AND @to " &
            "GROUP BY l.tax_rate, t.code, t.name_ar ORDER BY l.tax_rate DESC",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()

        Dim totals As New TaxReportTotals With {.Rows = rows}
        For Each r In rows
            totals.OutputBase += r.OutputBase
            totals.OutputTax += r.OutputTax
            totals.InputBase += r.InputBase
            totals.InputTax += r.InputTax
        Next
        totals.NetPayable = Money.Round2(totals.OutputTax - totals.InputTax)
        Return totals
    End Function

    Public Shared Function TaxAccountMovement(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                              Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT COALESCE(SUM(jl.credit - jl.debit),0) FROM journal_lines jl " &
            "JOIN journal_entries je ON je.id=jl.entry_id " &
            "JOIN accounts a ON a.id=jl.account_id " &
            "WHERE a.code=@code AND je.status='posted' AND je.branch_id=@b " &
            "AND je.entry_date BETWEEN @from AND @to",
            New With {.code = AccountingRepository.AccOutputTax, .b = branchId, .from = fromDate, .to = toDate}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    Public Shared Function StockValue(conn As IDbConnection, branchId As Long,
                                      Optional tx As IDbTransaction = Nothing) As List(Of StockValueRow)
        Return conn.Query(Of StockValueRow)(
            "SELECT item_id, code, name_ar, qty_on_hand, last_cost, (qty_on_hand * last_cost) AS value " &
            "FROM v_stock WHERE branch_id=@b AND qty_on_hand <> 0 ORDER BY value DESC",
            New With {.b = branchId}, tx).AsList()
    End Function

    Public Shared Function TopItems(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                    Optional limit As Integer = 50,
                                    Optional tx As IDbTransaction = Nothing) As List(Of ProfitRow)
        Return conn.Query(Of ProfitRow)(
            "SELECT l.item_id, i.code, i.name_ar, SUM(l.qty) AS qty_sold, " &
            "       SUM(l.line_total - l.tax_amount) AS revenue_ex_tax, " &
            "       SUM(l.qty * l.cost_at_sale) AS cost, " &
            "       SUM((l.line_total - l.tax_amount) - (l.qty * l.cost_at_sale)) AS profit " &
            "FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id JOIN items i ON i.id=l.item_id " &
            "WHERE v.branch_id=@b AND v.status='posted' AND v.invoice_type='sale' " &
            "  AND v.business_date BETWEEN @from AND @to " &
            "GROUP BY l.item_id, i.code, i.name_ar ORDER BY qty_sold DESC LIMIT @limit",
            New With {.b = branchId, .from = fromDate, .to = toDate, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function CashSummary(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                       Optional tx As IDbTransaction = Nothing) As List(Of KeyValuePair(Of String, Decimal))
        Return conn.Query(Of KeyValuePair(Of String, Decimal))(
            "SELECT pm.name AS `Key`, SUM(p.amount) AS `Value` FROM payments p " &
            "JOIN payment_methods pm ON pm.id=p.method_id " &
            "WHERE p.branch_id=@b AND p.paid_at >= @from AND p.paid_at < DATE_ADD(@to, INTERVAL 1 DAY) " &
            "GROUP BY pm.name ORDER BY pm.name",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function CustomerDebt(conn As IDbConnection, branchId As Long, limit As Integer,
                                        Optional tx As IDbTransaction = Nothing) As List(Of CustomerSummary)
        Return conn.Query(Of CustomerSummary)(
            "SELECT c.id, c.uuid, c.name, c.type, c.mobile, c.tax_no, c.credit_limit, " &
            "       c.opening_balance + COALESCE(SUM(v.due),0) AS balance " &
            "FROM customers c LEFT JOIN invoices v ON v.customer_id=c.id AND v.status='posted' AND v.branch_id=@b " &
            "WHERE c.is_active=1 GROUP BY c.id, c.uuid, c.name, c.type, c.mobile, c.tax_no, c.credit_limit, c.opening_balance " &
            "HAVING balance <> 0 ORDER BY balance DESC LIMIT @limit",
            New With {.b = branchId, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function CashMovesReport(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                           Optional direction As String = "") As List(Of CashMoveReportRow)
        Dim sql As String =
            "SELECT cm.id AS Id, COALESCE(cb.name, 'الخزينة الرئيسية') AS CashboxName, " &
            "       COALESCE(sh.shift_no, '-') AS ShiftNo, cm.direction AS Direction, " &
            "       cm.amount AS Amount, COALESCE(cm.ref_type, '') AS RefType, " &
            "       cm.created_at AS CreatedAt, COALESCE(u.username, 'system') AS UserName " &
            "FROM cash_moves cm " &
            "LEFT JOIN cashboxes cb ON cb.id = cm.cashbox_id " &
            "LEFT JOIN shifts sh ON sh.id = cm.shift_id " &
            "LEFT JOIN users u ON u.id = cm.created_by " &
            "WHERE (cb.branch_id = @b OR sh.branch_id = @b OR @b > 0) " &
            "  AND cm.created_at >= @from AND cm.created_at < DATE_ADD(@to, INTERVAL 1 DAY) "
        If Not String.IsNullOrWhiteSpace(direction) Then
            sql &= " AND cm.direction = @dir "
        End If
        sql &= "ORDER BY cm.id DESC LIMIT 500"
        Return conn.Query(Of CashMoveReportRow)(sql, New With {.b = branchId, .from = fromDate.Date, .to = toDate.Date, .dir = direction}).AsList()
    End Function

    Public Shared Function AuditLogReport(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                          Optional limit As Integer = 300) As List(Of AuditReportRow)
        Return conn.Query(Of AuditReportRow)(
            "SELECT id AS Id, action AS Action, entity_type AS EntityType, COALESCE(entity_id,'') AS EntityId, " &
            "       COALESCE(username,'system') AS Username, created_at AS CreatedAt, COALESCE(after_json,'') AS AfterJson " &
            "FROM audit_log " &
            "WHERE (branch_id=@b OR branch_id IS NULL) AND created_at >= @from AND created_at < DATE_ADD(@to, INTERVAL 1 DAY) " &
            "ORDER BY id DESC LIMIT @lim",
            New With {.b = branchId, .from = fromDate.Date, .to = toDate.Date, .lim = limit}).AsList()
    End Function
End Class
