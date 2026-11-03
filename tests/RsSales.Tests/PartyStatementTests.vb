Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

<Collection("Database")>
Public Class PartyStatementTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function UtcAt(day As Date, hour As Integer) As Date
        Return New Date(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc)
    End Function

    Private Function NewCustomer(opening As Decimal) As Long
        Dim name As String = "عميل كشف-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Return CustomerService.Save(_db.Session(), Nothing, name, "customer", Nothing, Nothing, Nothing, Nothing,
                                    Nothing, 0D, opening, True)
    End Function

    Private Function NewStockedItem(day As Date) As Long
        Dim code As String = "PS-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Return _db.CreateItemWithStock(code, 114D, 50D, 40D, occurredAt:=UtcAt(day, 8))
    End Function

    Private Function PostSale(customerId As Long, itemId As Long, day As Date,
                              qty As Decimal, paidAmount As Decimal) As InvoiceResult
        Dim request As New InvoiceRequest With {
            .CustomerId = customerId,
            .BusinessDate = day,
            .OccurredAt = UtcAt(day, 10),
            .PaymentMethodId = _db.CashMethodId,
            .Paid = paidAmount,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = qty, .UnitPrice = 114D}
            }
        }
        Return SalesService.PostSale(_db.Session(), request)
    End Function

    <Fact>
    Public Sub StatementChargesTheInvoiceAndCreditsItsSettlement()
        Dim day = _db.NextTestDate()
        Dim customerId = NewCustomer(100D)
        Dim sale = PostSale(customerId, NewStockedItem(day), day, 2D, 28D)

        Dim result = CustomerFinanceService.Statement(_db.Session(), customerId, day.AddDays(-1), day.AddDays(1))

        Assert.Equal("عميل", result.PartyTypeName)
        Assert.Equal(100D, result.OpeningBalance)
        Assert.Single(result.Lines)
        Dim line = result.Lines(0)
        Assert.Equal("document", line.Kind)
        Assert.Equal(day, line.EntryDate.Date)
        Assert.Equal("فاتورة مبيعات", line.DocumentName)
        Assert.Equal(sale.InvoiceNo, line.Reference)
        Assert.Equal(228D, line.Debit)
        Assert.Equal(28D, line.Credit)
        Assert.Equal(300D, line.Balance)
        Assert.Equal(228D, result.TotalDebit)
        Assert.Equal(28D, result.TotalCredit)
        Assert.Equal(300D, result.ClosingBalance)
        Assert.Equal(CustomerService.Balance(_db.Session(), customerId), result.ClosingBalance)
    End Sub

    <Fact>
    Public Sub StatementCarriesEarlierMovementsIntoTheOpeningBalance()
        Dim day = _db.NextTestDate()
        Dim customerId = NewCustomer(100D)
        PostSale(customerId, NewStockedItem(day), day, 2D, 28D)

        Dim later = CustomerFinanceService.Statement(_db.Session(), customerId, day.AddDays(2), day.AddDays(3))

        Assert.Empty(later.Lines)
        Assert.Equal(300D, later.OpeningBalance)
        Assert.Equal(300D, later.ClosingBalance)
    End Sub

    <Fact>
    Public Sub ReturnMovesItsValueToTheCreditSideAndKeepsTheRunningBalanceTrue()
        Dim day = _db.NextTestDate()
        Dim customerId = NewCustomer(0D)
        Dim itemId = NewStockedItem(day)
        Dim sale = PostSale(customerId, itemId, day, 2D, 228D)
        Dim originalLineId = _db.ScalarLong("SELECT id FROM invoice_lines WHERE invoice_id=@id",
                                            New With {.id = sale.InvoiceId})

        Dim returned As New InvoiceRequest With {
            .CustomerId = customerId,
            .BusinessDate = day,
            .OccurredAt = UtcAt(day, 12),
            .PaymentMethodId = _db.CashMethodId,
            .Paid = 0D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = 1D, .UnitPrice = 114D,
                                             .OriginalLineId = originalLineId}
            }
        }
        SalesService.PostSaleReturn(_db.Session(), sale.InvoiceId, returned)

        Dim result = CustomerFinanceService.Statement(_db.Session(), customerId, day.AddDays(-1), day.AddDays(1))

        Dim returnLine = result.Lines.FirstOrDefault(Function(row) row.DocumentName = "مرتجع مبيعات")
        Assert.NotNull(returnLine)
        Assert.Equal(0D, returnLine.Debit)
        Assert.Equal(114D, returnLine.Credit)
        Assert.Equal(-114D, returnLine.Balance)
        Assert.Equal(-114D, result.ClosingBalance)
        Assert.Equal(CustomerService.Balance(_db.Session(), customerId), result.ClosingBalance)
    End Sub

    <Fact>
    Public Sub PeriodEndingBeforeTheStartIsRejected()
        Dim day = _db.NextTestDate()
        Dim customerId = NewCustomer(0D)
        Assert.Throws(Of BusinessException)(
            Function() CustomerFinanceService.Statement(_db.Session(), customerId, day, day.AddDays(-1)))
        Assert.Throws(Of BusinessException)(
            Function() CustomerFinanceService.Statement(_db.Session(), 0L, day, day))
    End Sub
End Class
