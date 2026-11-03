Imports System

Public Class HrDepartmentSummary
    Public Property Id As Long
    Public Property Name As String
    Public Property Notes As String
    Public Property IsActive As Boolean
    Public Property SectionCount As Integer
    Public Property EmployeeCount As Integer

    Public ReadOnly Property StatusName As String
        Get
            Return If(IsActive, "مفعّل", "موقوف")
        End Get
    End Property
End Class

Public Class HrSectionSummary
    Public Property Id As Long
    Public Property DepartmentId As Long
    Public Property DepartmentName As String
    Public Property Name As String
    Public Property Notes As String
    Public Property IsActive As Boolean
    Public Property EmployeeCount As Integer

    Public ReadOnly Property StatusName As String
        Get
            Return If(IsActive, "مفعّل", "موقوف")
        End Get
    End Property
End Class

Public Class HrEmployeeSummary
    Public Property Id As Long
    Public Property Uuid As String
    Public Property Code As String
    Public Property Name As String
    Public Property EmployeeType As String
    Public Property DepartmentId As Long?
    Public Property DepartmentName As String
    Public Property SectionId As Long?
    Public Property SectionName As String
    Public Property Mobile As String
    Public Property Phone As String
    Public Property Email As String
    Public Property Address As String
    Public Property HireDate As Date?
    Public Property BaseSalary As Decimal
    Public Property CommissionPercent As Decimal
    Public Property DeliveryFee As Decimal
    Public Property Notes As String
    Public Property IsActive As Boolean

    Public ReadOnly Property TypeName As String
        Get
            Select Case EmployeeType
                Case "seller" : Return "بائع"
                Case "delivery" : Return "دليفري"
                Case Else : Return "موظف"
            End Select
        End Get
    End Property

    Public ReadOnly Property StatusName As String
        Get
            Return If(IsActive, "مفعّل", "موقوف")
        End Get
    End Property
End Class

Public Class HrPayrollRunSummary
    Public Property Id As Long
    Public Property Uuid As String
    Public Property PeriodYear As Integer
    Public Property PeriodMonth As Integer
    Public Property Status As String
    Public Property EmployeeCount As Integer
    Public Property TotalBase As Decimal
    Public Property TotalCommission As Decimal
    Public Property TotalDeductions As Decimal
    Public Property TotalBonus As Decimal
    Public Property TotalNet As Decimal
    Public Property Notes As String
    Public Property CreatedAt As Date
    Public Property PaidAt As Date?
    Public Property CreatedByName As String

    Public ReadOnly Property StatusName As String
        Get
            Select Case Status
                Case "paid" : Return "مصروف"
                Case "cancelled" : Return "ملغي"
                Case Else : Return "مسودة"
            End Select
        End Get
    End Property

    Public ReadOnly Property PeriodText As String
        Get
            Return HrArabicText.MonthName(PeriodMonth) & " " & PeriodYear.ToString()
        End Get
    End Property
End Class

Public Class HrPayrollLineSummary
    Public Property Id As Long
    Public Property RunId As Long
    Public Property EmployeeId As Long
    Public Property EmployeeCode As String
    Public Property EmployeeName As String
    Public Property EmployeeType As String
    Public Property DepartmentName As String
    Public Property BaseSalary As Decimal
    Public Property CommissionAmount As Decimal
    Public Property Deductions As Decimal
    Public Property Bonus As Decimal
    Public Property NetAmount As Decimal
    Public Property Notes As String

    Public ReadOnly Property TypeName As String
        Get
            Select Case EmployeeType
                Case "seller" : Return "بائع"
                Case "delivery" : Return "دليفري"
                Case Else : Return "موظف"
            End Select
        End Get
    End Property
End Class

Public Class HrCommissionSummary
    Public Property EmployeeId As Long
    Public Property Code As String
    Public Property EmployeeName As String
    Public Property EmployeeType As String
    Public Property DepartmentName As String
    Public Property Mobile As String
    Public Property InvoiceCount As Integer
    Public Property SalesTotal As Decimal
    Public Property CommissionPercent As Decimal
    Public Property DeliveryFee As Decimal
    Public Property CommissionAmount As Decimal

    Public ReadOnly Property RateText As String
        Get
            If EmployeeType = "seller" Then Return CommissionPercent.ToString("0.##") & "%"
            Return DeliveryFee.ToString("N2")
        End Get
    End Property
End Class

Public NotInheritable Class HrArabicText
    Private Sub New()
    End Sub

    Private Shared ReadOnly Months As String() = {
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    }

    Public Shared Function MonthName(month As Integer) As String
        If month < 1 OrElse month > 12 Then Return month.ToString()
        Return Months(month - 1)
    End Function
End Class
