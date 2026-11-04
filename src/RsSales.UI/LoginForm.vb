Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' DOCTOR TECH — branded identification & login screen.
''' Full-screen navy glass shell with the official logo, right-aligned credentials
''' card, live branch list and status bar. The credential card is intentionally empty on
''' open: no pre-filled username, no default password, no login hint.
''' </summary>
Public Class LoginForm
    Inherits Form

    Private ReadOnly txtUser As New TextBox()
    Private ReadOnly txtPassword As New TextBox()
    Private ReadOnly cboBranch As New ComboBox()
    Private ReadOnly btnLogin As New GlassButton()
    Private ReadOnly btnTogglePass As New GlassButton()
    Private ReadOnly btnRefreshBranch As New Button()
    Private ReadOnly lblStatus As New Label()
    Private lblClock As Label
    Private lblDate As Label
    Private lblOnline As Label
    Private ReadOnly _clockTimer As New Timer()
    Private ReadOnly _card As New GlassPanel()
    Private ReadOnly _cardHost As New Panel()

    Private ReadOnly _config As AppConfig
    Private _session As AppSession
    Private _branchesLoading As Boolean
    ''' <summary>لغة الكيبورد التي كان عليها الجهاز قبل الدخول؛ تُعاد عند إغلاق شاشة الدخول.</summary>
    Private _savedInputLanguage As InputLanguage

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public ReadOnly Property Session As AppSession
        Get
            Return _session
        End Get
    End Property

    Public Sub New(config As AppConfig)
        _config = If(config, AppConfig.Load())
        DoubleBuffered = True
        FormBorderStyle = FormBorderStyle.None
        WindowState = FormWindowState.Maximized
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = False
        KeyPreview = True
        BackColor = UiTheme.BrandNavyDark
        Padding = New Padding(UiTheme.ScreenMarginHalfCm)
        Text = "DOCTOR TECH — تسجيل الدخول"

        BuildLoginUi()
        UiTheme.ApplyIcon(Me)
        AcceptButton = btnLogin

        _clockTimer.Interval = 1000
        AddHandler _clockTimer.Tick, AddressOf UpdateClock
        UpdateClock(Me, EventArgs.Empty)
        _clockTimer.Start()
    End Sub

    Private Sub BuildLoginUi()
        Dim root As New BrandBackdropPanel With {
            .Dock = DockStyle.Fill,
            .Padding = New Padding(0)
        }

        ' === Header: logo + wordmark + close ===
        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 86,
            .BackColor = Color.Transparent,
            .Padding = New Padding(4, 4, 4, 0)
        }

        Dim brandFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Right,
            .AutoSize = True,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        Dim logoSmall As New PictureBox With {
            .Size = New Size(62, 62),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 0, 10, 0)
        }
        Dim logoImage As Image = UiTheme.LoadBrandLogo()
        If logoImage IsNot Nothing Then logoSmall.Image = logoImage
        brandFlow.Controls.Add(logoSmall)

        Dim wordmark As New Label With {
            .Text = "DOCTOR TECH" & Environment.NewLine & "نظام إدارة نقاط البيع والمخازن",
            .AutoSize = False,
            .Size = New Size(360, 62),
            .Font = UiTheme.Font(17.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleRight,
            .Padding = New Padding(0, 0, 4, 0)
        }
        brandFlow.Controls.Add(wordmark)
        header.Controls.Add(brandFlow)

        Dim btnExit As New GlassButton With {
            .Text = "✕  خروج (Esc)",
            .Size = New Size(130, 38),
            .Dock = DockStyle.Left,
            .CornerRadius = 10,
            .Margin = New Padding(0)
        }
        btnExit.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        AddHandler btnExit.Click, Sub(s, e)
                                      DialogResult = DialogResult.Cancel
                                      Close()
                                  End Sub
        header.Controls.Add(btnExit)

        ' === Center: brand story on the left, credential card on the right ===
        Dim content As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .RightToLeft = RightToLeft.No,
            .Padding = New Padding(2, 0, 2, 0)
        }
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 56.0F))
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 44.0F))

        content.Controls.Add(BuildBrandPane(), 0, 0)
        content.Controls.Add(BuildCardHost(), 1, 0)

        ' === Bottom status strip ===
        Dim footer As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = 54,
            .BackColor = Color.Transparent,
            .Padding = New Padding(2, 8, 2, 2)
        }
        Dim statusFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        lblOnline = MakePill("🟢 متصل بقاعدة البيانات", UiTheme.BrandCyan)
        lblDate = MakePill(Date.Today.ToString("yyyy-MM-dd"), UiTheme.BrandNavy)
        lblClock = MakePill(Date.Now.ToString("hh:mm:ss tt"), UiTheme.BrandNavy)
        Dim lblBrandPill As Label = MakePill("DOCTOR TECH ©", UiTheme.BrandNavyDark)
        statusFlow.Controls.Add(lblOnline)
        statusFlow.Controls.Add(lblDate)
        statusFlow.Controls.Add(lblClock)
        statusFlow.Controls.Add(lblBrandPill)
        footer.Controls.Add(statusFlow)

        root.Controls.Add(content)
        root.Controls.Add(header)
        root.Controls.Add(footer)
        Controls.Add(root)

        AddHandler KeyDown, AddressOf LoginKeyDown
    End Sub

    Private Function BuildBrandPane() As Control
        Dim pane As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .Padding = New Padding(10)
        }

        Dim logoBig As New PictureBox With {
            .Size = New Size(210, 210),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.None
        }
        Dim logoImage As Image = UiTheme.LoadBrandLogo()
        If logoImage IsNot Nothing Then logoBig.Image = logoImage

        Dim lblName As New Label With {
            .Text = "DOCTOR TECH",
            .AutoSize = False,
            .Size = New Size(560, 54),
            .Font = UiTheme.Font(31.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.None
        }

        Dim lblTag As New Label With {
            .Text = "نظام إدارة نقاط البيع والمخازن والحسابات",
            .AutoSize = False,
            .Size = New Size(560, 32),
            .Font = UiTheme.Font(13.5F, FontStyle.Bold),
            .ForeColor = UiTheme.Lighten(UiTheme.BrandMist, 0.20F),
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.None
        }

        Dim lblFeatures As New Label With {
            .Text = "بيع  •  مشتريات  •  مخازن  •  ولاء  •  تقارير  •  ورديات",
            .AutoSize = False,
            .Size = New Size(620, 28),
            .Font = UiTheme.Font(10.5F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.None
        }

        pane.Controls.Add(logoBig)
        pane.Controls.Add(lblName)
        pane.Controls.Add(lblTag)
        pane.Controls.Add(lblFeatures)

        Dim layoutBrand As Action = Sub()
                                        Dim cy As Integer = Math.Max(40, (pane.ClientSize.Height - 330) \ 2)
                                        Dim cx As Integer = (pane.ClientSize.Width - logoBig.Width) \ 2
                                        logoBig.Location = New Point(cx, cy)
                                        lblName.Location = New Point((pane.ClientSize.Width - lblName.Width) \ 2, logoBig.Bottom + 14)
                                        lblTag.Location = New Point((pane.ClientSize.Width - lblTag.Width) \ 2, lblName.Bottom + 2)
                                        lblFeatures.Location = New Point((pane.ClientSize.Width - lblFeatures.Width) \ 2, lblTag.Bottom + 10)
                                    End Sub
        AddHandler pane.Resize, Sub(s, e) layoutBrand()
        layoutBrand()
        Return pane
    End Function

    Private Function BuildCardHost() As Control
        _cardHost.Dock = DockStyle.Fill
        _cardHost.BackColor = Color.Transparent
        _card.Dock = DockStyle.None
        _card.Size = New Size(430, 500)
        _card.CornerRadius = 22
        _card.FillColor = Color.White
        _card.BorderColor = UiTheme.BrandBorder
        _card.BackColor = Color.White
        _card.Anchor = AnchorStyles.None

        BuildCardBody()
        _cardHost.Controls.Add(_card)

        Dim centerCard As Action = Sub()
                                      _card.Location = New Point(
                                          Math.Max(8, (_cardHost.ClientSize.Width - _card.Width) \ 2),
                                          Math.Max(8, (_cardHost.ClientSize.Height - _card.Height) \ 2))
                                  End Sub
        AddHandler _cardHost.Resize, Sub(s, e) centerCard()
        centerCard()
        Return _cardHost
    End Function

    Private Sub BuildCardBody()
        Dim table As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 10,
            .BackColor = Color.White,
            .Padding = New Padding(26, 22, 26, 18)
        }
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 24))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 10))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 58))
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        table.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim title As New Label With {
            .Text = "تسجيل الدخول",
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(19.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight
        }
        Dim subtitle As New Label With {
            .Text = "أدخل بيانات المستخدم واختر الفرع ثم اضغط دخول",
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(9.0F),
            .ForeColor = UiTheme.Muted,
            .TextAlign = ContentAlignment.MiddleRight
        }

        txtUser.Font = UiTheme.Font(12.0F, FontStyle.Bold)
        txtUser.BorderStyle = BorderStyle.FixedSingle
        txtUser.RightToLeft = RightToLeft.Yes
        txtUser.TextAlign = HorizontalAlignment.Right
        txtUser.Dock = DockStyle.Fill
        txtUser.Margin = New Padding(0, 2, 0, 6)
        AddHandler txtUser.GotFocus, AddressOf ForceEnglishInput

        txtPassword.Font = UiTheme.Font(12.0F, FontStyle.Bold)
        txtPassword.UseSystemPasswordChar = True
        txtPassword.BorderStyle = BorderStyle.FixedSingle
        txtPassword.RightToLeft = RightToLeft.Yes
        txtPassword.TextAlign = HorizontalAlignment.Right
        txtPassword.Dock = DockStyle.Fill
        txtPassword.Margin = New Padding(0, 2, 0, 6)
        AddHandler txtPassword.GotFocus, AddressOf ForceEnglishInput

        cboBranch.DropDownStyle = ComboBoxStyle.DropDownList
        cboBranch.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        cboBranch.Dock = DockStyle.Fill
        cboBranch.Margin = New Padding(0, 2, 0, 6)

        table.Controls.Add(title, 0, 0)
        table.Controls.Add(subtitle, 0, 1)
        table.Controls.Add(MakeField("اسم المستخدم (User ID)", txtUser), 0, 3)

        Dim passPanel As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White, .Margin = New Padding(0)}
        btnTogglePass.Text = "👁"
        btnTogglePass.Size = New Size(46, 32)
        btnTogglePass.Dock = DockStyle.Left
        btnTogglePass.CornerRadius = 8
        btnTogglePass.SetTone(UiTheme.BrandMist, UiTheme.BrandLight)
        btnTogglePass.ForeColor = UiTheme.BrandNavy
        AddHandler btnTogglePass.Click, Sub(s, e) txtPassword.UseSystemPasswordChar = Not txtPassword.UseSystemPasswordChar
        passPanel.Controls.Add(txtPassword)
        passPanel.Controls.Add(btnTogglePass)
        table.Controls.Add(MakeField("كلمة المرور (Password)", passPanel), 0, 4)

        Dim branchPanel As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White, .Margin = New Padding(0)}
        btnRefreshBranch.Text = "↻"
        btnRefreshBranch.Size = New Size(46, 32)
        btnRefreshBranch.Dock = DockStyle.Left
        btnRefreshBranch.FlatStyle = FlatStyle.Flat
        btnRefreshBranch.BackColor = UiTheme.BrandMist
        btnRefreshBranch.ForeColor = UiTheme.BrandNavy
        btnRefreshBranch.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        btnRefreshBranch.Cursor = Cursors.Hand
        btnRefreshBranch.FlatAppearance.BorderSize = 0
        AddHandler btnRefreshBranch.Click, Sub(s, e) LoadBranches()
        branchPanel.Controls.Add(cboBranch)
        branchPanel.Controls.Add(btnRefreshBranch)
        table.Controls.Add(MakeField("الفرع (Store / Branch)", branchPanel), 0, 5)

        lblStatus.Dock = DockStyle.Fill
        lblStatus.ForeColor = UiTheme.DangerRed
        lblStatus.Font = UiTheme.Font(9.0F, FontStyle.Bold)
        lblStatus.TextAlign = ContentAlignment.MiddleRight
        table.Controls.Add(lblStatus, 0, 6)

        btnLogin.Text = "دخول للنظام  ➜"
        btnLogin.Dock = DockStyle.Fill
        btnLogin.Height = 50
        btnLogin.Font = UiTheme.Font(13.0F, FontStyle.Bold)
        btnLogin.CornerRadius = 12
        btnLogin.SetTone(UiTheme.BrandCyan, UiTheme.BrandNavyDark)
        btnLogin.ForeColor = Color.White
        AddHandler btnLogin.Click, AddressOf DoLogin
        table.Controls.Add(btnLogin, 0, 7)

        Dim hint As New Label With {
            .Text = "F2 دخول    •    F4 إظهار كلمة المرور",
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.5F, FontStyle.Bold),
            .ForeColor = UiTheme.Muted,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        table.Controls.Add(hint, 0, 8)

        _card.Controls.Add(table)
    End Sub

    Private Shared Function MakeField(caption As String, editor As Control) As Control
        Dim box As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 2,
            .BackColor = Color.White,
            .Margin = New Padding(0)
        }
        box.RowStyles.Add(New RowStyle(SizeType.Absolute, 22))
        box.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim lbl As New Label With {
            .Text = caption,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(9.3F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight
        }
        editor.Dock = DockStyle.Fill
        box.Controls.Add(lbl, 0, 0)
        box.Controls.Add(editor, 0, 1)
        Return box
    End Function

    Private Shared Function MakePill(text As String, color As Color) As Label
        Dim pill As New StatusPill With {
            .Text = text,
            .Size = New Size(210, 30),
            .PillColor = color,
            .Margin = New Padding(4, 2, 4, 2)
        }
        Return pill
    End Function

    Private Sub UpdateClock(sender As Object, e As EventArgs)
        If lblClock IsNot Nothing Then lblClock.Text = Date.Now.ToString("hh:mm:ss tt")
        If lblDate IsNot Nothing Then lblDate.Text = Date.Today.ToString("yyyy-MM-dd")
    End Sub

    Private Sub LoginKeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.F2, Keys.Enter
                If e.KeyCode = Keys.Enter AndAlso ActiveControl Is txtPassword Then Return
                e.SuppressKeyPress = True
                DoLogin(Me, EventArgs.Empty)
            Case Keys.F4
                e.SuppressKeyPress = True
                txtPassword.UseSystemPasswordChar = Not txtPassword.UseSystemPasswordChar
            Case Keys.Escape, Keys.F12
                e.SuppressKeyPress = True
                DialogResult = DialogResult.Cancel
                Close()
        End Select
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        BeginInvoke(New Action(Sub() LoadBranches()))
    End Sub

    ''' <summary>
    ''' بيانات الدخول لاتينية، فيتم تحويل الكيبورد للإنجليزي تلقائيًا عند الكتابة حتى لا يُكتب
    ''' اسم المستخدم أو كلمة المرور بحروف عربية. اللغة الأصلية تعود بعد إغلاق الشاشة
    ''' ليتمكن الكاشير من كتابة أسماء الأصناف بالعربي.
    ''' </summary>
    Private Sub ForceEnglishInput(sender As Object, e As EventArgs)
        If _savedInputLanguage Is Nothing Then _savedInputLanguage = InputLanguage.CurrentInputLanguage
        Dim english = EnglishInputLanguage()
        If english IsNot Nothing AndAlso english IsNot InputLanguage.CurrentInputLanguage Then
            InputLanguage.CurrentInputLanguage = english
        End If
    End Sub

    Private Shared Function EnglishInputLanguage() As InputLanguage
        For Each installed As Object In InputLanguage.InstalledInputLanguages
            Dim lang = TryCast(installed, InputLanguage)
            If lang IsNot Nothing AndAlso lang.Culture.TwoLetterISOLanguageName = "en" Then Return lang
        Next
        Return Nothing
    End Function

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _clockTimer.Stop()
        _clockTimer.Dispose()
        If _savedInputLanguage IsNot Nothing Then InputLanguage.CurrentInputLanguage = _savedInputLanguage
        MyBase.OnFormClosed(e)
    End Sub

    ''' <summary>أول ما تظهر الشاشة تكون إنجليزي، فلا يحتاج الكاشير لتبديل اللغة قبل كتابة اليوزر.</summary>
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        ForceEnglishInput(Nothing, EventArgs.Empty)
    End Sub

    ''' <summary>
    ''' Branch loading runs off the UI thread: the previous synchronous version froze the
    ''' window for ~15 seconds after it appeared (measured: handle at 482ms, UI blocked after).
    ''' EnsureDefaultSetup is no longer called here — AuthService.Login already runs it.
    ''' </summary>
    Private Sub LoadBranches()
        If _branchesLoading Then Return
        _branchesLoading = True
        If btnRefreshBranch IsNot Nothing Then btnRefreshBranch.Enabled = False
        lblStatus.ForeColor = UiTheme.BrandNavy
        lblStatus.Text = "جاري تحميل الفروع..."

        Dim connectionString As String = _config.ConnectionString
        Task.Run(Sub()
                     Dim branches As List(Of BranchRecord) = Nothing
                     Dim failure As String = Nothing
                     Try
                         Using db = DbSession.Open(connectionString)
                             branches = BranchesRepository.ListAll(db.Connection)
                         End Using
                     Catch ex As Exception
                         failure = ex.Message
                     End Try
                     Try
                         BeginInvoke(New Action(Sub() ApplyBranches(branches, failure)))
                     Catch
                     End Try
                 End Sub)
    End Sub

    Private Sub ApplyBranches(branches As List(Of BranchRecord), failure As String)
        _branchesLoading = False
        If btnRefreshBranch IsNot Nothing Then btnRefreshBranch.Enabled = True

        If failure IsNot Nothing Then
            lblStatus.ForeColor = UiTheme.DangerRed
            lblStatus.Text = "تعذر الاتصال بقاعدة البيانات: " & failure
            If lblOnline IsNot Nothing Then lblOnline.Text = "🔴 تعذر الاتصال"
            Return
        End If

        Dim rows As List(Of BranchRecord) = If(branches, New List(Of BranchRecord)())
        cboBranch.DataSource = Nothing
        cboBranch.DisplayMember = "Name"
        cboBranch.ValueMember = "Id"
        cboBranch.DataSource = rows
        If rows.Count > 0 Then cboBranch.SelectedIndex = 0
        lblStatus.ForeColor = UiTheme.BrandCyan
        lblStatus.Text = "متصل بقاعدة البيانات — الفروع المتاحة: " & rows.Count.ToString()
        If lblOnline IsNot Nothing Then lblOnline.Text = "🟢 متصل بقاعدة البيانات"
    End Sub

    Private Sub DoLogin(sender As Object, e As EventArgs)
        If _clockTimer IsNot Nothing Then _clockTimer.Stop()
        lblStatus.Text = "جاري التحقق من هوية المستخدم..."
        lblStatus.ForeColor = UiTheme.BrandNavy
        lblStatus.Refresh()
        btnLogin.Enabled = False
        Try
            Dim branchId As Long? = Nothing
            Dim selectedBranch = TryCast(cboBranch.SelectedItem, BranchRecord)
            If selectedBranch IsNot Nothing Then
                branchId = selectedBranch.Id
            ElseIf cboBranch.SelectedValue IsNot Nothing AndAlso IsNumeric(cboBranch.SelectedValue) Then
                branchId = Convert.ToInt64(cboBranch.SelectedValue)
            End If

            Dim userName As String = txtUser.Text.Trim()
            Dim password As String = txtPassword.Text
            If userName.Length = 0 OrElse password.Length = 0 Then
                lblStatus.ForeColor = UiTheme.DangerRed
                lblStatus.Text = "أدخل اسم المستخدم وكلمة المرور."
                Return
            End If

            Dim session = AuthService.Login(_config.ConnectionString, userName, password, branchId)
            _session = session
            DialogResult = DialogResult.OK
        Catch ex As Exception
            AppLog.Error("Login failed", ex)
            lblStatus.ForeColor = UiTheme.DangerRed
            lblStatus.Text = ex.Message
        Finally
            btnLogin.Enabled = True
            If _session Is Nothing AndAlso _clockTimer IsNot Nothing Then _clockTimer.Start()
        End Try
    End Sub
End Class
