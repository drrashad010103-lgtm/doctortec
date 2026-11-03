Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class CustomerBalancesForm
    Inherits SectionFormBase

    Private ReadOnly cboFilter As New ComboBox()
    Private _rows As List(Of CustomerBalanceSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "أرصدة حسابات العملاء",
                   "كشف أرصدة العملاء من الرصيد الافتتاحي والفواتير والمدفوعات",
                   Color.FromArgb(39, 174, 96), New Size(1080, 650))

        AddSearch("اسم العميل أو الموبايل", AddressOf Reload)

        cboFilter.Width = 170
        cboFilter.DropDownStyle = ComboBoxStyle.DropDownList
        cboFilter.Margin = New Padding(8, 5, 4, 4)
        cboFilter.Items.AddRange(New Object() {"كل العملاء", "المدينون", "الدائنون", "تجاوزوا حد الائتمان"})
        cboFilter.SelectedIndex = 0
        AddHandler cboFilter.SelectedIndexChanged, Sub(s, e) Reload()
        ToolBar.Controls.Add(UiTheme.MakeLabel("العرض:"))
        ToolBar.Controls.Add(cboFilter)

        AddRefresh(AddressOf Reload)

        Dim btnExport As Button = UiTheme.MakeButton("تصدير CSV", 110)
        AddHandler btnExport.Click, Sub(s, e) ExportCsv("customer-balances-" & Date.Now.ToString("yyyyMMdd") & ".csv")
        ToolBar.Controls.Add(btnExport)

        UiTheme.AddTextColumn(Grid, "Name", "العميل", 230, True)
        UiTheme.AddTextColumn(Grid, "Mobile", "الموبايل", 120)
        UiTheme.AddTextColumn(Grid, "OpeningBalance", "رصيد افتتاحي", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "InvoicedTotal", "إجمالي الفواتير", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "PaidTotal", "المدفوع", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "Due", "المتبقي", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "CreditLimit", "حد الائتمان", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "Balance", "الرصيد", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 95)

        Reload()
    End Sub

    Private ReadOnly Property FilterKey As String
        Get
            Select Case cboFilter.SelectedIndex
                Case 1 : Return "debit"
                Case 2 : Return "credit"
                Case 3 : Return "over"
                Case Else : Return "all"
            End Select
        End Get
    End Property

    Protected Overrides Sub LoadData()
        _rows = CustomerFinanceService.Balances(Session, SearchText, FilterKey)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        Dim debit As Decimal = 0D
        Dim credit As Decimal = 0D
        Dim due As Decimal = 0D
        For Each row In _rows
            If row.Balance > 0D Then debit += row.Balance
            If row.Balance < 0D Then credit += Math.Abs(row.Balance)
            due += row.Due
        Next
        SetFooter("عدد العملاء: " & _rows.Count.ToString() &
                  "   |   إجمالي المدين: " & UiTheme.Money(debit) &
                  "   |   إجمالي الدائن: " & UiTheme.Money(credit) &
                  "   |   المتبقي على الفواتير: " & UiTheme.Money(due))
    End Sub
End Class
