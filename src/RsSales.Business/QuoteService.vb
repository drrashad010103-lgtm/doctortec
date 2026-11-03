Imports System.Collections.Generic
Imports System.Linq
Imports Dapper

''' <summary>
''' Service quotations: draft/sent/accepted/rejected workflow plus one-click
''' conversion into a posted sales invoice. Quotations do not move stock or cash.
''' </summary>
Public NotInheritable Class QuoteService
    Private Sub New()
    End Sub

    Public Shared Function List(session As AppSession, status As String,
                                term As String) As List(Of QuoteSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Dim rows = QuotesRepository.List(db.Connection, session.BranchId, status, term)
            For Each row In rows
                row.StatusName = StatusName(row.Status)
            Next
            Return rows
        End Using
    End Function

    Public Shared Function [Get](session As AppSession, quoteId As Long) As QuoteDetail
        Using db = DbSession.Open(session.ConnectionString)
            Dim quote = QuotesRepository.GetById(db.Connection, quoteId)
            If quote Is Nothing Then Throw New BusinessException("عرض السعر غير موجود.")
            If quote.BranchId <> session.BranchId AndAlso Not session.HasPermission("reports.all_branches") Then
                Throw New BusinessException("عرض السعر يخص فرعًا آخر.")
            End If
            quote.StatusName = StatusName(quote.Status)
            quote.Lines = QuotesRepository.GetLines(db.Connection, quoteId)
            Return quote
        End Using
    End Function

    Public Shared Function Save(session As AppSession, quoteId As Long?, request As QuoteRequest) As Long
        session.RequirePermission("sales.create")
        If request Is Nothing OrElse request.Lines Is Nothing OrElse request.Lines.Count = 0 Then
            Throw New BusinessException("أضف بندًا واحدًا على الأقل لعرض السعر.")
        End If

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction

            If request.CustomerId.HasValue AndAlso request.CustomerId.Value > 0 Then
                Dim customer = CustomersRepository.GetById(db.Connection, request.CustomerId.Value, tx)
                If customer Is Nothing OrElse Not customer.IsActive Then
                    Throw New BusinessException("العميل المختار غير موجود أو غير مفعّل.")
                End If
            End If

            Dim prepared As New List(Of PreparedQuoteLine)()
            For Each line In request.Lines
                If line Is Nothing Then Throw New BusinessException("بند غير صالح.")
                If line.Qty <= 0D Then Throw New BusinessException("الكمية يجب أن تكون أكبر من صفر.")
                Dim item = ItemsRepository.GetById(db.Connection, line.ItemId, tx)
                If item Is Nothing OrElse Not item.IsActive Then
                    Throw New BusinessException("الصنف رقم " & line.ItemId.ToString() & " غير موجود أو غير مفعّل.")
                End If

                Dim unitPrice As Decimal = If(line.UnitPrice.HasValue, line.UnitPrice.Value, item.SalePrice)
                If unitPrice < 0D Then Throw New BusinessException("سعر البند لا يمكن أن يكون سالبًا.")
                If line.Discount < 0D Then Throw New BusinessException("خصم البند لا يمكن أن يكون سالبًا.")
                If line.Discount > Money.Round2(line.Qty * unitPrice) Then
                    Throw New BusinessException("خصم البند أكبر من قيمة البند للصنف «" & item.NameAr & "».")
                End If

                Dim taxCategoryId As Long = If(line.TaxCategoryId.HasValue AndAlso line.TaxCategoryId.Value > 0,
                                               line.TaxCategoryId.Value, item.TaxCategoryId)
                Dim taxCategory = TaxRepository.GetById(db.Connection, taxCategoryId, tx)
                If taxCategory Is Nothing Then Throw New BusinessException("التصنيف الضريبي للصنف غير موجود.")

                prepared.Add(New PreparedQuoteLine With {
                    .Item = item, .Request = line, .UnitPrice = unitPrice,
                    .TaxCategoryId = taxCategoryId, .TaxRate = taxCategory.Rate
                })
            Next

            Dim calcLines As New List(Of InvoiceLineInput)()
            For Each p In prepared
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = p.Request.Qty, .UnitPrice = p.UnitPrice,
                    .LineDiscount = p.Request.Discount, .TaxRate = p.TaxRate
                })
            Next

            Dim totals As InvoiceTotals
            Try
                totals = InvoiceCalculator.Calculate(calcLines, request.HeaderDiscountAmount,
                                                     session.PricesIncludeTax)
            Catch ex As ArgumentException
                Throw New BusinessException(ex.Message)
            End Try

            Dim netBeforeHeader As Decimal = Money.Round2(totals.Subtotal - totals.LineDiscount)
            If totals.HeaderDiscount > netBeforeHeader Then
                Throw New BusinessException("خصم الفاتورة أكبر من قيمة البنود.")
            End If

            Dim priceMode As String = If(session.PricesIncludeTax, "inclusive", "exclusive")
            Dim notes As String = TrimNote(request.Notes, 500)
            Dim savedId As Long

            If quoteId.HasValue AndAlso quoteId.Value > 0 Then
                Dim existing = QuotesRepository.GetById(db.Connection, quoteId.Value, tx)
                If existing Is Nothing Then Throw New BusinessException("عرض السعر غير موجود.")
                If existing.BranchId <> session.BranchId Then Throw New BusinessException("عرض السعر يخص فرعًا آخر.")
                If existing.Status <> "draft" Then
                    Throw New BusinessException("لا يمكن تعديل عرض سعر بعد إرساله أو تحويله.")
                End If

                QuotesRepository.Update(db.Connection, tx, quoteId.Value, request.CustomerId,
                                        request.ValidUntil, totals.Subtotal, totals.LineDiscount,
                                        totals.HeaderDiscount, totals.TaxBaseTotal, totals.TaxTotal,
                                        totals.Total, priceMode, notes)
                QuotesRepository.DeleteLines(db.Connection, tx, quoteId.Value)
                savedId = quoteId.Value
            Else
                Dim quoteNo As String = PostingHelper.NextDocumentNo(db.Connection, tx, session.BranchId,
                                                                     "quote", "QT", Date.Today)
                savedId = QuotesRepository.Insert(
                    db.Connection, tx, Uuid7.NewId(), quoteNo, session.BranchId, request.CustomerId,
                    request.ValidUntil, totals.Subtotal, totals.LineDiscount, totals.HeaderDiscount,
                    totals.TaxBaseTotal, totals.TaxTotal, totals.Total, priceMode, notes, session.UserId)
            End If

            For i As Integer = 0 To prepared.Count - 1
                Dim p = prepared(i)
                Dim r = totals.Lines(i)
                QuotesRepository.InsertLine(db.Connection, tx, savedId, p.Item.Id, p.Request.Qty,
                                            p.UnitPrice, p.Request.Discount, p.TaxCategoryId, p.TaxRate,
                                            r.TaxBase, r.TaxAmount, r.LineTotal,
                                            TrimNote(p.Request.Notes, 255))
            Next

            AuditRepository.Log(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.UserId,
                                session.Username, If(quoteId.HasValue, "quote_update", "quote_create"),
                                "quote", savedId.ToString(), Nothing,
                                "total=" & totals.Total.ToString("0.00"), session.DeviceId)
            db.Commit()
            Return savedId
        End Using
    End Function

    Public Shared Sub SetStatus(session As AppSession, quoteId As Long, newStatus As String)
        session.RequirePermission("sales.create")
        If String.IsNullOrWhiteSpace(newStatus) Then Throw New BusinessException("حالة عرض السعر مطلوبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim quote = QuotesRepository.GetById(db.Connection, quoteId, tx)
            If quote Is Nothing Then Throw New BusinessException("عرض السعر غير موجود.")
            If quote.BranchId <> session.BranchId Then Throw New BusinessException("عرض السعر يخص فرعًا آخر.")
            If Not IsAllowedTransition(quote.Status, newStatus) Then
                Throw New BusinessException("لا يمكن نقل عرض السعر من حالة «" & StatusName(quote.Status) &
                                            "» إلى «" & StatusName(newStatus) & "».")
            End If

            QuotesRepository.SetStatus(db.Connection, tx, quoteId, newStatus)
            AuditRepository.Log(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.UserId,
                                session.Username, "quote_status", "quote", quoteId.ToString(),
                                quote.Status, newStatus, session.DeviceId)
            db.Commit()
        End Using
    End Sub

    Public Shared Sub Delete(session As AppSession, quoteId As Long)
        session.RequirePermission("sales.create")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim quote = QuotesRepository.GetById(db.Connection, quoteId, tx)
            If quote Is Nothing Then Throw New BusinessException("عرض السعر غير موجود.")
            If quote.BranchId <> session.BranchId Then Throw New BusinessException("عرض السعر يخص فرعًا آخر.")
            If quote.Status <> "draft" Then Throw New BusinessException("لا يمكن حذف إلا عرض سعر في حالة مسودة.")

            QuotesRepository.Delete(db.Connection, tx, quoteId)
            AuditRepository.Log(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.UserId,
                                session.Username, "quote_delete", "quote", quoteId.ToString(),
                                quote.Status, Nothing, session.DeviceId)
            db.Commit()
        End Using
    End Sub

    ''' <summary>Posts the quotation as a real sale invoice, then marks it converted.</summary>
    Public Shared Function ConvertToSale(session As AppSession, quoteId As Long,
                                         paymentMethodId As Long) As InvoiceResult
        Dim quote = [Get](session, quoteId)
        If quote.Status = "converted" Then Throw New BusinessException("تم تحويل عرض السعر إلى فاتورة بالفعل.")
        If quote.Status = "cancelled" Then Throw New BusinessException("عرض السعر ملغي.")
        If quote.Status = "rejected" Then Throw New BusinessException("عرض السعر مرفوض.")
        If quote.Lines Is Nothing OrElse quote.Lines.Count = 0 Then
            Throw New BusinessException("عرض السعر لا يحتوي على بنود.")
        End If

        Dim request As New InvoiceRequest With {
            .CustomerId = quote.CustomerId,
            .PaymentMethodId = paymentMethodId,
            .Paid = quote.Total,
            .HeaderDiscountAmount = quote.HeaderDiscount,
            .BuyerName = quote.CustomerName,
            .Notes = "تحويل عرض سعر " & quote.QuoteNo
        }
        If Not String.IsNullOrWhiteSpace(quote.Notes) Then request.Notes &= " — " & quote.Notes
        For Each line In quote.Lines
            request.Lines.Add(New InvoiceLineRequest With {
                .ItemId = line.ItemId, .Qty = line.Qty, .UnitPrice = line.UnitPrice,
                .LineDiscount = line.Discount, .TaxCategoryId = line.TaxCategoryId
            })
        Next

        Dim shift = ShiftService.Current(session)
        If shift IsNot Nothing Then request.ShiftId = shift.Id

        Dim result = SalesService.PostSale(session, request)

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim changed = QuotesRepository.MarkConverted(db.Connection, tx, quoteId, result.InvoiceId)
            If changed = 0 Then
                Throw New BusinessException("تم ترحيل الفاتورة " & result.InvoiceNo &
                                            " لكن عرض السعر تغيّر أثناء التحويل. راجع السجل.")
            End If
            AuditRepository.Log(db.Connection, tx, Uuid7.NewId(), session.BranchId, session.UserId,
                                session.Username, "quote_convert", "quote", quoteId.ToString(),
                                quote.Status, result.InvoiceNo, session.DeviceId)
            db.Commit()
        End Using
        Return result
    End Function

    Public Shared Function StatusName(status As String) As String
        Select Case If(status, String.Empty)
            Case "draft" : Return "مسودة"
            Case "sent" : Return "مرسل"
            Case "accepted" : Return "مقبول"
            Case "rejected" : Return "مرفوض"
            Case "converted" : Return "محوّل لفاتورة"
            Case "cancelled" : Return "ملغي"
            Case Else : Return status
        End Select
    End Function

    Private Shared Function IsAllowedTransition(fromStatus As String, toStatus As String) As Boolean
        Select Case If(fromStatus, String.Empty)
            Case "draft"
                Return toStatus = "sent" OrElse toStatus = "accepted" OrElse toStatus = "cancelled"
            Case "sent"
                Return toStatus = "accepted" OrElse toStatus = "rejected" OrElse toStatus = "cancelled"
            Case "accepted"
                Return toStatus = "cancelled"
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function TrimNote(value As String, maxLength As Integer) As String
        Dim text As String = If(value, String.Empty).Trim()
        If text.Length > maxLength Then text = text.Substring(0, maxLength)
        Return text
    End Function

    Private Class PreparedQuoteLine
        Public Property Item As ItemRecord
        Public Property Request As QuoteLineRequest
        Public Property UnitPrice As Decimal
        Public Property TaxCategoryId As Long
        Public Property TaxRate As Decimal
    End Class
End Class
