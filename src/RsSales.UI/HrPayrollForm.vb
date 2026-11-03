Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class HrPayrollForm
    Inherits SectionFormBase

    Private _runs As List(Of HrPayrollRunSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "صرف الرواتب",
                   "إنشاء مسيرات الرواتب الشهرية واحتساب العمولات والخصومات والمكافآت",
                   Color.FromArgb(230, 126, 34), New Size(1120, 650))

        AddSearch("ملاحظات المسير أو الشهر (YYYY-MM)", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("مسير جديد", 120)
        AddHandler btnNew.Click, Sub(s, e) GenerateRun()
        ToolBar.Controls.Add(btnNew)

        Dim btnDetails As Button = UiTheme.MakeButton("تفاصيل المسير", 125)
        AddHandler btnDetails.Click, Sub(s, e) ShowDetails()
        ToolBar.Controls.Add(btnDetails)

        Dim btnPost As Button = UiTheme.MakeButton("صرف المسير", 115)
        AddHandler btnPost.Click, Sub(s, e) PostSelected()
        ToolBar.Controls.Add(btnPost)

        Dim btnDelete As Button = UiTheme.MakeButton("حذف", 90)
        AddHandler btnDelete.Click, Sub(s, e) DeleteSelected()
        ToolBar.Controls.Add(btnDelete)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "PeriodText", "الشهر", 125)
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 90)
        UiTheme.AddTextColumn(Grid, "EmployeeCount", "الموظفون", 95, False, "N0")
        UiTheme.AddTextColumn(Grid, "TotalBase", "الرواتب", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalCommission", "العمولات", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalDeductions", "الخصومات", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalBonus", "المكافآت", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalNet", "صافي الصرف", 125, False, "N2")
        UiTheme.AddTextColumn(Grid, "CreatedAt", "تاريخ الإنشاء", 130, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "CreatedByName", "أنشأه", 140, True)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then ShowDetails()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _runs = HrPayrollService.Runs(Session, SearchText)
        Grid.DataSource = New BindingSource With {.DataSource = _runs}

        Dim draftCount As Integer = 0
        Dim paidCount As Integer = 0
        Dim totalNet As Decimal = 0D
        For Each run In _runs
            If run.Status = "paid" Then
                paidCount += 1
            ElseIf run.Status = "draft" Then
                draftCount += 1
            End If
            totalNet += run.TotalNet
        Next
        SetFooter("عدد المسيرات: " & _runs.Count.ToString() &
                  "   |   مسودة: " & draftCount.ToString() &
                  "   |   مصروف: " & paidCount.ToString() &
                  "   |   إجمالي الصافي: " & UiTheme.Money(totalNet))
    End Sub

    Private Sub GenerateRun()
        Using dialog As New HrPayrollGenerateForm(Session)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ShowDetails()
        Dim run = SelectedItem(Of HrPayrollRunSummary)()
        If run Is Nothing Then
            UiTheme.Warn("اختر مسير رواتب أولاً.")
            Return
        End If
        Using dialog As New HrPayrollDetailsForm(Session, run.Id)
            dialog.ShowDialog(Me)
        End Using
        Reload()
    End Sub

    Private Sub PostSelected()
        Dim run = SelectedItem(Of HrPayrollRunSummary)()
        If run Is Nothing Then
            UiTheme.Warn("اختر مسير رواتب أولاً.")
            Return
        End If
        If run.Status <> "draft" Then
            UiTheme.Warn("لا يمكن صرف إلا مسير بحالة مسودة.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد صرف مسير «" & run.PeriodText & "» بصافي " &
                               UiTheme.Money(run.TotalNet) & "؟") Then Return
        Try
            HrPayrollService.Post(Session, run.Id)
            UiTheme.Info("تم صرف المسير بنجاح.")
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub DeleteSelected()
        Dim run = SelectedItem(Of HrPayrollRunSummary)()
        If run Is Nothing Then
            UiTheme.Warn("اختر مسير رواتب أولاً.")
            Return
        End If
        If run.Status <> "draft" Then
            UiTheme.Warn("لا يمكن حذف إلا مسير بحالة مسودة.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد حذف مسير «" & run.PeriodText & "» وكل بنوده؟") Then Return
        Try
            HrPayrollService.DeleteRun(Session, run.Id)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrPayrollGenerateForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly cboMonth As New ComboBox()
    Private ReadOnly numYear As New NumericUpDown()
    Private ReadOnly txtNotes As New TextBox()

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "مسير رواتب جديد"
        ClientSize = New Size(520, 400)
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

        For i As Integer = 1 To 12
            cboMonth.Items.Add(HrArabicText.MonthName(i))
        Next
        cboMonth.DropDownStyle = ComboBoxStyle.DropDownList
        Dim today As Date = Date.Today
        cboMonth.SelectedIndex = today.Month - 1

        numYear.Minimum = 2000D
        numYear.Maximum = 2100D
        numYear.Value = today.Year

        AddRow(table, "السنة", numYear)
        AddRow(table, "الشهر", cboMonth)
        AddRow(table, "ملاحظات", txtNotes)

        Dim hint As New Label With {
            .Text = "يتم احتساب عمولات البائعين والدليفري تلقائيًا من فواتير البيع المرحّلة خلال الشهر. يمكنك تعديل الخصومات والمكافآت لكل بند بعد الإنشاء.",
            .Dock = DockStyle.Fill,
            .ForeColor = UiTheme.Muted,
            .Font = UiTheme.Font(9.0F),
            .Padding = New Padding(4, 8, 4, 4)
        }
        Dim hintRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 70))
        table.Controls.Add(New Label(), 0, hintRow)
        table.Controls.Add(hint, 1, hintRow)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("إنشاء المسير", 130)
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
            HrPayrollService.Generate(_session, CInt(numYear.Value), cboMonth.SelectedIndex + 1,
                                      txtNotes.Text.Trim())
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrPayrollDetailsForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _runId As Long
    Private ReadOnly headerLabel As New Label()
    Private ReadOnly footerLabel As New Label()
    Private ReadOnly toolbar As New FlowLayoutPanel()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly btnEdit As New Button()
    Private ReadOnly btnPost As New Button()

    Private _run As HrPayrollRunSummary
    Private _lines As List(Of HrPayrollLineSummary)

    Public Sub New(session As AppSession, runId As Long)
        _session = session
        _runId = runId
        InitializeUi()
        LoadData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "تفاصيل مسير الرواتب"
        ClientSize = New Size(1080, 640)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False

        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 74,
            .BackColor = Color.FromArgb(230, 126, 34),
            .Padding = New Padding(18, 9, 18, 8)
        }
        headerLabel.Dock = DockStyle.Fill
        headerLabel.ForeColor = Color.White
        headerLabel.Font = UiTheme.Font(11.5F, FontStyle.Bold)
        headerLabel.TextAlign = ContentAlignment.MiddleRight
        header.Controls.Add(headerLabel)

        toolbar.Dock = DockStyle.Top
        toolbar.Height = 52
        toolbar.FlowDirection = FlowDirection.LeftToRight
        toolbar.WrapContents = False
        toolbar.BackColor = UiTheme.Surface
        toolbar.Padding = New Padding(6)

        btnEdit.Text = "تعديل بند"
        btnEdit.Width = 110
        btnEdit.Height = 34
        btnEdit.FlatStyle = FlatStyle.Flat
        btnEdit.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        AddHandler btnEdit.Click, Sub(s, e) EditLine()
        toolbar.Controls.Add(btnEdit)

        btnPost.Text = "صرف المسير"
        btnPost.Width = 115
        btnPost.Height = 34
        btnPost.FlatStyle = FlatStyle.Flat
        btnPost.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        btnPost.BackColor = UiTheme.Accent
        btnPost.ForeColor = Color.White
        AddHandler btnPost.Click, Sub(s, e) PostRun()
        toolbar.Controls.Add(btnPost)

        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 90)
        AddHandler btnRefresh.Click, Sub(s, e) LoadData()
        toolbar.Controls.Add(btnRefresh)

        Dim btnClose As Button = UiTheme.MakeButton("إغلاق", 90)
        AddHandler btnClose.Click, Sub(s, e) Close()
        toolbar.Controls.Add(btnClose)

        footerLabel.Dock = DockStyle.Bottom
        footerLabel.Height = 32
        footerLabel.TextAlign = ContentAlignment.MiddleRight
        footerLabel.Padding = New Padding(8, 0, 8, 0)
        footerLabel.ForeColor = UiTheme.Muted
        footerLabel.Font = UiTheme.Font(9.5F, FontStyle.Bold)

        UiTheme.AddTextColumn(grid, "EmployeeCode", "الكود", 100)
        UiTheme.AddTextColumn(grid, "EmployeeName", "الموظف", 200, True)
        UiTheme.AddTextColumn(grid, "TypeName", "النوع", 85)
        UiTheme.AddTextColumn(grid, "DepartmentName", "الإدارة", 140)
        UiTheme.AddTextColumn(grid, "BaseSalary", "الراتب", 105, False, "N2")
        UiTheme.AddTextColumn(grid, "CommissionAmount", "العمولة", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "Deductions", "الخصومات", 100, False, "N2")
        UiTheme.AddTextColumn(grid, "Bonus", "المكافأة", 100, False, "N2")
        UiTheme.AddTextColumn(grid, "NetAmount", "الصافي", 115, False, "N2")
        UiTheme.AddTextColumn(grid, "Notes", "ملاحظات", 150)
        AddHandler grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditLine()

        Controls.Add(grid)
        Controls.Add(footerLabel)
        Controls.Add(toolbar)
        Controls.Add(header)
    End Sub

    Private Sub LoadData()
        Try
            _run = HrPayrollService.GetRun(_session, _runId)
            If _run Is Nothing Then
                UiTheme.Warn("المسير غير موجود.")
                Close()
                Return
            End If
            _lines = HrPayrollService.Lines(_session, _runId)
            grid.DataSource = New BindingSource With {.DataSource = _lines}
            headerLabel.Text = "مسير " & _run.PeriodText & "   |   " & _run.StatusName &
                               "   |   الموظفون: " & _run.EmployeeCount.ToString() &
                               "   |   صافي الصرف: " & UiTheme.Money(_run.TotalNet)
            footerLabel.Text = "الرواتب: " & UiTheme.Money(_run.TotalBase) &
                               "   |   العمولات: " & UiTheme.Money(_run.TotalCommission) &
                               "   |   الخصومات: " & UiTheme.Money(_run.TotalDeductions) &
                               "   |   المكافآت: " & UiTheme.Money(_run.TotalBonus) &
                               "   |   الصافي: " & UiTheme.Money(_run.TotalNet)
            Dim editable As Boolean = (_run.Status = "draft")
            btnEdit.Enabled = editable
            btnPost.Enabled = editable
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub EditLine()
        If _run Is Nothing OrElse _run.Status <> "draft" Then
            UiTheme.Warn("لا يمكن تعديل بنود مسير تم صرفه.")
            Return
        End If
        If grid.CurrentRow Is Nothing Then
            UiTheme.Warn("اختر بندًا أولاً.")
            Return
        End If
        Dim line = TryCast(grid.CurrentRow.DataBoundItem, HrPayrollLineSummary)
        If line Is Nothing Then Return
        Using dialog As New HrPayrollLineEditForm(_session, line)
            If dialog.ShowDialog(Me) = DialogResult.OK Then LoadData()
        End Using
    End Sub

    Private Sub PostRun()
        If _run Is Nothing Then Return
        If _run.Status <> "draft" Then
            UiTheme.Warn("تم صرف هذا المسير بالفعل.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد صرف مسير " & _run.PeriodText & " بصافي " &
                               UiTheme.Money(_run.TotalNet) & "؟") Then Return
        Try
            HrPayrollService.Post(_session, _runId)
            UiTheme.Info("تم صرف المسير بنجاح.")
            LoadData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class HrPayrollLineEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _line As HrPayrollLineSummary
    Private ReadOnly lblEmployee As New Label()
    Private ReadOnly lblBase As New Label()
    Private ReadOnly lblCommission As New Label()
    Private ReadOnly lblNet As New Label()
    Private ReadOnly txtDeductions As New TextBox()
    Private ReadOnly txtBonus As New TextBox()
    Private ReadOnly txtNotes As New TextBox()

    Public Sub New(session As AppSession, line As HrPayrollLineSummary)
        _session = session
        _line = line
        InitializeUi()
        Fill(line)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "تعديل بند الراتب"
        ClientSize = New Size(560, 480)
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

        lblEmployee.AutoSize = True
        lblBase.AutoSize = True
        lblCommission.AutoSize = True
        lblNet.AutoSize = True
        lblNet.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        lblNet.ForeColor = UiTheme.Success

        AddRow(table, "الموظف", lblEmployee)
        AddRow(table, "الكود", New Label With {.Text = _line.EmployeeCode, .AutoSize = True})
        AddRow(table, "النوع", New Label With {.Text = _line.TypeName, .AutoSize = True})
        AddRow(table, "الراتب الأساسي", lblBase)
        AddRow(table, "العمولة المحتسبة", lblCommission)
        AddRow(table, "الخصومات", txtDeductions)
        AddRow(table, "المكافأة", txtBonus)
        AddRow(table, "الصافي", lblNet)
        AddRow(table, "ملاحظات", txtNotes)

        AddHandler txtDeductions.TextChanged, AddressOf RecalculateNet
        AddHandler txtBonus.TextChanged, AddressOf RecalculateNet

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

    Private Sub Fill(line As HrPayrollLineSummary)
        lblEmployee.Text = line.EmployeeName
        lblBase.Text = UiTheme.Money(line.BaseSalary)
        lblCommission.Text = UiTheme.Money(line.CommissionAmount)
        txtDeductions.Text = line.Deductions.ToString("0.00")
        txtBonus.Text = line.Bonus.ToString("0.00")
        txtNotes.Text = line.Notes
        RecalculateNet()
    End Sub

    Private Sub RecalculateNet(Optional sender As Object = Nothing, Optional e As EventArgs = Nothing)
        Dim net As Decimal = _line.BaseSalary + _line.CommissionAmount +
                             UiTheme.ToMoney(txtBonus.Text) - UiTheme.ToMoney(txtDeductions.Text)
        lblNet.Text = UiTheme.Money(net)
        lblNet.ForeColor = If(net < 0D, UiTheme.Danger, UiTheme.Success)
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            HrPayrollService.UpdateLine(_session, _line.Id, UiTheme.ToMoney(txtDeductions.Text),
                                        UiTheme.ToMoney(txtBonus.Text), txtNotes.Text.Trim())
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
