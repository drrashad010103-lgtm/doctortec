Imports System.Collections.Generic
Imports System.Linq

''' <summary>Input to invoice-level calculation (one line).</summary>
Public Class InvoiceLineInput
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal
    Public Property LineDiscount As Decimal
    Public Property TaxRate As Decimal
End Class

''' <summary>Invoice-level totals produced by <see cref="InvoiceCalculator"/>.</summary>
Public Class InvoiceTotals
    Public Property Subtotal As Decimal
    Public Property LineDiscount As Decimal
    Public Property HeaderDiscount As Decimal
    Public Property TaxBaseTotal As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Lines As New List(Of TaxLineResult)()
End Class

Public NotInheritable Class InvoiceCalculator
    Private Sub New()
    End Sub

    Public Shared Function Calculate(lines As IReadOnlyList(Of InvoiceLineInput),
                                     headerDiscountAmount As Decimal,
                                     pricesIncludeTax As Boolean) As InvoiceTotals
        If lines Is Nothing Then Throw New ArgumentNullException(NameOf(lines))
        If lines.Count = 0 Then Throw New ArgumentException("Invoice must contain at least one line.", NameOf(lines))

        headerDiscountAmount = Money.Round2(headerDiscountAmount)
        If headerDiscountAmount < 0D Then Throw New ArgumentException("Header discount cannot be negative.", NameOf(headerDiscountAmount))

        Dim hasNegative As Boolean = lines.Any(Function(l) l.Qty < 0D)
        If hasNegative AndAlso headerDiscountAmount <> 0D Then
            Throw New ArgumentException("Header discount is not allowed on return invoices.", NameOf(headerDiscountAmount))
        End If

        Dim weights(lines.Count - 1) As Decimal
        For i As Integer = 0 To lines.Count - 1
            Dim net As Decimal = lines(i).Qty * lines(i).UnitPrice - lines(i).LineDiscount
            If net < 0D Then net = 0D
            weights(i) = net
        Next

        Dim shares As Decimal() = DiscountAllocator.Allocate(headerDiscountAmount, weights)
        Dim totals As New InvoiceTotals With {.HeaderDiscount = headerDiscountAmount}

        For i As Integer = 0 To lines.Count - 1
            Dim r As TaxLineResult = TaxCalculator.CalculateLine(
                lines(i).Qty, lines(i).UnitPrice, lines(i).LineDiscount, shares(i),
                lines(i).TaxRate, pricesIncludeTax)
            totals.Lines.Add(r)
            totals.Subtotal += r.Gross
            totals.LineDiscount += r.LineDiscount
            totals.TaxBaseTotal += r.TaxBase
            totals.TaxTotal += r.TaxAmount
            totals.Total += r.LineTotal
        Next

        totals.Subtotal = Money.Round2(totals.Subtotal)
        totals.LineDiscount = Money.Round2(totals.LineDiscount)
        totals.TaxBaseTotal = Money.Round2(totals.TaxBaseTotal)
        totals.TaxTotal = Money.Round2(totals.TaxTotal)
        totals.Total = Money.Round2(totals.Total)
        Return totals
    End Function
End Class
