Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class CustomersForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _type As String
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly lblBalance As New Label()

    Public Sub New(session As AppSession, typeValue As String)
        _session = session
        _type = typeValue
        InitializeUi()
        Search()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_type = "supplier", "الموردون", "العملاء")
        ClientSize = New Size(980, 640)
        StartPosition = FormStartPosition.CenterParent

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        txtSearch.Width = 320
        txtSearch.PlaceholderText = "اسم / موبايل / رقم ضريبي"
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 90)
        AddHandler btnSearch.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnSearch)
        Dim btnNew As Button = UiTheme.PrimaryButton(If(_type = "supplier", "مورد جديد", "عميل جديد"), 150)
        AddHandler btnNew.Click, Sub(s, e) Edit(Nothing)
        toolbar.Controls.Add(btnNew)
        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 90)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        toolbar.Controls.Add(btnEdit)
        Dim btnBal As Button = UiTheme.MakeButton("عرض الرصيد", 130)
        AddHandler btnBal.Click, Sub(s, e) ShowBalance()
        toolbar.Controls.Add(btnBal)
        Dim btnStatement As Button = UiTheme.MakeButton("كشف حساب", 120)
        AddHandler btnStatement.Click, Sub(s, e) ShowStatement()
        toolbar.Controls.Add(btnStatement)

        UiTheme.AddTextColumn(grid, "Name", "الاسم", 240, True)
        UiTheme.AddTextColumn(grid, "Mobile", "الموبايل", 125)
        UiTheme.AddTextColumn(grid, "GroupName", "المجموعة", 130)
        UiTheme.AddTextColumn(grid, "TaxNo", "الرقم الضريبي", 140)
        UiTheme.AddTextColumn(grid, "CreditLimit", "حد الائتمان", 120, False, "N2")
        UiTheme.AddTextColumn(grid, "Balance", "الرصيد", 110, False, "N2")
        AddHandler grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        lblBalance.Dock = DockStyle.Bottom
        lblBalance.Height = 32
        lblBalance.TextAlign = ContentAlignment.MiddleRight
        lblBalance.Padding = New Padding(8, 0, 8, 0)
        lblBalance.ForeColor = UiTheme.Navy

        Controls.Add(grid)
        Controls.Add(lblBalance)
        Controls.Add(toolbar)
    End Sub

    Private Sub Search()
        Try
            Cursor = Cursors.WaitCursor
            Dim rows = CustomerService.Search(_session, txtSearch.Text, _type)
            grid.DataSource = New BindingSource With {.DataSource = rows}
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Function Selected() As CustomerSummary
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.DataBoundItem, CustomerSummary)
    End Function

    Private Sub EditSelected()
        Dim sRow = Selected()
        If sRow Is Nothing Then UiTheme.Warn("اختر سجلًا.") : Return
        Edit(sRow)
    End Sub

    Private Sub Edit(row As CustomerSummary)
        Using dlg As New CustomerEditForm(_session, _type, row)
            If dlg.ShowDialog(Me) = DialogResult.OK Then Search()
        End Using
    End Sub

    Private Sub ShowBalance()
        Dim sRow = Selected()
        If sRow Is Nothing Then UiTheme.Warn("اختر سجلًا.") : Return
        Try
            Dim bal = CustomerService.Balance(_session, sRow.Id)
            lblBalance.Text = "رصيد " & sRow.Name & ": " & UiTheme.Money(bal)
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ShowStatement()
        Dim sRow = Selected()
        If sRow Is Nothing Then UiTheme.Warn("اختر سجلًا.") : Return
        Using dlg As New PartyStatementForm(_session, sRow.Id)
            dlg.ShowDialog(Me)
        End Using
    End Sub
End Class

