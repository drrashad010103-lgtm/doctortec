Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class LoyaltyForm
    Inherits SectionFormBase

    Private _accounts As List(Of LoyaltyAccountSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "نقاطي — نقاط الولاء",
                   "أرصدة نقاط العملاء وإضافة أو استبدال النقاط يدويًا",
                   Color.FromArgb(230, 126, 34), New Size(980, 640))

        AddSearch("اسم العميل أو الموبايل", AddressOf Reload)

        Dim btnAdjust As Button = UiTheme.PrimaryButton("تعديل النقاط", 130)
        AddHandler btnAdjust.Click, Sub(s, e) AdjustSelected()
        ToolBar.Controls.Add(btnAdjust)

        Dim btnHistory As Button = UiTheme.MakeButton("سجل الحركات", 125)
        AddHandler btnHistory.Click, Sub(s, e) ShowHistory()
        ToolBar.Controls.Add(btnHistory)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "CustomerName", "العميل", 250, True)
        UiTheme.AddTextColumn(Grid, "Mobile", "الموبايل", 130)
        UiTheme.AddTextColumn(Grid, "Points", "الرصيد الحالي", 120, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalEarned", "إجمالي المكتسب", 130, False, "N2")
        UiTheme.AddTextColumn(Grid, "TotalRedeemed", "إجمالي المستبدل", 130, False, "N2")
        UiTheme.AddTextColumn(Grid, "UpdatedAt", "آخر تحديث", 150, False, "yyyy-MM-dd HH:mm")
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then AdjustSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _accounts = LoyaltyService.Search(Session, SearchText)
        Grid.DataSource = New BindingSource With {.DataSource = _accounts}
        Dim total As Decimal = 0D
        Dim withPoints As Integer = 0
        For Each account In _accounts
            total += account.Points
            If account.Points > 0D Then withPoints += 1
        Next
        SetFooter("عدد العملاء: " & _accounts.Count.ToString() &
                  "   |   عملاء لديهم نقاط: " & withPoints.ToString() &
                  "   |   إجمالي النقاط: " & total.ToString("N2"))
    End Sub

    Private Sub AdjustSelected()
        Dim account = SelectedItem(Of LoyaltyAccountSummary)()
        If account Is Nothing Then
            UiTheme.Warn("اختر عميلاً أولاً.")
            Return
        End If
        Using dialog As New LoyaltyAdjustForm(Session, account)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ShowHistory()
        Dim account = SelectedItem(Of LoyaltyAccountSummary)()
        If account Is Nothing Then
            UiTheme.Warn("اختر عميلاً أولاً.")
            Return
        End If
        Using dialog As New LoyaltyHistoryForm(Session, account)
            dialog.ShowDialog(Me)
        End Using
    End Sub
End Class

Public Class LoyaltyAdjustForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _account As LoyaltyAccountSummary
    Private ReadOnly cboType As New ComboBox()
    Private ReadOnly txtPoints As New TextBox()
    Private ReadOnly txtNote As New TextBox()
    Private ReadOnly lblCurrent As New Label()

    Public Sub New(session As AppSession, account As LoyaltyAccountSummary)
        _session = session
        _account = account
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "تعديل نقاط الولاء"
        ClientSize = New Size(520, 380)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        Dim customerLabel As New Label With {
            .Text = _account.CustomerName,
            .Font = UiTheme.Font(12.0F, FontStyle.Bold),
            .AutoSize = True,
            .ForeColor = UiTheme.Navy
        }
        Dim customerRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
        table.Controls.Add(customerLabel, 0, customerRow)
        table.SetColumnSpan(customerLabel, 2)

        lblCurrent.Text = "الرصيد الحالي: " & _account.Points.ToString("N2") & " نقطة"
        lblCurrent.ForeColor = UiTheme.Muted
        lblCurrent.AutoSize = True
        Dim currentRow As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))
        table.Controls.Add(lblCurrent, 0, currentRow)
        table.SetColumnSpan(lblCurrent, 2)

        cboType.Items.AddRange(New Object() {"إضافة نقاط", "استبدال نقاط", "تسوية يدوية"})
        cboType.SelectedIndex = 0
        AddRow(table, "نوع الحركة", cboType)
        AddRow(table, "عدد النقاط", txtPoints)
        AddRow(table, "ملاحظات", txtNote)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("تطبيق", 120)
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
            Dim rawValue As Decimal = UiTheme.ToMoney(txtPoints.Text)
            If rawValue = 0D Then
                UiTheme.Warn("أدخل عدد النقاط.")
                Return
            End If
            Dim kind As String = "earn"
            If cboType.SelectedIndex = 1 Then kind = "redeem"
            If cboType.SelectedIndex = 2 Then kind = "adjust"

            Dim delta As Decimal
            Select Case kind
                Case "earn" : delta = Math.Abs(rawValue)
                Case "redeem" : delta = -Math.Abs(rawValue)
                Case Else : delta = rawValue
            End Select

            Dim newBalance As Decimal = LoyaltyService.Adjust(_session, _account.CustomerId, delta, kind,
                                                              txtNote.Text.Trim())
            UiTheme.Info("تم تحديث النقاط. الرصيد الجديد: " & newBalance.ToString("N2") & " نقطة")
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class LoyaltyHistoryForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _account As LoyaltyAccountSummary
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()

    Public Sub New(session As AppSession, account As LoyaltyAccountSummary)
        _session = session
        _account = account
        InitializeUi()
        LoadHistory()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "سجل حركات النقاط — " & _account.CustomerName
        ClientSize = New Size(820, 500)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False

        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .BackColor = Color.FromArgb(230, 126, 34),
            .Padding = New Padding(14, 8, 14, 8)
        }
        Dim lbl As New Label With {
            .Text = "سجل حركات النقاط — " & _account.CustomerName &
                    "   |   الرصيد الحالي: " & _account.Points.ToString("N2"),
            .Dock = DockStyle.Fill,
            .ForeColor = Color.White,
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(lbl)

        UiTheme.AddTextColumn(grid, "CreatedAt", "التاريخ", 145, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(grid, "TypeName", "الحركة", 100)
        UiTheme.AddTextColumn(grid, "PointsDelta", "النقاط", 90, False, "N2")
        UiTheme.AddTextColumn(grid, "InvoiceNo", "الفاتورة", 120)
        UiTheme.AddTextColumn(grid, "Note", "ملاحظات", 220, True)
        UiTheme.AddTextColumn(grid, "CreatedByName", "المستخدم", 120)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 54,
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

    Private Sub LoadHistory()
        Try
            grid.DataSource = New BindingSource With {
                .DataSource = LoyaltyService.Transactions(_session, _account.CustomerId)
            }
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
