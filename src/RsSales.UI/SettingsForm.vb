Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms

' <summary>
' DOCTOR TECH — لوحة تحكم المحل وإعدادات الشاشات والميزان والاتصال
' تحفظ فعلياً في جدول settings بقاعدة البيانات وفي ملف appsettings.json.
' </summary>
Public Class SettingsForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _initialMode As String

    ' Store & Screen DB Settings
    Private ReadOnly chkAllowNegativeStock As New CheckBox With {.Text = "السماح بالبيع عند عدم كفاية الرصيد (البيع بالسالب)", .AutoSize = True}
    Private ReadOnly chkRequireOpenShift As New CheckBox With {.Text = "إلزام فتح وردية كاشير قبل تسجيل المبيعات النقدية", .AutoSize = True}
    Private ReadOnly chkAutoPrintReceipt As New CheckBox With {.Text = "طباعة إيصال الكاشير تلقائياً فور ترحيل الفاتورة", .AutoSize = True}
    Private ReadOnly chkReceipt80mm As New CheckBox With {.Text = "اعتماد مقاس طابعة الكاشير الحرارية 80mm كافتراضي", .AutoSize = True}
    Private ReadOnly txtLoyaltyEarnPct As New TextBox()
    Private ReadOnly txtLoyaltyPointValue As New TextBox()
    Private ReadOnly txtScalePrefix As New TextBox()

    ' AppConfig Settings
    Private ReadOnly txtConn As New TextBox()
    Private ReadOnly txtServer As New TextBox()
    Private ReadOnly txtBatch As New TextBox()
    Private ReadOnly txtLogDir As New TextBox()
    Private ReadOnly lblInfo As New Label()

    Public Sub New(session As AppSession, Optional initialMode As String = "store")
        _session = session
        _initialMode = If(initialMode, "store")
        InitializeUi()
        LoadSettingsData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — لوحة تحكم المحل وإعدادات الشاشات والنظام"
        ClientSize = New Size(860, 620)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        Dim t As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(20),
            .BackColor = Color.White,
            .AutoScroll = True
        }
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 230))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        AddRow(t, "سياسة رصيد المخزون:", chkAllowNegativeStock)
        AddRow(t, "سياسة وردية الكاشير:", chkRequireOpenShift)
        AddRow(t, "الطباعة التلقائية بالفاتورة:", chkAutoPrintReceipt)
        AddRow(t, "مقاس ورق الطباعة:", chkReceipt80mm)
        AddRow(t, "نسبة اكتساب نقاط الولاء %:", txtLoyaltyEarnPct)
        AddRow(t, "قيمة النقطة الواحدة (ج.م):", txtLoyaltyPointValue)
        AddRow(t, "بادئة باركود الميزان (Scale):", txtScalePrefix)

        txtConn.Multiline = True
        txtConn.Height = 56
        txtConn.ScrollBars = ScrollBars.Vertical
        AddRow(t, "سلسلة الاتصال بقاعدة البيانات:", txtConn)
        AddRow(t, "عنوان السيرفر المركزي (Sync):", txtServer)
        AddRow(t, "حجم دفعة المزامنة:", txtBatch)
        AddRow(t, "مجلد السجلات (Logs):", txtLogDir)

        lblInfo.Dock = DockStyle.Bottom
        lblInfo.Height = 46
        lblInfo.Padding = New Padding(12, 6, 12, 0)
        lblInfo.ForeColor = UiTheme.DigitalBlue
        lblInfo.Font = UiTheme.Font(9.0F, FontStyle.Bold)
        lblInfo.Text = "💡 جميع الإعدادات تُحفظ مباشرة في جدول (settings) بقاعدة البيانات وفي ملف (appsettings.json) مع تسجيل عملية الحفظ في سجل الرقابة."

        Dim btns As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 58,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(12, 8, 12, 8),
            .BackColor = UiTheme.IceBlue
        }

        Dim btnSave As Button = UiTheme.MakeGlassButton("💾 حفظ الإعدادات (F9)", 190, 38, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 12)
        AddHandler btnSave.Click, AddressOf SaveSettingsClick

        Dim btnOpen As Button = UiTheme.MakeButton("📂 فتح مجلد السجلات", 160)
        AddHandler btnOpen.Click, AddressOf OpenLogs

        Dim btnClose As Button = UiTheme.MakeButton("إغلاق (Esc)", 110)
        AddHandler btnClose.Click, Sub(s, e) Close()

        btns.Controls.Add(btnSave)
        btns.Controls.Add(btnOpen)
        btns.Controls.Add(btnClose)

        Controls.Add(t)
        Controls.Add(lblInfo)
        Controls.Add(btns)

        AddHandler KeyDown, Sub(s, e)
                                If e.KeyCode = Keys.F9 Then SaveSettingsClick(Me, EventArgs.Empty)
                                If e.KeyCode = Keys.Escape Then Close()
                            End Sub
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, If(ed.Height > 40, 64, 38)))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r)
        ed.Dock = DockStyle.Fill
        t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub LoadSettingsData()
        Try
            Dim cfg = AppConfig.Load()
            txtConn.Text = cfg.ConnectionString
            txtServer.Text = cfg.ServerUrl
            txtBatch.Text = cfg.SyncBatchSize.ToString(CultureInfo.InvariantCulture)
            txtLogDir.Text = cfg.LogDirectory

            Using db = DbSession.Open(_session.ConnectionString)
                Dim c = db.Connection
                chkAllowNegativeStock.Checked = SettingsRepository.GetBool(c, "company.allow_sale_without_stock", False)
                chkRequireOpenShift.Checked = SettingsRepository.GetBool(c, "pos.require_open_shift", False)
                chkAutoPrintReceipt.Checked = SettingsRepository.GetBool(c, "pos.auto_print_receipt", False)
                chkReceipt80mm.Checked = SettingsRepository.GetBool(c, "pos.receipt_80mm", True)
                txtLoyaltyEarnPct.Text = SettingsRepository.GetDecimal(c, "loyalty.earn_rate_pct", 1D).ToString("0.##", CultureInfo.InvariantCulture)
                txtLoyaltyPointValue.Text = SettingsRepository.GetDecimal(c, "loyalty.point_value", 1D).ToString("0.##", CultureInfo.InvariantCulture)
                Dim scalePref As String = SettingsRepository.Get(c, "scale.barcode_prefix")
                txtScalePrefix.Text = If(String.IsNullOrWhiteSpace(scalePref), "20", scalePref)
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub SaveSettingsClick(sender As Object, e As EventArgs)
        Try
            Dim batchSize As Integer = 50
            If Not Integer.TryParse(txtBatch.Text.Trim(), batchSize) OrElse batchSize <= 0 Then batchSize = 50

            Dim earnPct As Decimal = UiTheme.ToMoney(txtLoyaltyEarnPct.Text)
            Dim ptVal As Decimal = UiTheme.ToMoney(txtLoyaltyPointValue.Text)
            If ptVal <= 0D Then ptVal = 1D

            Using db = DbSession.Open(_session.ConnectionString)
                db.BeginTransaction()
                Dim tx = db.Transaction
                SettingsRepository.Set(db.Connection, tx, "company.allow_sale_without_stock", If(chkAllowNegativeStock.Checked, "1", "0"), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "stock.allow_negative", If(chkAllowNegativeStock.Checked, "1", "0"), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "pos.require_open_shift", If(chkRequireOpenShift.Checked, "1", "0"), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "pos.auto_print_receipt", If(chkAutoPrintReceipt.Checked, "1", "0"), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "pos.receipt_80mm", If(chkReceipt80mm.Checked, "1", "0"), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "loyalty.earn_rate_pct", earnPct.ToString("0.##", CultureInfo.InvariantCulture), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "loyalty.point_value", ptVal.ToString("0.##", CultureInfo.InvariantCulture), _session.UserId)
                SettingsRepository.Set(db.Connection, tx, "scale.barcode_prefix", txtScalePrefix.Text.Trim(), _session.UserId)

                AuditRepository.Log(db.Connection, tx, Nothing, _session.BranchId, _session.UserId,
                                    _session.Username, "settings_save", "settings", "store_and_screen",
                                    Nothing, $"allowNeg={chkAllowNegativeStock.Checked};earnPct={earnPct}", _session.DeviceId)
                db.Commit()
            End Using

            ' Persist appsettings.json as well
            Try
                Dim cfgObj = New With {
                    .ConnectionString = If(String.IsNullOrWhiteSpace(txtConn.Text), _session.ConnectionString, txtConn.Text.Trim()),
                    .ServerUrl = txtServer.Text.Trim(),
                    .SyncBatchSize = batchSize,
                    .SyncMaxAttempts = 10,
                    .LogDirectory = txtLogDir.Text.Trim()
                }
                Dim jsonText As String = JsonSerializer.Serialize(cfgObj, New JsonSerializerOptions With {.WriteIndented = True})
                Dim cfgPath As String = Path.Combine(AppContext.BaseDirectory, "appsettings.json")
                File.WriteAllText(cfgPath, jsonText, New UTF8Encoding(False))
            Catch
            End Try

            UiTheme.Info("تم حفظ إعدادات المحل والشاشات والميزان والاتصال في قاعدة البيانات بنجاح ✔")
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر حفظ الإعدادات")
        End Try
    End Sub

    Private Sub OpenLogs(sender As Object, e As EventArgs)
        Try
            Directory.CreateDirectory(AppLog.Directory)
            Process.Start("explorer.exe", AppLog.Directory)
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
