Imports System.Collections.Generic

Public Class InvoiceLineRequest
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal?
    Public Property LineDiscount As Decimal
    Public Property TaxCategoryId As Long?
    Public Property BatchNo As String
    Public Property ExpiryDate As Date?
    Public Property OriginalLineId As Long?
End Class

Public Class InvoiceRequest
    Public Property ClientEventId As String
    Public Property CustomerId As Long?
    Public Property SellerId As Long?
    Public Property DeliveryId As Long?
    Public Property ShiftId As Long?
    Public Property HeaderDiscountAmount As Decimal
    Public Property Paid As Decimal
    Public Property PaymentMethodId As Long
    Public Property BuyerTaxNo As String
    Public Property BuyerName As String
    Public Property BuyerAddress As String
    Public Property Notes As String
    Public Property BusinessDate As Date?
    Public Property OccurredAt As Date?
    Public Property CouponCode As String
    Public Property RedeemLoyaltyPoints As Decimal
    Public Property VoucherCode As String
    Public Property VoucherAmount As Decimal
    Public Property Lines As New List(Of InvoiceLineRequest)()
End Class

Public Class InvoiceLineResult
    Public Property LineId As Long
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal
    Public Property Discount As Decimal
    Public Property TaxRate As Decimal
    Public Property TaxBase As Decimal
    Public Property TaxAmount As Decimal
    Public Property LineTotal As Decimal
    Public Property CostAtSale As Decimal
End Class

Public Class InvoiceResult
    Public Property InvoiceId As Long
    Public Property InvoiceNo As String
    Public Property EventId As String
    Public Property Subtotal As Decimal
    Public Property LineDiscount As Decimal
    Public Property HeaderDiscount As Decimal
    Public Property TaxBaseTotal As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
    Public Property QrPayload As String
    Public Property CouponDiscount As Decimal
    Public Property LoyaltyRedeemedPoints As Decimal
    Public Property LoyaltyEarnedPoints As Decimal
    Public Property VoucherPaidAmount As Decimal
    Public Property Lines As New List(Of InvoiceLineResult)()
End Class

Public Class ItemSummary
    Public Property Id As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property NameEn As String
    Public Property SalePrice As Decimal
    Public Property CostPrice As Decimal
    Public Property WholesalePrice As Decimal
    Public Property MinPrice As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
    Public Property TaxCode As String
    Public Property Barcode As String
    Public Property QtyOnHand As Decimal
    Public Property ReorderLevel As Decimal
    Public Property IsActive As Boolean

    ''' <summary>نص القائمة المنسدلة: الاسم أولاً ثم الكود فقط — الأسعار والكميات تُترك لبطاقة الصنف
    ''' لأن الأرقام داخل قائمة يمين-لليسار تتشكّل بأرقام عربية وتزيد طول السطر فيُقطع الاسم.</summary>
    Public ReadOnly Property DropdownDisplay As String
        Get
            Dim label As String = If(NameAr, String.Empty).Trim()
            If label.Length = 0 Then label = If(NameEn, String.Empty).Trim()
            If label.Length = 0 Then label = "صنف " & Id.ToString()
            If String.IsNullOrWhiteSpace(Code) Then Return label
            Return label & "  ·  " & Code
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return DropdownDisplay
    End Function
End Class

Public Class StockSummary
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property QtyOnHand As Decimal
    Public Property LastCost As Decimal
    Public Property SalePrice As Decimal
    Public Property ReorderLevel As Decimal
End Class

Public Class CustomerSummary
    Public Property Id As Long
    Public Property Uuid As String
    Public Property Code As String
    Public Property Name As String
    Public Property Type As String
    Public Property Mobile As String
    Public Property Phone As String
    Public Property TaxNo As String
    Public Property CreditLimit As Decimal
    Public Property Balance As Decimal
    Public Property GroupId As Long?
    Public Property GroupName As String
    Public Property GroupDiscountPercent As Decimal

    Public ReadOnly Property DisplayText As String
        Get
            If Id <= 0 Then Return Name
            Dim codePart As String = If(String.IsNullOrWhiteSpace(Code), "#" & Id.ToString(), Code)
            Dim phonePart As String = If(Not String.IsNullOrWhiteSpace(Mobile), Mobile, Phone)
            If Not String.IsNullOrWhiteSpace(phonePart) Then
                Return "[" & codePart & "] " & Name & " — " & phonePart
            End If
            Return "[" & codePart & "] " & Name
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return DisplayText
    End Function
End Class

Public Class TaxCategorySummary
    Public Property Id As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property Rate As Decimal
    Public Property IsExempt As Boolean
    Public Property IsActive As Boolean
    Public Property EffectiveFrom As Date
End Class


Public Class StockForecastRow
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property QtyOnHand As Decimal
    Public Property ReorderLevel As Decimal
    Public Property SoldLast30Days As Decimal
    Public Property DailyBurnRate As Decimal
    Public Property DaysOfSupply As Decimal
    Public Property SuggestedOrderQty As Decimal
    Public Property LastCost As Decimal
    Public Property EstimatedOrderCost As Decimal
    Public Property StatusText As String
End Class

Public Class HeldInvoiceRecord
    Public Property Id As Long
    Public Property SlotTitle As String
    Public Property BranchId As Long
    Public Property UserId As Long
    Public Property CustomerId As Long?
    Public Property HeaderDiscount As Decimal
    Public Property CouponCode As String
    Public Property VoucherCode As String
    Public Property RedeemPoints As Decimal
    Public Property Notes As String
    Public Property LinesJson As String
    Public Property CreatedAt As Date

    Public Overrides Function ToString() As String
        Return SlotTitle
    End Function
End Class
