Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

Public Class TaxCalculatorTests
    <Fact>
    Public Sub InclusivePriceSplitsTaxOutOfTheTotal()
        Dim result = TaxCalculator.CalculateLine(qty:=2D, unitPrice:=114D, lineDiscount:=0D,
                                                 headerDiscountShare:=0D, rate:=14D,
                                                 pricesIncludeTax:=True)
        Assert.Equal(228D, result.Gross)
        Assert.Equal(228D, result.NetAfterDiscounts)
        Assert.Equal(28D, result.TaxAmount)
        Assert.Equal(200D, result.TaxBase)
        Assert.Equal(228D, result.LineTotal)
    End Sub

    <Fact>
    Public Sub ExclusivePriceAddsTaxOnTop()
        Dim result = TaxCalculator.CalculateLine(2D, 100D, 0D, 0D, 14D, False)
        Assert.Equal(200D, result.TaxBase)
        Assert.Equal(28D, result.TaxAmount)
        Assert.Equal(228D, result.LineTotal)
    End Sub

    <Fact>
    Public Sub ZeroRateHasNoTax()
        Dim result = TaxCalculator.CalculateLine(3D, 50D, 0D, 0D, 0D, True)
        Assert.Equal(150D, result.TaxBase)
        Assert.Equal(0D, result.TaxAmount)
        Assert.Equal(150D, result.LineTotal)
    End Sub

    <Fact>
    Public Sub DiscountReducesBaseBeforeTax()
        Dim result = TaxCalculator.CalculateLine(2D, 114D, 14D, 0D, 14D, True)
        Assert.Equal(214D, result.NetAfterDiscounts)
        Assert.Equal(Decimal.Round(214D * 14D / 114D, 2, MidpointRounding.AwayFromZero), result.TaxAmount)
        Assert.Equal(result.TaxBase + result.TaxAmount, result.LineTotal)
    End Sub

    <Fact>
    Public Sub InvalidRateIsRejected()
        Assert.Throws(Of ArgumentOutOfRangeException)(
            Function() TaxCalculator.CalculateLine(1D, 100D, 0D, 0D, 101D, True))
    End Sub

    <Fact>
    Public Sub QrPayloadContainsTheFiveEtaTags()
        Dim payload = TaxCalculator.BuildQrPayload("صيدلية", "100000000000003",
                                                   New DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                                                   228D, 28D)
        Dim bytes = Convert.FromBase64String(payload)
        Dim tags As New List(Of Integer)()
        Dim i As Integer = 0
        While i < bytes.Length
            Dim tag As Integer = bytes(i)
            Dim length As Integer = bytes(i + 1)
            tags.Add(tag)
            i += 2 + length
        End While
        Assert.Equal(New Integer() {1, 2, 3, 4, 5}, tags)
        Assert.Equal(bytes.Length, i)
    End Sub
End Class

Public Class DiscountAllocatorTests
    <Fact>
    Public Sub SharesAlwaysAddUpToTheTotal()
        Dim weights = New Decimal() {10D, 20D, 30D}
        Dim shares = DiscountAllocator.Allocate(10D, weights)
        Assert.Equal(10D, shares.Sum())
    End Sub

    <Fact>
    Public Sub LargestRemainderIsUsedForUnevenSplits()
        Dim shares = DiscountAllocator.Allocate(1D, New Decimal() {1D, 1D, 1D})
        Assert.Equal(1D, shares.Sum())
        Assert.Equal(1, shares.Count(Function(s) s = 0.34D))
        Assert.Equal(2, shares.Count(Function(s) s = 0.33D))
    End Sub

    <Fact>
    Public Sub ZeroDiscountGivesZeroShares()
        Dim shares = DiscountAllocator.Allocate(0D, New Decimal() {5D, 5D})
        Assert.All(shares, Sub(s) Assert.Equal(0D, s))
    End Sub

    <Fact>
    Public Sub NegativeWeightIsRejected()
        Assert.Throws(Of ArgumentException)(
            Function() DiscountAllocator.Allocate(5D, New Decimal() {1D, -1D}))
    End Sub
End Class

Public Class InvoiceCalculatorTests
    <Fact>
    Public Sub HeaderDiscountIsAllocatedBeforeTax()
        Dim lines As New List(Of InvoiceLineInput) From {
            New InvoiceLineInput With {.Qty = 1D, .UnitPrice = 114D, .TaxRate = 14D},
            New InvoiceLineInput With {.Qty = 1D, .UnitPrice = 57D, .TaxRate = 14D}
        }
        Dim totals = InvoiceCalculator.Calculate(lines, 11.4D, True)

        Assert.Equal(171D, totals.Subtotal)
        Assert.Equal(11.4D, totals.HeaderDiscount)
        Assert.Equal(159.6D, Money.Round2(totals.TaxBaseTotal + totals.TaxTotal))
        Assert.Equal(19.6D, totals.TaxTotal)
        Assert.Equal(159.6D, totals.Total)
    End Sub

    <Fact>
    Public Sub MixedRatesProduceSeparateTaxAmounts()
        Dim lines As New List(Of InvoiceLineInput) From {
            New InvoiceLineInput With {.Qty = 1D, .UnitPrice = 114D, .TaxRate = 14D},
            New InvoiceLineInput With {.Qty = 1D, .UnitPrice = 50D, .TaxRate = 0D}
        }
        Dim totals = InvoiceCalculator.Calculate(lines, 0D, True)
        Assert.Equal(164D, totals.Total)
        Assert.Equal(14D, totals.TaxTotal)
        Assert.Equal(2, totals.Lines.Count)
    End Sub

    <Fact>
    Public Sub ReturnsRejectHeaderDiscount()
        Dim lines As New List(Of InvoiceLineInput) From {
            New InvoiceLineInput With {.Qty = -1D, .UnitPrice = 114D, .TaxRate = 14D}
        }
        Assert.Throws(Of ArgumentException)(Function() InvoiceCalculator.Calculate(lines, 5D, True))
    End Sub

    <Fact>
    Public Sub EmptyInvoiceIsRejected()
        Assert.Throws(Of ArgumentException)(
            Function() InvoiceCalculator.Calculate(New List(Of InvoiceLineInput)(), 0D, True))
    End Sub
End Class

Public Class MoneyTests
    <Fact>
    Public Sub RoundingIsAwayFromZero()
        Assert.Equal(0.13D, Money.Round2(0.125D))
        Assert.Equal(2.35D, Money.Round2(2.345D))
        Assert.Equal(1.2346D, Money.Round4(1.23455D))
    End Sub
End Class

Public Class Uuid7Tests
    <Fact>
    Public Sub UuidIsVersion7AndUnique()
        Dim first = Uuid7.NewId()
        Dim second = Uuid7.NewId()
        Assert.NotEqual(first, second)
        Assert.Equal("7", first.Substring(14, 1))
        Assert.Equal(36, first.Length)
    End Sub

    <Fact>
    Public Sub UuidsSortByCreationTime()
        Dim ids = Enumerable.Range(0, 50).Select(Function(i)
                                                     Threading.Thread.Sleep(2)
                                                     Return Uuid7.NewId()
                                                 End Function).ToList()
        Dim sorted = ids.OrderBy(Function(s) s, StringComparer.Ordinal).ToList()
        Assert.Equal(ids, sorted)
    End Sub
End Class
