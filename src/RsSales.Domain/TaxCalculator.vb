''' <summary>One invoice line after discounts and tax.</summary>
Public Class TaxLineResult
    Public Property Gross As Decimal
    Public Property LineDiscount As Decimal
    Public Property HeaderDiscountShare As Decimal
    Public Property NetAfterDiscounts As Decimal
    Public Property TaxBase As Decimal
    Public Property TaxAmount As Decimal
    Public Property LineTotal As Decimal
End Class

''' <summary>
''' Tax arithmetic. Pipeline: gross -> line discount -> header discount share
''' -> net -> tax base/tax -> line total. All values Decimal, 2 decimals.
''' </summary>
Public NotInheritable Class TaxCalculator
    Private Sub New()
    End Sub

    Public Shared Function CalculateLine(qty As Decimal,
                                         unitPrice As Decimal,
                                         lineDiscount As Decimal,
                                         headerDiscountShare As Decimal,
                                         rate As Decimal,
                                         pricesIncludeTax As Boolean) As TaxLineResult
        If rate < 0D OrElse rate > 100D Then
            Throw New ArgumentOutOfRangeException(NameOf(rate), "Tax rate must be between 0 and 100.")
        End If

        Dim gross As Decimal = qty * unitPrice
        Dim net As Decimal = gross - lineDiscount - headerDiscountShare
        If net < 0D Then net = 0D
        net = Money.Round2(net)

        Dim taxAmount As Decimal
        Dim taxBase As Decimal

        If pricesIncludeTax Then
            If rate = 0D Then
                taxAmount = 0D
                taxBase = net
            Else
                taxAmount = Money.Round2(net * rate / (100D + rate))
                taxBase = net - taxAmount
            End If
        Else
            taxBase = net
            taxAmount = Money.Round2(taxBase * rate / 100D)
        End If

        Dim lineTotal As Decimal = taxBase + taxAmount

        Return New TaxLineResult With {
            .Gross = Money.Round2(gross),
            .LineDiscount = Money.Round2(lineDiscount),
            .HeaderDiscountShare = Money.Round2(headerDiscountShare),
            .NetAfterDiscounts = net,
            .TaxBase = taxBase,
            .TaxAmount = taxAmount,
            .LineTotal = lineTotal
        }
    End Function

    ''' <summary>Builds the ETA-style TLV QR payload and returns Base64.</summary>
    Public Shared Function BuildQrPayload(sellerName As String,
                                          sellerTaxNo As String,
                                          timestamp As DateTimeOffset,
                                          totalWithTax As Decimal,
                                          taxTotal As Decimal) As String
        Using ms As New IO.MemoryStream()
            WriteTlv(ms, 1, sellerName)
            WriteTlv(ms, 2, sellerTaxNo)
            WriteTlv(ms, 3, timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
            WriteTlv(ms, 4, totalWithTax.ToString("0.00", Globalization.CultureInfo.InvariantCulture))
            WriteTlv(ms, 5, taxTotal.ToString("0.00", Globalization.CultureInfo.InvariantCulture))
            Return Convert.ToBase64String(ms.ToArray())
        End Using
    End Function

    Private Shared Sub WriteTlv(stream As IO.Stream, tag As Byte, value As String)
        Dim bytes As Byte() = Text.Encoding.UTF8.GetBytes(If(value, String.Empty))
        If bytes.Length > 255 Then
            Dim trimmed(254) As Byte
            Array.Copy(bytes, trimmed, 255)
            bytes = trimmed
        End If
        stream.WriteByte(tag)
        stream.WriteByte(CByte(bytes.Length))
        stream.Write(bytes, 0, bytes.Length)
    End Sub
End Class
