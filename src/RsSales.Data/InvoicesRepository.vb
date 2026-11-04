Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public Class InvoiceListRow
    Public Property Id As Long
    Public Property InvoiceNo As String
    Public Property InvoiceType As String
    Public Property Status As String
    Public Property BusinessDate As Date
    Public Property CustomerName As String
    Public Property Subtotal As Decimal
    Public Property HeaderDiscount As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
    Public Property PaymentStatus As String

    Public ReadOnly Property PaymentStatusName As String
        Get
            Select Case If(PaymentStatus, String.Empty)
                Case "paid" : Return "مدفوعة"
                Case "partial" : Return "مدفوعة جزئيًا"
                Case "unpaid" : Return "غير مدفوعة"
                Case Else : Return PaymentStatus
            End Select
        End Get
    End Property
End Class

Public Class ReturnableLine
    Public Property LineId As Long
    Public Property ItemId As Long
    Public Property ItemName As String
    Public Property Qty As Decimal
    Public Property ReturnedQty As Decimal
    Public Property UnitPrice As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
End Class

Public NotInheritable Class InvoicesRepository
    Private Sub New()
    End Sub

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction,
                                  eventId As String, branchId As Long, invoiceNo As String,
                                  invoiceType As String, status As String, customerId As Long?,
                                  sellerId As Long?, deliveryId As Long?,
                                  originalInvoiceId As Long?, shiftId As Long?,
                                  subtotal As Decimal, lineDiscount As Decimal, headerDiscount As Decimal,
                                  taxBaseTotal As Decimal, taxTotal As Decimal, total As Decimal,
                                  paid As Decimal, due As Decimal, paymentStatus As String,
                                  priceMode As String, sellerTaxNo As String, buyerTaxNo As String,
                                  buyerName As String, buyerAddress As String, qrPayload As String,
                                  occurredAt As Date, businessDate As Date, userId As Long,
                                  deviceId As Long?, notes As String) As Long
        conn.Execute(
            "INSERT INTO invoices(event_id, branch_id, invoice_no, invoice_type, status, customer_id, " &
            "seller_id, delivery_id, original_invoice_id, shift_id, subtotal, line_discount, header_discount, " &
            "tax_base_total, tax_total, total, paid, due, payment_status, price_mode, seller_tax_no, buyer_tax_no, " &
            "buyer_name, buyer_address, qr_payload, occurred_at, business_date, posted_at, created_by, device_id, notes) " &
            "VALUES(@eventId, @branchId, @invoiceNo, @type, @status, @customerId, @sellerId, @deliveryId, " &
            "@origId, @shiftId, " &
            "@subtotal, @lineDiscount, @headerDiscount, @taxBase, @taxTotal, @total, @paid, @due, @payStatus, " &
            "@priceMode, @sellerTaxNo, @buyerTaxNo, @buyerName, @buyerAddress, @qr, @occurredAt, @businessDate, " &
            "UTC_TIMESTAMP(6), @userId, @deviceId, @notes)",
            New With {
                .eventId = eventId, .branchId = branchId, .invoiceNo = invoiceNo, .type = invoiceType,
                .status = status, .customerId = customerId, .sellerId = sellerId, .deliveryId = deliveryId,
                .origId = originalInvoiceId, .shiftId = shiftId,
                .subtotal = subtotal, .lineDiscount = lineDiscount, .headerDiscount = headerDiscount,
                .taxBase = taxBaseTotal, .taxTotal = taxTotal, .total = total, .paid = paid, .due = due,
                .payStatus = paymentStatus, .priceMode = priceMode, .sellerTaxNo = sellerTaxNo,
                .buyerTaxNo = buyerTaxNo, .buyerName = buyerName, .buyerAddress = buyerAddress,
                .qr = qrPayload, .occurredAt = occurredAt, .businessDate = businessDate,
                .userId = userId, .deviceId = deviceId, .notes = notes
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Function InsertLine(conn As IDbConnection, tx As IDbTransaction, invoiceId As Long,
                                      itemId As Long, unitId As Long?, qty As Decimal, bonusQty As Decimal,
                                      unitPrice As Decimal, discount As Decimal, taxCategoryId As Long,
                                      taxRate As Decimal, taxBase As Decimal, taxAmount As Decimal,
                                      priceMode As String, lineTotal As Decimal,
                                      costAtSale As Decimal, costAtSaleInclTax As Decimal,
                                      originalLineId As Long?, batchNo As String, expiryDate As Date?,
                                      notes As String) As Long
        conn.Execute(
            "INSERT INTO invoice_lines(invoice_id, item_id, unit_id, qty, bonus_qty, unit_price, discount, " &
            "tax_category_id, tax_rate, tax_base, tax_amount, price_mode, line_total, cost_at_sale, " &
            "cost_at_sale_incl_tax, original_line_id, batch_no, expiry_date, notes) " &
            "VALUES(@invoiceId, @itemId, @unitId, @qty, @bonus, @price, @discount, @taxCat, @taxRate, " &
            "@taxBase, @taxAmount, @priceMode, @lineTotal, @cost, @costIncl, @origLine, @batch, @expiry, @notes)",
            New With {
                .invoiceId = invoiceId, .itemId = itemId, .unitId = unitId, .qty = qty, .bonus = bonusQty,
                .price = unitPrice, .discount = discount, .taxCat = taxCategoryId, .taxRate = taxRate,
                .taxBase = taxBase, .taxAmount = taxAmount, .priceMode = priceMode, .lineTotal = lineTotal,
                .cost = costAtSale, .costIncl = costAtSaleInclTax, .origLine = originalLineId,
                .batch = batchNo, .expiry = expiryDate, .notes = notes
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    ''' <summary>Rewrites header totals for invoices whose lines are built after the header insert.</summary>
    Public Shared Sub UpdateTotals(conn As IDbConnection, tx As IDbTransaction, id As Long,
                                   subtotal As Decimal, taxBaseTotal As Decimal, taxTotal As Decimal,
                                   total As Decimal, paid As Decimal, due As Decimal, paymentStatus As String)
        conn.Execute(
            "UPDATE invoices SET subtotal=@subtotal, tax_base_total=@taxBase, tax_total=@taxTotal, " &
            "total=@total, paid=@paid, due=@due, payment_status=@payStatus WHERE id=@id AND status='posted'",
            New With {
                .id = id, .subtotal = subtotal, .taxBase = taxBaseTotal, .taxTotal = taxTotal,
                .total = total, .paid = paid, .due = due, .payStatus = paymentStatus
            }, tx)
    End Sub

    Public Shared Sub InsertPayment(conn As IDbConnection, tx As IDbTransaction, eventId As String,
                                    invoiceId As Long?, branchId As Long, methodId As Long,
                                    amount As Decimal, reference As String, paidAt As Date, userId As Long)
        conn.Execute(
            "INSERT INTO payments(event_id, invoice_id, branch_id, method_id, amount, reference, paid_at, created_by) " &
            "VALUES(@eventId, @invoiceId, @branchId, @methodId, @amount, @reference, @paidAt, @userId)",
            New With {
                .eventId = eventId, .invoiceId = invoiceId, .branchId = branchId, .methodId = methodId,
                .amount = amount, .reference = reference, .paidAt = paidAt, .userId = userId
            }, tx)
    End Sub

    ''' <summary>
    ''' Stores a settlement on an already posted invoice: only the paid/due pair and the
    ''' payment status change, so the totals, lines and tax of the document stay untouched.
    ''' </summary>
    Public Shared Sub SetSettlement(conn As IDbConnection, tx As IDbTransaction, id As Long,
                                    paid As Decimal, due As Decimal, paymentStatus As String)
        conn.Execute(
            "UPDATE invoices SET paid=@paid, due=@due, payment_status=@payStatus " &
            "WHERE id=@id AND status='posted'",
            New With {.id = id, .paid = paid, .due = due, .payStatus = paymentStatus}, tx)
    End Sub

    Public Shared Function GetById(conn As IDbConnection, id As Long, Optional tx As IDbTransaction = Nothing) As InvoiceHeaderRecord
        Return conn.QueryFirstOrDefault(Of InvoiceHeaderRecord)(
            "SELECT id, event_id, branch_id, invoice_no, invoice_type, status, customer_id, subtotal, " &
            "line_discount, header_discount, tax_base_total, tax_total, total, paid, due, price_mode, " &
            "qr_payload, business_date, occurred_at FROM invoices WHERE id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function GetLines(conn As IDbConnection, invoiceId As Long, Optional tx As IDbTransaction = Nothing) As List(Of InvoiceLineRecord)
        Return conn.Query(Of InvoiceLineRecord)(
            "SELECT id, invoice_id, item_id, qty, unit_price, discount, tax_category_id, tax_rate, " &
            "tax_base, tax_amount, line_total, cost_at_sale, original_line_id " &
            "FROM invoice_lines WHERE invoice_id=@id ORDER BY id",
            New With {.id = invoiceId}, tx).AsList()
    End Function

    Public Shared Function List(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                invoiceType As String, Optional limit As Integer = 500,
                                Optional tx As IDbTransaction = Nothing) As List(Of InvoiceListRow)
        Dim sql As String =
            "SELECT v.id, v.invoice_no, v.invoice_type, v.status, v.business_date, " &
            "       COALESCE(c.name, v.buyer_name, '') AS customer_name, v.subtotal, v.header_discount, " &
            "       v.tax_total, v.total, v.paid, v.due, v.payment_status " &
            "FROM invoices v LEFT JOIN customers c ON c.id=v.customer_id " &
            "WHERE v.branch_id=@b AND v.business_date BETWEEN @from AND @to "
        If Not String.IsNullOrWhiteSpace(invoiceType) Then sql &= " AND v.invoice_type=@type "
        sql &= "ORDER BY v.id DESC LIMIT @limit"

        Return conn.Query(Of InvoiceListRow)(sql,
            New With {.b = branchId, .from = fromDate, .to = toDate, .type = invoiceType, .limit = limit}, tx).AsList()
    End Function

    ''' <summary>Lines of an original invoice with the quantity already returned.</summary>
    Public Shared Function GetReturnableLines(conn As IDbConnection, originalInvoiceId As Long,
                                              Optional tx As IDbTransaction = Nothing) As List(Of ReturnableLine)
        Return conn.Query(Of ReturnableLine)(
            "SELECT l.id AS line_id, l.item_id, i.name_ar AS item_name, l.qty, " &
            "       COALESCE((SELECT SUM(rl.qty) FROM invoice_lines rl " &
            "                 JOIN invoices r ON r.id=rl.invoice_id " &
            "                 WHERE rl.original_line_id=l.id AND r.status='posted'),0) AS returned_qty, " &
            "       l.unit_price, l.tax_category_id, l.tax_rate " &
            "FROM invoice_lines l JOIN items i ON i.id=l.item_id " &
            "WHERE l.invoice_id=@id ORDER BY l.id",
            New With {.id = originalInvoiceId}, tx).AsList()
    End Function

    Public Shared Sub Void(conn As IDbConnection, tx As IDbTransaction, id As Long, reason As String, userId As Long)
        conn.Execute(
            "UPDATE invoices SET status='voided', voided_at=UTC_TIMESTAMP(6), void_reason=@reason, voided_by=@uid " &
            "WHERE id=@id AND status='posted'",
            New With {.id = id, .reason = reason, .uid = userId}, tx)
    End Sub

    Public Shared Function CountForBranch(conn As IDbConnection, branchId As Long, businessDate As Date,
                                          Optional tx As IDbTransaction = Nothing) As Long
        Return conn.ExecuteScalar(Of Long)(
            "SELECT COUNT(*) FROM invoices WHERE branch_id=@b AND business_date=@d AND status='posted'",
            New With {.b = branchId, .d = businessDate}, tx)
    End Function
End Class
