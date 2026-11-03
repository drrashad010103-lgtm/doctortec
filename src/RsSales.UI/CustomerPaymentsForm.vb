Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class CustomerPaymentsForm
    Inherits SectionFormBase

    Private ReadOnly dtpFrom As New DateTimePicker()
    Private ReadOnly dtpTo As New DateTimePicker()
    Private _rows As List(Of CustomerPaymentSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "حركة آخر سداد",
                   "متابعة الدفعات المسجلة على فواتير العملاء خلال فترة محددة",
                   Color.FromArgb(230, 126, 34), New Size(1120, 650))

        AddSearch("اسم العميل أو رقم الفاتورة", AddressOf Reload)

        dtpFrom.Width = 125
        dtpFrom.Format = DateTimePickerFormat.Short
        dtpFrom.Value = Date.Today.AddDays(-30)
        dtpFrom.Margin = New Padding(6, 5, 4, 4)
        AddHandler dtpFrom.ValueChanged, Sub(s, e) Reload()
        dtpTo.Width = 125
        dtpTo.Format = DateTimePickerFormat.Short
        dtpTo.Value = Date.Today
        dtpTo.Margin = New Padding(4, 5, 8, 4)
        AddHandler dtpTo.ValueChanged, Sub(s, e) Reload()

        ToolBar.Controls.Add(UiTheme.MakeLabel("من:"))
        ToolBar.Controls.Add(dtpFrom)
        ToolBar.Controls.Add(UiTheme.MakeLabel("إلى:"))
        ToolBar.Controls.Add(dtpTo)

        AddRefresh(AddressOf Reload)

        Dim btnExport As Button = UiTheme.MakeButton("تصدير CSV", 110)
        AddHandler btnExport.Click, Sub(s, e) ExportCsv("customer-payments-" & Date.Now.ToString("yyyyMMdd") & ".csv")
        ToolBar.Controls.Add(btnExport)

        UiTheme.AddTextColumn(Grid, "PaidAt", "تاريخ السداد", 145, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(Grid, "InvoiceNo", "الفاتورة", 125)
        UiTheme.AddTextColumn(Grid, "InvoiceTypeName", "نوع الفاتورة", 115)
        UiTheme.AddTextColumn(Grid, "CustomerName", "العميل", 220, True)
        UiTheme.AddTextColumn(Grid, "MethodName", "طريقة الدفع", 120)
        UiTheme.AddTextColumn(Grid, "Amount", "المبلغ", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "Reference", "المرجع", 130)
        UiTheme.AddTextColumn(Grid, "UserName", "المستخدم", 120)

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        If dtpTo.Value.Date < dtpFrom.Value.Date Then
            SetFooter("تاريخ النهاية يجب ألا يسبق تاريخ البداية.")
            Grid.DataSource = New BindingSource With {.DataSource = New List(Of CustomerPaymentSummary)()}
            Return
        End If

        _rows = CustomerFinanceService.Payments(Session, dtpFrom.Value.Date, dtpTo.Value.Date, SearchText)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        Dim total As Decimal = 0D
        Dim methods As New Dictionary(Of String, Decimal)()
        For Each row In _rows
            total += row.Amount
            Dim method As String = If(String.IsNullOrWhiteSpace(row.MethodName), "غير محدد", row.MethodName)
            If methods.ContainsKey(method) Then
                methods(method) += row.Amount
            Else
                methods(method) = row.Amount
            End If
        Next
        Dim parts As New List(Of String)()
        For Each pair In methods
            parts.Add(pair.Key & ": " & UiTheme.Money(pair.Value))
        Next
        SetFooter("عدد الدفعات: " & _rows.Count.ToString() &
                  "   |   إجمالي السداد: " & UiTheme.Money(total) &
                  If(parts.Count > 0, "   |   " & String.Join("  |  ", parts.ToArray()), String.Empty))
    End Sub
End Class
