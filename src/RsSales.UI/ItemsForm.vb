Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class ItemsForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly chkInactive As New CheckBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly lblCount As New Label()
    Private _items As New List(Of ItemSummary)()

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        Search()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "الأصناف والباركود"
        ClientSize = New Size(1120, 680)
        StartPosition = FormStartPosition.CenterParent

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        txtSearch.Width = 300
        txtSearch.PlaceholderText = "كود / باركود / اسم الصنف"
        AddHandler txtSearch.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              Search()
                                          End If
                                      End Sub
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 90)
        AddHandler btnSearch.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnSearch)
        chkInactive.Text = "إظهار غير المفعّلة"
        chkInactive.AutoSize = True
        chkInactive.Margin = New Padding(12, 10, 4, 4)
        AddHandler chkInactive.CheckedChanged, Sub(s, e) Search()
        toolbar.Controls.Add(chkInactive)
        Dim btnNew As Button = UiTheme.PrimaryButton("صنف جديد", 120)
        AddHandler btnNew.Click, Sub(s, e) EditItem(Nothing)
        toolbar.Controls.Add(btnNew)
        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 100)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        toolbar.Controls.Add(btnEdit)
        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / تعطيل", 130)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        toolbar.Controls.Add(btnToggle)
        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 90)
        AddHandler btnRefresh.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnRefresh)

        UiTheme.AddTextColumn(grid, "Code", "الكود", 110)
        UiTheme.AddTextColumn(grid, "NameAr", "الاسم العربي", 280, True)
        UiTheme.AddTextColumn(grid, "NameEn", "الاسم الإنجليزي", 150)
        UiTheme.AddTextColumn(grid, "SalePrice", "سعر البيع", 100, False, "N2")
        UiTheme.AddTextColumn(grid, "CostPrice", "التكلفة", 100, False, "N2")
        UiTheme.AddTextColumn(grid, "TaxRate", "الضريبة %", 80, False, "N0")
        UiTheme.AddTextColumn(grid, "QtyOnHand", "الرصيد", 90, False, "N2")
        UiTheme.AddTextColumn(grid, "ReorderLevel", "حد الطلب", 90, False, "N2")
        UiTheme.AddTextColumn(grid, "IsActive", "مفعّل", 70)
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                             If e.RowIndex >= 0 Then EditSelected()
                                         End Sub

        lblCount.Dock = DockStyle.Bottom
        lblCount.Height = 28
        lblCount.TextAlign = ContentAlignment.MiddleRight
        lblCount.Padding = New Padding(8, 0, 8, 0)
        lblCount.ForeColor = UiTheme.Muted

        Controls.Add(grid)
        Controls.Add(lblCount)
        Controls.Add(toolbar)
    End Sub

    Private Sub Search()
        Try
            Cursor = Cursors.WaitCursor
            _items = ItemService.Search(_session, txtSearch.Text, chkInactive.Checked, 500)
            grid.DataSource = New BindingSource With {.DataSource = _items}
            lblCount.Text = "عدد الأصناف: " & _items.Count.ToString()
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Function SelectedItem() As ItemSummary
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.DataBoundItem, ItemSummary)
    End Function

    Private Sub EditSelected()
        Dim item = SelectedItem()
        If item Is Nothing Then
            UiTheme.Warn("اختر صنفًا أولاً.")
            Return
        End If
        EditItem(item)
    End Sub

    Private Sub EditItem(item As ItemSummary)
        Using dialog As New ItemEditForm(_session, item)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Search()
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim item = SelectedItem()
        If item Is Nothing Then
            UiTheme.Warn("اختر صنفًا أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(item.IsActive, "تعطيل", "تفعيل") & " الصنف «" & item.NameAr & "»؟") Then Return
        Try
            ItemService.SetActive(_session, item.Id, Not item.IsActive)
            Search()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class ItemEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _item As ItemSummary

    Private ReadOnly txtCode As New TextBox()
    Private ReadOnly txtNameAr As New TextBox()
    Private ReadOnly txtNameEn As New TextBox()
    Private ReadOnly cboGroup As New ComboBox()
    Private ReadOnly cboUnit As New ComboBox()
    Private ReadOnly txtCost As New TextBox()
    Private ReadOnly txtSale As New TextBox()
    Private ReadOnly txtWholesale As New TextBox()
    Private ReadOnly txtMin As New TextBox()
    Private ReadOnly txtReorder As New TextBox()
    Private ReadOnly cboTax As New ComboBox()
    Private ReadOnly chkActive As New CheckBox()
    Private ReadOnly txtBarcode As New TextBox()

    Public Sub New(session As AppSession, item As ItemSummary)
        _session = session
        _item = item
        InitializeUi()
        LoadLookups()
        If item IsNot Nothing Then Fill(item)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_item Is Nothing, "صنف جديد", "تعديل الصنف")
        ClientSize = New Size(560, 520)
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

        AddRow(table, "الكود", txtCode)
        AddRow(table, "الاسم العربي", txtNameAr)
        AddRow(table, "الاسم الإنجليزي", txtNameEn)
        AddRow(table, "المجموعة", cboGroup)
        AddRow(table, "الوحدة", cboUnit)
        AddRow(table, "سعر الشراء", txtCost)
        AddRow(table, "سعر البيع", txtSale)
        AddRow(table, "سعر الجملة", txtWholesale)
        AddRow(table, "أقل سعر بيع", txtMin)
        AddRow(table, "حد الطلب", txtReorder)
        AddRow(table, "التصنيف الضريبي", cboTax)
        AddRow(table, "الباركود", txtBarcode)

        chkActive.Text = "الصنف مفعّل"
        chkActive.Checked = True
        chkActive.AutoSize = True
        Dim checkRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        table.Controls.Add(New Label(), 0, checkRow)
        table.Controls.Add(chkActive, 1, checkRow)

        cboGroup.DisplayMember = "Name"
        cboGroup.ValueMember = "Id"
        cboUnit.DisplayMember = "Name"
        cboUnit.ValueMember = "Id"
        cboTax.DisplayMember = "Name"
        cboTax.ValueMember = "Id"

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 60,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("حفظ", 130)
        AddHandler btnSave.Click, AddressOf SaveClicked
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 100)
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
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        table.Controls.Add(UiTheme.MakeLabel(caption, True), 0, row)
        editor.Dock = DockStyle.Fill
        table.Controls.Add(editor, 1, row)
    End Sub

    Private Sub LoadLookups()
        Try
            cboTax.DataSource = UiLookup.TaxCategories(_session)
            cboGroup.DataSource = UiLookup.Groups(_session)
            cboUnit.DataSource = UiLookup.Units(_session)
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub Fill(item As ItemSummary)
        txtCode.Text = item.Code
        txtNameAr.Text = item.NameAr
        txtNameEn.Text = item.NameEn
        txtCost.Text = item.CostPrice.ToString("0.00")
        txtSale.Text = item.SalePrice.ToString("0.00")
        txtBarcode.Text = item.Barcode
        chkActive.Checked = item.IsActive
        SetSelected(cboTax, item.TaxCategoryId)
        Try
            Dim record = ItemService.GetById(_session, item.Id)
            If record IsNot Nothing Then
                txtWholesale.Text = record.WholesalePrice.ToString("0.00")
                txtMin.Text = record.MinPrice.ToString("0.00")
                txtReorder.Text = record.ReorderLevel.ToString("0.##")
                SetSelected(cboGroup, record.GroupId)
                SetSelected(cboUnit, record.BaseUnitId)
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Shared Sub SetSelected(combo As ComboBox, id As Object)
        If id Is Nothing Then Return
        Try
            combo.SelectedValue = CLng(id)
        Catch
            ' Ignore lookups the current user cannot see.
        End Try
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            If String.IsNullOrWhiteSpace(txtCode.Text) OrElse String.IsNullOrWhiteSpace(txtNameAr.Text) Then
                UiTheme.Warn("الكود والاسم العربي مطلوبان.")
                Return
            End If
            Dim taxId As Long = If(cboTax.SelectedValue Is Nothing, 0L, CLng(cboTax.SelectedValue))
            If taxId <= 0 Then
                UiTheme.Warn("اختر التصنيف الضريبي.")
                Return
            End If
            Dim groupId As Long? = Nothing
            If cboGroup.SelectedValue IsNot Nothing Then groupId = CLng(cboGroup.SelectedValue)
            Dim unitId As Long? = Nothing
            If cboUnit.SelectedValue IsNot Nothing Then unitId = CLng(cboUnit.SelectedValue)

            Dim itemId As Long? = Nothing
            If _item IsNot Nothing Then itemId = _item.Id

            ItemService.Save(_session, itemId, txtCode.Text.Trim(), txtNameAr.Text.Trim(), txtNameEn.Text.Trim(),
                             groupId, unitId, UiTheme.ToMoney(txtCost.Text), UiTheme.ToMoney(txtSale.Text),
                             UiTheme.ToMoney(txtWholesale.Text), UiTheme.ToMoney(txtMin.Text),
                             UiTheme.ToMoney(txtReorder.Text), taxId, chkActive.Checked, txtBarcode.Text.Trim())
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
