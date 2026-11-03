Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class HrCommissionsForm
    Inherits SectionFormBase

    Private ReadOnly _employeeType As String
    Private ReadOnly dtpFrom As New DateTimePicker()
    Private ReadOnly dtpTo As New DateTimePicker()
    Private _rows As List(Of HrCommissionSummary)

    Public Sub New(session As AppSession, employeeType As String)
        MyBase.New(session, TitleFor(employeeType), SubtitleFor(employeeType),
                   Color.FromArgb(39, 174, 96), New Size(1080, 640))
        _employeeType = employeeType

        AddSearch("الاسم أو الكود أو الموبايل", AddressOf Reload)

        dtpFrom.Format = DateTimePickerFormat.Short
        dtpFrom.Width = 120
        dtpFrom.Margin = New Padding(4)
        Dim firstDay As New Date(Date.Today.Year, Date.Today.Month, 1)
        dtpFrom.Value = firstDay

        dtpTo.Format = DateTimePickerFormat.Short
        dtpTo.Width = 120
        dtpTo.Margin = New Padding(4)
        dtpTo.Value = Date.Today

        ToolBar.Controls.Add(UiTheme.MakeLabel("من:"))
        ToolBar.Controls.Add(dtpFrom)
        ToolBar.Controls.Add(UiTheme.MakeLabel("إلى:"))
        ToolBar.Controls.Add(dtpTo)

        Dim btnShow As Button = UiTheme.PrimaryButton("عرض", 90)
        AddHandler btnShow.Click, Sub(s, e) Reload()
        ToolBar.Controls.Add(btnShow)

        Dim btnExport As Button = UiTheme.MakeButton("تصدير CSV", 110)
        AddHandler btnExport.Click, Sub(s, e) ExportCsv(If(_employeeType = "seller",
                                                          "seller-commissions.csv", "delivery-commissions.csv"))
        ToolBar.Controls.Add(btnExport)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Code", "الكود", 100)
        UiTheme.AddTextColumn(Grid, "EmployeeName", "الاسم", 220, True)
        UiTheme.AddTextColumn(Grid, "DepartmentName", "الإدارة", 150)
        UiTheme.AddTextColumn(Grid, "Mobile", "الموبايل", 120)
        UiTheme.AddTextColumn(Grid, "InvoiceCount", "عدد الفواتير", 105, False, "N0")
        UiTheme.AddTextColumn(Grid, "SalesTotal", "إجمالي المبيعات", 140, False, "N2")
        UiTheme.AddTextColumn(Grid, "RateText", If(employeeType = "seller", "النسبة", "قيمة التوصيل"), 115)
        UiTheme.AddTextColumn(Grid, "CommissionAmount", "العمولة المستحقة", 140, False, "N2")

        Reload()
    End Sub

    Private Shared Function TitleFor(employeeType As String) As String
        If employeeType = "delivery" Then Return "تقرير عمولة دليفري"
        Return "تقرير عمولة بائعين"
    End Function

    Private Shared Function SubtitleFor(employeeType As String) As String
        If employeeType = "delivery" Then
            Return "عمولة كل مندوب توصيل محسوبة من فواتير البيع المرحّلة خلال الفترة"
        End If
        Return "عمولة كل بائع محسوبة كنسبة من فواتير البيع المرحّلة خلال الفترة"
    End Function

    Protected Overrides Sub LoadData()
        _rows = HrCommissionService.ForRange(Session, _employeeType, dtpFrom.Value.Date, dtpTo.Value.Date,
                                             SearchText)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        Dim invoiceTotal As Integer = 0
        Dim salesTotal As Decimal = 0D
        Dim commissionTotal As Decimal = 0D
        For Each row In _rows
            invoiceTotal += row.InvoiceCount
            salesTotal += row.SalesTotal
            commissionTotal += row.CommissionAmount
        Next
        SetFooter("عدد السجلات: " & _rows.Count.ToString() &
                  "   |   الفواتير: " & invoiceTotal.ToString() &
                  "   |   إجمالي المبيعات: " & UiTheme.Money(salesTotal) &
                  "   |   إجمالي العمولة: " & UiTheme.Money(commissionTotal))
    End Sub
End Class
