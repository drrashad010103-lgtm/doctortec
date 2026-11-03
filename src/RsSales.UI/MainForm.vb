Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' DOCTOR TECH — main options screen.
''' RTL-branded shell: top module tabs, prompt/response strip, right-hand quick-action
''' rail, centered logo workspace with KPI tiles and big sale/purchase/report actions,
''' plus a live status and global navigation footer.
''' </summary>
Public Class MainForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _config As AppConfig
    Private ReadOnly _timer As New Timer()
    Private ReadOnly _clockTimer As New Timer()

    Private ReadOnly topFlow As New FlowLayoutPanel()
    Private ReadOnly ribbonPanel As New Panel()
    Private ReadOnly ribbonFlow As New FlowLayoutPanel()
    Private ReadOnly centerWorkspace As New Panel()
    Private ReadOnly contentHost As New Panel()
    Private ReadOnly rightNavPanel As New Panel()
    Private ReadOnly rightNavFlow As New FlowLayoutPanel()
    Private ReadOnly rootLayout As New TableLayoutPanel()
    Private ReadOnly topBand As New TableLayoutPanel()
    Private ribbonRowStyle As RowStyle

    Private ReadOnly lblPrompt As New Label()
    Private ReadOnly lblResponse As New Label()
    Private ReadOnly lblStatusReg As New Label()
    Private ReadOnly lblStatusDate As New Label()
    Private ReadOnly lblStatusCashier As New Label()
    Private ReadOnly lblStatusClock As New Label()
    Private ReadOnly lblStatusScreen As New Label()
    Private ReadOnly lblStatusMode As New Label()

    Private ReadOnly logoCard As New Panel()
    Private ReadOnly lblMainPharmacy As New Label()
    Private ReadOnly cardsContainer As New Panel()
    Private ReadOnly kpiTable As New TableLayoutPanel()
    Private ReadOnly lblKpiItems As New Label()
    Private ReadOnly lblKpiInvoices As New Label()
    Private ReadOnly lblKpiSales As New Label()
    Private ReadOnly lblKpiShift As New Label()

    Private _activeTabKey As String = String.Empty
    Private ReadOnly _tabButtons As New Dictionary(Of String, Button)()
    Private _itemsCount As Integer = 0
    Private _todaySalesTotal As Decimal = 0D
    Private _todayInvoicesCount As Integer = 0
    Private _shiftStatusText As String = "مغلقة"
    Private _trainingMode As Boolean = False
    Private _reentryMode As Boolean = False
    Private btnF8Training As GlassButton
    Private btnF9Reentry As GlassButton

    Private Structure RibbonCardDef
        Public Title As String
        Public IconText As String
        Public IconColor As Color
        Public Action As Action

        Public Sub New(title As String, iconText As String, iconColor As Color, action As Action)
            Me.Title = title
            Me.IconText = iconText
            Me.IconColor = iconColor
            Me.Action = action
        End Sub
    End Structure

    Public Sub New(session As AppSession, config As AppConfig)
        _session = session
        _config = config
        InitializeUi()
        RefreshDashboard()

        _timer.Interval = 60000
        AddHandler _timer.Tick, Sub(s, e) RefreshDashboard()
        _timer.Start()

        _clockTimer.Interval = 1000
        AddHandler _clockTimer.Tick, Sub(s, e) UpdateFooterText()
        _clockTimer.Start()
    End Sub

    ' ------------------------------------------------------------------- UI --
    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        RightToLeftLayout = False
        Text = "DOCTOR TECH — الشاشة الرئيسية"
        WindowState = FormWindowState.Maximized
        StartPosition = FormStartPosition.CenterScreen
        DoubleBuffered = True
        KeyPreview = True
        BackColor = UiTheme.Surface
        Padding = New Padding(0)

        ' Header and module tabs share one navy band so the top reads as a single piece.
        topBand.Dock = DockStyle.Top
        topBand.AutoSize = True
        topBand.AutoSizeMode = AutoSizeMode.GrowAndShrink
        topBand.ColumnCount = 1
        topBand.RowCount = 2
        topBand.BackColor = UiTheme.BrandNavyDark
        topBand.RowStyles.Add(New RowStyle(SizeType.Absolute, 78))
        topBand.RowStyles.Add(New RowStyle(SizeType.Absolute, 58))
        topBand.Controls.Add(BuildHeaderBar(), 0, 0)
        topBand.Controls.Add(BuildTopTabs(), 0, 1)

        rootLayout.Dock = DockStyle.Fill
        rootLayout.Padding = New Padding(UiTheme.ScreenMarginHalfCm)
        rootLayout.ColumnCount = 1
        rootLayout.RowCount = 4
        rootLayout.BackColor = UiTheme.Surface
        rootLayout.RightToLeft = RightToLeft.No
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        ribbonRowStyle = New RowStyle(SizeType.Absolute, 0)
        rootLayout.RowStyles.Add(ribbonRowStyle)
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 100))

        BuildRibbon()
        BuildContent()

        rootLayout.Controls.Add(BuildPromptStrip(), 0, 0)
        rootLayout.Controls.Add(ribbonPanel, 0, 1)
        rootLayout.Controls.Add(contentHost, 0, 2)
        rootLayout.Controls.Add(BuildFooter(), 0, 3)
        Controls.Add(rootLayout)
        Controls.Add(topBand)

        AddHandler KeyDown, AddressOf MainKeyDown
    End Sub

    Private Function BuildHeaderBar() As Control
        Dim bar As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavyDark,
            .Padding = New Padding(10, 6, 14, 6)
        }

        Dim brandFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Right,
            .AutoSize = True,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        Dim logoPicture As New PictureBox With {
            .Size = New Size(50, 50),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 0, 10, 0)
        }
        Dim logoImage As Image = UiTheme.LoadBrandLogo()
        If logoImage IsNot Nothing Then logoPicture.Image = logoImage
        brandFlow.Controls.Add(logoPicture)

        Dim brandText As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1,
            .RowCount = 2,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 2, 0, 0)
        }
        brandText.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        brandText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        brandText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim lblBrandWord As New Label With {
            .Text = "DOCTOR TECH",
            .AutoSize = True,
            .Font = UiTheme.Font(14.5F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0)
        }
        Dim lblBrandSub As New Label With {
            .Text = "نظام إدارة نقاط البيع والمخازن",
            .AutoSize = True,
            .Font = UiTheme.Font(8.8F),
            .ForeColor = UiTheme.Lighten(UiTheme.BrandMist, 0.35F),
            .BackColor = Color.Transparent,
            .Margin = New Padding(0)
        }
        brandText.Controls.Add(lblBrandWord, 0, 0)
        brandText.Controls.Add(lblBrandSub, 0, 1)
        brandFlow.Controls.Add(brandText)
        bar.Controls.Add(brandFlow)

        Dim btnLogout As New GlassButton With {
            .Text = "خروج (F12)",
            .Size = New Size(118, 34),
            .Dock = DockStyle.Left,
            .CornerRadius = 9
        }
        btnLogout.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        AddHandler btnLogout.Click, Sub(s, e) Logout()
        bar.Controls.Add(btnLogout)

        Dim infoFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        Dim lblUser As New Label With {
            .Text = "المستخدم: " & _session.Username & "    |    الفرع: " & _session.BranchName,
            .AutoSize = True,
            .Font = UiTheme.Font(10.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .BackColor = Color.Transparent,
            .Margin = New Padding(8, 16, 8, 4)
        }
        infoFlow.Controls.Add(lblUser)
        bar.Controls.Add(infoFlow)
        Return bar
    End Function

    Private Function BuildTopTabs() As Control
        Dim tabsBar As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavyDark,
            .Padding = New Padding(8, 2, 8, 6)
        }
        topFlow.Dock = DockStyle.Fill
        topFlow.FlowDirection = FlowDirection.RightToLeft
        topFlow.WrapContents = False
        topFlow.AutoScroll = True
        topFlow.BackColor = Color.Transparent

        AddTopTab("basic", "البيانات الأساسية", 128)
        AddTopTab("customers", "العملاء والولاء", 116)
        AddTopTab("hr", "شئون الموظفين", 112)
        AddTopTab("sales", "مبيعات ومشتريات", 128)
        AddTopTab("reports", "تقارير بيع وشراء", 124)
        AddTopTab("stock", "المخازن والجرد", 112)
        AddTopTab("money", "الخزينة والحسابات", 126)
        AddTopTab("offers", "العروض والكوبونات", 126)
        AddTopTab("settings", "إعدادات النظام", 112)
        AddTopTab("about", "عن البرنامج", 96)

        tabsBar.Controls.Add(topFlow)
        Return tabsBar
    End Function

    Private Function BuildPromptStrip() As Control
        Dim strip As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.Surface,
            .ColumnCount = 2,
            .RowCount = 1,
            .Padding = New Padding(0, 4, 0, 4),
            .RightToLeft = RightToLeft.Yes
        }
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68.0F))
        strip.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32.0F))
        strip.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim promptCard As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = Color.White,
            .BorderColor = UiTheme.BrandBorder,
            .CornerRadius = 12,
            .Margin = New Padding(0, 0, 4, 0),
            .Padding = New Padding(14, 2, 14, 2)
        }
        lblPrompt.Text = "اختر قسماً من التبويبات العلوية أو من قائمة الإجراءات الجانبية — Choose an option."
        lblPrompt.Dock = DockStyle.Fill
        lblPrompt.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        lblPrompt.ForeColor = UiTheme.BrandNavy
        lblPrompt.BackColor = Color.White
        lblPrompt.TextAlign = ContentAlignment.MiddleRight
        promptCard.Controls.Add(lblPrompt)

        Dim responseCard As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = UiTheme.BrandNavy,
            .GradientBottom = UiTheme.BrandBlue,
            .BorderColor = UiTheme.BrandBlue,
            .CornerRadius = 12,
            .Margin = New Padding(4, 0, 0, 0),
            .Padding = New Padding(12, 2, 12, 2)
        }
        lblResponse.Text = "متصل بقاعدة البيانات | جاهز للتشغيل"
        lblResponse.Dock = DockStyle.Fill
        lblResponse.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        lblResponse.ForeColor = UiTheme.BrandGold
        lblResponse.BackColor = Color.Transparent
        lblResponse.TextAlign = ContentAlignment.MiddleCenter
        responseCard.Controls.Add(lblResponse)

        strip.Controls.Add(promptCard, 0, 0)
        strip.Controls.Add(responseCard, 1, 0)
        Return strip
    End Function

    Private Sub BuildRibbon()
        ribbonPanel.Dock = DockStyle.Fill
        ribbonPanel.Height = 140
        ribbonPanel.BackColor = UiTheme.BrandLight
        ribbonPanel.Padding = New Padding(14, 10, 14, 10)
        ribbonPanel.Visible = False
        ribbonFlow.Dock = DockStyle.Fill
        ribbonFlow.FlowDirection = FlowDirection.RightToLeft
        ribbonFlow.WrapContents = True
        ribbonFlow.AutoScroll = True
        ribbonFlow.BackColor = Color.Transparent
        ribbonPanel.Controls.Add(ribbonFlow)
    End Sub

    Private Sub BuildContent()
        contentHost.Dock = DockStyle.Fill
        contentHost.BackColor = UiTheme.Surface

        centerWorkspace.Dock = DockStyle.Fill
        centerWorkspace.BackColor = Color.White
        centerWorkspace.AutoScroll = True
        AddHandler centerWorkspace.Paint, AddressOf CenterWorkspace_Paint
        AddHandler centerWorkspace.Resize, Sub(s, e) LayoutCenterContent()
        AddHandler centerWorkspace.Click, Sub(s, e) HideRibbon()

        BuildCenterContent()

        rightNavPanel.Dock = DockStyle.Right
        rightNavPanel.Width = 216
        rightNavPanel.BackColor = UiTheme.BrandLight
        rightNavPanel.Padding = New Padding(8, 8, 8, 8)

        Dim lblNavTitle As New Label With {
            .Text = "الإجراءات السريعة",
            .Dock = DockStyle.Top,
            .Height = 32,
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        rightNavFlow.Dock = DockStyle.Fill
        rightNavFlow.FlowDirection = FlowDirection.TopDown
        rightNavFlow.WrapContents = False
        rightNavFlow.AutoScroll = True
        rightNavFlow.BackColor = Color.Transparent

        Dim btnF2DailyOps = MakeNavBtn("F2 / Daily Ops", "العمليات اليومية والوردية", False)
        AddHandler btnF2DailyOps.Click, Sub(s, e) OpenForm(New ShiftForm(_session))

        Dim btnF3Pos = MakeNavBtn("F3 / POS", "نقطة البيع (فاتورة بيع)", True)
        AddHandler btnF3Pos.Click, Sub(s, e) OpenForm(New SaleForm(_session))

        Dim btnF4Admin = MakeNavBtn("F4 / Items", "الإدارة والأصناف", False)
        AddHandler btnF4Admin.Click, Sub(s, e) OpenForm(New ItemsForm(_session))

        Dim btnF5Clock = MakeNavBtn("F5 / Payroll", "الحضور والرواتب", False)
        AddHandler btnF5Clock.Click, Sub(s, e) OpenForm(New HrPayrollForm(_session))

        Dim btnF6Alerts = MakeNavBtn("F6 / Purchase", "المشتريات والتوريد", True)
        AddHandler btnF6Alerts.Click, Sub(s, e) OpenForm(New PurchaseForm(_session))

        Dim btnF7BackOffice = MakeNavBtn("F7 / Reports", "التقارير والميزان", False)
        AddHandler btnF7BackOffice.Click, Sub(s, e) OpenForm(New ReportsForm(_session))

        btnF8Training = MakeNavBtn("F8 / Training", "وضع التدريب", False)
        AddHandler btnF8Training.Click, Sub(s, e) ToggleTrainingMode()

        btnF9Reentry = MakeNavBtn("F9 / Re-entry", "وضع إعادة الإدخال", False)
        AddHandler btnF9Reentry.Click, Sub(s, e) ToggleReentryMode()

        rightNavFlow.Controls.AddRange({btnF2DailyOps, btnF3Pos, btnF4Admin, btnF5Clock, btnF6Alerts, btnF7BackOffice, btnF8Training, btnF9Reentry})
        rightNavPanel.Controls.Add(rightNavFlow)
        rightNavPanel.Controls.Add(lblNavTitle)

        contentHost.Controls.Add(centerWorkspace)
        contentHost.Controls.Add(rightNavPanel)
    End Sub

    Private Function BuildFooter() As Control
        Dim bottom As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavyDark,
            .Padding = New Padding(6, 4, 6, 4)
        }

        Dim statusTable As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 30,
            .ColumnCount = 6,
            .RowCount = 1,
            .BackColor = UiTheme.BrandMist,
            .Padding = New Padding(2)
        }
        For i As Integer = 0 To 5
            statusTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.66F))
        Next
        statusTable.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        InitStatusCell(lblStatusReg)
        InitStatusCell(lblStatusDate)
        InitStatusCell(lblStatusCashier)
        InitStatusCell(lblStatusClock)
        InitStatusCell(lblStatusScreen)
        InitStatusCell(lblStatusMode, True)
        statusTable.Controls.Add(lblStatusReg, 0, 0)
        statusTable.Controls.Add(lblStatusDate, 1, 0)
        statusTable.Controls.Add(lblStatusCashier, 2, 0)
        statusTable.Controls.Add(lblStatusClock, 3, 0)
        statusTable.Controls.Add(lblStatusScreen, 4, 0)
        statusTable.Controls.Add(lblStatusMode, 5, 0)

        Dim globalBar As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 6,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .Padding = New Padding(2, 4, 2, 0)
        }
        For i As Integer = 0 To 5
            globalBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.66F))
        Next
        globalBar.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim gF1 = MakeGlobalBtn("F1 / Help", "مساعدة النظام", False)
        AddHandler gF1.Click, Sub(s, e) ShowAbout()

        Dim gF11 = MakeGlobalBtn("F11 / Refresh", "تحديث الأرصدة", False)
        AddHandler gF11.Click, Sub(s, e) RefreshDashboard()

        Dim gF12 = MakeGlobalBtn("F12 / Cancel", "تسجيل الخروج", True)
        AddHandler gF12.Click, Sub(s, e) Logout()

        Dim gEsc = MakeGlobalBtn("Esc / Undo", "إخفاء القوائم", False)
        AddHandler gEsc.Click, Sub(s, e) HideRibbon()

        Dim gEnter = MakeGlobalBtn("Enter / Next", "فتح شاشة البيع", True)
        AddHandler gEnter.Click, Sub(s, e) OpenForm(New SaleForm(_session))

        Dim brandCell As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = UiTheme.BrandNavy,
            .GradientBottom = UiTheme.BrandBlue,
            .BorderColor = UiTheme.BrandBlue,
            .CornerRadius = 10,
            .Margin = New Padding(3, 1, 3, 1)
        }
        Dim lblBrand As New Label With {
            .Text = "DOCTOR TECH",
            .Dock = DockStyle.Fill,
            .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        brandCell.Controls.Add(lblBrand)

        globalBar.Controls.Add(gF1, 0, 0)
        globalBar.Controls.Add(gF11, 1, 0)
        globalBar.Controls.Add(gF12, 2, 0)
        globalBar.Controls.Add(gEsc, 3, 0)
        globalBar.Controls.Add(gEnter, 4, 0)
        globalBar.Controls.Add(brandCell, 5, 0)

        bottom.Controls.Add(globalBar)
        bottom.Controls.Add(statusTable)
        Return bottom
    End Function

    Private Shared Sub InitStatusCell(lbl As Label, Optional isMode As Boolean = False)
        lbl.Dock = DockStyle.Fill
        lbl.BackColor = Color.White
        lbl.ForeColor = If(isMode, UiTheme.BrandNavy, UiTheme.Ink)
        lbl.Font = UiTheme.Font(8.8F, FontStyle.Bold)
        lbl.TextAlign = ContentAlignment.MiddleCenter
        lbl.Margin = New Padding(2, 1, 2, 1)
    End Sub

    Private Shared Function MakeNavBtn(fKey As String, caption As String, highlight As Boolean) As GlassButton
        Dim b As New GlassButton With {
            .Text = fKey & Environment.NewLine & caption,
            .Width = 196,
            .Height = 50,
            .Font = UiTheme.Font(9.2F, FontStyle.Bold),
            .ForeColor = Color.White,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(2, 3, 2, 3),
            .CornerRadius = 11
        }
        If highlight Then
            b.SetTone(UiTheme.BrandCyan, UiTheme.BrandNavyDark)
        Else
            b.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        End If
        Return b
    End Function

    Private Shared Function MakeGlobalBtn(fKey As String, caption As String, isDanger As Boolean) As GlassButton
        Dim b As New GlassButton With {
            .Text = fKey & Environment.NewLine & caption,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(9.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(3, 2, 3, 2),
            .CornerRadius = 10
        }
        If isDanger Then
            b.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        Else
            b.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        End If
        Return b
    End Function

    Private Sub ToggleTrainingMode()
        _trainingMode = Not _trainingMode
        If _trainingMode Then _reentryMode = False
        btnF8Training.Text = If(_trainingMode, "F8 / Training Off" & Environment.NewLine & "إيقاف وضع التدريب",
                                "F8 / Training" & Environment.NewLine & "وضع التدريب")
        btnF8Training.SetTone(If(_trainingMode, UiTheme.WarningOrange, UiTheme.BrandBlue),
                              If(_trainingMode, Color.FromArgb(140, 70, 10), UiTheme.BrandNavy))
        btnF9Reentry.Text = "F9 / Re-entry" & Environment.NewLine & "وضع إعادة الإدخال"
        btnF9Reentry.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        UpdateFooterText()
        centerWorkspace.Invalidate()
    End Sub

    Private Sub ToggleReentryMode()
        _reentryMode = Not _reentryMode
        If _reentryMode Then _trainingMode = False
        btnF9Reentry.Text = If(_reentryMode, "F9 / Re-entry Off" & Environment.NewLine & "إيقاف إعادة الإدخال",
                               "F9 / Re-entry" & Environment.NewLine & "وضع إعادة الإدخال")
        btnF9Reentry.SetTone(If(_reentryMode, UiTheme.WarningOrange, UiTheme.BrandBlue),
                             If(_reentryMode, Color.FromArgb(140, 70, 10), UiTheme.BrandNavy))
        btnF8Training.Text = "F8 / Training" & Environment.NewLine & "وضع التدريب"
        btnF8Training.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        UpdateFooterText()
        centerWorkspace.Invalidate()
    End Sub

    Private Sub AddTopTab(key As String, text As String, width As Integer)
        Dim btn As New GlassButton With {
            .Text = text,
            .AutoSize = False,
            .Size = New Size(width, 36),
            .Margin = New Padding(3, 1, 3, 1),
            .ForeColor = Color.White,
            .Font = UiTheme.Font(9.5F, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .CornerRadius = 10
        }
        btn.SetTone(UiTheme.BrandNavy, UiTheme.BrandNavyDark)
        AddHandler btn.Click, Sub(s, e) ToggleRibbonTab(key)
        _tabButtons(key) = btn
        topFlow.Controls.Add(btn)
    End Sub

    Private Sub ToggleRibbonTab(key As String)
        If _activeTabKey = key AndAlso ribbonPanel.Visible Then
            HideRibbon()
            Return
        End If
        _activeTabKey = key
        UpdateTabTones()
        Dim cards As List(Of RibbonCardDef) = GetRibbonCardsForTab(key)
        ribbonFlow.SuspendLayout()
        ribbonFlow.Controls.Clear()
        For Each c In cards
            ribbonFlow.Controls.Add(CreateRibbonCardControl(c))
        Next
        ribbonFlow.ResumeLayout()
        ribbonRowStyle.Height = If(cards.Count > 11, 236.0F, 140.0F)
        ribbonPanel.Height = CInt(ribbonRowStyle.Height)
        ribbonPanel.Visible = True
        LayoutCenterContent()
    End Sub

    Private Sub HideRibbon()
        ribbonPanel.Visible = False
        ribbonRowStyle.Height = 0
        _activeTabKey = String.Empty
        UpdateTabTones()
        LayoutCenterContent()
    End Sub

    Private Sub UpdateTabTones()
        For Each kvp In _tabButtons
            Dim btn As GlassButton = TryCast(kvp.Value, GlassButton)
            If btn Is Nothing Then Continue For
            If kvp.Key = _activeTabKey Then
                btn.SetTone(UiTheme.BrandCyan, UiTheme.BrandNavyDark)
            Else
                btn.SetTone(UiTheme.BrandNavy, UiTheme.BrandNavyDark)
            End If
        Next
    End Sub

    Private Function GetRibbonCardsForTab(key As String) As List(Of RibbonCardDef)
        Dim list As New List(Of RibbonCardDef)()
        Dim cNavy As Color = UiTheme.BrandNavyDark
        Dim cBlue As Color = UiTheme.BrandBlue
        Dim cCyan As Color = UiTheme.BrandCyan
        Dim cGold As Color = Color.FromArgb(198, 148, 30)
        Dim cRed As Color = UiTheme.DangerRed

        Select Case key
            Case "basic"
                list.Add(New RibbonCardDef("بيانات الشركة", "🏢", cNavy, Sub() OpenForm(New CompanyForm(_session))))
                list.Add(New RibbonCardDef("تعريف الفروع", "🌐", cBlue, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Branches()))))
                list.Add(New RibbonCardDef("قيود افتتاحية", "📒", cGold, Sub() OpenForm(New OpeningEntryForm(_session))))
                list.Add(New RibbonCardDef("تعريف الخزن", "🗄️", cBlue, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Cashboxes()))))
                list.Add(New RibbonCardDef("تعريف المخازن", "📦", cGold, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Warehouses()))))
                list.Add(New RibbonCardDef("تعريف البنوك", "🏦", cBlue, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Banks()))))
                list.Add(New RibbonCardDef("تعريف مصروفات", "💸", cCyan, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.ExpenseAccounts()))))
                list.Add(New RibbonCardDef("مجموعات رئيسية", "📁", cGold, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.ItemGroups()))))
                list.Add(New RibbonCardDef("تعريف الأصناف", "🏷️", cNavy, Sub() OpenForm(New ItemsForm(_session))))
                list.Add(New RibbonCardDef("الوحدات", "📏", cGold, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Units()))))
                list.Add(New RibbonCardDef("بيانات المناطق", "🗺️", cBlue, Sub() OpenForm(New DefinitionForm(_session, DefinitionsCatalog.Areas()))))
            Case "customers"
                list.Add(New RibbonCardDef("واتساب", "💬", cCyan, Sub() OpenForm(New WhatsAppForm(_session))))
                list.Add(New RibbonCardDef("بيانات العملاء", "👥", cNavy, Sub() OpenForm(New CustomersForm(_session, "customer"))))
                list.Add(New RibbonCardDef("نقاطي", "⭐", cGold, Sub() OpenForm(New LoyaltyForm(_session))))
                list.Add(New RibbonCardDef("أرصدة حسابات العملاء", "⚖️", cBlue, Sub() OpenForm(New CustomerBalancesForm(_session))))
                list.Add(New RibbonCardDef("حركة آخر سداد", "💰", cGold, Sub() OpenForm(New CustomerPaymentsForm(_session))))
                list.Add(New RibbonCardDef("مجموعات العملاء", "📁", cCyan, Sub() OpenForm(New CustomerGroupsForm(_session))))
                list.Add(New RibbonCardDef("كوبون خصم", "🎟️", cGold, Sub() OpenForm(New CouponsForm(_session))))
                list.Add(New RibbonCardDef("قسيمة مشتريات", "💳", cNavy, Sub() OpenForm(New PurchaseVouchersForm(_session))))
            Case "hr"
                list.Add(New RibbonCardDef("صرف الرواتب", "💰", cGold, Sub() OpenForm(New HrPayrollForm(_session))))
                list.Add(New RibbonCardDef("تعريف الإدارات", "🏢", cNavy, Sub() OpenForm(New HrDepartmentsForm(_session))))
                list.Add(New RibbonCardDef("تعريف الأقسام", "🗂️", cGold, Sub() OpenForm(New HrSectionsForm(_session))))
                list.Add(New RibbonCardDef("تعريف الموظف", "👤", cBlue, Sub() OpenForm(New HrEmployeesForm(_session, "employee"))))
                list.Add(New RibbonCardDef("تعريف بائعين", "💼", cGold, Sub() OpenForm(New HrEmployeesForm(_session, "seller"))))
                list.Add(New RibbonCardDef("تقرير عمولة بائعين", "📊", cCyan, Sub() OpenForm(New HrCommissionsForm(_session, "seller"))))
                list.Add(New RibbonCardDef("تعريف دليفري", "🛵", cCyan, Sub() OpenForm(New HrEmployeesForm(_session, "delivery"))))
                list.Add(New RibbonCardDef("تقرير عمولة دليفري", "📋", cBlue, Sub() OpenForm(New HrCommissionsForm(_session, "delivery"))))
            Case "sales"
                list.Add(New RibbonCardDef("فاتورة موبايل", "📱", cCyan, Sub() OpenForm(New MobileSaleForm(_session))))
                list.Add(New RibbonCardDef("فاتورة مشتريات", "🛒", cBlue, Sub() OpenForm(New PurchaseForm(_session))))
                list.Add(New RibbonCardDef("مرتجع مشتريات", "↩️", cGold, Sub() OpenForm(New ReturnsForm(_session, "purchase"))))
                list.Add(New RibbonCardDef("فاتورة مبيعات POS", "🧾", cNavy, Sub() OpenForm(New SaleForm(_session))))
                list.Add(New RibbonCardDef("مرتجع مبيعات", "↩️", cGold, Sub() OpenForm(New ReturnsForm(_session, "sale"))))
                list.Add(New RibbonCardDef("عرض سعر خدمات", "📋", cCyan, Sub() OpenForm(New QuotesForm(_session))))
                list.Add(New RibbonCardDef("طباعة باركود", "🖨️", cBlue, Sub() OpenForm(New BarcodePrintForm(_session))))
                list.Add(New RibbonCardDef("الورديات", "🕒", cCyan, Sub() OpenForm(New ShiftsForm(_session))))
            Case "reports"
                list.Add(New RibbonCardDef("الداشبورد الحية", "📊", cCyan, Sub() OpenForm(New ReportsForm(_session, "sales_by_day"))))
                list.Add(New RibbonCardDef("داشبورد المحل", "📊", cNavy, Sub() OpenForm(New ReportsForm(_session, "sales_by_day"))))
                list.Add(New RibbonCardDef("تقارير بالتواريخ", "📅", cRed, Sub() OpenForm(New ReportsForm(_session, "sales_by_day"))))
                list.Add(New RibbonCardDef("فواتير بالعملاء", "🧾", cCyan, Sub() OpenForm(New ReportsForm(_session, "sales_invoices"))))
                list.Add(New RibbonCardDef("مبيعات ومشتريات", "📝", cGold, Sub() OpenForm(New ReportsForm(_session, "sales_by_type"))))
                list.Add(New RibbonCardDef("أرباح مبيعات", "💰", cGold, Sub() OpenForm(New ReportsForm(_session, "profit"))))
                list.Add(New RibbonCardDef("أرباح الأقسام", "🏢", cBlue, Sub() OpenForm(New ReportsForm(_session, "profit_groups"))))
                list.Add(New RibbonCardDef("حركة يومية", "📆", cBlue, Sub() OpenForm(New ReportsForm(_session, "cash_moves"))))
                list.Add(New RibbonCardDef("تحليل الحركات", "🔍", cCyan, Sub() OpenForm(New ReportsForm(_session, "audit_log"))))
                list.Add(New RibbonCardDef("الأكثر مبيعاً", "🔥", cNavy, Sub() OpenForm(New ReportsForm(_session, "top_items"))))
                list.Add(New RibbonCardDef("مبيعات طرق الدفع", "💳", cGold, Sub() OpenForm(New ReportsForm(_session, "cash_summary"))))
                list.Add(New RibbonCardDef("أصناف حد الطلب", "⚠️", cGold, Sub() OpenForm(New StockForm(_session, False, "order_room"))))
                list.Add(New RibbonCardDef("تقييم المخزون", "📦", cGold, Sub() OpenForm(New ReportsForm(_session, "stock_value"))))
                list.Add(New RibbonCardDef("الإقرار الضريبي", "🧾", cBlue, Sub() OpenForm(New ReportsForm(_session, "tax"))))
                list.Add(New RibbonCardDef("مديونيات العملاء", "👥", cCyan, Sub() OpenForm(New ReportsForm(_session, "customer_debt"))))
                list.Add(New RibbonCardDef("ميزان المراجعة", "⚖️", cGold, Sub() OpenForm(New ReportsForm(_session, "trial_balance"))))
            Case "stock"
                list.Add(New RibbonCardDef("تنبؤ المخزون الذكي", "🔮", cCyan, Sub() OpenForm(New StockForm(_session, False, "forecast"))))
                list.Add(New RibbonCardDef("غرفة الطلبية", "🧠", cRed, Sub() OpenForm(New StockForm(_session, False, "order_room"))))
                list.Add(New RibbonCardDef("مخزون أول المدة", "📦", cGold, Sub() OpenForm(New OpeningStockForm(_session))))
                list.Add(New RibbonCardDef("حركات المخازن", "🔄", cNavy, Sub() OpenForm(New TransferForm(_session, False))))
                list.Add(New RibbonCardDef("تقرير تحويلات", "📋", cGold, Sub() OpenForm(New TransferForm(_session, True))))
                list.Add(New RibbonCardDef("كشف تجهيز الجرد", "📋", cBlue, Sub() OpenForm(New InventoryCountForm(_session))))
                list.Add(New RibbonCardDef("كشف جرد المخازن", "📦", cGold, Sub() OpenForm(New StockForm(_session, False, "balances"))))
                list.Add(New RibbonCardDef("جرد سكانر", "🧮", cCyan, Sub() OpenForm(New InventoryCountForm(_session))))
                list.Add(New RibbonCardDef("أرصدة المخزن", "📊", cCyan, Sub() OpenForm(New StockForm(_session, False, "balances"))))
                list.Add(New RibbonCardDef("حركة صنف محددة", "🔍", cCyan, Sub() OpenForm(New StockForm(_session, False, "balances"))))
            Case "money"
                list.Add(New RibbonCardDef("تحويل بين خزائن", "💸", cCyan, Sub() OpenForm(New ShiftForm(_session, "transfer"))))
                list.Add(New RibbonCardDef("سندات قبض للخزينة", "🧾", cNavy, Sub() OpenForm(New ShiftForm(_session, "payin"))))
                list.Add(New RibbonCardDef("سندات صرف للخزينة", "🧾", cGold, Sub() OpenForm(New ShiftForm(_session, "payout"))))
                list.Add(New RibbonCardDef("أرصدة الخزن", "💰", cGold, Sub() OpenForm(New ShiftForm(_session, "balances"))))
                list.Add(New RibbonCardDef("حركة خزينة", "📒", cGold, Sub() OpenForm(New ShiftForm(_session, "ledger"))))
                list.Add(New RibbonCardDef("إدارة الوردية", "🕒", cCyan, Sub() OpenForm(New ShiftForm(_session, "shift"))))
                list.Add(New RibbonCardDef("تقرير مصروفات", "📊", cBlue, Sub() OpenForm(New ReportsForm(_session, "expenses_report"))))
                list.Add(New RibbonCardDef("ميزان المراجعة", "⚖️", cGold, Sub() OpenForm(New ReportsForm(_session, "trial_balance"))))
                list.Add(New RibbonCardDef("كشف حساب تفصيلي", "🔍", cNavy, Sub() OpenForm(New CustomersForm(_session, "customer"))))
            Case "offers"
                list.Add(New RibbonCardDef("عروض الأصناف", "🏷️", cGold, Sub() OpenForm(New ItemsForm(_session))))
                list.Add(New RibbonCardDef("كوبونات الخصم", "🎟️", cNavy, Sub() OpenForm(New CouponsForm(_session))))
                list.Add(New RibbonCardDef("نقاط الولاء", "⭐", cCyan, Sub() OpenForm(New LoyaltyForm(_session))))
            Case "settings"
                list.Add(New RibbonCardDef("لوحة تحكم المحل", "🎛️", cNavy, Sub() OpenForm(New SettingsForm(_session, "store"))))
                list.Add(New RibbonCardDef("كوبري المزامنة", "🔄", cCyan, Sub() OpenForm(New SyncForm(_session, _config, False))))
                list.Add(New RibbonCardDef("خصائص المستخدم", "⚙️", cBlue, Sub() ChangePassword()))
                list.Add(New RibbonCardDef("إعدادات الشاشات", "🎨", cGold, Sub() OpenForm(New SettingsForm(_session, "screen"))))
                list.Add(New RibbonCardDef("نسخة احتياطية (.SQL)", "💾", cNavy, Sub() OpenForm(New SyncForm(_session, _config, True))))
            Case "about"
                list.Add(New RibbonCardDef("عن البرنامج", "🔷", cBlue, Sub() ShowAbout()))
                list.Add(New RibbonCardDef("الخروج من البرنامج", "🚪", cRed, Sub() Logout()))
        End Select
        Return list
    End Function

    Private Function CreateRibbonCardControl(def As RibbonCardDef) As Control
        Dim card As New Panel With {
            .Size = New Size(98, 98),
            .Margin = New Padding(5, 4, 5, 4),
            .BackColor = Color.White,
            .Cursor = Cursors.Hand
        }
        SetRounded(card, 14)

        AddHandler card.Paint, Sub(s, e)
                                   Dim g As Graphics = e.Graphics
                                   g.SmoothingMode = SmoothingMode.AntiAlias
                                   Using pen As New Pen(UiTheme.BrandBorder, 1.4F)
                                       g.DrawPath(pen, GetRoundPath(New Rectangle(0, 0, card.Width - 1, card.Height - 1), 14))
                                   End Using
                                   Dim sf As New StringFormat With {
                                       .Alignment = StringAlignment.Center,
                                       .LineAlignment = StringAlignment.Center
                                   }
                                   Using iconBrush As New SolidBrush(def.IconColor)
                                       g.DrawString(def.IconText, New Font("Segoe UI Emoji", 18.0F, FontStyle.Regular), iconBrush, New RectangleF(0, 8, card.Width, 42), sf)
                                   End Using
                                   Using txtBrush As New SolidBrush(UiTheme.BrandNavy)
                                       g.DrawString(def.Title, UiTheme.Font(8.2F, FontStyle.Bold), txtBrush, New RectangleF(2, 50, card.Width - 4, 44), sf)
                                   End Using
                               End Sub

        AddHandler card.Click, Sub(s, e) If def.Action IsNot Nothing Then def.Action.Invoke()
        AddHandler card.MouseEnter, Sub(s, e) card.BackColor = UiTheme.BrandLight
        AddHandler card.MouseLeave, Sub(s, e) card.BackColor = Color.White
        Return card
    End Function

    Private Function CreateBigActionCard(title As String, subtitle As String, topColor As Color, bottomColor As Color, iconKind As String, onClick As Action) As Panel
        Dim card As New Panel With {
            .Size = New Size(200, 230),
            .Cursor = Cursors.Hand,
            .BackColor = bottomColor
        }
        SetRounded(card, 18)

        Dim hovered As Boolean = False

        AddHandler card.Paint, Sub(s, e)
                                   Dim g As Graphics = e.Graphics
                                   g.SmoothingMode = SmoothingMode.AntiAlias
                                   Dim bounds As New Rectangle(0, 0, card.Width - 1, card.Height - 1)

                                   Using path As GraphicsPath = GetRoundPath(bounds, 18)
                                       Using bodyBrush As New LinearGradientBrush(bounds, topColor, bottomColor, LinearGradientMode.Vertical)
                                           g.FillPath(bodyBrush, path)
                                       End Using

                                       Using glassBrush As New LinearGradientBrush(
                                               bounds,
                                               Color.FromArgb(If(hovered, 82, 58), 255, 255, 255),
                                               Color.FromArgb(6, 255, 255, 255),
                                               LinearGradientMode.Vertical)
                                           g.FillPath(glassBrush, path)
                                       End Using

                                       Dim halfH As Integer = Math.Max(1, card.Height \ 2)
                                       Dim oldClip As Region = g.Clip
                                       g.SetClip(path, CombineMode.Intersect)
                                       Using shineBrush As New LinearGradientBrush(
                                               New Rectangle(0, 0, card.Width, halfH),
                                               Color.FromArgb(If(hovered, 70, 48), 255, 255, 255),
                                               Color.FromArgb(0, 255, 255, 255),
                                               LinearGradientMode.Vertical)
                                           g.FillRectangle(shineBrush, 0, 0, card.Width, halfH)
                                       End Using
                                       g.Clip = oldClip

                                       Using glassPen As New Pen(Color.FromArgb(If(hovered, 200, 135), 255, 255, 255), 1.6F)
                                           g.DrawPath(glassPen, path)
                                       End Using
                                   End Using

                                   DrawCardIcon(g, iconKind, New Rectangle((card.Width - 64) \ 2, 44, 64, 64))
                                   Dim sf As New StringFormat With {.Alignment = StringAlignment.Center}
                                   g.DrawString(title, UiTheme.Font(14.0F, FontStyle.Bold), Brushes.White, New RectangleF(0, 140, card.Width, 34), sf)
                                   If Not String.IsNullOrEmpty(subtitle) Then
                                       Using subBr As New SolidBrush(Color.FromArgb(230, 240, 255))
                                           g.DrawString(subtitle, UiTheme.Font(9.5F, FontStyle.Bold), subBr, New RectangleF(0, 178, card.Width, 26), sf)
                                       End Using
                                   End If
                               End Sub

        AddHandler card.MouseEnter, Sub(s, e)
                                        hovered = True
                                        card.Invalidate()
                                    End Sub
        AddHandler card.MouseLeave, Sub(s, e)
                                        hovered = False
                                        card.Invalidate()
                                    End Sub
        AddHandler card.Click, Sub(s, e) onClick()
        Return card
    End Function

    Private Shared Sub DrawCardIcon(g As Graphics, kind As String, rect As Rectangle)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim glassWhite As Color = Color.FromArgb(242, 255, 255, 255)
        Dim fillWhite As Color = Color.FromArgb(46, 255, 255, 255)

        Using pen As New Pen(glassWhite, 3.0F)
            pen.StartCap = LineCap.Round
            pen.EndCap = LineCap.Round
            pen.LineJoin = LineJoin.Round

            Select Case kind
                Case "receipt"
                    Dim w As Integer = 38
                    Dim h As Integer = 48
                    Dim x As Integer = rect.X + (rect.Width - w) \ 2
                    Dim y As Integer = rect.Y + (rect.Height - h) \ 2
                    Using body As New GraphicsPath()
                        body.AddLine(x, y, x + w, y)
                        body.AddLine(x + w, y, x + w, y + h - 10)
                        body.AddLine(x + w, y + h - 10, x + w - 9, y + h)
                        body.AddLine(x + w - 9, y + h, x + w - 18, y + h - 10)
                        body.AddLine(x + w - 18, y + h - 10, x + w - 27, y + h)
                        body.AddLine(x + w - 27, y + h, x, y + h - 10)
                        body.CloseFigure()
                        Using fill As New SolidBrush(fillWhite)
                            g.FillPath(fill, body)
                        End Using
                        g.DrawPath(pen, body)
                    End Using
                    Using linePen As New Pen(Color.FromArgb(230, 255, 255, 255), 2.4F)
                        linePen.StartCap = LineCap.Round
                        linePen.EndCap = LineCap.Round
                        g.DrawLine(linePen, x + 9, y + 14, x + w - 9, y + 14)
                        g.DrawLine(linePen, x + 9, y + 24, x + w - 9, y + 24)
                        g.DrawLine(linePen, x + 9, y + 34, x + w - 16, y + 34)
                    End Using

                Case "box"
                    Dim w As Integer = 46
                    Dim h As Integer = 42
                    Dim x As Integer = rect.X + (rect.Width - w) \ 2
                    Dim y As Integer = rect.Y + 12
                    Dim half As Integer = w \ 2
                    Dim topH As Integer = 12
                    Dim topFace As PointF() = {
                        New PointF(x + half, y),
                        New PointF(x + w, y + topH),
                        New PointF(x + half, y + topH * 2),
                        New PointF(x, y + topH)
                    }
                    Dim leftFace As PointF() = {
                        New PointF(x, y + topH),
                        New PointF(x + half, y + topH * 2),
                        New PointF(x + half, y + h),
                        New PointF(x, y + h - topH)
                    }
                    Dim rightFace As PointF() = {
                        New PointF(x + half, y + topH * 2),
                        New PointF(x + w, y + topH),
                        New PointF(x + w, y + h - topH),
                        New PointF(x + half, y + h)
                    }
                    Using topFill As New SolidBrush(Color.FromArgb(72, 255, 255, 255))
                        g.FillPolygon(topFill, topFace)
                    End Using
                    Using sideFill As New SolidBrush(Color.FromArgb(34, 255, 255, 255))
                        g.FillPolygon(sideFill, leftFace)
                        g.FillPolygon(sideFill, rightFace)
                    End Using
                    g.DrawPolygon(pen, topFace)
                    g.DrawPolygon(pen, leftFace)
                    g.DrawPolygon(pen, rightFace)
                Case Else
                    Dim baseY As Integer = rect.Y + 52
                    Dim barW As Integer = 8
                    Dim xs As Integer() = {rect.X + 9, rect.X + 23, rect.X + 37, rect.X + 51}
                    Dim hs As Integer() = {14, 26, 19, 36}
                    Using axisPen As New Pen(Color.FromArgb(150, 255, 255, 255), 2.0F)
                        g.DrawLine(axisPen, rect.X + 6, baseY, rect.Right - 6, baseY)
                    End Using
                    For i As Integer = 0 To xs.Length - 1
                        Using barBrush As New SolidBrush(Color.FromArgb(215, 255, 255, 255))
                            g.FillRectangle(barBrush, xs(i), baseY - hs(i), barW, hs(i))
                        End Using
                    Next
            End Select
        End Using
    End Sub

    Private Shared Function GetRoundPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Return UiTheme.RoundedPath(rect, radius)
    End Function

    Private Shared Sub SetRounded(ctrl As Control, radius As Integer)
        UiTheme.SetRoundedRegion(ctrl, radius)
    End Sub

    Private Sub BuildCenterContent()
        logoCard.Size = New Size(340, 250)
        logoCard.BackColor = Color.White
        logoCard.Anchor = AnchorStyles.None
        AddHandler logoCard.Paint, AddressOf DrawCenterLogoCard
        SetRounded(logoCard, 22)
        centerWorkspace.Controls.Add(logoCard)

        lblMainPharmacy.Text = "DOCTOR TECH"
        lblMainPharmacy.Font = UiTheme.Font(22.0F, FontStyle.Bold)
        lblMainPharmacy.ForeColor = Color.White
        lblMainPharmacy.Size = New Size(420, 38)
        lblMainPharmacy.TextAlign = ContentAlignment.MiddleCenter
        lblMainPharmacy.BackColor = Color.Transparent
        centerWorkspace.Controls.Add(lblMainPharmacy)

        Dim lblSubtitle As New Label With {
            .Text = "نقاط البيع  •  المخازن  •  الحسابات  •  التقارير",
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .Size = New Size(420, 26),
            .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = Color.Transparent,
            .Name = "lblBrandSubtitle"
        }
        centerWorkspace.Controls.Add(lblSubtitle)

        cardsContainer.Size = New Size(660, 230)
        cardsContainer.BackColor = Color.Transparent

        Dim cardReports As Panel = CreateBigActionCard(
            "F7 / التقارير", "الداشبورد والميزان",
            UiTheme.BrandBlue, UiTheme.BrandNavy,
            "chart", Sub() OpenForm(New ReportsForm(_session)))
        cardReports.Location = New Point(0, 0)

        Dim cardPurchase As Panel = CreateBigActionCard(
            "F6 / فاتورة شراء", "توريد مخزني مباشر",
            UiTheme.BrandCyan, UiTheme.BrandNavy,
            "box", Sub() OpenForm(New PurchaseForm(_session)))
        cardPurchase.Location = New Point(230, 0)

        Dim cardSale As Panel = CreateBigActionCard(
            "F3 / فاتورة بيع", "شاشة الكاشير الاحترافية",
            Color.FromArgb(32, 152, 232), UiTheme.BrandNavyDark,
            "receipt", Sub() OpenForm(New SaleForm(_session)))
        cardSale.Location = New Point(460, 0)

        cardsContainer.Controls.Add(cardReports)
        cardsContainer.Controls.Add(cardPurchase)
        cardsContainer.Controls.Add(cardSale)

        centerWorkspace.Controls.Add(cardsContainer)

        kpiTable.Dock = DockStyle.Bottom
        kpiTable.Height = 94
        kpiTable.ColumnCount = 4
        kpiTable.RowCount = 1
        kpiTable.BackColor = Color.Transparent
        kpiTable.Padding = New Padding(10, 6, 10, 10)
        For i As Integer = 0 To 3
            kpiTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        kpiTable.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        kpiTable.Controls.Add(MakeKpiTile("الأصناف", lblKpiItems, UiTheme.BrandNavy), 0, 0)
        kpiTable.Controls.Add(MakeKpiTile("فواتير اليوم", lblKpiInvoices, UiTheme.BrandBlue), 1, 0)
        kpiTable.Controls.Add(MakeKpiTile("مبيعات اليوم", lblKpiSales, UiTheme.BrandCyan), 2, 0)
        kpiTable.Controls.Add(MakeKpiTile("الوردية", lblKpiShift, UiTheme.BrandNavyDark), 3, 0)
        centerWorkspace.Controls.Add(kpiTable)
    End Sub

    Private Shared Function MakeKpiTile(caption As String, valueLabel As Label, accent As Color) As Control
        Dim tile As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = Color.White,
            .BorderColor = UiTheme.BrandBorder,
            .CornerRadius = 14,
            .Margin = New Padding(5, 2, 5, 2),
            .Padding = New Padding(8, 4, 8, 4)
        }
        Dim head As New Label With {
            .Text = caption,
            .Dock = DockStyle.Top,
            .Height = 22,
            .Font = UiTheme.Font(8.8F, FontStyle.Bold),
            .ForeColor = UiTheme.Muted,
            .BackColor = Color.White,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        valueLabel.Dock = DockStyle.Fill
        valueLabel.Font = UiTheme.Font(13.5F, FontStyle.Bold)
        valueLabel.ForeColor = accent
        valueLabel.BackColor = Color.White
        valueLabel.TextAlign = ContentAlignment.MiddleCenter
        valueLabel.Text = "—"
        tile.Controls.Add(valueLabel)
        tile.Controls.Add(head)
        Return tile
    End Function

    Private Sub LayoutCenterContent()
        Dim w As Integer = centerWorkspace.ClientSize.Width
        Dim h As Integer = centerWorkspace.ClientSize.Height - kpiTable.Height
        If w <= 0 OrElse h <= 0 Then Return

        Dim subtitle As Label = TryCast(centerWorkspace.Controls("lblBrandSubtitle"), Label)
        Dim subtitleHeight As Integer = If(subtitle IsNot Nothing, subtitle.Height, 0)
        Dim totalH As Integer = logoCard.Height + 10 + lblMainPharmacy.Height + 4 + subtitleHeight + 12 + cardsContainer.Height
        Dim startY As Integer = Math.Max(8, (h - totalH) \ 2)

        logoCard.Location = New Point((w - logoCard.Width) \ 2, startY)
        lblMainPharmacy.Location = New Point((w - lblMainPharmacy.Width) \ 2, logoCard.Bottom + 8)
        If subtitle IsNot Nothing Then
            subtitle.Location = New Point((w - subtitle.Width) \ 2, lblMainPharmacy.Bottom + 2)
            cardsContainer.Location = New Point((w - cardsContainer.Width) \ 2, subtitle.Bottom + 12)
        Else
            cardsContainer.Location = New Point((w - cardsContainer.Width) \ 2, lblMainPharmacy.Bottom + 12)
        End If
    End Sub

    Private Sub CenterWorkspace_Paint(sender As Object, e As PaintEventArgs)
        e.Graphics.Clear(Color.White)
    End Sub

    Private Sub DrawCenterLogoCard(sender As Object, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim bounds As New Rectangle(0, 0, logoCard.Width - 1, logoCard.Height - 1)
        Using path As GraphicsPath = GetRoundPath(bounds, 22)
            Using brush As New SolidBrush(Color.White)
                g.FillPath(brush, path)
            End Using
            Using pen As New Pen(UiTheme.BrandBorder, 1.4F)
                g.DrawPath(pen, path)
            End Using
        End Using

        Dim logoImage As Image = UiTheme.LoadBrandLogo()
        If logoImage IsNot Nothing Then
            Dim side As Integer = Math.Min(logoCard.Width - 40, logoCard.Height - 46)
            Dim target As New Rectangle((logoCard.Width - side) \ 2, 12, side, side)
            g.DrawImage(logoImage, target)
        Else
            Dim sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
            g.DrawString("DOCTOR TECH", UiTheme.Font(16.0F, FontStyle.Bold), New SolidBrush(UiTheme.BrandNavy),
                         New RectangleF(0, 50, logoCard.Width, 40), sf)
        End If

        Dim sfBottom As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
        g.DrawString("DOCTOR TECH", UiTheme.Font(10.0F, FontStyle.Bold), New SolidBrush(UiTheme.BrandNavy),
                     New RectangleF(0, logoCard.Height - 34, logoCard.Width, 24), sfBottom)
    End Sub

    Private Sub ChangePassword()
        Using dialog As New ChangePasswordForm(_session)
            UiChrome.Apply(dialog)
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Sub ShowAbout()
        UiTheme.Info("DOCTOR TECH — نظام إدارة نقاط البيع والمخازن والحسابات." & Environment.NewLine &
                     "متصل مباشرة بقاعدة بيانات MySQL مع دعم كامل للورديات، الربحية، الباركود، والولاء." & Environment.NewLine &
                     "© " & Date.Today.Year.ToString() & " DOCTOR TECH. جميع الحقوق محفوظة.", "عن البرنامج")
    End Sub

    Private Sub MainKeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.F1
                ShowAbout() : e.Handled = True
            Case Keys.F2
                OpenForm(New ShiftForm(_session)) : e.Handled = True
            Case Keys.F3, Keys.Enter
                OpenForm(New SaleForm(_session)) : e.Handled = True
            Case Keys.F4
                OpenForm(New ItemsForm(_session)) : e.Handled = True
            Case Keys.F5
                OpenForm(New HrPayrollForm(_session)) : e.Handled = True
            Case Keys.F6
                OpenForm(New PurchaseForm(_session)) : e.Handled = True
            Case Keys.F7
                OpenForm(New ReportsForm(_session)) : e.Handled = True
            Case Keys.F8
                ToggleTrainingMode() : e.Handled = True
            Case Keys.F9
                ToggleReentryMode() : e.Handled = True
            Case Keys.F11
                RefreshDashboard() : e.Handled = True
            Case Keys.F12
                Logout() : e.Handled = True
            Case Keys.Escape
                If ribbonPanel.Visible Then HideRibbon() : e.Handled = True
        End Select
    End Sub

    Private Sub OpenForm(form As Form)
        Try
            UiChrome.Apply(form)
            form.ShowDialog(Me)
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            form.Dispose()
        End Try
        RefreshDashboard()
    End Sub

    Public Sub RefreshDashboard()
        Try
            _itemsCount = ItemService.CountAll(_session, True)
        Catch ex As Exception
            AppLog.Warn("Dashboard items count failed: " & ex.Message)
        End Try

        Try
            Dim todayInvs = SalesService.ListInvoices(_session, Date.Today, Date.Today, "sale")
            _todayInvoicesCount = todayInvs.Count
            _todaySalesTotal = todayInvs.Sum(Function(x) x.Total)
        Catch
        End Try

        Try
            Dim sh = ShiftService.Current(_session)
            If sh Is Nothing Then
                _shiftStatusText = "مغلقة (اضغط F2 للفتح)"
            Else
                Dim exp = ShiftService.ExpectedCash(_session, sh.Id)
                _shiftStatusText = sh.ShiftNo & " | بالدرج: " & UiTheme.Money(exp)
            End If
        Catch
        End Try

        UpdateFooterText()
    End Sub

    Private Sub UpdateFooterText()
        lblStatusReg.Text = $"Register: REG-{_session.BranchId:00} ({_session.BranchName})"
        lblStatusDate.Text = $"Date: {Date.Today:yyyy-MM-dd}"
        lblStatusCashier.Text = $"Cashier: {_session.Username}"
        lblStatusClock.Text = $"Time: {Date.Now:hh:mm:ss tt}"
        lblStatusScreen.Text = $"الأصناف: {_itemsCount:N0} | فواتير اليوم: {_todayInvoicesCount}"

        If _trainingMode Then
            lblStatusMode.Text = "🟠 *Training Mode*"
            lblStatusMode.ForeColor = UiTheme.WarningOrange
        ElseIf _reentryMode Then
            lblStatusMode.Text = "🟡 *Transaction Re-entry*"
            lblStatusMode.ForeColor = Color.FromArgb(150, 110, 0)
        Else
            lblStatusMode.Text = "🔵 Online — DB Active"
            lblStatusMode.ForeColor = UiTheme.BrandNavy
        End If

        lblResponse.Text = $"الوردية: {_shiftStatusText}   |   مبيعات اليوم: {UiTheme.Money(_todaySalesTotal)} ج.م"

        lblKpiItems.Text = _itemsCount.ToString("N0")
        lblKpiInvoices.Text = _todayInvoicesCount.ToString("N0")
        lblKpiSales.Text = UiTheme.Money(_todaySalesTotal)
        lblKpiShift.Text = _shiftStatusText
    End Sub

    Private Sub Logout()
        If Not UiTheme.Confirm("هل تريد تسجيل الخروج والعودة لشاشة الدخول؟") Then Return
        _timer.Stop()
        _clockTimer.Stop()
        DialogResult = DialogResult.Retry
        Close()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        _timer.Stop()
        _clockTimer.Stop()
        MyBase.OnFormClosing(e)
    End Sub
End Class
