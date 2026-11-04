Imports System
Imports System.Linq

Public Class CustomerGroupSummary
    Public Property Id As Long
    Public Property Name As String
    Public Property DiscountPercent As Decimal
    Public Property Notes As String
    Public Property IsActive As Boolean
    Public Property CustomerCount As Integer
    Public Property TotalBalance As Decimal

    Public ReadOnly Property StatusName As String
        Get
            Return If(IsActive, "مفعّل", "موقوف")
        End Get
    End Property
End Class

Public Class LoyaltyAccountSummary
    Public Property CustomerId As Long
    Public Property CustomerName As String
    Public Property Mobile As String
    Public Property Points As Decimal
    Public Property TotalEarned As Decimal
    Public Property TotalRedeemed As Decimal
    Public Property UpdatedAt As Date?
End Class

Public Class LoyaltyTransactionSummary
    Public Property Id As Long
    Public Property CustomerId As Long
    Public Property InvoiceNo As String
    Public Property PointsDelta As Decimal
    Public Property Type As String
    Public Property Note As String
    Public Property CreatedAt As Date
    Public Property CreatedByName As String

    Public ReadOnly Property TypeName As String
        Get
            Select Case Type
                Case "earn" : Return "إضافة"
                Case "redeem" : Return "استبدال"
                Case "adjust" : Return "تسوية"
                Case "expire" : Return "انتهاء"
                Case Else : Return Type
            End Select
        End Get
    End Property
End Class

Public Class CustomerBalanceSummary
    Public Property Id As Long
    Public Property Name As String
    Public Property Mobile As String
    Public Property OpeningBalance As Decimal
    Public Property InvoicedTotal As Decimal
    Public Property PaidTotal As Decimal
    Public Property Due As Decimal
    Public Property CreditLimit As Decimal
    Public Property Balance As Decimal

    Public ReadOnly Property StatusName As String
        Get
            If Balance > 0D AndAlso CreditLimit > 0D AndAlso Balance > CreditLimit Then Return "تجاوز الحد"
            If Balance > 0D Then Return "مدين"
            If Balance < 0D Then Return "دائن"
            Return "مسدد"
        End Get
    End Property
End Class

Public Class CustomerPaymentSummary
    Public Property Id As Long
    Public Property PaidAt As Date
    Public Property InvoiceNo As String
    Public Property InvoiceType As String
    Public Property CustomerName As String
    Public Property MethodName As String
    Public Property Amount As Decimal
    Public Property Reference As String
    Public Property UserName As String

    Public ReadOnly Property InvoiceTypeName As String
        Get
            Select Case InvoiceType
                Case "sale" : Return "مبيعات"
                Case "sale_return" : Return "مرتجع مبيعات"
                Case "purchase" : Return "مشتريات"
                Case "purchase_return" : Return "مرتجع مشتريات"
                Case Else : Return InvoiceType
            End Select
        End Get
    End Property
End Class

''' <summary>A posted invoice that still has an amount due on it.</summary>
Public Class CustomerDueInvoice
    Public Property InvoiceId As Long
    Public Property InvoiceNo As String
    Public Property BusinessDate As Date
    Public Property Total As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
End Class

''' <summary>What one تحصيل did: how much it settled, on how many invoices, and what is left.</summary>
Public Class CustomerCollectionResult
    Public Property CustomerId As Long
    Public Property CustomerName As String
    Public Property Amount As Decimal
    Public Property MethodName As String
    Public Property SettledInvoiceCount As Integer
    Public Property RemainingDue As Decimal
    Public Property BalanceAfter As Decimal
    Public Property PaidAt As Date
End Class

