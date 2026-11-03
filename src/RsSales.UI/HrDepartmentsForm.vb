Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class HrDepartmentsForm
    Inherits SectionFormBase

    Private _departments As List(Of HrDepartmentSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "تعريف الإدارات",
                   "إدارة الهيكل الإداري للمنشأة وربط الأقسام والموظفين بكل إدارة",
                   Color.FromArgb(41, 128, 185), New Size(940, 620))

        AddSearch("اسم الإدارة", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("إدارة جديدة", 130)
        AddHandler btnNew.Click, Sub(s, e) EditDepartment(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 95)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 125)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Name", "الإدارة", 260, True)
        UiTheme.AddTextColumn(Grid, "SectionCount", "عدد الأقسام", 110, False, "N0")
        UiTheme.AddTextColumn(Grid, "EmployeeCount", "عدد الموظفين", 120, False, "N0")
        UiTheme.AddTextColumn(Grid, "Notes", "ملاحظات", 250)
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 100)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _departments = HrDepartmentService.Search(Session, SearchText, True)
        Grid.DataSource = New BindingSource With {.DataSource = _departments}

        Dim activeCount As Integer = 0
        Dim sectionCount As Integer = 0
        Dim employeeCount As Integer = 0
        For Each department In _departments
            If department.IsActive Then activeCount += 1
            sectionCount += department.SectionCount
            employeeCount += department.EmployeeCount
        Next
        SetFooter("عدد الإدارات: " & _departments.Count.ToString() &
                  "   |   المفعّلة: " & activeCount.ToString() &
                  "   |   الأقسام: " & sectionCount.ToString() &
                  "   |   الموظفون: " & employeeCount.ToString())
    End Sub

    Private Sub EditSelected()
        Dim department = SelectedItem(Of HrDepartmentSummary)()
        If department Is Nothing Then
            UiTheme.Warn("اختر إدارة أولاً.")
            Return
        End If
        EditDepartment(department)
    End Sub

    Private Sub EditDepartment(department As HrDepartmentSummary)
        Using dialog As New HrDepartmentEditForm(Session, department)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim department = SelectedItem(Of HrDepartmentSummary)()
        If department Is Nothing Then
            UiTheme.Warn("اختر إدارة أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(department.IsActive, "إيقاف", "تفعيل") &
                               " الإدارة «" & department.Name & "»؟") Then Return
        Try
            HrDepartmentService.SetActive(Session, department.Id, Not department.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrDepartmentEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _department As HrDepartmentSummary
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly chkActive As New CheckBox()

    Public Sub New(session As AppSession, department As HrDepartmentSummary)
        _session = session
        _department = department
        InitializeUi()
        If department IsNot Nothing Then Fill(department)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_department Is Nothing, "إدارة جديدة", "تعديل الإدارة")
        ClientSize = New Size(520, 330)
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

        AddRow(table, "اسم الإدارة", txtName)
        AddRow(table, "ملاحظات", txtNotes)

        chkActive.Text = "الإدارة مفعّلة"
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

    Private Sub Fill(department As HrDepartmentSummary)
        txtName.Text = department.Name
        txtNotes.Text = department.Notes
        chkActive.Checked = department.IsActive
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim id As Long? = Nothing
            If _department IsNot Nothing Then id = _department.Id
            HrDepartmentService.Save(_session, id, txtName.Text.Trim(), txtNotes.Text.Trim(), chkActive.Checked)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
