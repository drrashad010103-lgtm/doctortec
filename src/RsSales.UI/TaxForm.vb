Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class TaxForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly tab As New TabControl()

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadTaxData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — الضرائب والتصنيفات الضريبية"
        ClientSize = New Size(820, 580)
        StartPosition = FormStartPosition.CenterParent
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        Dim p1 As New TabPage("تصنيفات ضريبية")
        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        Dim btnNew As Button = UiTheme.PrimaryButton("إضافة تصنيف", 140)
        AddHandler btnNew.Click, AddressOf NewCategoryClick
        toolbar.Controls.Add(btnNew)
        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 100)
        AddHandler btnEdit.Click, AddressOf EditSelectedClick
        toolbar.Controls.Add(btnEdit)
        Dim btnDef As Button = UiTheme.MakeButton("إنشاء الافتراضية", 160)
        AddHandler btnDef.Click, AddressOf EnsureDefaultsClick
        toolbar.Controls.Add(btnDef)

        UiTheme.AddTextColumn(grid, "Code", "الكود", 100)
        UiTheme.AddTextColumn(grid, "NameAr", "الاسم", 260, True)
        UiTheme.AddTextColumn(grid, "Rate", "النسبة %", 100, False, "N2")
        UiTheme.AddTextColumn(grid, "IsExempt", "معفى", 80)
        UiTheme.AddTextColumn(grid, "IsActive", "مفعّل", 80)
        AddHandler grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        p1.Controls.Add(grid)
        p1.Controls.Add(toolbar)

        tab.Dock = DockStyle.Fill
        tab.Controls.Add(p1)
        Controls.Add(tab)
    End Sub

    Private Sub LoadTaxData()
        Try
            Dim rows = TaxService.ListCategories(_session)
            grid.DataSource = New BindingSource With {.DataSource = rows}
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Function Selected() As TaxCategorySummary
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.DataBoundItem, TaxCategorySummary)
    End Function

    Private Sub NewCategoryClick(sender As Object, e As EventArgs)
        EditCat(Nothing)
    End Sub

    Private Sub EditSelected()
        Dim sRow = Selected()
        If sRow Is Nothing Then UiTheme.Warn("اختر تصنيفًا.") : Return
        EditCat(sRow)
    End Sub

    Private Sub EditSelectedClick(sender As Object, e As EventArgs)
        EditSelected()
    End Sub

    Private Sub EditCat(row As TaxCategorySummary)
        Using dlg As New TaxCatEditForm(_session, row)
            If dlg.ShowDialog(Me) = DialogResult.OK Then LoadTaxData()
        End Using
    End Sub

    Private Sub EnsureDefaultsClick(sender As Object, e As EventArgs)
        Try
            Dim n = TaxService.EnsureDefaultCategories(_session)
            UiTheme.Info("تم إنشاء/تأكيد " & n.ToString() & " تصنيف ضريبي.")
            LoadTaxData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class TaxCatEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _row As TaxCategorySummary
    Private ReadOnly txtCode As New TextBox()
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly txtRate As New TextBox()
    Private ReadOnly chkExempt As New CheckBox()
    Private ReadOnly chkActive As New CheckBox()
    Private ReadOnly dtEff As New DateTimePicker()

    Public Sub New(session As AppSession, row As TaxCategorySummary)
        _session = session
        _row = row
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_row Is Nothing, "تصنيف ضريبي جديد", "تعديل تصنيف ضريبي")
        ClientSize = New Size(460, 330)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(20)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        AddRow(t, "الكود", txtCode)
        AddRow(t, "الاسم", txtName)
        AddRow(t, "النسبة %", txtRate)
        AddRow(t, "ساري من", dtEff)
        dtEff.Format = DateTimePickerFormat.Short
        chkExempt.Text = "معفى من الضريبة"
        chkActive.Text = "مفعّل"
        chkActive.Checked = True
        Dim r1 = t.RowCount : t.RowCount += 1 : t.RowStyles.Add(New RowStyle(SizeType.Absolute, 32)) : t.Controls.Add(New Label(), 0, r1) : t.Controls.Add(chkExempt, 1, r1)
        Dim r2 = t.RowCount : t.RowCount += 1 : t.RowStyles.Add(New RowStyle(SizeType.Absolute, 32)) : t.Controls.Add(New Label(), 0, r2) : t.Controls.Add(chkActive, 1, r2)

        If _row IsNot Nothing Then
            txtCode.Text = _row.Code
            txtName.Text = _row.NameAr
            txtRate.Text = _row.Rate.ToString("0.00")
            chkExempt.Checked = _row.IsExempt
            chkActive.Checked = _row.IsActive
            txtCode.ReadOnly = True
        Else
            dtEff.Value = Date.Today
        End If

        Dim btns As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 58, .FlowDirection = FlowDirection.LeftToRight, .Padding = New Padding(12)}
        Dim bs As Button = UiTheme.PrimaryButton("حفظ", 120)
        AddHandler bs.Click, AddressOf SaveCategory
        Dim bc As Button = UiTheme.MakeButton("إلغاء", 100)
        AddHandler bc.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        btns.Controls.Add(bs)
        btns.Controls.Add(bc)

        Controls.Add(t)
        Controls.Add(btns)
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r)
        ed.Dock = DockStyle.Fill
        t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub SaveCategory(sender As Object, e As EventArgs)
        Try
            If String.IsNullOrWhiteSpace(txtCode.Text) OrElse String.IsNullOrWhiteSpace(txtName.Text) Then
                UiTheme.Warn("الكود والاسم مطلوبان.")
                Return
            End If
            Dim rate = UiTheme.ToMoney(txtRate.Text)
            Dim id As Long? = Nothing
            If _row IsNot Nothing Then id = _row.Id
            Dim eff As Date = If(_row Is Nothing, dtEff.Value.Date, _row.EffectiveFrom)
            TaxService.SaveCategory(_session, id, txtCode.Text.Trim(), txtName.Text.Trim(), rate, chkExempt.Checked, chkActive.Checked, eff)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
