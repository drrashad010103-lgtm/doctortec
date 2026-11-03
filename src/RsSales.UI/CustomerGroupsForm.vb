Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class CustomerGroupsForm
    Inherits SectionFormBase

    Private _groups As List(Of CustomerGroupSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "مجموعات العملاء",
                   "تصنيف العملاء وتحديد نسبة خصم افتراضية لكل مجموعة",
                   Color.FromArgb(230, 126, 34), New Size(900, 620))

        AddSearch("اسم المجموعة", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("مجموعة جديدة", 140)
        AddHandler btnNew.Click, Sub(s, e) EditGroup(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 95)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 125)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Name", "المجموعة", 240, True)
        UiTheme.AddTextColumn(Grid, "DiscountPercent", "الخصم %", 100, False, "N2")
        UiTheme.AddTextColumn(Grid, "CustomerCount", "عدد العملاء", 110, False, "N0")
        UiTheme.AddTextColumn(Grid, "TotalBalance", "إجمالي الأرصدة", 140, False, "N2")
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 100)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _groups = CustomerGroupService.Search(Session, SearchText, True)
        Grid.DataSource = New BindingSource With {.DataSource = _groups}
        Dim activeCount As Integer = 0
        Dim total As Decimal = 0D
        For Each group In _groups
            If group.IsActive Then activeCount += 1
            total += group.TotalBalance
        Next
        SetFooter("عدد المجموعات: " & _groups.Count.ToString() &
                  "   |   المفعّلة: " & activeCount.ToString() &
                  "   |   إجمالي أرصدة العملاء: " & UiTheme.Money(total))
    End Sub

    Private Sub EditSelected()
        Dim group = SelectedItem(Of CustomerGroupSummary)()
        If group Is Nothing Then
            UiTheme.Warn("اختر مجموعة أولاً.")
            Return
        End If
        EditGroup(group)
    End Sub

    Private Sub EditGroup(group As CustomerGroupSummary)
        Using dialog As New CustomerGroupEditForm(Session, group)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim group = SelectedItem(Of CustomerGroupSummary)()
        If group Is Nothing Then
            UiTheme.Warn("اختر مجموعة أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(group.IsActive, "إيقاف", "تفعيل") &
                               " المجموعة «" & group.Name & "»؟") Then Return
        Try
            CustomerGroupService.SetActive(Session, group.Id, Not group.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class CustomerGroupEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _group As CustomerGroupSummary
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly txtDiscount As New TextBox()
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly chkActive As New CheckBox()

    Public Sub New(session As AppSession, group As CustomerGroupSummary)
        _session = session
        _group = group
        InitializeUi()
        If group IsNot Nothing Then Fill(group)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_group Is Nothing, "مجموعة عملاء جديدة", "تعديل مجموعة العملاء")
        ClientSize = New Size(500, 330)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        AddRow(table, "اسم المجموعة", txtName)
        AddRow(table, "نسبة الخصم %", txtDiscount)
        AddRow(table, "ملاحظات", txtNotes)

        chkActive.Text = "المجموعة مفعّلة"
        chkActive.Checked = True
        chkActive.AutoSize = True
        Dim checkRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
        table.Controls.Add(New Label(), 0, checkRow)
        table.Controls.Add(chkActive, 1, checkRow)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("حفظ", 120)
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

    Private Sub Fill(group As CustomerGroupSummary)
        txtName.Text = group.Name
        txtDiscount.Text = group.DiscountPercent.ToString("0.00")
        txtNotes.Text = group.Notes
        chkActive.Checked = group.IsActive
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim id As Long? = Nothing
            If _group IsNot Nothing Then id = _group.Id
            CustomerGroupService.Save(_session, id, txtName.Text.Trim(), UiTheme.ToMoney(txtDiscount.Text),
                                      txtNotes.Text.Trim(), chkActive.Checked)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
