Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class HrSectionsForm
    Inherits SectionFormBase

    Private _sections As List(Of HrSectionSummary)
    Private ReadOnly cboDepartment As New ComboBox()
    Private _departments As List(Of HrDepartmentSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "تعريف الأقسام",
                   "تقسيم الإدارات إلى أقسام وربط الموظفين بكل قسم",
                   Color.FromArgb(230, 126, 34), New Size(980, 620))

        AddSearch("اسم القسم أو الإدارة", AddressOf Reload)

        cboDepartment.Width = 180
        cboDepartment.DropDownStyle = ComboBoxStyle.DropDownList
        cboDepartment.Margin = New Padding(4)
        AddHandler cboDepartment.SelectedIndexChanged, Sub(s, e) Reload()
        ToolBar.Controls.Add(UiTheme.MakeLabel("الإدارة:"))
        ToolBar.Controls.Add(cboDepartment)

        Dim btnNew As Button = UiTheme.PrimaryButton("قسم جديد", 120)
        AddHandler btnNew.Click, Sub(s, e) EditSection(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 95)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 125)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "DepartmentName", "الإدارة", 180)
        UiTheme.AddTextColumn(Grid, "Name", "القسم", 230, True)
        UiTheme.AddTextColumn(Grid, "EmployeeCount", "عدد الموظفين", 120, False, "N0")
        UiTheme.AddTextColumn(Grid, "Notes", "ملاحظات", 240)
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 100)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        LoadDepartmentFilter()
        Reload()
    End Sub

    Private Sub LoadDepartmentFilter()
        Try
            _departments = HrDepartmentService.Search(Session, String.Empty, True)
            Dim items As New List(Of HrDepartmentSummary)()
            items.Add(New HrDepartmentSummary With {.Id = 0, .Name = "كل الإدارات"})
            items.AddRange(_departments)
            cboDepartment.DisplayMember = "Name"
            cboDepartment.ValueMember = "Id"
            cboDepartment.DataSource = items
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Protected Overrides Sub LoadData()
        Dim allSections As List(Of HrSectionSummary) = HrSectionService.Search(Session, SearchText, True)
        Dim departmentId As Long = 0
        If cboDepartment.SelectedItem IsNot Nothing Then
            departmentId = DirectCast(cboDepartment.SelectedItem, HrDepartmentSummary).Id
        End If
        If departmentId > 0 Then
            _sections = allSections.FindAll(Function(section) section.DepartmentId = departmentId)
        Else
            _sections = allSections
        End If

        Grid.DataSource = New BindingSource With {.DataSource = _sections}
        Dim activeCount As Integer = 0
        Dim employeeCount As Integer = 0
        For Each section In _sections
            If section.IsActive Then activeCount += 1
            employeeCount += section.EmployeeCount
        Next
        SetFooter("عدد الأقسام: " & _sections.Count.ToString() &
                  "   |   المفعّلة: " & activeCount.ToString() &
                  "   |   الموظفون: " & employeeCount.ToString())
    End Sub

    Private Sub EditSelected()
        Dim section = SelectedItem(Of HrSectionSummary)()
        If section Is Nothing Then
            UiTheme.Warn("اختر قسمًا أولاً.")
            Return
        End If
        EditSection(section)
    End Sub

    Private Sub EditSection(section As HrSectionSummary)
        Using dialog As New HrSectionEditForm(Session, section)
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                LoadDepartmentFilter()
                Reload()
            End If
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim section = SelectedItem(Of HrSectionSummary)()
        If section Is Nothing Then
            UiTheme.Warn("اختر قسمًا أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(section.IsActive, "إيقاف", "تفعيل") &
                               " القسم «" & section.Name & "»؟") Then Return
        Try
            HrSectionService.SetActive(Session, section.Id, Not section.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrSectionEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _section As HrSectionSummary
    Private ReadOnly _departments As List(Of HrDepartmentSummary)
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly cboDepartment As New ComboBox()
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly chkActive As New CheckBox()

    Public Sub New(session As AppSession, section As HrSectionSummary)
        _session = session
        _section = section
        _departments = HrDepartmentService.Search(session, String.Empty, True)
        InitializeUi()
        FillDepartments()
        If section IsNot Nothing Then Fill(section)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_section Is Nothing, "قسم جديد", "تعديل القسم")
        ClientSize = New Size(540, 370)
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

        AddRow(table, "الإدارة", cboDepartment)
        AddRow(table, "اسم القسم", txtName)
        AddRow(table, "ملاحظات", txtNotes)

        chkActive.Text = "القسم مفعّل"
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

    Private Sub FillDepartments()
        cboDepartment.DisplayMember = "Name"
        cboDepartment.ValueMember = "Id"
        cboDepartment.DataSource = New List(Of HrDepartmentSummary)(_departments)
    End Sub

    Private Sub Fill(section As HrSectionSummary)
        txtName.Text = section.Name
        txtNotes.Text = section.Notes
        chkActive.Checked = section.IsActive
        For i As Integer = 0 To cboDepartment.Items.Count - 1
            Dim department = DirectCast(cboDepartment.Items(i), HrDepartmentSummary)
            If department.Id = section.DepartmentId Then
                cboDepartment.SelectedIndex = i
                Exit For
            End If
        Next
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            If cboDepartment.SelectedItem Is Nothing Then
                UiTheme.Warn("اختر الإدارة أولاً.")
                Return
            End If
            Dim id As Long? = Nothing
            If _section IsNot Nothing Then id = _section.Id
            Dim department = DirectCast(cboDepartment.SelectedItem, HrDepartmentSummary)
            HrSectionService.Save(_session, id, department.Id, txtName.Text.Trim(), txtNotes.Text.Trim(),
                                  chkActive.Checked)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
