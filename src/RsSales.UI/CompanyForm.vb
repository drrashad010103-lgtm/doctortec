Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Windows.Forms

''' <summary>
''' شاشة بيانات الشركة: بيانات المؤسسة، الشعار وQR، شكل التقرير،
''' السنة المالية، سياسات التقارير وتعريفات العملات — كما في النظام القديم.
''' </summary>
Public Class CompanyForm
    Inherits Form

    Private ReadOnly _session As AppSession

    ' بيانات المؤسسة
    Private ReadOnly txtNameAr As New TextBox()
    Private ReadOnly txtNameEn As New TextBox()
    Private ReadOnly txtActivityAr As New TextBox()
    Private ReadOnly txtActivityEn As New TextBox()
    Private ReadOnly txtCommercialReg As New TextBox()
    Private ReadOnly txtLicenseNo As New TextBox()
    Private ReadOnly txtPhone As New TextBox()
    Private ReadOnly txtFax As New TextBox()
    Private ReadOnly txtMobile As New TextBox()
    Private ReadOnly txtEmail As New TextBox()
    Private ReadOnly txtWebsite As New TextBox()
    Private ReadOnly txtTaxRate As New TextBox()
    Private ReadOnly txtTaxNo As New TextBox()
    Private ReadOnly txtAddress As New TextBox()
    Private ReadOnly txtInvoiceNotes As New TextBox()
    Private ReadOnly txtInvoiceNature As New TextBox()
    Private ReadOnly cboPole As New ComboBox()
    Private ReadOnly chkAllowNoStock As New CheckBox()
    Private ReadOnly chkOtherDefinitions As New CheckBox()

    ' شكل التقرير والسياسات
    Private ReadOnly chkPrintAuto As New CheckBox()
    Private ReadOnly chkPrintInvoice As New CheckBox()
    Private ReadOnly chkPrintA5 As New CheckBox()
    Private ReadOnly radPolicy1 As New RadioButton()
    Private ReadOnly radPolicy2 As New RadioButton()
    Private ReadOnly dtpFiscalStart As New DateTimePicker()
    Private ReadOnly dtpFiscalEnd As New DateTimePicker()

    ' العملات
    Private ReadOnly txtCurrencyEgp As New TextBox()
    Private ReadOnly txtCurrencyUsd As New TextBox()
    Private ReadOnly txtCurrencyChf As New TextBox()
    Private ReadOnly txtCurrencyEur As New TextBox()

    ' الشعار وQR
    Private ReadOnly picQr As New PictureBox()
    Private ReadOnly picLogo As New PictureBox()
    Private _logoPath As String = String.Empty

    ' تبويب الفواتير
    Private ReadOnly cboPrinterA4 As New ComboBox()
    Private ReadOnly cboPrinterReceipt As New ComboBox()
    Private ReadOnly cboPrinterBarcode As New ComboBox()
    Private ReadOnly txtReceiptFooter As New TextBox()

    Private ReadOnly pnlBasic As New Panel()
    Private ReadOnly pnlInvoices As New Panel()
    Private btnTabBasic As Button
    Private btnTabInvoice As Button

    Private ReadOnly Purple As Color = Color.FromArgb(46, 125, 50)
    Private ReadOnly PurpleLight As Color = Color.FromArgb(46, 125, 50)
    Private ReadOnly PageBack As Color = Color.FromArgb(239, 240, 244)
    Private ReadOnly CaptionInk As Color = Color.FromArgb(55, 55, 65)

    Private _dragging As Boolean
    Private _dragOffset As Point

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadSettings()
        UpdateQr()
    End Sub

    ' ------------------------------------------------------------------ UI --
    Private Sub InitializeUi()
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = False
        Font = New Font("Cairo", 9.0F)
        Text = "بيانات الشركة"
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.CenterScreen
        ClientSize = New Size(1010, 780)
        BackColor = PageBack
        DoubleBuffered = True
        KeyPreview = True
        UiTheme.ApplyIcon(Me)

        Dim titleBar As New Panel With {
            .Dock = DockStyle.Top, .Height = 38, .BackColor = Purple, .Padding = New Padding(0)
        }
        Dim lblTitle As New Label With {
            .Text = "بيانات الشركة", .ForeColor = Color.White,
            .Font = New Font("Cairo", 10.5F, FontStyle.Bold),
            .Dock = DockStyle.Right, .Width = 240,
            .TextAlign = ContentAlignment.MiddleRight, .Padding = New Padding(0, 0, 14, 0)
        }
        titleBar.Controls.Add(lblTitle)

        Dim btnClose As Button = TitleButton("✕", 6)
        Dim btnMax As Button = TitleButton("□", 34)
        Dim btnMin As Button = TitleButton("—", 62)
        AddHandler btnClose.Click, Sub(s, e) Close()
        AddHandler btnMax.Click, Sub(s, e) WindowState = If(WindowState = FormWindowState.Maximized, FormWindowState.Normal, FormWindowState.Maximized)
        AddHandler btnMin.Click, Sub(s, e) WindowState = FormWindowState.Minimized
        titleBar.Controls.AddRange({btnClose, btnMax, btnMin})

        AddHandler titleBar.MouseDown, Sub(s, e) StartDrag(e)
        AddHandler titleBar.MouseMove, Sub(s, e) DoDragForm(e)
        AddHandler titleBar.MouseUp, Sub(s, e) _dragging = False
        AddHandler lblTitle.MouseDown, Sub(s, e) StartDrag(e)
        AddHandler lblTitle.MouseMove, Sub(s, e) DoDragForm(e)
        AddHandler lblTitle.MouseUp, Sub(s, e) _dragging = False

        Dim tabsBar As New Panel With {
            .Dock = DockStyle.Top, .Height = 42, .BackColor = Color.FromArgb(248, 248, 251)
        }
        btnTabBasic = TabButton("بيانات اساسية", 872)
        btnTabInvoice = TabButton("إعدادات فواتير", 732)
        AddHandler btnTabBasic.Click, Sub(s, e) ShowTab(True)
        AddHandler btnTabInvoice.Click, Sub(s, e) ShowTab(False)
        tabsBar.Controls.AddRange({btnTabBasic, btnTabInvoice})

        Dim content As New Panel With {.Dock = DockStyle.Fill, .BackColor = PageBack, .Padding = New Padding(10, 8, 10, 8)}
        pnlBasic.Dock = DockStyle.Fill
        pnlBasic.BackColor = PageBack
        pnlInvoices.Dock = DockStyle.Fill
        pnlInvoices.BackColor = PageBack

        BuildBasicTab()
        BuildInvoicesTab()

        content.Controls.Add(pnlBasic)
        content.Controls.Add(pnlInvoices)

        Dim saveBar As New Panel With {.Dock = DockStyle.Bottom, .Height = 50, .BackColor = PageBack, .Padding = New Padding(10, 6, 10, 6)}
        Dim btnSave As New Button With {
            .Text = "حفظ", .Dock = DockStyle.Fill, .Height = 36, .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(232, 232, 236), .ForeColor = Color.FromArgb(35, 35, 45),
            .Font = New Font("Cairo", 9.5F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        btnSave.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 190)
        AddHandler btnSave.Click, Sub(s, e) SaveSettings()
        saveBar.Controls.Add(btnSave)

        Controls.Add(content)
        Controls.Add(saveBar)
        Controls.Add(tabsBar)
        Controls.Add(titleBar)

        ShowTab(True)
        AddHandler KeyDown, Sub(s, e) If e.KeyCode = Keys.Escape Then Close()
    End Sub

    Private Function TitleButton(text As String, x As Integer) As Button
        Dim b As New Button With {
            .Text = text, .Size = New Size(24, 22), .Location = New Point(x, 8),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(18, 65, 24), .ForeColor = Color.White,
            .Font = New Font("Cairo", 8.0F, FontStyle.Bold), .Cursor = Cursors.Hand,
            .TabStop = False
        }
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    Private Function TabButton(text As String, x As Integer) As Button
        Dim b As New Button With {
            .Text = text, .Size = New Size(130, 32), .Location = New Point(x, 6),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.White, .ForeColor = PurpleLight,
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 214, 222)
        Return b
    End Function

    Private Sub ShowTab(basic As Boolean)
        pnlBasic.Visible = basic
        pnlInvoices.Visible = Not basic
        If basic Then pnlBasic.BringToFront() Else pnlInvoices.BringToFront()
        StyleTab(btnTabBasic, basic)
        StyleTab(btnTabInvoice, Not basic)
    End Sub

    Private Sub StyleTab(btn As Button, active As Boolean)
        btn.BackColor = If(active, Color.White, Color.FromArgb(236, 236, 242))
        btn.ForeColor = If(active, PurpleLight, Color.FromArgb(105, 105, 115))
        btn.FlatAppearance.BorderSize = If(active, 1, 0)
        btn.Font = New Font("Cairo", 9.0F, If(active, FontStyle.Bold, FontStyle.Regular))
    End Sub

    ' ---------------------------------------------------------- basic tab --
    Private Sub BuildBasicTab()
        pnlBasic.Controls.Clear()

        Dim rightHost As New Panel With {
            .Location = New Point(366, 0), .Size = New Size(624, 620), .BackColor = PageBack
        }
        Dim leftHost As New Panel With {
            .Location = New Point(0, 0), .Size = New Size(350, 620), .BackColor = PageBack
        }

        Dim captionLabel As New Label With {
            .Text = "ادخل بيانات المؤسسة", .Location = New Point(0, 0), .Size = New Size(624, 26),
            .TextAlign = ContentAlignment.MiddleRight, .Font = New Font("Cairo", 10.0F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 55)
        }
        rightHost.Controls.Add(captionLabel)

        Dim y As Integer = 28
        For Each lineCtl As Control In New Control() {
                FieldRow("الاسم:", 110, FieldTag("AR"), txtNameAr, 182, FieldTag("EN"), txtNameEn, 182),
                FieldRow("النشاط التجاري:", 110, FieldTag("AR"), txtActivityAr, 182, FieldTag("EN"), txtActivityEn, 182),
                FieldRow("رقم سجل تجاري:", 110, txtCommercialReg, 172, RowCaption("رقم الرخصة:", 82), txtLicenseNo, 172),
                FieldRow("رقم الهاتف:", 105, txtPhone, 104, RowCaption("رقم الفاكس:", 72), txtFax, 100, RowCaption("رقم محمول:", 72), txtMobile, 100),
                FieldRow("البريد الإلكتروني:", 110, txtEmail, 180, RowCaption("الموقع الرسمي:", 88), txtWebsite, 180),
                FieldRow("الضريبة Tax %", 110, txtTaxRate, 80, RowCaption("رقم الضريبة:", 88), txtTaxNo, 180)}
            lineCtl.Location = New Point(0, y)
            y += 30
            rightHost.Controls.Add(lineCtl)
        Next

        cboPole.DropDownStyle = ComboBoxStyle.DropDownList
        cboPole.Width = 200
        cboPole.Font = New Font("Cairo", 9.0F)
        cboPole.Margin = New Padding(2, 3, 6, 3)
        cboPole.Items.AddRange({"بدون", "Pole Display 1", "Pole Display 2"})
        cboPole.SelectedIndex = 0
        Dim poleRow As Control = FieldRow("Pole device", 110, cboPole, 200)
        poleRow.Location = New Point(0, y) : y += 30
        rightHost.Controls.Add(poleRow)

        Dim addressRow As Control = FieldRow("العنوان:", 110, txtAddress, 468)
        addressRow.Location = New Point(0, y) : y += 30
        rightHost.Controls.Add(addressRow)

        Dim notesRow As FlowLayoutPanel = FieldRow("ملاحظات فاتورة:", 110)
        txtInvoiceNotes.Multiline = True
        txtInvoiceNotes.ScrollBars = ScrollBars.Vertical
        txtInvoiceNotes.Width = 450
        txtInvoiceNotes.Height = 44
        txtInvoiceNotes.Font = New Font("Cairo", 8.5F)
        txtInvoiceNotes.RightToLeft = RightToLeft.Yes
        txtInvoiceNotes.Margin = New Padding(2, 3, 6, 3)
        notesRow.Height = 52
        notesRow.Controls.Add(txtInvoiceNotes)
        notesRow.Location = New Point(0, y) : y += 54
        rightHost.Controls.Add(notesRow)

        Dim natureRow As FlowLayoutPanel = FieldRow("طبيعة الفاتورة:", 110, txtInvoiceNature, 190)
        Dim btnValidity As New Button With {
            .Text = "الصلاحية", .Size = New Size(86, 26), .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(240, 240, 244), .Font = New Font("Cairo", 8.5F),
            .Cursor = Cursors.Hand, .Margin = New Padding(6, 2, 2, 2)
        }
        btnValidity.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 200)
        AddHandler btnValidity.Click, Sub(s, e) UiTheme.Info("مدة صلاحية الفاتورة تُدار من إعدادات الفواتير.")
        natureRow.Controls.Add(btnValidity)
        natureRow.Location = New Point(0, y) : y += 32
        rightHost.Controls.Add(natureRow)

        ' QR + الشعار
        Dim qrRow As New Panel With {.Location = New Point(0, y), .Size = New Size(624, 132), .BackColor = PageBack}
        picQr.Size = New Size(118, 118)
        picQr.Location = New Point(240, 2)
        picQr.SizeMode = PictureBoxSizeMode.Zoom
        picQr.BackColor = Color.White
        picQr.BorderStyle = BorderStyle.FixedSingle
        qrRow.Controls.Add(picQr)

        picLogo.Size = New Size(150, 74)
        picLogo.Location = New Point(430, 2)
        picLogo.SizeMode = PictureBoxSizeMode.Zoom
        picLogo.BackColor = Color.White
        picLogo.BorderStyle = BorderStyle.FixedSingle
        qrRow.Controls.Add(picLogo)

        Dim btnLogo As New Button With {
            .Text = "+", .Size = New Size(26, 24), .Location = New Point(430, 80),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(240, 240, 244),
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        btnLogo.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 200)
        AddHandler btnLogo.Click, Sub(s, e) PickLogo()
        qrRow.Controls.Add(btnLogo)

        Dim lblLogo As New Label With {
            .Text = "الشعار", .Location = New Point(462, 82), .Size = New Size(110, 20),
            .TextAlign = ContentAlignment.MiddleRight, .Font = New Font("Cairo", 8.5F, FontStyle.Bold),
            .ForeColor = CaptionInk
        }
        qrRow.Controls.Add(lblLogo)
        rightHost.Controls.Add(qrRow)
        y += 134

        chkAllowNoStock.Text = "السماح بالبيع بدون كمية بالمخزن"
        chkAllowNoStock.AutoSize = True
        chkAllowNoStock.Font = New Font("Cairo", 8.5F, FontStyle.Bold)
        chkAllowNoStock.Margin = New Padding(4, 4, 4, 2)
        Dim stockRow As Control = RowControl(chkAllowNoStock)
        stockRow.Location = New Point(0, y) : y += 28
        rightHost.Controls.Add(stockRow)

        chkOtherDefinitions.Text = "تفعيل تعريفات أخرى 12-11"
        chkOtherDefinitions.AutoSize = True
        chkOtherDefinitions.Font = New Font("Cairo", 8.5F, FontStyle.Bold)
        chkOtherDefinitions.Margin = New Padding(4, 2, 4, 2)
        Dim otherRow As Control = RowControl(chkOtherDefinitions)
        otherRow.Location = New Point(0, y)
        rightHost.Controls.Add(otherRow)

        ' العمود الأيسر: شكل التقرير والسنة المالية والسياسات والعملات
        Dim reportForm = ReportFormGroup()
        reportForm.Location = New Point(0, 0)
        reportForm.Size = New Size(350, 104)
        Dim fiscal = FiscalYearGroup()
        fiscal.Location = New Point(0, 110)
        fiscal.Size = New Size(350, 92)
        Dim policies = PoliciesGroup()
        policies.Location = New Point(0, 208)
        policies.Size = New Size(350, 78)
        Dim currencies = CurrencyGroup()
        currencies.Location = New Point(0, 292)
        currencies.Size = New Size(350, 172)
        leftHost.Controls.Add(reportForm)
        leftHost.Controls.Add(fiscal)
        leftHost.Controls.Add(policies)
        leftHost.Controls.Add(currencies)

        pnlBasic.Controls.Add(rightHost)
        pnlBasic.Controls.Add(leftHost)
    End Sub

    Private Function ReportFormGroup() As GroupBox
        Dim g As New GroupBox With {
            .Text = "شكل التقرير",
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .ForeColor = CaptionInk,
            .Padding = New Padding(8)
        }
        chkPrintAuto.Text = "طباعة تلقائية للفاتورة"
        chkPrintInvoice.Text = "طباعة على الفاتورة"
        chkPrintA5.Text = "طباعة على A5"
        Dim y As Integer = 22
        For Each c In New CheckBox() {chkPrintAuto, chkPrintInvoice, chkPrintA5}
            c.AutoSize = True
            c.Font = New Font("Cairo", 8.5F)
            c.Location = New Point(12, y)
            c.CheckAlign = ContentAlignment.MiddleRight
            g.Controls.Add(c)
            y += 24
        Next
        Return g
    End Function

    Private Function FiscalYearGroup() As GroupBox
        Dim g As New GroupBox With {
            .Text = "تاريخ السنة المالية",
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .ForeColor = CaptionInk
        }
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .Padding = New Padding(8, 16, 8, 4)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
        AddFiscalRow(t, 0, "بداية السنة المالية", dtpFiscalStart)
        AddFiscalRow(t, 1, "نهاية السنة المالية", dtpFiscalEnd)
        g.Controls.Add(t)
        Return g
    End Function

    Private Shared Sub AddFiscalRow(t As TableLayoutPanel, row As Integer, caption As String, picker As DateTimePicker)
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Cairo", 8.0F)
        }
        picker.Format = DateTimePickerFormat.Custom
        picker.CustomFormat = "yyyy/MM/dd"
        picker.Width = 110
        picker.Dock = DockStyle.Right
        picker.Font = New Font("Cairo", 8.5F)
        t.Controls.Add(lbl, 0, row)
        t.Controls.Add(picker, 1, row)
    End Sub

    Private Function PoliciesGroup() As GroupBox
        Dim g As New GroupBox With {
            .Text = "سياسات التقارير",
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .ForeColor = CaptionInk
        }
        radPolicy1.Text = "تفعيل شاشة التقارير 1"
        radPolicy2.Text = "تفعيل شاشة التقارير 2"
        radPolicy1.Checked = True
        Dim y As Integer = 22
        For Each c In New RadioButton() {radPolicy1, radPolicy2}
            c.AutoSize = True
            c.Font = New Font("Cairo", 8.5F)
            c.Location = New Point(14, y)
            c.CheckAlign = ContentAlignment.MiddleRight
            g.Controls.Add(c)
            y += 24
        Next
        Return g
    End Function

    Private Function CurrencyGroup() As GroupBox
        Dim g As New GroupBox With {
            .Text = "تعريفات العملات",
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .ForeColor = CaptionInk
        }
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 4, .Padding = New Padding(8, 16, 8, 4)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
        AddCurrencyRow(t, 0, "جنيه", txtCurrencyEgp)
        AddCurrencyRow(t, 1, "دولار", txtCurrencyUsd)
        AddCurrencyRow(t, 2, "فرنك", txtCurrencyChf)
        AddCurrencyRow(t, 3, "يورو", txtCurrencyEur)
        g.Controls.Add(t)
        Return g
    End Function

    Private Shared Sub AddCurrencyRow(t As TableLayoutPanel, row As Integer, caption As String, box As TextBox)
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold)
        }
        box.Dock = DockStyle.Right
        box.Width = 105
        box.TextAlign = HorizontalAlignment.Center
        box.Font = New Font("Cairo", 8.5F)
        t.Controls.Add(lbl, 0, row)
        t.Controls.Add(box, 1, row)
    End Sub

    ' ------------------------------------------------------- invoices tab --
    Private Sub BuildInvoicesTab()
        pnlInvoices.Controls.Clear()

        Dim printers As New GroupBox With {
            .Text = "طابعات الفواتير", .Dock = DockStyle.Top, .Height = 160,
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .ForeColor = CaptionInk,
            .Padding = New Padding(10)
        }
        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 3, .Padding = New Padding(6, 18, 6, 6)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 60))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
        AddPrinterRow(t, 0, "طباعة فواتير A4", cboPrinterA4)
        AddPrinterRow(t, 1, "طباعة فواتير ريسيت", cboPrinterReceipt)
        AddPrinterRow(t, 2, "طابعة باركود", cboPrinterBarcode)
        printers.Controls.Add(t)

        Dim footer As New GroupBox With {
            .Text = "نص أسفل الفاتورة", .Dock = DockStyle.Top, .Height = 150,
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .ForeColor = CaptionInk,
            .Padding = New Padding(10)
        }
        txtReceiptFooter.Multiline = True
        txtReceiptFooter.ScrollBars = ScrollBars.Vertical
        txtReceiptFooter.Dock = DockStyle.Fill
        txtReceiptFooter.Font = New Font("Cairo", 9.0F)
        txtReceiptFooter.RightToLeft = RightToLeft.Yes
        footer.Controls.Add(txtReceiptFooter)

        pnlInvoices.Controls.Add(footer)
        pnlInvoices.Controls.Add(printers)
    End Sub

    Private Shared Sub AddPrinterRow(t As TableLayoutPanel, row As Integer, caption As String, combo As ComboBox)
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold)
        }
        combo.Dock = DockStyle.Right
        combo.Width = 240
        combo.DropDownStyle = ComboBoxStyle.DropDownList
        combo.Font = New Font("Cairo", 9.0F)
        t.Controls.Add(lbl, 0, row)
        t.Controls.Add(combo, 1, row)
    End Sub

    ' ----------------------------------------------------- layout helpers --
    Private Shared Function RowCaption(text As String, width As Integer) As Label
        Return New Label With {
            .Text = text, .Width = width, .Height = 24, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .ForeColor = Color.FromArgb(55, 55, 65),
            .Margin = New Padding(2, 4, 2, 2)
        }
    End Function

    Private Shared Function FieldTag(text As String) As Label
        Return New Label With {
            .Text = text, .Width = 26, .Height = 24, .TextAlign = ContentAlignment.MiddleCenter,
            .Font = New Font("Cairo", 7.5F, FontStyle.Bold), .ForeColor = Color.FromArgb(120, 120, 130),
            .Margin = New Padding(0, 4, 2, 2)
        }
    End Function

    Private Shared Function Field(box As TextBox, width As Integer) As TextBox
        box.Width = width
        box.Font = New Font("Cairo", 9.0F)
        box.TextAlign = HorizontalAlignment.Right
        box.RightToLeft = RightToLeft.Yes
        box.Margin = New Padding(2, 3, 6, 3)
        box.BorderStyle = BorderStyle.FixedSingle
        Return box
    End Function

    Private Shared Function FieldRow(caption As String, captionWidth As Integer,
                                ParamArray items As Object()) As FlowLayoutPanel
        Dim line As New FlowLayoutPanel With {
            .Size = New Size(624, 30), .FlowDirection = FlowDirection.RightToLeft, .RightToLeft = RightToLeft.No,
            .WrapContents = False, .BackColor = Color.Transparent, .Margin = New Padding(0)
        }
        line.Controls.Add(RowCaption(caption, captionWidth))
        Dim i As Integer = 0
        While i < items.Length
            If TypeOf items(i) Is TextBox Then
                line.Controls.Add(Field(DirectCast(items(i), TextBox), CInt(items(i + 1))))
                i += 2
            ElseIf TypeOf items(i) Is Control Then
                Dim ctl As Control = DirectCast(items(i), Control)
                If i + 1 < items.Length AndAlso TypeOf items(i + 1) Is Integer Then
                    ctl.Width = CInt(items(i + 1))
                    i += 2
                Else
                    i += 1
                End If
                line.Controls.Add(ctl)
            Else
                i += 1
            End If
        End While
        Return line
    End Function

    Private Shared Function RowControl(ctrl As Control) As FlowLayoutPanel
        Dim row As New FlowLayoutPanel With {
            .Size = New Size(624, 28), .FlowDirection = FlowDirection.RightToLeft, .RightToLeft = RightToLeft.No,
            .WrapContents = False, .BackColor = Color.Transparent, .Margin = New Padding(0)
        }
        row.Controls.Add(ctrl)
        Return row
    End Function

    Private Sub PickLogo()
        Using dialog As New OpenFileDialog With {
            .Filter = "صور|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            .Title = "اختر شعار الشركة"
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using stream = IO.File.OpenRead(dialog.FileName)
                    picLogo.Image = Image.FromStream(stream)
                End Using
                _logoPath = dialog.FileName
            Catch ex As Exception
                UiTheme.Handle(ex, "تعذّر تحميل الشعار")
            End Try
        End Using
    End Sub

    ' ------------------------------------------------------------- storage --
    Private Sub LoadSettings()
        Try
            Using db = DbSession.Open(_session.ConnectionString)
                Dim conn = db.Connection
                txtNameAr.Text = Setting(conn, "company.name_ar", Setting(conn, "company.name", String.Empty))
                txtNameEn.Text = Setting(conn, "company.name_en", String.Empty)
                txtActivityAr.Text = Setting(conn, "company.activity_ar", String.Empty)
                txtActivityEn.Text = Setting(conn, "company.activity_en", String.Empty)
                txtCommercialReg.Text = Setting(conn, "company.commercial_reg", String.Empty)
                txtLicenseNo.Text = Setting(conn, "company.license_no", String.Empty)
                txtPhone.Text = Setting(conn, "company.phone", String.Empty)
                txtFax.Text = Setting(conn, "company.fax", String.Empty)
                txtMobile.Text = Setting(conn, "company.mobile", String.Empty)
                txtEmail.Text = Setting(conn, "company.email", String.Empty)
                txtWebsite.Text = Setting(conn, "company.website", String.Empty)
                txtTaxRate.Text = Setting(conn, "company.tax_rate", String.Empty)
                txtTaxNo.Text = Setting(conn, "company.tax_no", _session.BranchTaxNo)
                txtAddress.Text = Setting(conn, "company.address", String.Empty)
                txtInvoiceNotes.Text = Setting(conn, "company.invoice_notes", String.Empty)
                txtInvoiceNature.Text = Setting(conn, "company.invoice_nature", String.Empty)
                SelectCombo(cboPole, Setting(conn, "company.pole_device", "بدون"))

                chkAllowNoStock.Checked = SettingsRepository.GetBool(conn, "company.allow_sale_without_stock",
                                                                     SettingsRepository.GetBool(conn, "stock.allow_negative", False))
                chkOtherDefinitions.Checked = SettingsRepository.GetBool(conn, "company.other_definitions", False)
                chkPrintAuto.Checked = SettingsRepository.GetBool(conn, "company.report_print_auto", False)
                chkPrintInvoice.Checked = SettingsRepository.GetBool(conn, "company.report_print_invoice", True)
                chkPrintA5.Checked = SettingsRepository.GetBool(conn, "company.report_print_a5", False)

                Dim policy As String = Setting(conn, "company.report_policy", "1")
                radPolicy1.Checked = policy <> "2"
                radPolicy2.Checked = policy = "2"

                dtpFiscalStart.Value = ParseDate(Setting(conn, "company.fiscal_start", String.Empty),
                                                 New Date(Date.Today.Year, 1, 1))
                dtpFiscalEnd.Value = ParseDate(Setting(conn, "company.fiscal_end", String.Empty),
                                               New Date(Date.Today.Year, 12, 31))

                txtCurrencyEgp.Text = Setting(conn, "company.currency_egp", "1")
                txtCurrencyUsd.Text = Setting(conn, "company.currency_usd", String.Empty)
                txtCurrencyChf.Text = Setting(conn, "company.currency_chf", String.Empty)
                txtCurrencyEur.Text = Setting(conn, "company.currency_eur", String.Empty)

                txtReceiptFooter.Text = Setting(conn, "sales.receipt_footer", String.Empty)
                FillPrinters(cboPrinterA4, Setting(conn, "company.printer_a4", String.Empty))
                FillPrinters(cboPrinterReceipt, Setting(conn, "company.printer_receipt", String.Empty))
                FillPrinters(cboPrinterBarcode, Setting(conn, "company.printer_barcode", String.Empty))

                _logoPath = Setting(conn, "company.logo_path", String.Empty)
                If Not String.IsNullOrWhiteSpace(_logoPath) AndAlso IO.File.Exists(_logoPath) Then
                    Try
                        Using stream = IO.File.OpenRead(_logoPath)
                            picLogo.Image = Image.FromStream(stream)
                        End Using
                    Catch
                        ' شعار تالف أو غير مقروء: اتركه فارغًا.
                    End Try
                End If
            End Using
        Catch ex As Exception
            AppLog.Warn("Company settings load failed: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Setting(conn As System.Data.IDbConnection, key As String, fallback As String) As String
        Dim value As String = SettingsRepository.Get(conn, key)
        Return If(String.IsNullOrWhiteSpace(value), fallback, value)
    End Function

    Private Shared Function ParseDate(text As String, fallback As Date) As Date
        Dim parsed As Date
        If Date.TryParse(text, parsed) Then Return parsed
        Return fallback
    End Function

    Private Shared Sub SelectCombo(combo As ComboBox, value As String)
        If String.IsNullOrWhiteSpace(value) Then Return
        For i As Integer = 0 To combo.Items.Count - 1
            If String.Equals(Convert.ToString(combo.Items(i)), value, StringComparison.OrdinalIgnoreCase) Then
                combo.SelectedIndex = i
                Return
            End If
        Next
        combo.Items.Add(value)
        combo.SelectedIndex = combo.Items.Count - 1
    End Sub

    Private Shared Sub FillPrinters(combo As ComboBox, selected As String)
        combo.Items.Clear()
        combo.Items.Add("بدون")
        For Each printer In PrinterSettings.InstalledPrinters
            combo.Items.Add(printer.ToString())
        Next
        SelectCombo(combo, If(String.IsNullOrWhiteSpace(selected), "بدون", selected))
    End Sub

    Private Sub UpdateQr()
        Dim payload As String =
            "شركة: " & txtNameAr.Text.Trim() & Environment.NewLine &
            "هاتف: " & txtPhone.Text.Trim() & Environment.NewLine &
            "الرقم الضريبي: " & txtTaxNo.Text.Trim()
        Dim image = QrHelper.CreateImage(payload, 5)
        If picQr.Image IsNot Nothing Then picQr.Image.Dispose()
        picQr.Image = image
    End Sub

    Private Sub SaveSettings()
        Try
            Using db = DbSession.Open(_session.ConnectionString)
                db.BeginTransaction()
                Dim tx = db.Transaction
                Put(db.Connection, tx, "company.name_ar", txtNameAr.Text)
                Put(db.Connection, tx, "company.name", txtNameAr.Text)
                Put(db.Connection, tx, "company.name_en", txtNameEn.Text)
                Put(db.Connection, tx, "company.activity_ar", txtActivityAr.Text)
                Put(db.Connection, tx, "company.activity_en", txtActivityEn.Text)
                Put(db.Connection, tx, "company.commercial_reg", txtCommercialReg.Text)
                Put(db.Connection, tx, "company.license_no", txtLicenseNo.Text)
                Put(db.Connection, tx, "company.phone", txtPhone.Text)
                Put(db.Connection, tx, "company.fax", txtFax.Text)
                Put(db.Connection, tx, "company.mobile", txtMobile.Text)
                Put(db.Connection, tx, "company.email", txtEmail.Text)
                Put(db.Connection, tx, "company.website", txtWebsite.Text)
                Put(db.Connection, tx, "company.tax_rate", txtTaxRate.Text)
                Put(db.Connection, tx, "company.tax_no", txtTaxNo.Text)
                Put(db.Connection, tx, "company.address", txtAddress.Text)
                Put(db.Connection, tx, "company.invoice_notes", txtInvoiceNotes.Text)
                Put(db.Connection, tx, "company.invoice_nature", txtInvoiceNature.Text)
                Put(db.Connection, tx, "company.pole_device", Convert.ToString(cboPole.SelectedItem))
                _session.RequirePermission("settings.manage")
                Dim allowNoStock As String = If(chkAllowNoStock.Checked, "1", "0")
                Put(db.Connection, tx, "company.allow_sale_without_stock", allowNoStock)
                Put(db.Connection, tx, "stock.allow_negative", allowNoStock)
                Put(db.Connection, tx, "company.other_definitions", If(chkOtherDefinitions.Checked, "1", "0"))
                Put(db.Connection, tx, "company.report_print_auto", If(chkPrintAuto.Checked, "1", "0"))
                Put(db.Connection, tx, "company.report_print_invoice", If(chkPrintInvoice.Checked, "1", "0"))
                Put(db.Connection, tx, "company.report_print_a5", If(chkPrintA5.Checked, "1", "0"))
                Put(db.Connection, tx, "company.report_policy", If(radPolicy2.Checked, "2", "1"))
                Put(db.Connection, tx, "company.fiscal_start", dtpFiscalStart.Value.ToString("yyyy-MM-dd"))
                Put(db.Connection, tx, "company.fiscal_end", dtpFiscalEnd.Value.ToString("yyyy-MM-dd"))
                Put(db.Connection, tx, "company.currency_egp", txtCurrencyEgp.Text)
                Put(db.Connection, tx, "company.currency_usd", txtCurrencyUsd.Text)
                Put(db.Connection, tx, "company.currency_chf", txtCurrencyChf.Text)
                Put(db.Connection, tx, "company.currency_eur", txtCurrencyEur.Text)
                Put(db.Connection, tx, "company.logo_path", _logoPath)
                Put(db.Connection, tx, "company.printer_a4", Convert.ToString(cboPrinterA4.SelectedItem))
                Put(db.Connection, tx, "company.printer_receipt", Convert.ToString(cboPrinterReceipt.SelectedItem))
                Put(db.Connection, tx, "company.printer_barcode", Convert.ToString(cboPrinterBarcode.SelectedItem))
                db.Commit()
            End Using
            UpdateQr()
            UiTheme.Info("تم حفظ بيانات الشركة بنجاح.")
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر حفظ بيانات الشركة")
        End Try
    End Sub

    Private Sub Put(conn As System.Data.IDbConnection, tx As System.Data.IDbTransaction, key As String, value As String)
        SettingsRepository.Set(conn, tx, key, If(value, String.Empty).Trim(), _session.UserId)
    End Sub

    ' -------------------------------------------------------- drag & paint --
    Private Sub StartDrag(e As MouseEventArgs)
        _dragging = True
        _dragOffset = e.Location
    End Sub

    Private Sub DoDragForm(e As MouseEventArgs)
        If Not _dragging Then Return
        Location = New Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Using pen As New Pen(Color.FromArgb(27, 94, 32), 1)
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1)
        End Using
    End Sub
End Class
