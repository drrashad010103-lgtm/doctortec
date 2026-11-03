Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

' <summary>
' DOCTOR TECH — منظومة الخزينة والأموال المتكاملة:
' 1. فتح وإقفال الوردية وسندات القبض وسندات الصرف (shift / payin / payout)
' 2. أرصدة الخزن والتحويل بين الخزائن (balances / transfer)
' 3. كشف حركة الخزينة التفصيلي (ledger)
' بالإضافة إلى نافذة تغيير كلمة المرور (ChangePasswordForm).
' </summary>
Public Class ShiftForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _initialMode As String
    Private ReadOnly tab As New TabControl()

    ' Tab 1: الوردية وسندات القبض والصرف
    Private ReadOnly lblStatus As New Label()
    Private ReadOnly lblShift As New Label()
    Private ReadOnly lblExpected As New Label()
    Private ReadOnly txtOpening As New TextBox()
    Private ReadOnly txtCounted As New TextBox()
    Private ReadOnly txtCashMoveAmount As New TextBox()
    Private ReadOnly txtNotes As New TextBox()
    Private _openShift As ShiftRecord

    ' Tab 2: أرصدة الخزن والتحويل بين الخزائن
    Private ReadOnly gridSafes As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly cboFromSafe As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190}
    Private ReadOnly cboToSafe As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 190}
    Private ReadOnly txtTransferAmount As New TextBox With {.Width = 110, .Text = "0.00"}
    Private ReadOnly txtTransferNotes As New TextBox With {.Width = 200}

    ' Tab 3: كشف حركة الخزينة التفصيلي
    Private ReadOnly dtpFrom As New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "yyyy-MM-dd", .Width = 115, .Value = Date.Today.AddDays(-30)}
    Private ReadOnly dtpTo As New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "yyyy-MM-dd", .Width = 115, .Value = Date.Today}
    Private ReadOnly cboDirFilter As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
    Private ReadOnly gridLedger As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly lblLedgerTotals As New Label()

    Public Sub New(session As AppSession, Optional initialMode As String = "shift")
        _session = session
        _initialMode = If(initialMode, "shift").Trim().ToLowerInvariant()
        InitializeUi()
        LoadAllTreasuryData()
        ApplyInitialMode()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — إدارة الوردية، سندات القبض والصرف، التحويل بين الخزائن، وحركة الخزينة"
        ClientSize = New Size(1020, 640)
        StartPosition = FormStartPosition.CenterParent
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        ' ---------------- Tab 1: الوردية وسندات القبض والصرف ----------------
        Dim pageShift As New TabPage("🧾 الوردية وسندات القبض والصرف")
        pageShift.RightToLeft = RightToLeft.Yes
        pageShift.BackColor = Color.White
        pageShift.Padding = New Padding(18)

        lblStatus.Font = UiTheme.Font(13.0F, FontStyle.Bold)
        lblStatus.AutoSize = True
        lblStatus.Location = New Point(24, 18)

        lblShift.AutoSize = True
        lblShift.Location = New Point(24, 50)
        lblShift.ForeColor = UiTheme.Muted

        lblExpected.AutoSize = True
        lblExpected.Location = New Point(24, 76)
        lblExpected.Font = UiTheme.Font(11.5F, FontStyle.Bold)
        lblExpected.ForeColor = UiTheme.DigitalBlue

        Dim t As New TableLayoutPanel With {
            .Location = New Point(24, 114),
            .Size = New Size(860, 195),
            .ColumnCount = 2,
            .BackColor = UiTheme.IceBlue,
            .Padding = New Padding(14)
        }
        UiTheme.SetRoundedRegion(t, 14)
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 220))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        txtOpening.Text = "0.00"
        txtCashMoveAmount.Text = "0.00"
        AddRow(t, "نقدية الافتتاح (Opening Cash):", txtOpening)
        AddRow(t, "النقد الفعلي عند الإقفال (Counted):", txtCounted)
        AddRow(t, "مبلغ سند القبض / سند الصرف (ج.م):", txtCashMoveAmount)
        AddRow(t, "بيان السند / السبب / الملاحظات:", txtNotes)

        Dim btns As New FlowLayoutPanel With {
            .Location = New Point(24, 325),
            .Width = 860,
            .Height = 56,
            .FlowDirection = FlowDirection.LeftToRight
        }
        Dim btnOpen As Button = UiTheme.MakeGlassButton("🟢 فتح وردية جديدة", 155, 40, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 12)
        AddHandler btnOpen.Click, AddressOf OpenShiftClick

        Dim btnClose As Button = UiTheme.MakeGlassButton("🔴 إقفال الوردية", 145, 40, UiTheme.DangerRed, Color.FromArgb(165, 28, 28), Color.White, 12)
        AddHandler btnClose.Click, AddressOf CloseShiftClick

        Dim btnPayIn As Button = UiTheme.MakeGlassButton("➕ حفظ سند قبض للخزينة", 185, 40, UiTheme.SoftTechBlue, UiTheme.DigitalBlue, Color.White, 12)
        AddHandler btnPayIn.Click, Sub(s, e) ExecuteCashMove("payin", "سند قبض للخزينة")

        Dim btnPayOut As Button = UiTheme.MakeGlassButton("➖ حفظ سند صرف من الخزينة", 190, 40, UiTheme.DigitalBlue, UiTheme.DeepTechNavy, Color.White, 12)
        AddHandler btnPayOut.Click, Sub(s, e) ExecuteCashMove("payout", "سند صرف من الخزينة")

        Dim btnRefresh As Button = UiTheme.MakeButton("🔄 تحديث", 105)
        AddHandler btnRefresh.Click, Sub(s, e) LoadAllTreasuryData()

        btns.Controls.AddRange({btnOpen, btnClose, btnPayIn, btnPayOut, btnRefresh})

        pageShift.Controls.Add(lblStatus)
        pageShift.Controls.Add(lblShift)
        pageShift.Controls.Add(lblExpected)
        pageShift.Controls.Add(t)
        pageShift.Controls.Add(btns)

        ' ---------------- Tab 2: أرصدة الخزن والتحويل بين الخزائن ----------------
        Dim pageSafes As New TabPage("💰 أرصدة الخزن والتحويل بين خزائن")
        pageSafes.RightToLeft = RightToLeft.Yes
        pageSafes.BackColor = Color.White

        Dim transferBar As FlowLayoutPanel = UiTheme.Toolbar()
        transferBar.Height = 56
        transferBar.BackColor = UiTheme.IceBlue
        transferBar.Controls.Add(UiTheme.MakeLabel("من خزينة:", True))
        transferBar.Controls.Add(cboFromSafe)
        transferBar.Controls.Add(UiTheme.MakeLabel("إلى خزينة:", True))
        transferBar.Controls.Add(cboToSafe)
        transferBar.Controls.Add(UiTheme.MakeLabel("المبلغ:", True))
        transferBar.Controls.Add(txtTransferAmount)
        transferBar.Controls.Add(UiTheme.MakeLabel("البيان:"))
        transferBar.Controls.Add(txtTransferNotes)

        Dim btnDoTransfer As Button = UiTheme.MakeGlassButton("💸 تنفيذ التحويل بين الخزائن", 195, 36, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 10)
        AddHandler btnDoTransfer.Click, AddressOf ExecuteSafeTransferClick
        transferBar.Controls.Add(btnDoTransfer)

        UiTheme.AddTextColumn(gridSafes, "Code", "كود الخزينة", 120)
        UiTheme.AddTextColumn(gridSafes, "Name", "اسم الخزينة", 260, True)
        UiTheme.AddTextColumn(gridSafes, "TotalIn", "إجمالي الوارد (مقبوضات)", 170, False, "N2")
        UiTheme.AddTextColumn(gridSafes, "TotalOut", "إجمالي الصادر (مدفوعات)", 170, False, "N2")
        UiTheme.AddTextColumn(gridSafes, "CurrentBalance", "الرصيد الفعلي الحالي بالخزينة", 190, False, "N2")

        pageSafes.Controls.Add(gridSafes)
        pageSafes.Controls.Add(transferBar)

        ' ---------------- Tab 3: كشف حركة الخزينة التفصيلي ----------------
        Dim pageLedger As New TabPage("📒 كشف حركة الخزينة التفصيلي")
        pageLedger.RightToLeft = RightToLeft.Yes
        pageLedger.BackColor = Color.White

        Dim ledgerBar As FlowLayoutPanel = UiTheme.Toolbar()
        ledgerBar.Height = 52
        cboDirFilter.Items.AddRange(New Object() {"كل الحركات (وارد وصادر)", "سندات القبض والوارد فقط", "سندات الصرف والصادر فقط"})
        cboDirFilter.SelectedIndex = 0
        AddHandler cboDirFilter.SelectedIndexChanged, Sub(s, e) LoadLedgerMoves()
        AddHandler dtpFrom.ValueChanged, Sub(s, e) LoadLedgerMoves()
        AddHandler dtpTo.ValueChanged, Sub(s, e) LoadLedgerMoves()

        Dim btnFilterLedger As Button = UiTheme.PrimaryButton("🔍 تحديث الكشف", 130)
        AddHandler btnFilterLedger.Click, Sub(s, e) LoadLedgerMoves()

        ledgerBar.Controls.Add(UiTheme.MakeLabel("من تاريخ:", True))
        ledgerBar.Controls.Add(dtpFrom)
        ledgerBar.Controls.Add(UiTheme.MakeLabel("إلى تاريخ:", True))
        ledgerBar.Controls.Add(dtpTo)
        ledgerBar.Controls.Add(UiTheme.MakeLabel("نوع الحركة:", True))
        ledgerBar.Controls.Add(cboDirFilter)
        ledgerBar.Controls.Add(btnFilterLedger)

        lblLedgerTotals.Dock = DockStyle.Bottom
        lblLedgerTotals.Height = 36
        lblLedgerTotals.BackColor = UiTheme.IceBlue
        lblLedgerTotals.ForeColor = UiTheme.DeepTechNavy
        lblLedgerTotals.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        lblLedgerTotals.TextAlign = ContentAlignment.MiddleRight
        lblLedgerTotals.Padding = New Padding(12, 0, 12, 0)

        UiTheme.AddTextColumn(gridLedger, "Id", "رقم الحركة", 90)
        UiTheme.AddTextColumn(gridLedger, "CashboxName", "الخزينة", 165)
        UiTheme.AddTextColumn(gridLedger, "ShiftNo", "الوردية", 125)
        UiTheme.AddTextColumn(gridLedger, "DirectionArabic", "الاتجاه", 120)
        UiTheme.AddTextColumn(gridLedger, "Amount", "المبلغ (ج.م)", 130, False, "N2")
        UiTheme.AddTextColumn(gridLedger, "RefType", "نوع السند / المرجع", 180, True)
        UiTheme.AddTextColumn(gridLedger, "UserName", "المستخدم", 125)
        UiTheme.AddTextColumn(gridLedger, "CreatedAt", "التاريخ والوقت", 150, False, "yyyy-MM-dd HH:mm")

        pageLedger.Controls.Add(gridLedger)
        pageLedger.Controls.Add(lblLedgerTotals)
        pageLedger.Controls.Add(ledgerBar)

        tab.Dock = DockStyle.Fill
        tab.RightToLeftLayout = True
        tab.Controls.Add(pageShift)
        tab.Controls.Add(pageSafes)
        tab.Controls.Add(pageLedger)
        Controls.Add(tab)
    End Sub

    Private Sub ApplyInitialMode()
        Select Case _initialMode
            Case "payin", "receipt_voucher"
                tab.SelectedIndex = 0
                Text = "DOCTOR TECH — سندات قبض للخزينة"
                txtNotes.Text = "سند قبض نقدي للخزينة"
                txtCashMoveAmount.Focus()
            Case "payout", "payment_voucher"
                tab.SelectedIndex = 0
                Text = "DOCTOR TECH — سندات صرف من الخزينة"
                txtNotes.Text = "سند صرف نقدي من الخزينة"
                txtCashMoveAmount.Focus()
            Case "transfer", "safe_transfer"
                tab.SelectedIndex = 1
                Text = "DOCTOR TECH — تحويل نقدية بين الخزائن"
                txtTransferAmount.Focus()
            Case "balances", "safe_balances"
                tab.SelectedIndex = 1
                Text = "DOCTOR TECH — أرصدة الخزن الحية"
            Case "ledger", "cash_ledger"
                tab.SelectedIndex = 2
                Text = "DOCTOR TECH — كشف حركة الخزينة التفصيلي"
            Case Else
                tab.SelectedIndex = 0
        End Select
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r)
        ed.Dock = DockStyle.Fill
        t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub LoadAllTreasuryData()
        LoadShiftData()
        LoadSafeBalancesData()
        LoadLedgerMoves()
    End Sub

    Private Sub LoadShiftData()
        Try
            _openShift = ShiftService.Current(_session)
            If _openShift Is Nothing Then
                lblStatus.Text = "لا توجد وردية مفتوحة حاليًا"
                lblStatus.ForeColor = UiTheme.DangerRed
                lblShift.Text = "اضغط «فتح وردية جديدة» لبدء العمل على الخزينة."
                lblExpected.Text = "الرصيد المتوقع بالدرج: 0.00 ج.م"
            Else
                Dim expected As Decimal = ShiftService.ExpectedCash(_session, _openShift.Id)
                lblStatus.Text = "🟢 وردية مفتوحة ونشطة"
                lblStatus.ForeColor = UiTheme.Success
                lblShift.Text = "رقم الوردية: " & _openShift.ShiftNo & " | وقت الفتح: " & _openShift.OpenedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                lblExpected.Text = "النقدية المتوقعة بالدرج الآن: " & UiTheme.Money(expected) & " ج.م"
                txtCounted.Text = expected.ToString("0.00")
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub LoadSafeBalancesData()
        Try
            Dim safes = ShiftService.ListSafeBalances(_session)
            gridSafes.DataSource = New BindingSource With {.DataSource = safes}

            cboFromSafe.DataSource = safes.ToList()
            cboFromSafe.DisplayMember = "Name"
            cboFromSafe.ValueMember = "Id"

            cboToSafe.DataSource = safes.ToList()
            cboToSafe.DisplayMember = "Name"
            cboToSafe.ValueMember = "Id"
            If cboToSafe.Items.Count > 1 Then cboToSafe.SelectedIndex = 1
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub LoadLedgerMoves()
        Try
            Dim dir As String = ""
            If cboDirFilter.SelectedIndex = 1 Then dir = "in"
            If cboDirFilter.SelectedIndex = 2 Then dir = "out"
            Dim rows = ReportService.CashMovesReport(_session, dtpFrom.Value.Date, dtpTo.Value.Date, dir)
            gridLedger.DataSource = New BindingSource With {.DataSource = rows}

            Dim totIn As Decimal = rows.Where(Function(r) r.Direction = "in").Sum(Function(r) r.Amount)
            Dim totOut As Decimal = rows.Where(Function(r) r.Direction = "out").Sum(Function(r) r.Amount)
            lblLedgerTotals.Text = $"إجمالي حركات الكشف ({rows.Count})   |   إجمالي المقبوضات (وارد): {UiTheme.Money(totIn)} ج.م   |   إجمالي المصروفات (صادر): {UiTheme.Money(totOut)} ج.م   |   الصافي: {UiTheme.Money(totIn - totOut)} ج.م"
        Catch ex As Exception
            lblLedgerTotals.Text = "تنبيه الكشف: " & ex.Message
        End Try
    End Sub

    Private Sub OpenShiftClick(sender As Object, e As EventArgs)
        Try
            Dim cash = UiTheme.ToMoney(txtOpening.Text)
            Dim shift = ShiftService.Open(_session, cash)
            _openShift = shift
            UiTheme.Info("تم فتح الوردية بنجاح برقم: " & _openShift.ShiftNo)
            LoadAllTreasuryData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub CloseShiftClick(sender As Object, e As EventArgs)
        If _openShift Is Nothing Then
            UiTheme.Warn("لا توجد وردية مفتوحة.")
            Return
        End If
        Try
            Dim counted = UiTheme.ToMoney(txtCounted.Text)
            Dim variance = ShiftService.Close(_session, _openShift.Id, counted, txtNotes.Text.Trim())
            UiTheme.Info("تم إقفال الوردية بنجاح. فرق الجرد: " & UiTheme.Money(variance))
            _openShift = Nothing
            LoadAllTreasuryData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ExecuteCashMove(opType As String, title As String)
        Try
            Dim amt As Decimal = UiTheme.ToMoney(txtCashMoveAmount.Text)
            If amt <= 0D Then
                Dim input = UiTheme.Prompt(title, "أدخل المبلغ بالجنيه:", "100")
                If String.IsNullOrWhiteSpace(input) Then Return
                amt = UiTheme.ToMoney(input)
            End If
            If amt <= 0D Then
                UiTheme.Warn("المبلغ يجب أن يكون أكبر من صفر.")
                Return
            End If
            Dim reason As String = If(String.IsNullOrWhiteSpace(txtNotes.Text), title, txtNotes.Text.Trim())
            Dim newExpected = ShiftService.RecordTillOperation(_session, opType, amt, reason)
            txtCashMoveAmount.Text = "0.00"
            UiTheme.Info($"تم حفظ «{title}» بمبلغ {UiTheme.Money(amt)} ج.م في قاعدة البيانات." & Environment.NewLine &
                         $"الرصيد المتوقع بالدرج الآن: {UiTheme.Money(newExpected)} ج.م")
            LoadAllTreasuryData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ExecuteSafeTransferClick(sender As Object, e As EventArgs)
        Try
            Dim fromRow = TryCast(cboFromSafe.SelectedItem, ShiftService.SafeBalanceRow)
            Dim toRow = TryCast(cboToSafe.SelectedItem, ShiftService.SafeBalanceRow)
            If fromRow Is Nothing OrElse toRow Is Nothing Then
                UiTheme.Warn("اختر خزينة المصدر وخزينة الوجهة أولاً.")
                Return
            End If
            Dim amt As Decimal = UiTheme.ToMoney(txtTransferAmount.Text)
            If amt <= 0D Then
                UiTheme.Warn("أدخل مبلغ تحويل أكبر من صفر.")
                Return
            End If
            ShiftService.TransferBetweenSafes(_session, fromRow.Id, toRow.Id, amt, txtTransferNotes.Text.Trim())
            txtTransferAmount.Text = "0.00"
            txtTransferNotes.Clear()
            UiTheme.Info($"تم تحويل مبلغ {UiTheme.Money(amt)} ج.م من «{fromRow.Name}» إلى «{toRow.Name}» بنجاح.")
            LoadAllTreasuryData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class ChangePasswordForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtOld As New TextBox With {.UseSystemPasswordChar = True}
    Private ReadOnly txtNew As New TextBox With {.UseSystemPasswordChar = True}
    Private ReadOnly txtConfirm As New TextBox With {.UseSystemPasswordChar = True}

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — تغيير كلمة المرور"
        ClientSize = New Size(460, 300)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(24)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 155))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        AddRow(t, "كلمة المرور الحالية:", txtOld)
        AddRow(t, "كلمة المرور الجديدة:", txtNew)
        AddRow(t, "تأكيد كلمة المرور:", txtConfirm)

        Dim btns As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 58, .FlowDirection = FlowDirection.LeftToRight, .Padding = New Padding(12)}
        Dim btnSave As Button = UiTheme.PrimaryButton("حفظ التغيير", 130)
        AddHandler btnSave.Click, AddressOf SaveClick
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 100)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        btns.Controls.Add(btnSave)
        btns.Controls.Add(btnCancel)

        Controls.Add(t)
        Controls.Add(btns)
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r)
        ed.Dock = DockStyle.Fill
        t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub SaveClick(sender As Object, e As EventArgs)
        If txtNew.Text <> txtConfirm.Text Then
            UiTheme.Warn("تأكيد كلمة المرور غير مطابق.")
            Return
        End If
        Try
            AuthService.ChangePassword(_session.ConnectionString, _session.UserId, txtOld.Text, txtNew.Text, _session.Username)
            UiTheme.Info("تم تحديث كلمة المرور بنجاح.")
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