Public Class CustomerEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _type As String
    Private ReadOnly _row As CustomerSummary
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly txtMobile As New TextBox()
    Private ReadOnly txtPhone As New TextBox()
    Private ReadOnly txtEmail As New TextBox()
    Private ReadOnly txtTax As New TextBox()
    Private ReadOnly txtAddress As New TextBox()
    Private ReadOnly txtCredit As New TextBox()
    Private ReadOnly txtOpen As New TextBox()
    Private ReadOnly cboGroup As New ComboBox()
    Private ReadOnly chkActive As New CheckBox()

    Public Sub New(session As AppSession, typeValue As String, row As CustomerSummary)
        _session = session
        _type = typeValue
        _row = row
        InitializeUi()
        LoadGroups()
        LoadData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_row Is Nothing, If(_type = "supplier", "مورد جديد", "عميل جديد"), "تعديل بيانات")
        ClientSize = New Size(520, 460)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False : MinimizeBox = False

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(20)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130)) : t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        AddRow(t, "الاسم", txtName)
        AddRow(t, "الموبايل", txtMobile)
        AddRow(t, "الهاتف", txtPhone)
        AddRow(t, "البريد", txtEmail)
        AddRow(t, "الرقم الضريبي", txtTax)
        AddRow(t, "العنوان", txtAddress)
        AddRow(t, "المجموعة", cboGroup)
        AddRow(t, "حد الائتمان", txtCredit)
        AddRow(t, "الرصيد الافتتاحي", txtOpen)
        chkActive.Text = "مفعّل" : chkActive.Checked = True : chkActive.AutoSize = True
        Dim cr As Integer = t.RowCount : t.RowCount += 1 : t.RowStyles.Add(New RowStyle(SizeType.Absolute, 36)) : t.Controls.Add(New Label(), 0, cr) : t.Controls.Add(chkActive, 1, cr)

        Dim btns As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 58, .FlowDirection = FlowDirection.LeftToRight, .Padding = New Padding(12)}
        Dim bs As Button = UiTheme.PrimaryButton("حفظ", 130) : AddHandler bs.Click, AddressOf SaveCustomer
        Dim bc As Button = UiTheme.MakeButton("إلغاء", 100) : AddHandler bc.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        btns.Controls.Add(bs) : btns.Controls.Add(bc)

        Controls.Add(t) : Controls.Add(btns) : AcceptButton = bs
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount : t.RowCount += 1 : t.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r) : ed.Dock = DockStyle.Fill : t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub LoadData()
        If _row Is Nothing Then Return
        Try
            Dim rec = CustomerService.GetById(_session, _row.Id)
            If rec IsNot Nothing Then
                txtName.Text = rec.Name : txtMobile.Text = rec.Mobile : txtPhone.Text = rec.Phone
                txtEmail.Text = rec.Email : txtTax.Text = rec.TaxNo : txtAddress.Text = rec.Address
                txtCredit.Text = rec.CreditLimit.ToString("0.00") : txtOpen.Text = rec.OpeningBalance.ToString("0.00")
                chkActive.Checked = rec.IsActive
                cboGroup.SelectedValue = If(rec.GroupId.HasValue, rec.GroupId.Value, 0L)
            End If
        Catch ex As Exception : UiTheme.Handle(ex) : End Try
    End Sub

    Private Sub LoadGroups()
        Try
            Dim rows As New List(Of CustomerGroupSummary)()
            rows.Add(New CustomerGroupSummary With {.Id = 0, .Name = "بدون مجموعة"})
            rows.AddRange(CustomerGroupService.Search(_session, String.Empty, True))
            cboGroup.DisplayMember = "Name"
            cboGroup.ValueMember = "Id"
            cboGroup.DataSource = rows
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub SaveCustomer(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(txtName.Text) Then UiTheme.Warn("الاسم مطلوب.") : Return
        Try
            Dim id As Long? = Nothing
            If _row IsNot Nothing Then id = _row.Id
            Dim groupId As Long? = Nothing
            If cboGroup.SelectedValue IsNot Nothing Then
                Dim selectedGroup As Long = CLng(cboGroup.SelectedValue)
                If selectedGroup > 0 Then groupId = selectedGroup
            End If
            CustomerService.Save(_session, id, txtName.Text.Trim(), _type, txtMobile.Text.Trim(), txtPhone.Text.Trim(),
                                 txtEmail.Text.Trim(), txtAddress.Text.Trim(), txtTax.Text.Trim(),
                                 UiTheme.ToMoney(txtCredit.Text), UiTheme.ToMoney(txtOpen.Text), chkActive.Checked, groupId)
            DialogResult = DialogResult.OK : Close()
        Catch ex As Exception : UiTheme.Handle(ex) : End Try
    End Sub
End Class
