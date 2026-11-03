Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Public Class CouponsForm
    Inherits SectionFormBase

    Private _coupons As List(Of CouponSummary)

    Public Sub New(session As AppSession)
        MyBase.New(session, "كوبونات الخصم",
                   "إنشاء وإدارة أكواد الخصم وحدود استخدامها وصلاحيتها",
                   Color.FromArgb(230, 126, 34), New Size(1050, 640))

        AddSearch("كود الكوبون أو اسمه", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("كوبون جديد", 125)
        AddHandler btnNew.Click, Sub(s, e) EditCoupon(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 90)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnToggle As Button = UiTheme.MakeButton("تفعيل / إيقاف", 120)
        AddHandler btnToggle.Click, Sub(s, e) ToggleActive()
        ToolBar.Controls.Add(btnToggle)

        Dim btnHistory As Button = UiTheme.MakeButton("سجل الاستخدام", 125)
        AddHandler btnHistory.Click, Sub(s, e) ShowRedemptions()
        ToolBar.Controls.Add(btnHistory)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "Code", "الكود", 125)
        UiTheme.AddTextColumn(Grid, "Name", "اسم الكوبون", 220, True)
        UiTheme.AddTextColumn(Grid, "TypeName", "نوع الخصم", 105)
        UiTheme.AddTextColumn(Grid, "DiscountValue", "قيمة الخصم", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "StartsAt", "يبدأ", 105, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "EndsAt", "ينتهي", 105, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "MaxUses", "حد الاستخدام", 100, False, "N0")
        UiTheme.AddTextColumn(Grid, "UsedCount", "المستخدم", 90, False, "N0")
        UiTheme.AddTextColumn(Grid, "RemainingUsesText", "المتبقي", 95)
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 95)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _coupons = CouponService.Search(Session, SearchText, True)
        Grid.DataSource = New BindingSource With {.DataSource = _coupons}
        Dim active As Integer = 0
        For Each coupon In _coupons
            If coupon.IsActive Then active += 1
        Next
        SetFooter("عدد الكوبونات: " & _coupons.Count.ToString() & "   |   المفعّلة: " & active.ToString())
    End Sub

    Private Sub EditSelected()
        Dim coupon = SelectedItem(Of CouponSummary)()
        If coupon Is Nothing Then
            UiTheme.Warn("اختر كوبونًا أولاً.")
            Return
        End If
        EditCoupon(coupon)
    End Sub

    Private Sub EditCoupon(coupon As CouponSummary)
        Using dialog As New CouponEditForm(Session, coupon)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ToggleActive()
        Dim coupon = SelectedItem(Of CouponSummary)()
        If coupon Is Nothing Then
            UiTheme.Warn("اختر كوبونًا أولاً.")
            Return
        End If
        If Not UiTheme.Confirm("هل تريد " & If(coupon.IsActive, "إيقاف", "تفعيل") &
                               " الكوبون «" & coupon.Code & "»؟") Then Return
        Try
            CouponService.SetActive(Session, coupon.Id, Not coupon.IsActive)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ShowRedemptions()
        Dim coupon = SelectedItem(Of CouponSummary)()
        If coupon Is Nothing Then
            UiTheme.Warn("اختر كوبونًا أولاً.")
            Return
        End If
        Using dialog As New CouponRedemptionsForm(Session, coupon)
            dialog.ShowDialog(Me)
        End Using
    End Sub
End Class

