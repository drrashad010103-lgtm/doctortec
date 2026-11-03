Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>Shift history for the branch with open/close and cash reconciliation.</summary>
Public Class ShiftsForm
    Inherits SectionFormBase

    Private ReadOnly _fromDate As New DateTimePicker()
    Private ReadOnly _toDate As New DateTimePicker()
    Private ReadOnly _chkMine As New CheckBox()
    Private ReadOnly _lblCurrent As New Label()
    Private _rows As New List(Of ShiftRecord)()

    Public Sub New(session As AppSession)
        MyBase.New(session, "الورديات",
                   "فتح وإغلاق ورديات الخزينة ومطابقة النقدية المتوقعة مع المبلغ المعدود فعليًا",
                   UiTheme.Teal, New Size(1180, 650))

        _fromDate.Format = DateTimePickerFormat.Custom
        _fromDate.CustomFormat = "yyyy-MM-dd"
        _fromDate.Value = Date.Today.AddDays(-30)
        _fromDate.Width = 118
        _toDate.Format = DateTimePickerFormat.Custom
        _toDate.CustomFormat = "yyyy-MM-dd"
        _toDate.Value = Date.Today
        _toDate.Width = 118

        ToolBar.Controls.Add(UiTheme.MakeLabel("من:"))
        ToolBar.Controls.Add(_fromDate)
        ToolBar.Controls.Add(UiTheme.MakeLabel("إلى:"))
        ToolBar.Controls.Add(_toDate)

        _chkMine.Text = "ورديتي فقط"
        _chkMine.Width = 110
        _chkMine.TextAlign = ContentAlignment.MiddleRight
        AddHandler _chkMine.CheckedChanged, Sub(s, e) Reload()
        ToolBar.Controls.Add(_chkMine)

        Dim btnOpen As Button = UiTheme.PrimaryButton("فتح وردية", 120)
        AddHandler btnOpen.Click, Sub(s, e) OpenShift()
        ToolBar.Controls.Add(btnOpen)

        Dim btnClose As Button = UiTheme.MakeButton("إغلاق الوردية", 135)
        AddHandler btnClose.Click, Sub(s, e) CloseSelected()
        ToolBar.Controls.Add(btnClose)

        AddRefresh(AddressOf Reload)

        _lblCurrent.AutoSize = True
        _lblCurrent.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        _lblCurrent.Padding = New Padding(8, 8, 0, 0)
        ToolBar.Controls.Add(_lblCurrent)

        UiTheme.AddTextColumn(Grid, "ShiftNo", "رقم الوردية", 150)
        UiTheme.AddTextColumn(Grid, "UserName", "الكاشير", 170)
        UiTheme.AddTextColumn(Grid, "OpenedAt", "فتح", 135, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(Grid, "ClosedAt", "إغلاق", 135, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(Grid, "OpeningCash", "نقدية افتتاحية", 115, False, "N2")
        UiTheme.AddTextColumn(Grid, "ExpectedCash", "المتوقع", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "CountedCash", "المعدود", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "Variance", "الفرق", 100, False, "N2")
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 95)
        UiTheme.AddTextColumn(Grid, "Notes", "ملاحظات", 160, True)

        AddHandler Grid.CellFormatting, AddressOf FormatRow
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then CloseSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _rows = ShiftService.List(Session, _fromDate.Value.Date, _toDate.Value.Date, _chkMine.Checked)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        Dim openCount As Integer = _rows.Where(Function(r) r.Status = "open").Count()
        Dim variance As Decimal = _rows.Where(Function(r) r.Variance.HasValue).Sum(Function(r) r.Variance.Value)
        SetFooter("الورديات: " & _rows.Count.ToString() &
                  "   |   المفتوحة: " & openCount.ToString() &
                  "   |   إجمالي الفروق: " & UiTheme.Money(variance))

        RefreshCurrent()
    End Sub

    Private Sub RefreshCurrent()
        Try
            Dim current = ShiftService.Current(Session)
            If current Is Nothing Then
                _lblCurrent.Text = "لا توجد وردية مفتوحة لك"
                _lblCurrent.ForeColor = UiTheme.Danger
            Else
                Dim expected = ShiftService.ExpectedCash(Session, current.Id)
                _lblCurrent.Text = "ورديتك: " & current.ShiftNo & "  •  المتوقع: " & UiTheme.Money(expected)
                _lblCurrent.ForeColor = UiTheme.Success
            End If
        Catch
            _lblCurrent.Text = String.Empty
        End Try
    End Sub

    Private Sub FormatRow(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.RowIndex >= _rows.Count Then Return
        Dim row = _rows(e.RowIndex)
        Dim columnName As String = Grid.Columns(e.ColumnIndex).Name
        If columnName = "Variance" AndAlso row.Variance.HasValue AndAlso row.Variance.Value <> 0D Then
            e.CellStyle.ForeColor = UiTheme.Danger
            e.CellStyle.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        ElseIf columnName = "StatusName" Then
            e.CellStyle.ForeColor = If(row.Status = "open", UiTheme.Success, UiTheme.Muted)
            e.CellStyle.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        End If
    End Sub

    Private Sub OpenShift()
        If Not Session.HasPermission("shifts.open") Then
            UiTheme.Warn("ليس لديك صلاحية فتح وردية.")
            Return
        End If
        Dim current = ShiftService.Current(Session)
        If current IsNot Nothing Then
            UiTheme.Warn("لديك وردية مفتوحة بالفعل: " & current.ShiftNo)
            Return
        End If

        Using dialog As New ShiftOpenForm()
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim shift = ShiftService.Open(Session, dialog.OpeningCash)
                UiTheme.Info("تم فتح الوردية " & shift.ShiftNo & " بنقدية افتتاحية " &
                             UiTheme.Money(shift.OpeningCash) & ".")
                Reload()
            Catch ex As Exception
                UiTheme.Handle(ex)
            End Try
        End Using
    End Sub

    Private Sub CloseSelected()
        Dim row = SelectedItem(Of ShiftRecord)()
        If row Is Nothing Then
            UiTheme.Warn("اختر وردية من القائمة أولًا.")
            Return
        End If
        If row.Status <> "open" Then
            UiTheme.Warn("هذه الوردية مغلقة بالفعل.")
            Return
        End If
        If Not Session.HasPermission("shifts.close") Then
            UiTheme.Warn("ليس لديك صلاحية إغلاق الوردية.")
            Return
        End If

        Try
            Dim expected = ShiftService.ExpectedCash(Session, row.Id)
            Using dialog As New ShiftCloseForm(row, expected)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim variance = ShiftService.Close(Session, row.Id, dialog.CountedCash, dialog.Notes)
                Dim message As String = "تم إغلاق الوردية " & row.ShiftNo & "." & Environment.NewLine &
                                        "المتوقع: " & UiTheme.Money(expected) & Environment.NewLine &
                                        "المعدود: " & UiTheme.Money(dialog.CountedCash) & Environment.NewLine &
                                        "الفرق: " & UiTheme.Money(variance)
                If variance = 0D Then
                    UiTheme.Info(message)
                Else
                    UiTheme.Warn(message)
                End If
                Reload()
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

''' <summary>Asks for the opening cash before opening a new shift.</summary>
Public Class ShiftOpenForm
    Inherits Form

    Private ReadOnly _txtCash As New TextBox()
    Private _openingCash As Decimal

    Public ReadOnly Property OpeningCash As Decimal
        Get
            Return _openingCash
        End Get
    End Property

    Public Sub New()
        UiTheme.Apply(Me)
        Text = "فتح وردية"
        ClientSize = New Size(420, 230)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim title As New Label With {
            .Text = "فتح وردية جديدة", .Font = UiTheme.Font(13.0F, FontStyle.Bold),
            .Dock = DockStyle.Top, .Height = 48, .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = UiTheme.Teal, .ForeColor = Color.White
        }
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(20)}
        Dim caption As New Label With {
            .Text = "النقدية الافتتاحية (ج.م):", .AutoSize = True, .Location = New Point(20, 22),
            .Font = UiTheme.Font(10.0F, FontStyle.Bold)
        }
        _txtCash.Location = New Point(20, 50)
        _txtCash.Width = 360
        _txtCash.Text = "0.00"
        _txtCash.TextAlign = HorizontalAlignment.Left
        _txtCash.Font = UiTheme.Font(12.0F, FontStyle.Bold)

        Dim ok As Button = UiTheme.PrimaryButton("فتح الوردية", 130)
        ok.Location = New Point(250, 104)
        AddHandler ok.Click, Sub(s, e)
                                 Dim cash As Decimal = UiTheme.ToMoney(_txtCash.Text)
                                 If cash < 0D Then
                                     UiTheme.Warn("النقدية الافتتاحية لا يمكن أن تكون سالبة.")
                                     Return
                                 End If
                                 _openingCash = cash
                                 DialogResult = DialogResult.OK
                             End Sub
        Dim cancel As Button = UiTheme.MakeButton("إلغاء", 90)
        cancel.Location = New Point(150, 104)
        AddHandler cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        body.Controls.Add(caption)
        body.Controls.Add(_txtCash)
        body.Controls.Add(ok)
        body.Controls.Add(cancel)
        Controls.Add(body)
        Controls.Add(title)
        AcceptButton = ok
        CancelButton = cancel
    End Sub
End Class

''' <summary>Counts the drawer, shows the variance live and closes the shift.</summary>
Public Class ShiftCloseForm
    Inherits Form

    Private ReadOnly _shift As ShiftRecord
    Private ReadOnly _expected As Decimal
    Private ReadOnly _txtCounted As New TextBox()
    Private ReadOnly _lblVariance As New Label()
    Private ReadOnly _txtNotes As New TextBox()
    Private _countedCash As Decimal
    Private _notes As String

    Public ReadOnly Property CountedCash As Decimal
        Get
            Return _countedCash
        End Get
    End Property

    Public ReadOnly Property Notes As String
        Get
            Return _notes
        End Get
    End Property

    Public Sub New(shift As ShiftRecord, expectedCash As Decimal)
        _shift = shift
        _expected = expectedCash
        UiTheme.Apply(Me)
        Text = "إغلاق وردية " & shift.ShiftNo
        ClientSize = New Size(500, 390)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim title As New Label With {
            .Text = "إغلاق الوردية " & shift.ShiftNo, .Font = UiTheme.Font(13.0F, FontStyle.Bold),
            .Dock = DockStyle.Top, .Height = 48, .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = UiTheme.Navy, .ForeColor = Color.White
        }
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(24)}
        Dim info As New Label With {
            .Text = "الكاشير: " & shift.UserName & Environment.NewLine &
                    "فتحت: " & shift.OpenedAt.ToString("yyyy-MM-dd HH:mm") & Environment.NewLine &
                    "الافتتاحي: " & UiTheme.Money(shift.OpeningCash) & Environment.NewLine &
                    "النقدية المتوقعة: " & UiTheme.Money(_expected),
            .Location = New Point(24, 18), .Size = New Size(440, 92),
            .Font = UiTheme.Font(10.0F), .ForeColor = UiTheme.Ink
        }

        Dim lblCounted As New Label With {
            .Text = "النقدية المعدودة فعليًا (ج.م):", .AutoSize = True,
            .Location = New Point(24, 120), .Font = UiTheme.Font(10.0F, FontStyle.Bold)
        }
        _txtCounted.Location = New Point(24, 148)
        _txtCounted.Width = 210
        _txtCounted.Font = UiTheme.Font(12.0F, FontStyle.Bold)
        _txtCounted.TextAlign = HorizontalAlignment.Left
        _txtCounted.Text = expectedCash.ToString("0.00")
        AddHandler _txtCounted.TextChanged, Sub(s, e) UpdateVariance()

        _lblVariance.Location = New Point(250, 143)
        _lblVariance.Size = New Size(225, 32)
        _lblVariance.TextAlign = ContentAlignment.MiddleCenter
        _lblVariance.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        _lblVariance.BorderStyle = BorderStyle.FixedSingle

        Dim lblNotes As New Label With {
            .Text = "ملاحظات:", .AutoSize = True,
            .Location = New Point(24, 192), .Font = UiTheme.Font(10.0F, FontStyle.Bold)
        }
        _txtNotes.Location = New Point(24, 218)
        _txtNotes.Width = 450
        _txtNotes.Height = 56
        _txtNotes.Multiline = True
        _txtNotes.ScrollBars = ScrollBars.Vertical

        Dim ok As Button = UiTheme.PrimaryButton("تأكيد الإغلاق", 140)
        ok.Location = New Point(334, 292)
        AddHandler ok.Click, Sub(s, e)
                                 Dim counted As Decimal = UiTheme.ToMoney(_txtCounted.Text)
                                 If counted < 0D Then
                                     UiTheme.Warn("النقدية المعدودة لا يمكن أن تكون سالبة.")
                                     Return
                                 End If
                                 _countedCash = counted
                                 _notes = _txtNotes.Text.Trim()
                                 DialogResult = DialogResult.OK
                             End Sub
        Dim cancel As Button = UiTheme.MakeButton("إلغاء", 95)
        cancel.Location = New Point(229, 292)
        AddHandler cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel

        body.Controls.Add(info)
        body.Controls.Add(lblCounted)
        body.Controls.Add(_txtCounted)
        body.Controls.Add(_lblVariance)
        body.Controls.Add(lblNotes)
        body.Controls.Add(_txtNotes)
        body.Controls.Add(ok)
        body.Controls.Add(cancel)
        Controls.Add(body)
        Controls.Add(title)
        AcceptButton = ok
        CancelButton = cancel
        UpdateVariance()
    End Sub

    Private Sub UpdateVariance()
        Dim variance As Decimal = UiTheme.ToMoney(_txtCounted.Text) - _expected
        _lblVariance.Text = "الفرق: " & UiTheme.Money(variance)
        If variance = 0D Then
            _lblVariance.BackColor = Color.FromArgb(232, 245, 233)
            _lblVariance.ForeColor = UiTheme.Success
        Else
            _lblVariance.BackColor = Color.FromArgb(255, 235, 238)
            _lblVariance.ForeColor = UiTheme.Danger
        End If
    End Sub
End Class
