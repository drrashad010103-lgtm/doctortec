Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class PurchaseVouchersForm
    Inherits SectionFormBase

    Private _vouchers As List(Of PurchaseVoucherSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "قسيمة مشتريات",
                   "إصدار قسائم شراء مدفوعة للعملاء ومتابعة رصيدها واستردادها",
                   Color.FromArgb(41, 128, 185), New Size(1050, 640))

        AddSearch("كود القسيمة أو اسم العميل", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("إصدار قسيمة", 130)
        AddHandler btnNew.Click, Sub(s, e) IssueVoucher()
        ToolBar.Controls.Add(btnNew)

        Dim btnRedeem As Button = UiTheme.MakeButton("استرداد", 100)
        AddHandler btnRedeem.Click, Sub(s, e) RedeemSelected()
        ToolBar.Controls.Add(btnRedeem)

        Dim btnHistory As Button = UiTheme.MakeButton("سجل الاسترداد", 125)
        AddHandler btnHistory.Click, Sub(s, e) ShowHistory()
        ToolBar.Controls.Add(btnHistory)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 120)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Code", "كود القسيمة", 150)
        UiTheme.AddTextColumn(Grid, "CustomerName", "العميل", 220, True)
        UiTheme.AddTextColumn(Grid, "Amount", "قيمة القسيمة", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "RemainingAmount", "الرصيد المتبقي", 120, False, "N2")
        UiTheme.AddTextColumn(Grid, "UsedAmount", "المستخدم", 100, False, "N2")
        UiTheme.AddTextColumn(Grid, "IssuedAt", "تاريخ الإصدار", 110, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "ExpiresAt", "تاريخ الانتهاء", 110, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 95)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then RedeemSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _vouchers = PurchaseVoucherService.Search(Session, SearchText, True)
        Grid.DataSource = New BindingSource With {.DataSource = _vouchers}
        Dim total As Decimal = 0D
        Dim remaining As Decimal = 0D
        Dim active As Integer = 0
        For Each voucher In _vouchers
            total += voucher.Amount
            remaining += voucher.RemainingAmount
            If voucher.StatusName = "سارية" Then active += 1
        Next
        SetFooter("عدد القسائم: " & _vouchers.Count.ToString() &
                  "   |   السارية: " & active.ToString() &
                  "   |   إجمالي القيمة: " & UiTheme.Money(total) &
                  "   |   إجمالي الرصيد المتبقي: " & UiTheme.Money(remaining))
    End Sub

    Private Sub IssueVoucher()
        Using dialog As New PurchaseVoucherIssueForm(Session)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub RedeemSelected()
        Dim voucher = SelectedItem(Of PurchaseVoucherSummary)()
        If voucher Is Nothing Then
            UiTheme.Warn("اختر قسيمة أولاً.")
            Return
        End If
        Using dialog As New PurchaseVoucherRedeemForm(Session, voucher)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ShowHistory()
        Dim voucher = SelectedItem(Of PurchaseVoucherSummary)()
        If voucher Is Nothing Then
            UiTheme.Warn("اختر قسيمة أولاً.")
            Return
        End If
        Using dialog As New PurchaseVoucherHistoryForm(Session, voucher)
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim voucher = SelectedItem(Of PurchaseVoucherSummary)()
        If voucher Is Nothing Then
            UiTheme.Warn("اختر قسيمة أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(voucher.IsActive, "إيقاف", "تفعيل") &
                               " القسيمة «" & voucher.Code & "»؟") Then Return
        Try
            PurchaseVoucherService.SetActive(Session, voucher.Id, Not voucher.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class PurchaseVoucherIssueForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly cboCustomer As New ComboBox()
    Private ReadOnly txtAmount As New TextBox()
    Private ReadOnly dtpExpiry As New DateTimePicker()
    Private ReadOnly txtNotes As New TextBox()

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadCustomers()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "إصدار قسيمة مشتريات"
        ClientSize = New Size(540, 360)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 145))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        dtpExpiry.Format = DateTimePickerFormat.Short
        dtpExpiry.ShowCheckBox = True
        dtpExpiry.Checked = False

        AddRow(table, "العميل", cboCustomer)
        AddRow(table, "قيمة القسيمة", txtAmount)
        AddRow(table, "تاريخ الانتهاء", dtpExpiry)
        AddRow(table, "ملاحظات", txtNotes)

        Dim hint As New Label With {
            .Text = "يتم توليد كود القسيمة تلقائيًا، ويمكن استردادها على دفعات حتى نفاد الرصيد.",
            .ForeColor = UiTheme.Muted,
            .AutoSize = False,
            .Height = 32,
            .Dock = DockStyle.Top,
            .TextAlign = ContentAlignment.MiddleRight,
            .Padding = New Padding(20, 0, 20, 0)
        }

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("إصدار", 120)
        AddHandler btnSave.Click, AddressOf SaveClicked
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 95)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        buttons.Controls.Add(btnSave)
        buttons.Controls.Add(btnCancel)

        Controls.Add(table)
        Controls.Add(hint)
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

    Private Sub LoadCustomers()
        Try
            Dim rows As New List(Of CustomerSummary)()
            rows.Add(New CustomerSummary With {.Id = 0, .Name = "بدون عميل"})
            rows.AddRange(CustomerService.Search(_session, String.Empty, "customer"))
            cboCustomer.DisplayMember = "Name"
            cboCustomer.ValueMember = "Id"
            cboCustomer.DataSource = rows
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim customerId As Long? = Nothing
            If cboCustomer.SelectedValue IsNot Nothing Then
                Dim selected As Long = CLng(cboCustomer.SelectedValue)
                If selected > 0 Then customerId = selected
            End If
            Dim expiresAt As Date? = Nothing
            If dtpExpiry.Checked Then expiresAt = dtpExpiry.Value.Date
            PurchaseVoucherService.Issue(_session, customerId, UiTheme.ToMoney(txtAmount.Text),
                                         expiresAt, txtNotes.Text.Trim())
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class PurchaseVoucherRedeemForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _voucher As PurchaseVoucherSummary
    Private ReadOnly txtAmount As New TextBox()
    Private ReadOnly txtNote As New TextBox()

    Public Sub New(session As AppSession, voucher As PurchaseVoucherSummary)
        _session = session
        _voucher = voucher
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "استرداد من القسيمة — " & _voucher.Code
        ClientSize = New Size(520, 330)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim info As New Label With {
            .Text = _voucher.Code & " — " & _voucher.CustomerName & Environment.NewLine &
                    "الرصيد المتبقي: " & _voucher.RemainingAmount.ToString("N2"),
            .AutoSize = False,
            .Height = 46,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(10.5F, FontStyle.Bold),
            .ForeColor = UiTheme.Navy
        }
        Dim infoRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
        table.Controls.Add(info, 0, infoRow)
        table.SetColumnSpan(info, 2)

        AddRow(table, "مبلغ الاسترداد", txtAmount)
        AddRow(table, "ملاحظات", txtNote)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("استرداد", 120)
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

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            PurchaseVoucherService.Redeem(_session, _voucher.Id, UiTheme.ToMoney(txtAmount.Text),
                                          txtNote.Text.Trim())
            UiTheme.Info("تم تنفيذ الاسترداد بنجاح.")
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class PurchaseVoucherHistoryForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _voucher As PurchaseVoucherSummary
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()

    Public Sub New(session As AppSession, voucher As PurchaseVoucherSummary)
        _session = session
        _voucher = voucher
        InitializeUi()
        LoadData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "سجل استرداد القسيمة — " & _voucher.Code
        ClientSize = New Size(720, 440)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False

        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 50,
            .BackColor = Color.FromArgb(41, 128, 185),
            .Padding = New Padding(14, 8, 14, 8)
        }
        Dim lbl As New Label With {
            .Text = _voucher.Code & " — " & _voucher.CustomerName &
                    "   |   المتبقي: " & _voucher.RemainingAmount.ToString("N2"),
            .Dock = DockStyle.Fill,
            .ForeColor = Color.White,
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(lbl)

        UiTheme.AddTextColumn(grid, "RedeemedAt", "تاريخ الاسترداد", 165, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(grid, "Amount", "المبلغ", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "InvoiceNo", "الفاتورة", 125)
        UiTheme.AddTextColumn(grid, "Note", "ملاحظات", 220, True)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(10)
        }
        Dim btnClose As Button = UiTheme.MakeButton("إغلاق", 100)
        AddHandler btnClose.Click, Sub(s, e) Close()
        buttons.Controls.Add(btnClose)

        Controls.Add(grid)
        Controls.Add(buttons)
        Controls.Add(header)
    End Sub

    Private Sub LoadData()
        Try
            grid.DataSource = New BindingSource With {
                .DataSource = PurchaseVoucherService.Redemptions(_session, _voucher.Id)
            }
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
