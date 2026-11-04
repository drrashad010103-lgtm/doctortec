Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' تحصيل آجل: the customer pays part or all of what is on their unpaid invoices, the
''' invoices' remaining goes down and the money enters the till and the ledger.
''' </summary>
Public Class CustomerCollectionForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _preselectedCustomerId As Long
    Private ReadOnly cboCustomer As New ComboBox()
    Private ReadOnly cboInvoice As New ComboBox()
    Private ReadOnly txtAmount As New TextBox()
    Private ReadOnly cboMethod As New ComboBox()
    Private ReadOnly txtNote As New TextBox()
    Private ReadOnly lblStatus As New Label()
    Private _dueRows As List(Of CustomerDueInvoice) = New List(Of CustomerDueInvoice)()
    Private _loading As Boolean

    Public Sub New(session As AppSession, Optional customerId As Long = 0)
        _session = session
        _preselectedCustomerId = customerId
        InitializeUi()
        LoadLookups()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "تحصيل آجل من العملاء"
        ClientSize = New Size(600, 430)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        cboCustomer.DisplayMember = "Name"
        cboCustomer.ValueMember = "Id"
        AddHandler cboCustomer.SelectedIndexChanged, AddressOf CustomerChanged

        cboInvoice.DisplayMember = "Text"
        cboInvoice.ValueMember = "InvoiceId"
        cboInvoice.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler cboInvoice.SelectedIndexChanged, AddressOf InvoiceChanged

        cboMethod.DisplayMember = "Name"
        cboMethod.ValueMember = "Id"
        cboMethod.DropDownStyle = ComboBoxStyle.DropDownList

        lblStatus.ForeColor = UiTheme.Muted
        lblStatus.AutoSize = False
        lblStatus.Height = 34
        lblStatus.TextAlign = ContentAlignment.MiddleRight

        AddRow(table, "العميل", cboCustomer)
        AddRow(table, "الفاتورة", cboInvoice)
        AddRow(table, "المبلغ المحصّل", txtAmount)
        AddRow(table, "طريقة الدفع", cboMethod)
        AddRow(table, "مرجع / ملاحظات", txtNote)

        Dim statusRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        table.Controls.Add(lblStatus, 1, statusRow)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("تحصيل", 120)
        AddHandler btnSave.Click, AddressOf SaveClicked
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 95)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        buttons.Controls.Add(btnSave)
        buttons.Controls.Add(btnCancel)

        Controls.Add(table)
        Controls.Add(buttons)
        AcceptButton = btnSave
        CancelButton = btnCancel
    End Sub

    Private Shared Sub AddRow(table As TableLayoutPanel, caption As String, editor As Control)
        Dim row As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
        table.Controls.Add(UiTheme.MakeLabel(caption, True), 0, row)
        editor.Dock = DockStyle.Fill
        table.Controls.Add(editor, 1, row)
    End Sub

    Private Sub LoadLookups()
        _loading = True
        Try
            Dim customers = CustomerService.Search(_session, String.Empty, "customer")
            cboCustomer.DataSource = customers

            cboMethod.DataSource = UiLookup.PaymentMethods(_session)
            If cboMethod.Items.Count > 0 Then cboMethod.SelectedIndex = 0

            If _preselectedCustomerId > 0 Then
                For Each c In customers
                    If c.Id = _preselectedCustomerId Then
                        cboCustomer.SelectedValue = c.Id
                        Exit For
                    End If
                Next
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
        _loading = False
        RefreshInvoices()
    End Sub

    Private ReadOnly Property CurrentCustomerId As Long
        Get
            If cboCustomer.SelectedValue Is Nothing Then Return 0
            Return CLng(cboCustomer.SelectedValue)
        End Get
    End Property

    Private Sub CustomerChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        RefreshInvoices()
    End Sub

    ''' <summary>Lists the customer's unpaid invoices: one entry per invoice plus
    ''' "the oldest first", which is what gets used when no single invoice is chosen.</summary>
    Private Sub RefreshInvoices()
        _dueRows = New List(Of CustomerDueInvoice)()
        If CurrentCustomerId > 0 Then
            Try
                _dueRows = CustomerCollectionService.Outstanding(_session, CurrentCustomerId)
            Catch ex As Exception
                UiTheme.Handle(ex)
            End Try
        End If

        Dim options As New List(Of CustomerCollectionInvoiceOption)()
        Dim totalDue As Decimal = 0D
        For Each row In _dueRows
            totalDue += row.Due
            options.Add(New CustomerCollectionInvoiceOption With {
                .InvoiceId = row.InvoiceId,
                .Text = row.InvoiceNo & " — " & row.BusinessDate.ToString("yyyy-MM-dd") &
                        "  (متبقي " & row.Due.ToString("N2") & ")"
            })
        Next
        options.Insert(0, New CustomerCollectionInvoiceOption With {
            .InvoiceId = 0L,
            .Text = "كل الفواتير (الأقدم أولاً)"
        })

        _loading = True
        cboInvoice.DataSource = options
        cboInvoice.SelectedIndex = 0
        _loading = False

        totalDue = Money.Round2(totalDue)
        If totalDue > 0D Then txtAmount.Text = totalDue.ToString("0.##")
        lblStatus.Text = If(_dueRows.Count = 0,
                            "مفيش فواتير غير مسددة على هذا العميل.",
                            "عدد الفواتير غير المسددة: " & _dueRows.Count.ToString() &
                            "   |   إجمالي المتبقي: " & UiTheme.Money(totalDue))
    End Sub

    Private Sub InvoiceChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Dim selected As Decimal = SelectedInvoiceDue()
        If selected > 0D Then txtAmount.Text = selected.ToString("0.##")
    End Sub

    Private Function SelectedInvoiceDue() As Decimal
        If cboInvoice.SelectedValue Is Nothing Then Return 0D
        Dim id As Long = CLng(cboInvoice.SelectedValue)
        If id = 0L Then Return 0D
        For Each row In _dueRows
            If row.InvoiceId = id Then Return Money.Round2(row.Due)
        Next
        Return 0D
    End Function

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim invoiceId As Long? = Nothing
            If cboInvoice.SelectedValue IsNot Nothing AndAlso CLng(cboInvoice.SelectedValue) > 0L Then
                invoiceId = CLng(cboInvoice.SelectedValue)
            End If
            Dim methodId As Long = 0L
            If cboMethod.SelectedValue IsNot Nothing Then methodId = CLng(cboMethod.SelectedValue)

            Dim result = CustomerCollectionService.Collect(_session, CurrentCustomerId,
                                                            UiTheme.ToMoney(txtAmount.Text), methodId,
                                                            invoiceId, txtNote.Text.Trim())
            UiTheme.Info("تم تحصيل " & UiTheme.Money(result.Amount) & " من " & result.CustomerName &
                         Environment.NewLine &
                         "عدد الفواتير اللي اتسددت: " & result.SettledInvoiceCount.ToString() &
                         Environment.NewLine &
                         "المتبقي على العميل: " & UiTheme.Money(result.RemainingDue), "تم التحصيل")
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

''' <summary>One entry of the invoice picker; id 0 means "spread over every unpaid invoice".</summary>
Public Class CustomerCollectionInvoiceOption
    Public Property InvoiceId As Long
    Public Property Text As String
End Class
