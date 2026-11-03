Imports System.Collections.Generic

Public Class QuoteLineRequest
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal?
    Public Property Discount As Decimal
    Public Property TaxCategoryId As Long?
    Public Property Notes As String
End Class

Public Class QuoteRequest
    Public Property CustomerId As Long?
    Public Property ValidUntil As Date?
    Public Property HeaderDiscountAmount As Decimal
    Public Property Notes As String
    Public Property Lines As New List(Of QuoteLineRequest)()
End Class

Public Class QuoteSummary
    Public Property Id As Long
    Public Property QuoteNo As String
    Public Property CustomerName As String
    Public Property Status As String
    Public Property StatusName As String
    Public Property ValidUntil As Date?
    Public Property CreatedAt As Date
    Public Property Total As Decimal
    Public Property ConvertedInvoiceId As Long?
    Public Property ConvertedInvoiceNo As String
    Public Property CreatedByName As String
End Class

Public Class QuoteLineDetail
    Public Property Id As Long
    Public Property ItemId As Long
    Public Property ItemCode As String
    Public Property ItemName As String
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal
    Public Property Discount As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
    Public Property TaxBase As Decimal
    Public Property TaxAmount As Decimal
    Public Property LineTotal As Decimal
    Public Property Notes As String
End Class

Public Class QuoteDetail
    Public Property Id As Long
    Public Property QuoteNo As String
    Public Property BranchId As Long
    Public Property CustomerId As Long?
    Public Property CustomerName As String
    Public Property Status As String
    Public Property StatusName As String
    Public Property ValidUntil As Date?
    Public Property Subtotal As Decimal
    Public Property LineDiscount As Decimal
    Public Property HeaderDiscount As Decimal
    Public Property TaxBaseTotal As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Notes As String
    Public Property CreatedAt As Date
    Public Property CreatedByName As String
    Public Property ConvertedInvoiceId As Long?
    Public Property ConvertedInvoiceNo As String
    Public Property Lines As New List(Of QuoteLineDetail)()
End Class
