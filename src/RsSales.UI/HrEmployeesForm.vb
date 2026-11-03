Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class HrEmployeesForm
    Inherits SectionFormBase

    Private ReadOnly _employeeType As String
    Private _employees As List(Of HrEmployeeSummary)

    Public Sub New(session As AppSession, employeeType As String)
        MyBase.New(session, TitleFor(employeeType), SubtitleFor(employeeType), AccentFor(employeeType),
                   New Size(1100, 640))
        _employeeType = employeeType

        AddSearch("الاسم أو الكود أو الموبايل", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton(NewButtonText(employeeType), 140)
        AddHandler btnNew.Click, Sub(s, e) EditEmployee(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 95)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 125)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Code", "الكود", 100)
        UiTheme.AddTextColumn(Grid, "Name", "الاسم", 220, True)
        UiTheme.AddTextColumn(Grid, "DepartmentName", "الإدارة", 150)
        UiTheme.AddTextColumn(Grid, "SectionName", "القسم", 130)
        UiTheme.AddTextColumn(Grid, "Mobile", "الموبايل", 120)
        UiTheme.AddTextColumn(Grid, "BaseSalary", "الراتب الأساسي", 120, False, "N2")
        If employeeType = "seller" Then
            UiTheme.AddTextColumn(Grid, "CommissionPercent", "نسبة العمولة %", 125, False, "N2")
        ElseIf employeeType = "delivery" Then
            UiTheme.AddTextColumn(Grid, "DeliveryFee", "عمولة التوصيل", 125, False, "N2")
        End If
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 90)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        Reload()
    End Sub

    Friend Shared Function TitleFor(employeeType As String) As String
        Select Case employeeType
            Case "seller" : Return "تعريف بائعين"
            Case "delivery" : Return "تعريف دليفري"
            Case Else : Return "تعريف الموظف"
        End Select
    End Function

    Private Shared Function SubtitleFor(employeeType As String) As String
        Select Case employeeType
            Case "seller"
                Return "بيانات البائعين ونسب عمولاتهم على فواتير البيع المرحّلة"
            Case "delivery"
                Return "بيانات مندوبي التوصيل وقيمة عمولة كل فاتورة مسلّمة"
            Case Else
                Return "بيانات الموظفين والرواتب الأساسية والإدارات والأقسام"
        End Select
    End Function

    Private Shared Function AccentFor(employeeType As String) As Color
        Select Case employeeType
            Case "seller" : Return Color.FromArgb(230, 126, 34)
            Case "delivery" : Return Color.FromArgb(41, 128, 185)
            Case Else : Return Color.FromArgb(41, 128, 185)
        End Select
    End Function

    Private Shared Function NewButtonText(employeeType As String) As String
        Select Case employeeType
            Case "seller" : Return "بائع جديد"
            Case "delivery" : Return "دليفري جديد"
            Case Else : Return "موظف جديد"
        End Select
    End Function

    Protected Overrides Sub LoadData()
        _employees = HrEmployeeService.Search(Session, SearchText, _employeeType, True)
        Grid.DataSource = New BindingSource With {.DataSource = _employees}

        Dim activeCount As Integer = 0
        Dim salaryTotal As Decimal = 0D
        For Each employee In _employees
            If employee.IsActive Then activeCount += 1
            salaryTotal += employee.BaseSalary
        Next
        SetFooter("عدد السجلات: " & _employees.Count.ToString() &
                  "   |   المفعّل: " & activeCount.ToString() &
                  "   |   إجمالي الرواتب الأساسية: " & UiTheme.Money(salaryTotal))
    End Sub

    Private Sub EditSelected()
        Dim employee = SelectedItem(Of HrEmployeeSummary)()
        If employee Is Nothing Then
            UiTheme.Warn("اختر سجلاً أولاً.")
            Return
        End If
        EditEmployee(employee)
    End Sub

    Private Sub EditEmployee(employee As HrEmployeeSummary)
        Using dialog As New HrEmployeeEditForm(Session, _employeeType, employee)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim employee = SelectedItem(Of HrEmployeeSummary)()
        If employee Is Nothing Then
            UiTheme.Warn("اختر سجلاً أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(employee.IsActive, "إيقاف", "تفعيل") &
                               " «" & employee.Name & "»؟") Then Return
        Try
            HrEmployeeService.SetActive(Session, employee.Id, Not employee.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrEmployeeEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _employeeType As String
    Private ReadOnly _employee As HrEmployeeSummary
    Private ReadOnly _departments As List(Of HrDepartmentSummary)

    Private ReadOnly txtCode As New TextBox()
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly cboDepartment As New ComboBox()
    Private ReadOnly cboSection As New ComboBox()
    Private ReadOnly txtMobile As New TextBox()
    Private ReadOnly txtPhone As New TextBox()
    Private ReadOnly txtEmail As New TextBox()
    Private ReadOnly txtAddress As New TextBox()
    Private ReadOnly dtpHireDate As New DateTimePicker()
    Private ReadOnly txtBaseSalary As New TextBox()
    Private ReadOnly txtCommission As New TextBox()
    Private ReadOnly txtDeliveryFee As New TextBox()
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly chkActive As New CheckBox()

    Private _loadingDepartments As Boolean

    Public Sub New(session As AppSession, employeeType As String, employee As HrEmployeeSummary)
        _session = session
        _employeeType = employeeType
        _employee = employee
        _departments = HrDepartmentService.Search(session, String.Empty, True)
        InitializeUi()
        FillDepartments()
        If employee IsNot Nothing Then Fill(employee)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_employee Is Nothing, "سجل جديد - " & HrEmployeesForm.TitleFor(_employeeType),
                  "تعديل - " & HrEmployeesForm.TitleFor(_employeeType))
        ClientSize = New Size(620, 680)
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

        txtCode.PlaceholderText = "يُولَّد تلقائيًا إذا تُرك فارغًا"
        dtpHireDate.Format = DateTimePickerFormat.Short
        dtpHireDate.ShowCheckBox = True
        dtpHireDate.Checked = False

        AddRow(table, "الكود", txtCode)
        AddRow(table, "الاسم", txtName)
        AddRow(table, "الإدارة", cboDepartment)
        AddRow(table, "القسم", cboSection)
        AddRow(table, "الموبايل", txtMobile)
        AddRow(table, "الهاتف", txtPhone)
        AddRow(table, "البريد الإلكتروني", txtEmail)
        AddRow(table, "العنوان", txtAddress)
        AddRow(table, "تاريخ التعيين", dtpHireDate)
        AddRow(table, "الراتب الأساسي", txtBaseSalary)
        If _employeeType = "seller" Then
            AddRow(table, "نسبة العمولة %", txtCommission)
        ElseIf _employeeType = "delivery" Then
            AddRow(table, "عمولة التوصيل للفاتورة", txtDeliveryFee)
        End If
        AddRow(table, "ملاحظات", txtNotes)

        chkActive.Text = "السجل مفعّل"
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
        _loadingDepartments = True
        Dim items As New List(Of HrDepartmentSummary)()
        items.Add(New HrDepartmentSummary With {.Id = 0, .Name = "بدون إدارة"})
        items.AddRange(_departments)
        cboDepartment.DisplayMember = "Name"
        cboDepartment.ValueMember = "Id"
        cboDepartment.DataSource = items
        AddHandler cboDepartment.SelectedIndexChanged, AddressOf DepartmentChanged
        _loadingDepartments = False
        LoadSections(0, 0)
    End Sub

    Private Sub DepartmentChanged(sender As Object, e As EventArgs)
        If _loadingDepartments Then Return
        LoadSections(SelectedId(cboDepartment), 0)
    End Sub

    Private Sub LoadSections(departmentId As Long, selectedSectionId As Long)
        Dim items As New List(Of HrSectionSummary)()
        items.Add(New HrSectionSummary With {.Id = 0, .Name = "بدون قسم"})
        If departmentId > 0 Then
            Try
                items.AddRange(HrSectionService.ListForDepartment(_session, departmentId, True))
            Catch ex As Exception
                UiTheme.Handle(ex)
            End Try
        End If
        cboSection.DisplayMember = "Name"
        cboSection.ValueMember = "Id"
        cboSection.DataSource = items
        SelectById(cboSection, selectedSectionId)
    End Sub

    Private Sub Fill(employee As HrEmployeeSummary)
        txtCode.Text = employee.Code
        txtName.Text = employee.Name
        txtMobile.Text = employee.Mobile
        txtPhone.Text = employee.Phone
        txtEmail.Text = employee.Email
        txtAddress.Text = employee.Address
        txtBaseSalary.Text = employee.BaseSalary.ToString("0.00")
        txtCommission.Text = employee.CommissionPercent.ToString("0.00")
        txtDeliveryFee.Text = employee.DeliveryFee.ToString("0.00")
        txtNotes.Text = employee.Notes
        chkActive.Checked = employee.IsActive
        If employee.HireDate.HasValue Then
            dtpHireDate.Value = employee.HireDate.Value
            dtpHireDate.Checked = True
        End If

        _loadingDepartments = True
        SelectById(cboDepartment, If(employee.DepartmentId.HasValue, employee.DepartmentId.Value, 0L))
        _loadingDepartments = False
        LoadSections(If(employee.DepartmentId.HasValue, employee.DepartmentId.Value, 0L),
                     If(employee.SectionId.HasValue, employee.SectionId.Value, 0L))
    End Sub

    Private Shared Function SelectedId(combo As ComboBox) As Long
        If combo.SelectedItem Is Nothing Then Return 0
        If TypeOf combo.SelectedItem Is HrDepartmentSummary Then
            Return DirectCast(combo.SelectedItem, HrDepartmentSummary).Id
        End If
        If TypeOf combo.SelectedItem Is HrSectionSummary Then
            Return DirectCast(combo.SelectedItem, HrSectionSummary).Id
        End If
        Return 0
    End Function

    Private Shared Sub SelectById(combo As ComboBox, id As Long)
        For i As Integer = 0 To combo.Items.Count - 1
            Dim itemId As Long
            If TypeOf combo.Items(i) Is HrDepartmentSummary Then
                itemId = DirectCast(combo.Items(i), HrDepartmentSummary).Id
            ElseIf TypeOf combo.Items(i) Is HrSectionSummary Then
                itemId = DirectCast(combo.Items(i), HrSectionSummary).Id
            Else
                Continue For
            End If
            If itemId = id Then
                combo.SelectedIndex = i
                Return
            End If
        Next
        If combo.Items.Count > 0 Then combo.SelectedIndex = 0
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim id As Long? = Nothing
            If _employee IsNot Nothing Then id = _employee.Id

            Dim departmentId As Long? = Nothing
            Dim selectedDepartment As Long = SelectedId(cboDepartment)
            If selectedDepartment > 0 Then departmentId = selectedDepartment

            Dim sectionId As Long? = Nothing
            Dim selectedSection As Long = SelectedId(cboSection)
            If selectedSection > 0 Then sectionId = selectedSection

            Dim hireDate As Date? = Nothing
            If dtpHireDate.Checked Then hireDate = dtpHireDate.Value.Date

            Dim commissionPercent As Decimal = 0D
            If _employeeType = "seller" Then commissionPercent = UiTheme.ToMoney(txtCommission.Text)
            Dim deliveryFee As Decimal = 0D
            If _employeeType = "delivery" Then deliveryFee = UiTheme.ToMoney(txtDeliveryFee.Text)

            HrEmployeeService.Save(_session, id, txtCode.Text.Trim(), txtName.Text.Trim(), _employeeType,
                                   departmentId, sectionId, txtMobile.Text.Trim(), txtPhone.Text.Trim(),
                                   txtEmail.Text.Trim(), txtAddress.Text.Trim(), hireDate,
                                   UiTheme.ToMoney(txtBaseSalary.Text), commissionPercent, deliveryFee,
                                   txtNotes.Text.Trim(), chkActive.Checked)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
