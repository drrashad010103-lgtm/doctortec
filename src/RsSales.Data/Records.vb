Public Class UserRecord
    Public Property Id As Long
    Public Property Username As String
    Public Property PasswordHash As String
    Public Property FullName As String
    Public Property IsActive As Boolean
    Public Property MustChangePassword As Boolean
    Public Property FailedAttempts As Integer
    Public Property LockedUntil As Date?
End Class

Public Class TaxCategoryRecord
    Public Property Id As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property NameEn As String
    Public Property Rate As Decimal
    Public Property IsExempt As Boolean
    Public Property IsActive As Boolean
    Public Property EffectiveFrom As Date
    Public Property EffectiveTo As Date?
End Class

Public Class ItemRecord
    Public Property Id As Long
    Public Property Uuid As String
    Public Property Code As String
    Public Property NameAr As String
    Public Property NameEn As String
    Public Property GroupId As Long?
    Public Property BaseUnitId As Long?
    Public Property CostPrice As Decimal
    Public Property SalePrice As Decimal
    Public Property WholesalePrice As Decimal
    Public Property MinPrice As Decimal
    Public Property ReorderLevel As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
    Public Property IsActive As Boolean
End Class

Public Class CustomerRecord
    Public Property Id As Long
    Public Property Uuid As String
    Public Property Code As String
    Public Property Name As String
    Public Property Type As String
    Public Property GroupId As Long?
    Public Property Mobile As String
    Public Property Phone As String
    Public Property Email As String
    Public Property Address As String
    Public Property TaxNo As String
    Public Property CreditLimit As Decimal
    Public Property OpeningBalance As Decimal
    Public Property IsActive As Boolean
End Class

Public Class InvoiceHeaderRecord
    Public Property Id As Long
    Public Property EventId As String
    Public Property BranchId As Long
    Public Property InvoiceNo As String
    Public Property InvoiceType As String
    Public Property Status As String
    Public Property CustomerId As Long?
    Public Property Subtotal As Decimal
    Public Property LineDiscount As Decimal
    Public Property HeaderDiscount As Decimal
    Public Property TaxBaseTotal As Decimal
    Public Property TaxTotal As Decimal
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
    Public Property PriceMode As String
    Public Property QrPayload As String
    Public Property BusinessDate As Date
    Public Property OccurredAt As Date
End Class

Public Class InvoiceLineRecord
    Public Property Id As Long
    Public Property InvoiceId As Long
    Public Property ItemId As Long
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal
    Public Property Discount As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
    Public Property TaxBase As Decimal
    Public Property TaxAmount As Decimal
    Public Property LineTotal As Decimal
    Public Property CostAtSale As Decimal
    Public Property OriginalLineId As Long?
End Class

Public Class OutboxRecord
    Public Property EventId As String
    Public Property BranchId As Long
    Public Property EntityType As String
    Public Property EntityId As String
    Public Property EventType As String
    Public Property Payload As String
    Public Property Attempts As Integer
End Class

Public Class ChangeLogRecord
    Public Property Seq As Long
    Public Property EntityType As String
    Public Property EntityId As String
    Public Property Op As String
    Public Property Payload As String
    Public Property BranchId As Long?
End Class

Public Class DeviceAuthRecord
    Public Property Id As Long
    Public Property BranchId As Long
    Public Property Name As String
    Public Property DeviceKeyHash As String
    Public Property IsActive As Boolean
End Class

Public Class SyncStats
    Public Property Pending As Long
    Public Property Failed As Long
    Public Property Dead As Long
    Public Property AckedToday As Long
    Public Property LastAckAt As Date?
End Class