Public Class CouponSummary
    Public Property Id As Long
    Public Property Code As String
    Public Property Name As String
    Public Property DiscountType As String
    Public Property DiscountValue As Decimal
    Public Property StartsAt As Date?
    Public Property EndsAt As Date?
    Public Property MaxUses As Integer
    Public Property UsedCount As Integer
    Public Property IsActive As Boolean

    Public ReadOnly Property TypeName As String
        Get
            Return If(DiscountType = "percent", "نسبة %", "مبلغ ثابت")
        End Get
    End Property

    Public ReadOnly Property RemainingUses As Integer
        Get
            If MaxUses <= 0 Then Return -1
            Return Math.Max(0, MaxUses - UsedCount)
        End Get
    End Property

    Public ReadOnly Property RemainingUsesText As String
        Get
            If MaxUses <= 0 Then Return "غير محدود"
            Return Math.Max(0, MaxUses - UsedCount).ToString()
        End Get
    End Property

    Public ReadOnly Property StatusName As String
        Get
            If Not IsActive Then Return "موقوف"
            If EndsAt.HasValue AndAlso EndsAt.Value.Date < Date.Today Then Return "منتهي"
            If StartsAt.HasValue AndAlso StartsAt.Value.Date > Date.Today Then Return "لم يبدأ"
            If MaxUses > 0 AndAlso UsedCount >= MaxUses Then Return "مستنفد"
            Return "ساري"
        End Get
    End Property
End Class

Public Class CouponRedemptionSummary
    Public Property Id As Long
    Public Property InvoiceNo As String
    Public Property CustomerName As String
    Public Property Amount As Decimal
    Public Property RedeemedAt As Date
End Class

Public Class PurchaseVoucherSummary
    Public Property Id As Long
    Public Property Code As String
    Public Property CustomerName As String
    Public Property Amount As Decimal
    Public Property RemainingAmount As Decimal
    Public Property IssuedAt As Date
    Public Property ExpiresAt As Date?
    Public Property IsActive As Boolean
    Public Property Notes As String

    Public ReadOnly Property StatusName As String
        Get
            If Not IsActive Then Return "موقوفة"
            If ExpiresAt.HasValue AndAlso ExpiresAt.Value.Date < Date.Today Then Return "منتهية"
            If RemainingAmount <= 0D Then Return "مستنفدة"
            Return "سارية"
        End Get
    End Property

    Public ReadOnly Property UsedAmount As Decimal
        Get
            Return Amount - RemainingAmount
        End Get
    End Property
End Class

Public Class PurchaseVoucherRedemptionSummary
    Public Property Id As Long
    Public Property InvoiceNo As String
    Public Property Amount As Decimal
    Public Property Note As String
    Public Property RedeemedAt As Date
End Class

''' <summary>One ledger row of a party account statement.</summary>
Public Class PartyStatementLine
    Public Property EntryDate As Date
    ''' <summary>"document" for a movement, "balance" for the amount carried into the period.</summary>
    Public Property Kind As String
    Public Property DocumentName As String
    Public Property Reference As String
    Public Property Description As String
    Public Property Debit As Decimal
    Public Property Credit As Decimal
    Public Property Balance As Decimal
End Class

''' <summary>
''' A party's balance carried into a period and every posted document inside it. The lines'
''' debit/credit pair sums to each document's due, so opening balance plus the line totals
''' reproduces the figure the balances screen shows for the same party.
''' </summary>
Public Class PartyStatement
    Public Property PartyId As Long
    Public Property PartyName As String
    Public Property PartyTypeName As String
    Public Property FromDate As Date
    Public Property ToDate As Date
    Public Property OpeningBalance As Decimal
    Public Property Lines As New List(Of PartyStatementLine)()

    Public ReadOnly Property TotalDebit As Decimal
        Get
            Return Lines.Sum(Function(line) line.Debit)
        End Get
    End Property

    Public ReadOnly Property TotalCredit As Decimal
        Get
            Return Lines.Sum(Function(line) line.Credit)
        End Get
    End Property

    Public ReadOnly Property ClosingBalance As Decimal
        Get
            Return OpeningBalance + TotalDebit - TotalCredit
        End Get
    End Property
End Class
