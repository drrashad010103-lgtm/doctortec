Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Public Class WhatsAppForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly cboTemplate As New ComboBox()
    Private ReadOnly txtMessage As New TextBox()
    Private ReadOnly txtCountryCode As New TextBox()
    Private ReadOnly lblSelected As New Label()
    Private _customers As List(Of CustomerSummary)

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadCustomers()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "واتساب — التواصل مع العملاء"
        ClientSize = New Size(1120, 680)
        MinimumSize = New Size(900, 560)
        StartPosition = FormStartPosition.CenterParent
        DoubleBuffered = True

        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 74,
            .BackColor = Color.FromArgb(37, 211, 102),
            .Padding = New Padding(18, 9, 18, 8)
        }
        Dim lblSubtitle As New Label With {
            .Text = "إرسال رسائل واتساب جاهزة للعملاء مع استبدال بيانات العميل تلقائيًا",
            .Font = UiTheme.Font(9.0F),
            .ForeColor = Color.FromArgb(235, 255, 240),
            .Dock = DockStyle.Top,
            .Height = 22,
            .TextAlign = ContentAlignment.MiddleRight
        }
        Dim lblTitle As New Label With {
            .Text = "واتساب",
            .Font = UiTheme.Font(14.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .Dock = DockStyle.Top,
            .Height = 34,
            .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(lblSubtitle)
        header.Controls.Add(lblTitle)

        Dim toolbar As New FlowLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .BackColor = UiTheme.Surface,
            .Padding = New Padding(6)
        }
        txtSearch.Width = 280
        txtSearch.PlaceholderText = "اسم العميل أو الموبايل"
        AddHandler txtSearch.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              LoadCustomers()
                                          End If
                                      End Sub
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 85)
        AddHandler btnSearch.Click, Sub(s, e) LoadCustomers()
        toolbar.Controls.Add(btnSearch)
        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 90)
        AddHandler btnRefresh.Click, Sub(s, e) LoadCustomers()
        toolbar.Controls.Add(btnRefresh)

        Dim split As New SplitContainer With {
            .Dock = DockStyle.Fill,
            .Orientation = Orientation.Vertical,
            .SplitterWidth = 6,
            .BackColor = Color.FromArgb(230, 233, 236)
        }

        UiTheme.AddTextColumn(grid, "Name", "العميل", 220, True)
        UiTheme.AddTextColumn(grid, "Mobile", "الموبايل", 130)
        UiTheme.AddTextColumn(grid, "Balance", "الرصيد", 110, False, "N2")
        AddHandler grid.SelectionChanged, Sub(s, e) ApplyTemplate()
        AddHandler grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then ApplyTemplate()
        split.Panel1.Controls.Add(grid)

        Dim composer As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .Padding = New Padding(14)
        }

        Dim form As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 0
        }

        lblSelected.Text = "اختر عميلاً من القائمة"
        lblSelected.Font = UiTheme.Font(10.5F, FontStyle.Bold)
        lblSelected.ForeColor = UiTheme.Navy
        lblSelected.Dock = DockStyle.Fill
        lblSelected.TextAlign = ContentAlignment.MiddleRight
        AddComposerRow(form, lblSelected, 34)

        cboTemplate.DropDownStyle = ComboBoxStyle.DropDownList
        cboTemplate.Dock = DockStyle.Fill
        cboTemplate.Items.AddRange(New Object() {
            "رسالة ترحيب", "رصيد الحساب", "رصيد نقاط الولاء",
            "عرض وخصم", "تذكير بموعد", "رسالة مخصصة"
        })
        cboTemplate.SelectedIndex = 0
        AddHandler cboTemplate.SelectedIndexChanged, Sub(s, e) ApplyTemplate()
        AddComposerRow(form, UiTheme.MakeLabel("الرسالة الجاهزة", True), 28)
        AddComposerRow(form, cboTemplate, 32)

        txtMessage.Multiline = True
        txtMessage.ScrollBars = ScrollBars.Vertical
        txtMessage.Dock = DockStyle.Fill
        txtMessage.Font = UiTheme.Font(10.0F)
        AddComposerRow(form, UiTheme.MakeLabel("نص الرسالة", True), 28)
        AddComposerRow(form, txtMessage, 190)

        txtCountryCode.Text = "20"
        txtCountryCode.Width = 70
        txtCountryCode.TextAlign = HorizontalAlignment.Center
        Dim countryPanel As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .AutoSize = True
        }
        countryPanel.Controls.Add(UiTheme.MakeLabel("كود الدولة (بدون +)", True))
        countryPanel.Controls.Add(txtCountryCode)
        AddComposerRow(form, countryPanel, 36)

        Dim actions As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .AutoSize = True
        }
        Dim btnSend As Button = UiTheme.PrimaryButton("فتح واتساب", 130)
        AddHandler btnSend.Click, Sub(s, e) OpenWhatsApp()
        Dim btnCopy As Button = UiTheme.MakeButton("نسخ الرسالة", 115)
        AddHandler btnCopy.Click, Sub(s, e) CopyMessage()
        Dim btnCopyPhone As Button = UiTheme.MakeButton("نسخ الرقم", 100)
        AddHandler btnCopyPhone.Click, Sub(s, e) CopyPhone()
        actions.Controls.Add(btnSend)
        actions.Controls.Add(btnCopy)
        actions.Controls.Add(btnCopyPhone)
        AddComposerRow(form, actions, 44)

        Dim hint As New Label With {
            .Text = "يفتح الزر محادثة واتساب مع العميل مع نص الرسالة جاهزًا للإرسال. " &
                    "تأكد من كود الدولة المناسب لبلد العميل.",
            .ForeColor = UiTheme.Muted,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.TopRight,
            .Font = UiTheme.Font(8.5F)
        }
        AddComposerRow(form, hint, 56)

        composer.Controls.Add(form)
        split.Panel2.Controls.Add(composer)

        Controls.Add(split)
        Controls.Add(toolbar)
        Controls.Add(header)

        AddHandler Shown, Sub(s, e)
                              Try
                                  split.SplitterDistance = Math.Max(480, split.Width - 390)
                              Catch
                              End Try
                          End Sub
    End Sub

    Private Shared Sub AddComposerRow(table As TableLayoutPanel, control As Control, height As Integer)
        Dim row As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, height))
        control.Margin = New Padding(0, 4, 0, 4)
        table.Controls.Add(control, 0, row)
    End Sub

    Private Sub LoadCustomers()
        Try
            Cursor = Cursors.WaitCursor
            _customers = CustomerService.Search(_session, txtSearch.Text, "customer")
            grid.DataSource = New BindingSource With {.DataSource = _customers}
            If _customers.Count > 0 AndAlso grid.Rows.Count > 0 Then
                grid.Rows(0).Selected = True
                grid.CurrentCell = grid.Rows(0).Cells(0)
            End If
            ApplyTemplate()
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Function SelectedCustomer() As CustomerSummary
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.DataBoundItem, CustomerSummary)
    End Function

    Private Sub ApplyTemplate()
        Dim customer = SelectedCustomer()
        If customer Is Nothing Then
            lblSelected.Text = "اختر عميلاً من القائمة"
            Return
        End If

        lblSelected.Text = customer.Name & If(String.IsNullOrWhiteSpace(customer.Mobile), " — لا يوجد رقم موبايل", " — " & customer.Mobile)

        Dim points As Decimal = 0D
        Try
            points = LoyaltyService.Points(_session, customer.Id)
        Catch
        End Try

        Dim storeName As String = If(String.IsNullOrWhiteSpace(_session.BranchName), "Doctor Beauty", _session.BranchName)
        Dim body As String
        Select Case cboTemplate.SelectedIndex
            Case 1
                body = "عزيزنا " & customer.Name & "، رصيد حسابك الحالي هو " &
                       UiTheme.Money(customer.Balance) & ". شكرًا لتعاملكم مع " & storeName & "."
            Case 2
                body = "عزيزنا " & customer.Name & "، رصيد نقاط الولاء لديك هو " &
                       points.ToString("N2") & " نقطة. يمكنك استبدالها عند الشراء من " & storeName & "."
            Case 3
                body = "عزيزنا " & customer.Name & "، استمتع بخصم خاص على مشترياتك اليوم من " &
                       storeName & ". ننتظر زيارتك!"
            Case 4
                body = "عزيزنا " & customer.Name & "، نذكرك بموعدك القادم في " & storeName & ". برجاء التواصل معنا لتأكيد الموعد."
            Case 5
                body = txtMessage.Text
            Case Else
                body = "مرحبًا " & customer.Name & "، أهلاً بك في " & storeName &
                       ". يسعدنا خدمتك وخدمة عائلتك."
        End Select
        txtMessage.Text = body
    End Sub

    Private Function NormalizedPhone() As String
        Dim customer = SelectedCustomer()
        If customer Is Nothing OrElse String.IsNullOrWhiteSpace(customer.Mobile) Then Return String.Empty
        Dim digits As String = New String(customer.Mobile.Where(Function(c) Char.IsDigit(c)).ToArray())
        If digits.StartsWith("0") Then digits = digits.Substring(1)
        If digits.StartsWith("00") Then digits = digits.Substring(2)
        Dim code As String = New String(txtCountryCode.Text.Where(Function(c) Char.IsDigit(c)).ToArray())
        If String.IsNullOrWhiteSpace(code) Then Return digits
        Return code & digits
    End Function

    Private Sub OpenWhatsApp()
        Dim customer = SelectedCustomer()
        If customer Is Nothing Then
            UiTheme.Warn("اختر عميلاً أولاً.")
            Return
        End If
        If String.IsNullOrWhiteSpace(customer.Mobile) Then
            UiTheme.Warn("لا يوجد رقم موبايل مسجل لهذا العميل.")
            Return
        End If
        Dim phone As String = NormalizedPhone()
        If phone.Length < 8 Then
            UiTheme.Warn("رقم الموبايل غير صالح. راجع الرقم وكود الدولة.")
            Return
        End If
        If String.IsNullOrWhiteSpace(txtMessage.Text) Then
            UiTheme.Warn("اكتب نص الرسالة أولاً.")
            Return
        End If

        Try
            Dim url As String = "https://wa.me/" & phone & "?text=" & Uri.EscapeDataString(txtMessage.Text)
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub CopyMessage()
        If String.IsNullOrWhiteSpace(txtMessage.Text) Then
            UiTheme.Warn("لا توجد رسالة لنسخها.")
            Return
        End If
        Clipboard.SetText(txtMessage.Text)
        UiTheme.Info("تم نسخ نص الرسالة.")
    End Sub

    Private Sub CopyPhone()
        Dim customer = SelectedCustomer()
        If customer Is Nothing OrElse String.IsNullOrWhiteSpace(customer.Mobile) Then
            UiTheme.Warn("لا يوجد رقم موبايل للنسخ.")
            Return
        End If
        Clipboard.SetText(customer.Mobile)
        UiTheme.Info("تم نسخ رقم الموبايل.")
    End Sub
End Class