Public Class CouponEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _coupon As CouponSummary
    Private ReadOnly txtCode As New TextBox()
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly cboType As New ComboBox()
    Private ReadOnly txtValue As New TextBox()
    Private ReadOnly dtpStart As New DateTimePicker()
    Private ReadOnly dtpEnd As New DateTimePicker()
    Private ReadOnly txtMaxUses As New TextBox()
    Private ReadOnly chkActive As New CheckBox()

    Public Sub New(session As AppSession, coupon As CouponSummary)
        _session = session
        _coupon = coupon
        InitializeUi()
        If coupon IsNot Nothing Then Fill(coupon)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_coupon Is Nothing, "كوبون خصم جديد", "تعديل كوبون الخصم")
        ClientSize = New Size(560, 470)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20)
        }
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 145))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        cboType.DropDownStyle = ComboBoxStyle.DropDownList
        cboType.Items.AddRange(New Object() {"نسبة مئوية %", "مبلغ ثابت"})
        cboType.SelectedIndex = 0

        dtpStart.Format = DateTimePickerFormat.Short
        dtpStart.ShowCheckBox = True
        dtpStart.Checked = False
        dtpEnd.Format = DateTimePickerFormat.Short
        dtpEnd.ShowCheckBox = True
        dtpEnd.Checked = False

        AddRow(table, "كود الكوبون", txtCode)
        AddRow(table, "اسم الكوبون", txtName)
        AddRow(table, "نوع الخصم", cboType)
        AddRow(table, "قيمة الخصم", txtValue)
        AddRow(table, "تاريخ البداية", dtpStart)
        AddRow(table, "تاريخ النهاية", dtpEnd)
        AddRow(table, "حد الاستخدام", txtMaxUses)

        chkActive.Text = "الكوبون مفعّل"
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

    Private Sub Fill(coupon As CouponSummary)
        txtCode.Text = coupon.Code
        txtName.Text = coupon.Name
        cboType.SelectedIndex = If(coupon.DiscountType = "amount", 1, 0)
        txtValue.Text = coupon.DiscountValue.ToString("0.00")
        If coupon.StartsAt.HasValue Then
            dtpStart.Value = coupon.StartsAt.Value
            dtpStart.Checked = True
        End If
        If coupon.EndsAt.HasValue Then
            dtpEnd.Value = coupon.EndsAt.Value
            dtpEnd.Checked = True
        End If
        txtMaxUses.Text = coupon.MaxUses.ToString()
        chkActive.Checked = coupon.IsActive
    End Sub

    Private Sub SaveClicked(sender As Object, e As EventArgs)
        Try
            Dim id As Long? = Nothing
            If _coupon IsNot Nothing Then id = _coupon.Id
            Dim startsAt As Date? = Nothing
            If dtpStart.Checked Then startsAt = dtpStart.Value.Date
            Dim endsAt As Date? = Nothing
            If dtpEnd.Checked Then endsAt = dtpEnd.Value.Date
            Dim discountType As String = If(cboType.SelectedIndex = 1, "amount", "percent")
            CouponService.Save(_session, id, txtCode.Text.Trim(), txtName.Text.Trim(), discountType,
                               UiTheme.ToMoney(txtValue.Text), startsAt, endsAt,
                               CInt(UiTheme.ToInt(txtMaxUses.Text)), chkActive.Checked)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class CouponRedemptionsForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _coupon As CouponSummary
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()

    Public Sub New(session As AppSession, coupon As CouponSummary)
        _session = session
        _coupon = coupon
        InitializeUi()
        LoadData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "سجل استخدام الكوبون — " & _coupon.Code
        ClientSize = New Size(760, 470)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False

        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 50,
            .BackColor = Color.FromArgb(230, 126, 34),
            .Padding = New Padding(14, 8, 14, 8)
        }
        Dim lbl As New Label With {
            .Text = "الكوبون: " & _coupon.Code & " — " & _coupon.Name &
                    "   |   المستخدم: " & _coupon.UsedCount.ToString(),
            .Dock = DockStyle.Fill,
            .ForeColor = Color.White,
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(lbl)

        UiTheme.AddTextColumn(grid, "RedeemedAt", "تاريخ الاستخدام", 160, False, "yyyy-MM-dd HH:mm")
        UiTheme.AddTextColumn(grid, "InvoiceNo", "الفاتورة", 150)
        UiTheme.AddTextColumn(grid, "CustomerName", "العميل", 220, True)
        UiTheme.AddTextColumn(grid, "Amount", "قيمة الخصم", 120, False, "N2")

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 52,
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

    Private Sub LoadData()
        Try
            grid.DataSource = New BindingSource With {
                .DataSource = CouponService.Redemptions(_session, _coupon.Id)
            }
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
