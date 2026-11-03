Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>Persistence for service quotations and their lines.</summary>
Public NotInheritable Class QuotesRepository
    Private Sub New()
    End Sub

    Private Const HeaderColumns As String =
        "q.id, q.quote_no, q.branch_id, q.customer_id, COALESCE(c.name,'') AS customer_name, " &
        "q.status, q.valid_until, q.subtotal, q.line_discount, q.header_discount, " &
        "q.tax_base_total, q.tax_total, q.total, q.notes, q.created_at, " &
        "COALESCE(u.full_name, u.username, '') AS created_by_name, " &
        "q.converted_invoice_id, COALESCE(v.invoice_no,'') AS converted_invoice_no"

    Public Shared Function List(conn As IDbConnection, branchId As Long, status As String,
                                term As String, Optional limit As Integer = 500,
                                Optional tx As IDbTransaction = Nothing) As List(Of QuoteSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT q.id, q.quote_no, COALESCE(c.name,'عميل نقدي') AS customer_name, q.status, " &
            "       q.valid_until, q.created_at, q.total, q.converted_invoice_id, " &
            "       COALESCE(v.invoice_no,'') AS converted_invoice_no, " &
            "       COALESCE(u.full_name, u.username, '') AS created_by_name " &
            "FROM quotes q " &
            "LEFT JOIN customers c ON c.id=q.customer_id " &
            "LEFT JOIN invoices v ON v.id=q.converted_invoice_id " &
            "LEFT JOIN users u ON u.id=q.created_by " &
            "WHERE q.branch_id=@b "
        If Not String.IsNullOrWhiteSpace(status) Then sql &= " AND q.status=@status "
        sql &= " AND (@term='' OR q.quote_no LIKE @like OR c.name LIKE @like) " &
               "ORDER BY q.id DESC LIMIT @limit"

        Return conn.Query(Of QuoteSummary)(sql,
            New With {.b = branchId, .status = status, .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As QuoteDetail
        Return conn.QueryFirstOrDefault(Of QuoteDetail)(
            "SELECT " & HeaderColumns & " FROM quotes q " &
            "LEFT JOIN customers c ON c.id=q.customer_id " &
            "LEFT JOIN users u ON u.id=q.created_by " &
            "LEFT JOIN invoices v ON v.id=q.converted_invoice_id " &
            "WHERE q.id=@id", New With {.id = id}, tx)
    End Function

    Public Shared Function GetLines(conn As IDbConnection, quoteId As Long,
                                    Optional tx As IDbTransaction = Nothing) As List(Of QuoteLineDetail)
        Return conn.Query(Of QuoteLineDetail)(
            "SELECT l.id, l.item_id, i.code AS item_code, i.name_ar AS item_name, l.qty, " &
            "       l.unit_price, l.discount, l.tax_category_id, l.tax_rate, l.tax_base, " &
            "       l.tax_amount, l.line_total, l.notes " &
            "FROM quote_lines l JOIN items i ON i.id=l.item_id " &
            "WHERE l.quote_id=@id ORDER BY l.id",
            New With {.id = quoteId}, tx).AsList()
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction,
                                  uuid As String, quoteNo As String, branchId As Long,
                                  customerId As Long?, validUntil As Date?, subtotal As Decimal,
                                  lineDiscount As Decimal, headerDiscount As Decimal,
                                  taxBaseTotal As Decimal, taxTotal As Decimal, total As Decimal,
                                  priceMode As String, notes As String, userId As Long) As Long
        conn.Execute(
            "INSERT INTO quotes(uuid, quote_no, branch_id, customer_id, status, valid_until, " &
            "subtotal, line_discount, header_discount, tax_base_total, tax_total, total, " &
            "price_mode, notes, created_by) VALUES(@uuid, @no, @branch, @customer, 'draft', " &
            "@validUntil, @subtotal, @lineDiscount, @headerDiscount, @taxBase, @taxTotal, @total, " &
            "@priceMode, @notes, @userId)",
            New With {
                .uuid = uuid, .no = quoteNo, .branch = branchId, .customer = customerId,
                .validUntil = validUntil, .subtotal = subtotal, .lineDiscount = lineDiscount,
                .headerDiscount = headerDiscount, .taxBase = taxBaseTotal, .taxTotal = taxTotal,
                .total = total, .priceMode = priceMode, .notes = notes, .userId = userId
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long,
                             customerId As Long?, validUntil As Date?, subtotal As Decimal,
                             lineDiscount As Decimal, headerDiscount As Decimal,
                             taxBaseTotal As Decimal, taxTotal As Decimal, total As Decimal,
                             priceMode As String, notes As String)
        conn.Execute(
            "UPDATE quotes SET customer_id=@customer, valid_until=@validUntil, subtotal=@subtotal, " &
            "line_discount=@lineDiscount, header_discount=@headerDiscount, tax_base_total=@taxBase, " &
            "tax_total=@taxTotal, total=@total, price_mode=@priceMode, notes=@notes WHERE id=@id",
            New With {
                .id = id, .customer = customerId, .validUntil = validUntil, .subtotal = subtotal,
                .lineDiscount = lineDiscount, .headerDiscount = headerDiscount, .taxBase = taxBaseTotal,
                .taxTotal = taxTotal, .total = total, .priceMode = priceMode, .notes = notes
            }, tx)
    End Sub

    Public Shared Sub DeleteLines(conn As IDbConnection, tx As IDbTransaction, quoteId As Long)
        conn.Execute("DELETE FROM quote_lines WHERE quote_id=@id", New With {.id = quoteId}, tx)
    End Sub

    Public Shared Function InsertLine(conn As IDbConnection, tx As IDbTransaction, quoteId As Long,
                                      itemId As Long, qty As Decimal, unitPrice As Decimal,
                                      discount As Decimal, taxCategoryId As Long, taxRate As Decimal,
                                      taxBase As Decimal, taxAmount As Decimal, lineTotal As Decimal,
                                      notes As String) As Long
        conn.Execute(
            "INSERT INTO quote_lines(quote_id, item_id, qty, unit_price, discount, tax_category_id, " &
            "tax_rate, tax_base, tax_amount, line_total, notes) VALUES(@quoteId, @itemId, @qty, " &
            "@price, @discount, @taxCat, @taxRate, @taxBase, @taxAmount, @lineTotal, @notes)",
            New With {
                .quoteId = quoteId, .itemId = itemId, .qty = qty, .price = unitPrice,
                .discount = discount, .taxCat = taxCategoryId, .taxRate = taxRate,
                .taxBase = taxBase, .taxAmount = taxAmount, .lineTotal = lineTotal, .notes = notes
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub SetStatus(conn As IDbConnection, tx As IDbTransaction, id As Long, status As String)
        conn.Execute("UPDATE quotes SET status=@status WHERE id=@id",
                     New With {.id = id, .status = status}, tx)
    End Sub

    ''' <summary>Marks the quotation converted; returns 0 when it was converted or cancelled meanwhile.</summary>
    Public Shared Function MarkConverted(conn As IDbConnection, tx As IDbTransaction, id As Long,
                                         invoiceId As Long) As Integer
        Return conn.Execute(
            "UPDATE quotes SET status='converted', converted_invoice_id=@invoiceId, " &
            "converted_at=UTC_TIMESTAMP(6) WHERE id=@id AND status IN ('draft','sent','accepted')",
            New With {.id = id, .invoiceId = invoiceId}, tx)
    End Function

    Public Shared Sub Delete(conn As IDbConnection, tx As IDbTransaction, id As Long)
        conn.Execute("DELETE FROM quote_lines WHERE quote_id=@id", New With {.id = id}, tx)
        conn.Execute("DELETE FROM quotes WHERE id=@id", New With {.id = id}, tx)
    End Sub
End Class
