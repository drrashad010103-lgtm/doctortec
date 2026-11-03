Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms

''' <summary>Alias class so callers can instantiate either ProSaleForm or SaleForm.</summary>
Public Class ProSaleForm
    Inherits SaleForm

    Public Sub New(session As AppSession)
        MyBase.New(session)
    End Sub
End Class

''' <summary>
''' شاشة الكاشير الاحترافية المتكاملة (Pro POS):
''' - إضافة أصناف من قائمة منسدلة ذكية (Drop-Down List) مباشرة بدون بوب أب بحث
''' - تعديل كامل داخل جدول الفاتورة (الكمية، السعر، خصم %، قيمة الخصم)
''' - زر «ℹ️ تفاصيل» بجوار اسم كل صنف يفتح بطاقة الصنف الكاملة (الباركود، الرصيد، الإكسبير والتشغيلات، التعليمات والجرعة)
''' - شاشة جانبية لحظية لتحليل الصنف المختار (سعر الشراء، سعر البيع، ربحية الوحدة، ربح السطر، نسبة الربح %، خصم الصنف %، وإجمالي ربح الفاتورة)
''' - بحث فوري عن العميل بـ (رقم العميل / كود العميل / الموبايل / التليفون / الاسم) + تسجيل عميل جديد مع إضافة نقاط ولاء فورية
''' - تعدد الفواتير المعلقة + حاسبة باقي النقدية (الفكة) + الكوبونات وقسائم الشراء
''' </summary>
Public Class ReceiptSnapshot
    Public Property InvoiceNo As String
    Public Property CustomerName As String
    Public Property Lines As New List(Of String)()
    Public Property Subtotal As Decimal
    Public Property Discount As Decimal
    Public Property Tax As Decimal
    Public Property NetTotal As Decimal
    Public Property Paid As Decimal
    Public Property Due As Decimal
    Public Property CouponDiscount As Decimal
    Public Property LoyaltyPoints As Decimal
    Public Property VoucherAmount As Decimal
End Class

Public Class SaleForm
    Inherits Form

    Private Class HeldInvoiceSlot
        Public Property SlotTitle As String
        Public Property CustomerId As Long?
        Public Property Lines As List(Of InvoiceEditorLine)
        Public Property HeaderDiscount As Decimal
        Public Property Notes As String

        Public Overrides Function ToString() As String
            Return SlotTitle
        End Function
    End Class

    Private ReadOnly _session As AppSession
    Private ReadOnly _editor As InvoiceEditor

    ' شريط الإضافة السريعة والـ Drop-Down List بدون بوب أب
    Private ReadOnly txtScan As New TextBox()
    Private ReadOnly txtDropFilter As New TextBox()
    Private ReadOnly cboItemDrop As New ComboBox()
    Private ReadOnly numDropQty As New NumericUpDown()
    Private ReadOnly btnAddDrop As New Button()
    Private ReadOnly lblPicked As New Label()
    Private ReadOnly lblQty As New Label()
    Private ReadOnly lblShift As New Label()

    ' شريط العميل والولاء والدفع
    Private ReadOnly txtCustomerSearch As New TextBox()
    Private ReadOnly cboCustomer As New ComboBox()
    Private ReadOnly lblCustomerMeta As New Label()
    Private ReadOnly cboSeller As New ComboBox()
    Private ReadOnly cboDelivery As New ComboBox()
    Private ReadOnly cboPayment As New ComboBox()
    Private ReadOnly txtPaid As New TextBox()
    Private ReadOnly txtCashReceived As New TextBox()
    Private ReadOnly lblChangeDue As New Label()
    Private ReadOnly txtDiscount As New TextBox()
    Private ReadOnly txtDiscountPctHeader As New TextBox()
    Private ReadOnly lblTotal As New Label()
    Private ReadOnly lblDue As New Label()
    Private ReadOnly lblTax As New Label()

    ' شريط الدفع العريض أسفل الأصناف
    Private ReadOnly lblBandSubtotal As New Label()
    Private ReadOnly lblBandDiscount As New Label()

    ' الشاشة الجانبية لتحليل ربحية الصنف والفاتورة
    Private ReadOnly lblSideItemName As New Label()
    Private ReadOnly lblCost As New Label()
    Private ReadOnly lblSale As New Label()
    Private ReadOnly lblWholesale As New Label()
    Private ReadOnly lblMinPrice As New Label()
    Private ReadOnly lblLineDiscountVal As New Label()
    Private ReadOnly lblDiscountPct As New Label()
    Private ReadOnly lblUnitProfit As New Label()
    Private ReadOnly lblLineProfit As New Label()
    Private ReadOnly lblInvoiceProfit As New Label()
    Private ReadOnly lblTotalDiscount As New Label()
    Private ReadOnly lblSummaryTax As New Label()
    Private ReadOnly lblSubtotal As New Label()
    Private ReadOnly lblNet As New Label()
    Private ReadOnly lblRemain As New Label()

    ' التبويبات السفلية (كوبون / ولاء / قسيمة / فواتير معلقة / ملاحظات)
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly lblQuantities As New Label()
    Private ReadOnly txtCouponCode As New TextBox()
    Private ReadOnly txtRedeemPoints As New TextBox()
    Private ReadOnly lblCustomerPoints As New Label()
    Private ReadOnly lblEarnedPointsPreview As New Label()
    Private ReadOnly txtVoucherCode As New TextBox()
    Private ReadOnly txtVoucherAmount As New TextBox()
    Private ReadOnly cboHeldInvoices As New ComboBox()

    Private _customers As New List(Of CustomerSummary)()
    Private _dropdownItems As New List(Of ItemSummary)()
    Private _heldSlots As New List(Of HeldInvoiceRecord)()
    Private _paidTouched As Boolean
    Private _updatingPaid As Boolean
    Private _isPosting As Boolean
    Private _draftEventId As String = Uuid7.NewId()
    Private _couponPreviewDiscount As Decimal
    Private _loyaltyPreviewDiscount As Decimal
    Private _voucherPreviewAmount As Decimal
    Private _updatingHeaderDiscount As Boolean
    Private _suppressDropFilter As Boolean
    Private _lastResult As InvoiceResult
    Private _lastReceipt As ReceiptSnapshot
    Private _keypadBuffer As String = String.Empty
    Private _dragging As Boolean
    Private _dragOffset As Point

    ' === DOCTOR TECH brand tones (logo palette) ============================
    Private ReadOnly Purple As Color = UiTheme.BrandNavyDark
    Private ReadOnly Green As Color = UiTheme.BrandNavy
    Private ReadOnly GreenDark As Color = UiTheme.BrandNavy
    Private ReadOnly Indigo As Color = UiTheme.BrandBlue
    Private ReadOnly PageBack As Color = UiTheme.Surface

    ' === Branded prompt / status strip =====================================
    Private ReadOnly lblBrandPrompt As New Label()
    Private ReadOnly lblBrandStatusReg As New Label()
    Private ReadOnly lblBrandStatusDate As New Label()
    Private ReadOnly lblBrandStatusCashier As New Label()
    Private ReadOnly lblBrandStatusScreen As New Label()
    Private ReadOnly lblBrandStatusCust As New Label()
    Private ReadOnly lblBrandStatusOnline As New Label()

    ' === Right-hand premium action rail ====================================
    Private ReadOnly _railScroll As New Panel()
    Private ReadOnly _railStack As New TableLayoutPanel()
    Private _railNeedsTopPin As Boolean

    Public Sub New(session As AppSession)
        _session = session
        Tag = "rs-custom-chrome"
        _editor = New InvoiceEditor(session, "sale")
        InitializeUi()
        LoadLookups()
        RefreshDropdownItems(String.Empty)
        RefreshShift()
        AddHandler _editor.TotalsChanged, AddressOf EditorTotalsChanged
        AddHandler _editor.ItemInfoRequested, AddressOf ShowItemFullCard
        AddHandler KeyDown, AddressOf SaleKeyDown
        EditorTotalsChanged(Me, EventArgs.Empty)
    End Sub

    ' ------------------------------------------------------------------ UI --
    Private Sub InitializeUi()
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = False
        Font = UiTheme.Font(9.0F)
        Text = "DOCTOR TECH — فاتورة بيع"
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.Manual
        Bounds = Screen.FromControl(Me).WorkingArea
        _editor.GridControl.RightToLeft = RightToLeft.Yes
        _editor.GridControl.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        _editor.GridControl.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        MinimumSize = New Size(1100, 720)
        BackColor = UiTheme.Surface
        DoubleBuffered = True
        KeyPreview = True
        Padding = New Padding(6)
        UiTheme.ApplyIcon(Me)

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 5,
            .BackColor = UiTheme.Surface,
            .RightToLeft = RightToLeft.No
        }
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 132))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 116))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 58))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 92))

        root.Controls.Add(BuildTitleBar(), 0, 0)
        root.Controls.Add(BuildBody(), 0, 1)
        root.Controls.Add(BuildSettlement(), 0, 2)
        root.Controls.Add(BuildActions(), 0, 3)
        root.Controls.Add(BuildFooter(), 0, 4)
        Controls.Add(root)
    End Sub

    Private Function BuildTitleBar() As Control
        Dim bar As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavyDark,
            .Padding = New Padding(12, 6, 12, 6)
        }

        Dim brandFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Right,
            .AutoSize = True,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        Dim logo As New PictureBox With {
            .Size = New Size(56, 56),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 0, 12, 0)
        }
        Dim logoImage As Image = UiTheme.LoadBrandLogo()
        If logoImage IsNot Nothing Then logo.Image = logoImage
        brandFlow.Controls.Add(logo)

        Dim wordmark As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1,
            .RowCount = 2,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 4, 6, 0)
        }
        wordmark.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        wordmark.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        wordmark.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim lblWord As New Label With {
            .Text = "DOCTOR TECH",
            .AutoSize = True,
            .Font = UiTheme.Font(12.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0)
        }
        Dim lblSub As New Label With {
            .Text = "فاتورة بيع — نقطة البيع",
            .AutoSize = True,
            .Font = UiTheme.Font(8.4F),
            .ForeColor = UiTheme.Lighten(UiTheme.BrandMist, 0.35F),
            .BackColor = Color.Transparent,
            .Margin = New Padding(0)
        }
        wordmark.Controls.Add(lblWord, 0, 0)
        wordmark.Controls.Add(lblSub, 0, 1)
        brandFlow.Controls.Add(wordmark)
        bar.Controls.Add(brandFlow)

        Dim winFlow As New FlowLayoutPanel With {
            .Dock = DockStyle.Left,
            .AutoSize = True,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }
        Dim btnMin As GlassButton = WindowButton("—", Color.FromArgb(70, 130, 200), Color.FromArgb(40, 85, 150))
        Dim btnMax As GlassButton = WindowButton("□", Color.FromArgb(70, 130, 200), Color.FromArgb(40, 85, 150))
        Dim btnClose As GlassButton = WindowButton("✕", Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        AddHandler btnMin.Click, Sub(s, e) WindowState = FormWindowState.Minimized
        AddHandler btnMax.Click, Sub(s, e) WindowState = If(WindowState = FormWindowState.Maximized, FormWindowState.Normal, FormWindowState.Maximized)
        AddHandler btnClose.Click, Sub(s, e) Close()
        winFlow.Controls.AddRange({btnClose, btnMax, btnMin})
        bar.Controls.Add(winFlow)

        Dim title As New Label With {
            .Text = "شاشة البيع الرئيسية  •  DOCTOR TECH RETAIL POS",
            .Dock = DockStyle.Fill,
            .ForeColor = UiTheme.BrandGold,
            .Font = UiTheme.Font(10.5F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0)
        }

        ' خانة إدخال كود الصنف/الباركود في أعلى منتصف الشاشة تمامًا
        Dim scanBox As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.Transparent,
            .Margin = New Padding(0), .Padding = New Padding(0, 10, 0, 10)
        }
        scanBox.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150.0F))
        scanBox.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        scanBox.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 116.0F))

        Dim lblScanCaption As New Label With {
            .Text = "كود الصنف / الباركود", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(9.6F, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent,
            .Margin = New Padding(0, 0, 8, 0)
        }
        txtScan.PlaceholderText = "امسح الباركود أو اكتب الكود ثم Enter"
        txtScan.Font = UiTheme.Font(11.5F, FontStyle.Bold)
        txtScan.RightToLeft = RightToLeft.No
        txtScan.TextAlign = HorizontalAlignment.Center
        txtScan.Margin = New Padding(0)
        txtScan.Dock = DockStyle.Fill
        AddHandler txtScan.KeyDown, Sub(s, e)
                                        If e.KeyCode = Keys.Enter Then
                                            e.SuppressKeyPress = True
                                            AddByScan()
                                        End If
                                    End Sub
        Dim scanBtn As GlassButton = RailButton("📷 باركود", 116, True)
        scanBtn.Dock = DockStyle.Fill
        scanBtn.Margin = New Padding(6, 0, 0, 0)
        AddHandler scanBtn.Click, Sub(s, e) AddByScan()

        scanBox.Controls.Add(lblScanCaption, 0, 0)
        scanBox.Controls.Add(txtScan, 1, 0)
        scanBox.Controls.Add(scanBtn, 2, 0)

        Dim scanHost As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0)
        }
        scanHost.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        scanHost.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 660.0F))
        scanHost.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        scanHost.Controls.Add(scanBox, 1, 0)

        Dim topStack As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Color.Transparent, .Margin = New Padding(0)
        }
        topStack.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        topStack.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        topStack.Controls.Add(title, 0, 0)
        topStack.Controls.Add(scanHost, 0, 1)
        bar.Controls.Add(topStack)

        lblShift.AutoSize = False
        lblShift.Dock = DockStyle.Bottom
        lblShift.Height = 22
        lblShift.Font = UiTheme.Font(8.5F, FontStyle.Bold)
        lblShift.ForeColor = UiTheme.Lighten(UiTheme.BrandMist, 0.25F)
        lblShift.TextAlign = ContentAlignment.MiddleCenter
        lblShift.BackColor = Color.Transparent
        bar.Controls.Add(lblShift)

        AddHandler bar.MouseDown, Sub(s, e) StartDrag(e)
        AddHandler bar.MouseMove, Sub(s, e) DoDragForm(e)
        AddHandler bar.MouseUp, Sub(s, e) _dragging = False
        AddHandler wordmark.MouseDown, Sub(s, e) StartDrag(e)
        AddHandler wordmark.MouseMove, Sub(s, e) DoDragForm(e)
        AddHandler wordmark.MouseUp, Sub(s, e) _dragging = False
        Return bar
    End Function

    Private Shared Function WindowButton(text As String, bottom As Color, top As Color) As GlassButton
        Dim b As New GlassButton With {
            .Text = text,
            .Size = New Size(34, 30),
            .Font = UiTheme.Font(9.0F, FontStyle.Bold),
            .CornerRadius = 8,
            .Margin = New Padding(2, 0, 2, 0),
            .TabStop = False
        }
        b.SetTone(bottom, top)
        Return b
    End Function

    Private Function BuildBody() As Control
        Dim body As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.Surface,
            .Padding = New Padding(0, 6, 0, 6)
        }

        _editor.Dock = DockStyle.Fill
        _editor.ToolbarVisible = False
        AddHandler _editor.GridControl.SelectionChanged, Sub(s, e)
                                                             _keypadBuffer = String.Empty
                                                             UpdateSelectedItem()
                                                         End Sub
        AddHandler _editor.GridControl.CellEndEdit, Sub(s, e) UpdateSelectedItem()

        Dim rail As Control = BuildRail()
        body.Controls.Add(_editor)
        body.Controls.Add(rail)
        Return body
    End Function

    ''' <summary>شريط الدفع العريض أسفل الأصناف: حقول الدفع والخصم في سطر، والإجماليات في بلاطات كبيرة.</summary>
    Private Function BuildSettlement() As Control
        Dim band As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 2,
            .BackColor = UiTheme.Surface,
            .Padding = New Padding(2, 4, 2, 2),
            .RightToLeft = RightToLeft.Yes
        }
        band.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        band.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' --- سطر الإدخال ---
        Dim inputs As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 11, .RowCount = 1,
            .BackColor = Color.White, .Margin = New Padding(0),
            .RightToLeft = RightToLeft.Yes, .Padding = New Padding(8, 3, 8, 3)
        }
        Dim widths = New Integer() {92, 150, 74, 116, 92, 70, 92, 62, 82, 72, 116}
        For Each w As Integer In widths
            inputs.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CSng(w)))
        Next

        Dim capFont = UiTheme.Font(8.8F, FontStyle.Bold)
        Dim makeCaption = Function(text As String) As Label
                             Return New Label With {
                                 .Text = text, .Dock = DockStyle.Fill,
                                 .TextAlign = ContentAlignment.MiddleRight,
                                 .Font = capFont, .ForeColor = UiTheme.Muted,
                                 .Margin = New Padding(6, 0, 4, 0)
                             }
                         End Function

        cboPayment.DropDownStyle = ComboBoxStyle.DropDownList
        cboPayment.Font = UiTheme.Font(9.4F)
        cboPayment.Margin = New Padding(0)

        txtPaid.Font = UiTheme.Font(10.5F, FontStyle.Bold)
        txtPaid.TextAlign = HorizontalAlignment.Center
        txtPaid.RightToLeft = RightToLeft.No
        txtPaid.Margin = New Padding(0)
        AddHandler txtPaid.TextChanged, Sub(s, e)
                                            If _updatingPaid Then Return
                                            _paidTouched = True
                                            UpdatePaymentLabels()
                                        End Sub

        Dim btnFull As GlassButton = RailButton("✔ الكامل", 92, True)
        btnFull.Margin = New Padding(4, 0, 0, 0)
        AddHandler btnFull.Click, Sub(s, e) PayFull()

        txtDiscount.Font = UiTheme.Font(9.8F)
        txtDiscount.TextAlign = HorizontalAlignment.Center
        txtDiscount.RightToLeft = RightToLeft.No
        txtDiscount.Margin = New Padding(0)
        AddHandler txtDiscount.Leave, Sub(s, e) ApplyHeaderDiscountFromAmount()
        AddHandler txtDiscount.KeyDown, Sub(s, e)
                                            If e.KeyCode = Keys.Enter Then
                                                e.SuppressKeyPress = True
                                                ApplyHeaderDiscountFromAmount()
                                            End If
                                        End Sub

        txtDiscountPctHeader.Font = UiTheme.Font(9.8F)
        txtDiscountPctHeader.TextAlign = HorizontalAlignment.Center
        txtDiscountPctHeader.RightToLeft = RightToLeft.No
        txtDiscountPctHeader.Margin = New Padding(0)
        AddHandler txtDiscountPctHeader.Leave, Sub(s, e) ApplyHeaderDiscountFromPercent()
        AddHandler txtDiscountPctHeader.KeyDown, Sub(s, e)
                                                     If e.KeyCode = Keys.Enter Then
                                                         e.SuppressKeyPress = True
                                                         ApplyHeaderDiscountFromPercent()
                                                     End If
                                                 End Sub

        txtCashReceived.Font = UiTheme.Font(9.8F)
        txtCashReceived.TextAlign = HorizontalAlignment.Center
        txtCashReceived.RightToLeft = RightToLeft.No
        txtCashReceived.Margin = New Padding(0)
        AddHandler txtCashReceived.TextChanged, Sub(s, e) UpdatePaymentLabels()

        Dim inputCells() As Control = {
            makeCaption("طريقة الدفع:"), cboPayment,
            makeCaption("المدفوع:"), txtPaid, btnFull,
            makeCaption("خصم ج.م:"), txtDiscount,
            makeCaption("خصم %:"), txtDiscountPctHeader,
            makeCaption("المستلم:"), txtCashReceived}
        For i As Integer = 0 To inputCells.Length - 1
            inputs.Controls.Add(inputCells(i), i, 0)
        Next
        band.Controls.Add(inputs, 0, 0)

        ' --- سطر البلاطات ---
        Dim tiles As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False, .BackColor = Color.White, .Margin = New Padding(0),
            .Padding = New Padding(6, 2, 6, 2)
        }
        tiles.Controls.Add(SettlementTile("قبل الخصم", lblBandSubtotal, UiTheme.BrandLight, UiTheme.Ink))
        tiles.Controls.Add(SettlementTile("إجمالي الخصم", lblBandDiscount, Color.FromArgb(253, 235, 235), UiTheme.DangerRed))
        tiles.Controls.Add(SettlementTile("الضريبة", lblTax, UiTheme.BrandLight, UiTheme.Muted))
        tiles.Controls.Add(SettlementTile("صافي الفاتورة", lblTotal, UiTheme.BrandNavy, Color.White))
        tiles.Controls.Add(SettlementTile("الآجل / المديونية", lblDue, Color.FromArgb(253, 235, 235), UiTheme.DangerRed))

        lblChangeDue.AutoSize = False
        lblChangeDue.Text = "0.00"
        tiles.Controls.Add(SettlementTile("الباقي للعميل", lblChangeDue, UiTheme.BrandGold, Color.White))
        band.Controls.Add(tiles, 0, 1)

        Return band
    End Function

    ''' <summary>بلاطة إجمالي واحدة: عنوان صغير فوق ورقم كبير واضح، بحدود بلون الخلفية.</summary>
    Private Shared Function SettlementTile(caption As String, value As Label, back As Color, fore As Color) As Control
        Dim tile As New Panel With {
            .Size = New Size(212, 64),
            .Margin = New Padding(4, 2, 4, 2),
            .BackColor = back
        }
        Dim cap As New Label With {
            .Text = caption, .Dock = DockStyle.Top, .Height = 20,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Font = UiTheme.Font(8.4F, FontStyle.Bold),
            .ForeColor = If(fore = Color.White, Color.FromArgb(225, 232, 245), UiTheme.Muted)
        }
        value.AutoSize = False
        value.Dock = DockStyle.Fill
        value.TextAlign = ContentAlignment.MiddleCenter
        value.BackColor = Color.Transparent
        value.ForeColor = fore
        value.Font = UiTheme.Font(13.5F, FontStyle.Bold)
        value.RightToLeft = RightToLeft.No
        value.Text = "0.00"
        tile.Controls.Add(value)
        tile.Controls.Add(cap)
        Return tile
    End Function

    Private Function BuildRail() As Control
        Dim rail As New Panel With {
            .Dock = DockStyle.Right,
            .Width = 420,
            .BackColor = UiTheme.Surface,
            .Padding = New Padding(6, 0, 0, 0)
        }

        lblBrandPrompt.Dock = DockStyle.Top
        lblBrandPrompt.Height = 34
        lblBrandPrompt.BackColor = UiTheme.BrandLight
        lblBrandPrompt.ForeColor = UiTheme.BrandNavy
        lblBrandPrompt.Font = UiTheme.Font(8.9F, FontStyle.Bold)
        lblBrandPrompt.TextAlign = ContentAlignment.MiddleRight
        lblBrandPrompt.RightToLeft = RightToLeft.Yes
        lblBrandPrompt.Padding = New Padding(8, 0, 8, 0)
        lblBrandPrompt.Text = "🟦 امسح الباركود أو اختر الصنف من القائمة ثم اضغط Enter"

        _railScroll.Dock = DockStyle.Fill
        _railScroll.AutoScroll = True
        _railScroll.BackColor = UiTheme.Surface
        AddHandler _railStack.Layout, Sub(s, ev)
                                          If _railNeedsTopPin Then
                                              _railNeedsTopPin = False
                                              _railScroll.ScrollControlIntoView(_railStack)
                                          End If
                                      End Sub

        _railStack.Dock = DockStyle.Top
        _railStack.ColumnCount = 1
        _railStack.AutoSize = True
        _railStack.AutoSizeMode = AutoSizeMode.GrowAndShrink
        _railStack.BackColor = UiTheme.Surface
        _railStack.Padding = New Padding(0)
        _railStack.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        BuildRailSections()

        _railScroll.Controls.Add(_railStack)
        rail.Controls.Add(_railScroll)
        rail.Controls.Add(lblBrandPrompt)
        Return rail
    End Function

    Private Function BuildRailCard(title As String) As TableLayoutPanel
        Dim card As New GlassPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FillColor = Color.White,
            .BorderColor = UiTheme.BrandBorder,
            .CornerRadius = 14,
            .Margin = New Padding(0, 0, 0, 8),
            .Padding = New Padding(10, 8, 10, 10)
        }

        Dim stack As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1,
            .BackColor = Color.White,
            .Margin = New Padding(0)
        }
        stack.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim head As New GlassPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = False,
            .Height = 30,
            .FillColor = UiTheme.BrandNavy,
            .GradientBottom = UiTheme.BrandBlue,
            .BorderColor = UiTheme.BrandNavy,
            .CornerRadius = 9,
            .Padding = New Padding(9, 0, 9, 0),
            .Margin = New Padding(0, 0, 0, 6)
        }
        Dim headLabel As New Label With {
            .Text = title,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(9.6F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleRight
        }
        head.Controls.Add(headLabel)
        stack.Controls.Add(head)
        card.Controls.Add(stack)
        _railStack.Controls.Add(card)
        Return stack
    End Function

    Private Sub AddCardControl(stack As TableLayoutPanel, ctl As Control, Optional height As Integer = 0)
        If height > 0 Then
            ctl.Dock = DockStyle.Top
            ctl.Height = height
        Else
            ctl.Dock = DockStyle.Top
            ctl.AutoSize = True
        End If
        ctl.Margin = New Padding(0, 2, 0, 2)
        stack.Controls.Add(ctl)
    End Sub

    Private Shared Function RailField(caption As String, editor As Control,
                                      Optional numeric As Boolean = False,
                                      Optional captionWidth As Single = 108.0F) As Control
        Dim row As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2,
            .RowCount = 1,
            .RightToLeft = RightToLeft.Yes,
            .BackColor = Color.White,
            .Margin = New Padding(0, 2, 0, 2)
        }
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, captionWidth))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        Dim lbl As New Label With {
            .Text = caption,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight,
            .Margin = New Padding(0, 0, 6, 0)
        }
        PrepareRailEditor(editor, numeric)
        row.Controls.Add(lbl, 0, 0)
        row.Controls.Add(editor, 1, 0)
        Return row
    End Function

    ''' <summary>Two caption/field pairs side by side — halves the rows a section needs.</summary>
    Private Shared Function RailPair(caption1 As String, editor1 As Control,
                                    caption2 As String, editor2 As Control,
                                    Optional numeric As Boolean = True) As Control
        Dim row As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 4,
            .RowCount = 1,
            .RightToLeft = RightToLeft.Yes,
            .BackColor = Color.White,
            .Margin = New Padding(0, 2, 0, 2)
        }
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        Dim lbl1 As New Label With {
            .Text = caption1, .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold), .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(0, 0, 5, 0)
        }
        Dim lbl2 As New Label With {
            .Text = caption2, .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold), .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(0, 0, 5, 0)
        }
        PrepareRailEditor(editor1, numeric)
        PrepareRailEditor(editor2, numeric)
        row.Controls.Add(lbl1, 0, 0)
        row.Controls.Add(editor1, 1, 0)
        row.Controls.Add(lbl2, 2, 0)
        row.Controls.Add(editor2, 3, 0)
        Return row
    End Function

    ''' <summary>Field with its action button on the same row.</summary>
    Private Shared Function RailFieldWithButton(caption As String, editor As Control,
                                                button As Control, Optional numeric As Boolean = False) As Control
        Dim row As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 3,
            .RowCount = 1,
            .RightToLeft = RightToLeft.Yes,
            .BackColor = Color.White,
            .Margin = New Padding(0, 2, 0, 2)
        }
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 108.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 112.0F))
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold), .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(0, 0, 6, 0)
        }
        PrepareRailEditor(editor, numeric)
        button.Dock = DockStyle.Fill
        button.Margin = New Padding(4, 0, 0, 0)
        row.Controls.Add(lbl, 0, 0)
        row.Controls.Add(editor, 1, 0)
        row.Controls.Add(button, 2, 0)
        Return row
    End Function

    ''' <summary>Arabic-reading controls make Uniscribe render digits in Arabic-Indic shapes with
    ''' comma separators, so money/quantity editors are pinned to left-to-right.</summary>
    Private Shared Sub PrepareRailEditor(editor As Control, numeric As Boolean)
        editor.Dock = DockStyle.Fill
        editor.Margin = New Padding(0)
        If numeric Then editor.RightToLeft = RightToLeft.No
    End Sub

    Private Shared Function RailButtonRow(ParamArray buttons() As Control) As Control
        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = True,
            .BackColor = Color.White,
            .Margin = New Padding(0, 2, 0, 2)
        }
        For Each b In buttons
            flow.Controls.Add(b)
        Next
        Return flow
    End Function

    Private Shared Function RailButton(text As String, width As Integer, Optional accent As Boolean = False) As GlassButton
        Dim b As New GlassButton With {
            .Text = text,
            .Size = New Size(width, 32),
            .Font = UiTheme.Font(8.8F, FontStyle.Bold),
            .CornerRadius = 9,
            .Margin = New Padding(2, 2, 2, 2)
        }
        If accent Then
            b.SetTone(UiTheme.BrandCyan, UiTheme.BrandNavyDark)
        Else
            b.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        End If
        Return b
    End Function

    Private Sub BuildRailSections()
        ' === 1) Item search / scan =========================================
        Dim itemStack As TableLayoutPanel = BuildRailCard("🔍 إضافة صنف للفاتورة")

        txtDropFilter.PlaceholderText = "اكتب جزءًا من الاسم أو الكود..."
        txtDropFilter.Font = UiTheme.Font(9.4F)
        txtDropFilter.RightToLeft = RightToLeft.Yes
        AddHandler txtDropFilter.TextChanged, Sub(s, e)
                                                  If _suppressDropFilter Then Return
                                                  RefreshDropdownItems(txtDropFilter.Text)
                                                  If cboItemDrop.Items.Count > 0 AndAlso txtDropFilter.Focused Then
                                                      OpenDropdown(cboItemDrop)
                                                  End If
                                              End Sub
        AddHandler txtDropFilter.KeyDown, Sub(s, e)
                                               If e.KeyCode = Keys.Down AndAlso cboItemDrop.Items.Count > 0 Then
                                                   e.SuppressKeyPress = True
                                                   cboItemDrop.Focus()
                                                   OpenDropdown(cboItemDrop)
                                               ElseIf e.KeyCode = Keys.Enter Then
                                                   e.SuppressKeyPress = True
                                                   AddSelectedDropdownItem()
                                               End If
                                           End Sub
        AddCardControl(itemStack, RailField("تصفية سريعة:", txtDropFilter), 30)

        cboItemDrop.DropDownWidth = 560
        cboItemDrop.MaxDropDownItems = 16
        cboItemDrop.DropDownStyle = ComboBoxStyle.DropDownList
        cboItemDrop.Font = UiTheme.Font(9.4F, FontStyle.Bold)
        cboItemDrop.RightToLeft = RightToLeft.Yes
        AddHandler cboItemDrop.KeyDown, Sub(s, e)
                                            If e.KeyCode = Keys.Enter Then
                                                e.SuppressKeyPress = True
                                                AddSelectedDropdownItem()
                                            End If
                                        End Sub
        AddCardControl(itemStack, cboItemDrop, 30)

        numDropQty.Minimum = 1D
        numDropQty.Maximum = 9999D
        numDropQty.Value = 1D
        numDropQty.DecimalPlaces = 0
        numDropQty.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        numDropQty.TextAlign = HorizontalAlignment.Center
        btnAddDrop.Text = "➕ أضف"
        btnAddDrop.Size = New Size(96, 28)
        btnAddDrop.FlatStyle = FlatStyle.Flat
        btnAddDrop.BackColor = UiTheme.BrandCyan
        btnAddDrop.ForeColor = Color.White
        btnAddDrop.Font = UiTheme.Font(8.4F, FontStyle.Bold)
        btnAddDrop.Cursor = Cursors.Hand
        btnAddDrop.FlatAppearance.BorderSize = 0
        AddHandler btnAddDrop.Click, Sub(s, e) AddSelectedDropdownItem()
        Dim qtyRow As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.White, .Margin = New Padding(0, 2, 0, 2)
        }
        qtyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70.0F))
        qtyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        qtyRow.Controls.Add(numDropQty, 0, 0)
        qtyRow.Controls.Add(btnAddDrop, 1, 0)
        numDropQty.Dock = DockStyle.Fill
        btnAddDrop.Dock = DockStyle.Fill
        AddCardControl(itemStack, qtyRow, 30)

        ' مربع المسح انتقل لأعلى منتصف الشاشة (BuildTitleBar) ليبقى أمام الكاشير فور فتح الفاتورة

        lblPicked.AutoSize = False
        lblPicked.Height = 24
        lblPicked.Text = "—"
        lblPicked.TextAlign = ContentAlignment.MiddleRight
        lblPicked.ForeColor = UiTheme.BrandNavy
        lblPicked.BackColor = UiTheme.BrandLight
        lblPicked.Font = UiTheme.Font(8.9F, FontStyle.Bold)
        lblPicked.Padding = New Padding(6, 0, 6, 0)

        lblQty.AutoSize = False
        lblQty.Size = New Size(54, 24)
        lblQty.Text = "0"
        lblQty.RightToLeft = RightToLeft.No
        lblQty.TextAlign = ContentAlignment.MiddleCenter
        lblQty.BackColor = UiTheme.BrandGold
        lblQty.ForeColor = Color.White
        lblQty.Font = UiTheme.Font(9.4F, FontStyle.Bold)

        Dim pickedRow As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.White, .Margin = New Padding(0, 2, 0, 0)
        }
        pickedRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 58.0F))
        pickedRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        pickedRow.Controls.Add(lblQty, 0, 0)
        pickedRow.Controls.Add(lblPicked, 1, 0)
        lblQty.Dock = DockStyle.Fill
        lblPicked.Dock = DockStyle.Fill
        AddCardControl(itemStack, pickedRow, 28)

        Dim btnManageItems As GlassButton = RailButton("📦 إدارة الأصناف", 160, True)
        AddHandler btnManageItems.Click, Sub(s, e) OpenItemManagement()
        Dim btnReloadItems As GlassButton = RailButton("🔄 تحديث القائمة", 150)
        AddHandler btnReloadItems.Click, Sub(s, e) RefreshDropdownItems(txtDropFilter.Text)
        AddCardControl(itemStack, RailButtonRow(btnManageItems, btnReloadItems))

        ' === 2) Customer + loyalty =========================================
        Dim custStack As TableLayoutPanel = BuildRailCard("👤 العميل ونقاط الولاء")

        txtCustomerSearch.PlaceholderText = "رقم / كود / موبايل / اسم العميل"
        txtCustomerSearch.Font = UiTheme.Font(9.4F)
        txtCustomerSearch.RightToLeft = RightToLeft.Yes
        AddHandler txtCustomerSearch.TextChanged, Sub(s, e) FilterCustomersLive(txtCustomerSearch.Text)
        AddHandler txtCustomerSearch.KeyDown, Sub(s, e)
                                                  If e.KeyCode = Keys.Enter AndAlso cboCustomer.Items.Count > 0 Then
                                                      e.SuppressKeyPress = True
                                                      OpenDropdown(cboCustomer)
                                                  End If
                                              End Sub
        AddCardControl(custStack, RailField("بحث عميل:", txtCustomerSearch), 30)

        cboCustomer.DropDownStyle = ComboBoxStyle.DropDownList
        cboCustomer.DropDownWidth = 420
        cboCustomer.Font = UiTheme.Font(9.4F, FontStyle.Bold)
        cboCustomer.RightToLeft = RightToLeft.Yes
        AddHandler cboCustomer.SelectedIndexChanged, Sub(s, e) RefreshCustomerExtras()
        AddCardControl(custStack, cboCustomer, 30)

        lblCustomerMeta.Text = "عميل نقدي | النقاط: 0 | المديونية: 0.00"
        lblCustomerMeta.AutoSize = False
        lblCustomerMeta.Height = 26
        lblCustomerMeta.TextAlign = ContentAlignment.MiddleRight
        lblCustomerMeta.BackColor = UiTheme.BrandLight
        lblCustomerMeta.ForeColor = UiTheme.BrandNavy
        lblCustomerMeta.Font = UiTheme.Font(8.9F, FontStyle.Bold)
        lblCustomerMeta.Padding = New Padding(6, 0, 6, 0)
        AddCardControl(custStack, lblCustomerMeta, 28)

        Dim btnQuickCust As GlassButton = RailButton("➕ عميل جديد", 140, True)
        AddHandler btnQuickCust.Click, Sub(s, e) OpenQuickCustomerDialog()
        Dim btnAddPts As GlassButton = RailButton("⭐ نقاط", 105)
        AddHandler btnAddPts.Click, Sub(s, e) QuickAwardPointsToCustomer()
        Dim btnCustHistory As GlassButton = RailButton("📄 السجل", 105)
        AddHandler btnCustHistory.Click, Sub(s, e)
                                             Dim custId = SelectedId(cboCustomer)
                                             If Not custId.HasValue Then
                                                 UiTheme.Warn("اختر عميلاً مسجلاً أولاً.")
                                                 Return
                                             End If
                                             Dim cust = TryCast(cboCustomer.SelectedItem, CustomerSummary)
                                             Dim custName As String = If(cust IsNot Nothing, cust.Name, "العميل")
                                             Using dlg As New CustomerHistoryDialog(_session, custId.Value, custName)
                                                 dlg.ShowDialog(Me)
                                             End Using
                                         End Sub
        AddCardControl(custStack, RailButtonRow(btnQuickCust, btnAddPts, btnCustHistory))

        lblCustomerPoints.AutoSize = False
        lblCustomerPoints.Size = New Size(70, 26)
        lblCustomerPoints.Text = "0"
        lblCustomerPoints.RightToLeft = RightToLeft.No
        lblCustomerPoints.TextAlign = ContentAlignment.MiddleCenter
        lblCustomerPoints.BackColor = UiTheme.BrandGold
        lblCustomerPoints.ForeColor = Color.White
        lblCustomerPoints.Font = UiTheme.Font(9.4F, FontStyle.Bold)
        txtRedeemPoints.Font = UiTheme.Font(9.4F)
        txtRedeemPoints.Text = "0"
        txtRedeemPoints.TextAlign = HorizontalAlignment.Center

        Dim loyaltyRow As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.White, .Margin = New Padding(0, 2, 0, 2)
        }
        loyaltyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 78.0F))
        loyaltyRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        loyaltyRow.Controls.Add(lblCustomerPoints, 0, 0)
        loyaltyRow.Controls.Add(RailField("استبدال:", txtRedeemPoints, True, 92.0F), 1, 0)
        lblCustomerPoints.Dock = DockStyle.Fill
        AddCardControl(custStack, loyaltyRow, 30)

        lblEarnedPointsPreview.Text = "+0"
        lblEarnedPointsPreview.AutoSize = False
        lblEarnedPointsPreview.Height = 20
        lblEarnedPointsPreview.ForeColor = UiTheme.BrandNavy
        lblEarnedPointsPreview.Font = UiTheme.Font(8.9F, FontStyle.Bold)
        lblEarnedPointsPreview.TextAlign = ContentAlignment.MiddleLeft
        AddCardControl(custStack, RailField("يكتسب (نقطة):", lblEarnedPointsPreview, True), 22)

        ' === 3) Coupon + voucher ===========================================
        Dim couponStack As TableLayoutPanel = BuildRailCard("🎟️ الكوبون والقسيمة")

        txtCouponCode.Font = UiTheme.Font(9.4F)
        txtCouponCode.RightToLeft = RightToLeft.Yes
        txtCouponCode.PlaceholderText = "كود الكوبون"
        Dim btnApplyCoupon As GlassButton = RailButton("✔ تطبيق", 104, True)
        AddHandler btnApplyCoupon.Click, Sub(s, e) ApplyCouponPreview()
        AddCardControl(couponStack, RailFieldWithButton("كود كوبون:", txtCouponCode, btnApplyCoupon), 30)

        txtVoucherCode.Font = UiTheme.Font(9.4F)
        txtVoucherCode.RightToLeft = RightToLeft.Yes
        txtVoucherCode.PlaceholderText = "كود القسيمة"
        txtVoucherAmount.Font = UiTheme.Font(9.4F)
        txtVoucherAmount.Text = "0.00"
        txtVoucherAmount.TextAlign = HorizontalAlignment.Center
        Dim btnCheckVoucher As GlassButton = RailButton("✔ تحقق", 104, True)
        AddHandler btnCheckVoucher.Click, Sub(s, e) ApplyVoucherPreview()
        AddCardControl(couponStack, RailFieldWithButton("قسيمة شراء:", txtVoucherCode, btnCheckVoucher), 30)
        AddCardControl(couponStack, RailField("مبلغ القسيمة:", txtVoucherAmount, True), 30)

        ' === 4) Seller + held invoices =====================================
        ' الدفع والخصم والإجماليات انتقلت لشريط عريض أسفل الأصناف (BuildSettlement)
        ' لأن الكاشير كان مضطر ينزلق في القائمة الجانبية عشان يشوف إجمالي الفاتورة.
        Dim payStack As TableLayoutPanel = BuildRailCard("🧾 البائع والمعلّقات")

        cboSeller.DropDownStyle = ComboBoxStyle.DropDownList
        cboSeller.DisplayMember = "Name"
        cboSeller.Font = UiTheme.Font(9.0F)
        cboDelivery.DropDownStyle = ComboBoxStyle.DropDownList
        cboDelivery.DisplayMember = "Name"
        cboDelivery.Font = UiTheme.Font(9.0F)
        AddCardControl(payStack, RailPair("البائع:", cboSeller, "الدليفري:", cboDelivery, False), 30)

        cboHeldInvoices.DropDownStyle = ComboBoxStyle.DropDownList
        cboHeldInvoices.Font = UiTheme.Font(9.0F)
        AddCardControl(payStack, RailField("المعلّقات:", cboHeldInvoices), 30)

        ' === 5) Profitability / pricing ====================================
        Dim profitStack As TableLayoutPanel = BuildRailCard("📈 الربحية والأسعار")

        Dim btnWhole As GlassButton = RailButton("سعر جملة", 90)
        AddHandler btnWhole.Click, Sub(s, e) ApplyWholesaleToSelectedLine()
        Dim btnDisc5 As GlassButton = RailButton("خصم 5%", 78)
        AddHandler btnDisc5.Click, Sub(s, e) ApplyLineDiscountPct(5D)
        Dim btnDisc10 As GlassButton = RailButton("خصم 10%", 82)
        AddHandler btnDisc10.Click, Sub(s, e) ApplyLineDiscountPct(10D)
        Dim btnInfoSide As GlassButton = RailButton("ℹ️ كارت الصنف", 118, True)
        AddHandler btnInfoSide.Click, Sub(s, e) ShowItemCardForContext()
        AddCardControl(profitStack, RailButtonRow(btnWhole, btnDisc5, btnDisc10, btnInfoSide))

        lblSideItemName.Dock = DockStyle.Top
        lblSideItemName.Height = 30
        lblSideItemName.TextAlign = ContentAlignment.MiddleCenter
        lblSideItemName.BackColor = UiTheme.BrandLight
        lblSideItemName.ForeColor = UiTheme.BrandNavy
        lblSideItemName.Font = UiTheme.Font(9.0F, FontStyle.Bold)
        lblSideItemName.Text = "اختر صنفًا من الفاتورة لعرض ربحيته"
        AddCardControl(profitStack, lblSideItemName, 30)

        Dim summary As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(0), .BackColor = Color.White, .Margin = New Padding(0)
        }
        summary.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        summary.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        Dim priceColA As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .BackColor = Color.White, .Margin = New Padding(0, 0, 5, 0)
        }
        priceColA.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        Dim priceColB As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .BackColor = Color.White, .Margin = New Padding(5, 0, 0, 0)
        }
        priceColB.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        AddPriceRow(priceColA, "التكلفة", lblCost, UiTheme.Ink, True)
        AddPriceRow(priceColA, "سعر البيع", lblSale, UiTheme.BrandBlue, True)
        AddPriceRow(priceColA, "الجملة / الأدنى", lblWholesale, UiTheme.Muted, True)
        AddPriceRow(priceColA, "خصم الصنف", lblLineDiscountVal, UiTheme.WarningOrange, True)
        AddPriceRow(priceColA, "خصم الصنف %", lblDiscountPct, UiTheme.WarningOrange, True)
        AddPriceRow(priceColA, "ربح الوحدة", lblUnitProfit, UiTheme.BrandNavy, True)
        AddPriceRow(priceColA, "ربح السطر %", lblLineProfit, UiTheme.BrandNavy, True)

        AddPriceRow(priceColB, "ربح الفاتورة", lblInvoiceProfit, UiTheme.BrandBlue, True)
        AddPriceRow(priceColB, "خصم الفاتورة", lblTotalDiscount, UiTheme.DangerRed, True)
        AddPriceRow(priceColB, "الضريبة", lblSummaryTax, UiTheme.Ink, True)
        AddPriceRow(priceColB, "قبل الخصم", lblSubtotal, UiTheme.Ink, True)
        AddPriceRow(priceColB, "الصافي", lblNet, UiTheme.BrandNavy, True)
        AddPriceRow(priceColB, "المتبقي (آجل)", lblRemain, UiTheme.DangerRed, True)

        summary.Controls.Add(priceColA, 0, 0)
        summary.Controls.Add(priceColB, 1, 0)
        AddCardControl(profitStack, summary)

        ' === 6) Notes / quantity summary ===================================
        Dim notesStack As TableLayoutPanel = BuildRailCard("📝 ملاحظات وملخص الكميات")

        txtNotes.Multiline = True
        txtNotes.Height = 64
        txtNotes.ScrollBars = ScrollBars.Vertical
        txtNotes.Font = UiTheme.Font(9.0F)
        txtNotes.RightToLeft = RightToLeft.Yes
        AddHandler txtNotes.TextChanged, Sub(s, e) UpdateQuantities()
        AddCardControl(notesStack, txtNotes, 64)

        lblQuantities.AutoSize = False
        lblQuantities.Height = 24
        lblQuantities.TextAlign = ContentAlignment.MiddleRight
        lblQuantities.ForeColor = UiTheme.Muted
        lblQuantities.Font = UiTheme.Font(8.9F, FontStyle.Bold)
        AddCardControl(notesStack, lblQuantities, 26)
    End Sub

    Private Shared Sub AddPaySummaryRow(stack As TableLayoutPanel, caption As String, value As Label, color As Color)
        Dim row As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.White, .Margin = New Padding(0, 2, 0, 2)
        }
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold), .ForeColor = UiTheme.Muted
        }
        value.AutoSize = False
        value.Dock = DockStyle.Fill
        value.Height = 26
        value.TextAlign = ContentAlignment.MiddleLeft
        value.Font = UiTheme.Font(9.4F, FontStyle.Bold)
        value.ForeColor = color
        value.RightToLeft = RightToLeft.No
        row.Controls.Add(lbl, 0, 0)
        row.Controls.Add(value, 1, 0)
        stack.Controls.Add(row)
    End Sub

    Private Shared Sub AddPriceRow(stack As TableLayoutPanel, caption As String, value As Label,
                                   color As Color, Optional compact As Boolean = False)
        Dim row As New TableLayoutPanel With {
            .Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1,
            .RightToLeft = RightToLeft.Yes, .BackColor = Color.White, .Margin = New Padding(0, 0, 0, 0)
        }
        If compact Then
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        Else
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 56.0F))
            row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 44.0F))
        End If
        Dim rowH As Integer = If(compact, 22, 24)
        Dim lbl As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .Height = rowH, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(If(compact, 8.3F, 8.9F), FontStyle.Bold), .ForeColor = Color.FromArgb(65, 65, 75)
        }
        value.Text = "0.00"
        value.AutoSize = False
        value.Dock = DockStyle.Fill
        value.Height = rowH
        value.TextAlign = ContentAlignment.MiddleLeft
        value.Font = UiTheme.Font(If(compact, 8.5F, 9.0F), FontStyle.Bold)
        value.ForeColor = color
        value.RightToLeft = RightToLeft.No
        row.Controls.Add(lbl, 0, 0)
        row.Controls.Add(value, 1, 0)
        stack.Controls.Add(row)
    End Sub

    Private Function BuildActions() As Control
        Dim bar As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.Surface,
            .Padding = New Padding(2, 4, 2, 4)
        }
        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft,
            .RightToLeft = RightToLeft.No, .WrapContents = False, .AutoScroll = True,
            .BackColor = Color.Transparent
        }

        Dim btnNew As GlassButton = ActionButton("جديد +", 84, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnNew.Click, Sub(s, e) ResetInvoice(True)
        Dim btnDelete As GlassButton = ActionButton("حذف بند", 88, Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        AddHandler btnDelete.Click, Sub(s, e) _editor.RemoveSelected()
        Dim btnHold As GlassButton = ActionButton("تعليق (F8)", 98, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnHold.Click, Sub(s, e) HoldInvoice()
        Dim btnResume As GlassButton = ActionButton("استئناف معلقة", 114, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnResume.Click, Sub(s, e) ResumeHeld()
        Dim btnInvoices As GlassButton = ActionButton("بحث الفواتير", 108, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnInvoices.Click, Sub(s, e) OpenInvoices()
        Dim btnCustomers As GlassButton = ActionButton("إدارة العملاء", 108, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnCustomers.Click, Sub(s, e) AddCustomer()
        Dim btnA4 As GlassButton = ActionButton("طباعة A4", 92, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnA4.Click, Sub(s, e) PrintTicket(False)
        Dim btnReceipt As GlassButton = ActionButton("ريسيت 80 (F10)", 112, UiTheme.BrandBlue, UiTheme.BrandNavy)
        AddHandler btnReceipt.Click, Sub(s, e) PrintTicket(True)
        Dim btnPay As GlassButton = ActionButton("💾 سداد وترحيل الفاتورة (F9)", 208, UiTheme.BrandCyan, UiTheme.BrandNavyDark)
        btnPay.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        AddHandler btnPay.Click, Sub(s, e) PostSale()

        flow.Controls.AddRange({btnNew, btnDelete, btnHold, btnResume, btnInvoices,
                                btnCustomers, btnA4, btnReceipt, btnPay})
        bar.Controls.Add(flow)
        Return bar
    End Function

    Private Function BuildFooter() As Control
        Dim container As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavyDark
        }

        Dim statusGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 26,
            .ColumnCount = 6,
            .RowCount = 1,
            .BackColor = UiTheme.BrandMist,
            .Padding = New Padding(2, 1, 2, 1)
        }
        For i As Integer = 0 To 5
            statusGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.66F))
        Next
        statusGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim cells As Label() = {lblBrandStatusReg, lblBrandStatusDate, lblBrandStatusCashier, lblBrandStatusScreen, lblBrandStatusCust, lblBrandStatusOnline}
        For i As Integer = 0 To cells.Length - 1
            cells(i).Dock = DockStyle.Fill
            cells(i).BackColor = Color.White
            cells(i).ForeColor = If(i = 5, UiTheme.BrandNavy, UiTheme.Ink)
            cells(i).Font = UiTheme.Font(8.9F, FontStyle.Bold)
            cells(i).TextAlign = ContentAlignment.MiddleCenter
            cells(i).Margin = New Padding(1)
            statusGrid.Controls.Add(cells(i), i, 0)
        Next

        lblBrandStatusReg.Text = "Register: REG-" & _session.BranchId.ToString("00") & " (" & _session.BranchName & ")"
        lblBrandStatusDate.Text = "Date: " & Date.Today.ToString("yyyy-MM-dd")
        lblBrandStatusCashier.Text = "Cashier: " & _session.Username
        lblBrandStatusScreen.Text = "Screen: Sale Invoice"
        lblBrandStatusCust.Text = "Customer: عميل نقدي"
        lblBrandStatusOnline.Text = "🔵 Online — DB Connected"

        Dim globalBar As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 6,
            .RowCount = 1,
            .BackColor = UiTheme.BrandNavyDark,
            .Padding = New Padding(4, 3, 4, 3)
        }
        For i As Integer = 0 To 5
            globalBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.66F))
        Next
        globalBar.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim btnF1 = MakeFooterButton("F1 / Help", "دليل واختصارات البرنامج")
        AddHandler btnF1.Click, Sub(s, e) ShowBrandHelpDialog()
        Dim btnF11 = MakeFooterButton("F11 / Delete", "حذف السطر المحدد")
        AddHandler btnF11.Click, Sub(s, e) _editor.RemoveSelected()
        Dim btnF12 = MakeFooterButton("F12 / Cancel", "إلغاء وتفريغ المعاملة")
        AddHandler btnF12.Click, Sub(s, e) ResetInvoice(True)
        Dim btnEsc = MakeFooterButton("Esc / Close", "إغلاق شاشة البيع")
        AddHandler btnEsc.Click, Sub(s, e) Close()
        Dim btnEnter = MakeFooterButton("Enter / Post (F9)", "اعتماد وترحيل الفاتورة")
        AddHandler btnEnter.Click, Sub(s, e) PostSale()

        Dim brandCell As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = UiTheme.BrandNavy,
            .GradientBottom = UiTheme.BrandBlue,
            .BorderColor = UiTheme.BrandBlue,
            .CornerRadius = 10,
            .Margin = New Padding(3, 1, 3, 1)
        }
        Dim lblBrand As New Label With {
            .Text = "DOCTOR TECH" & Environment.NewLine & "RETAIL POS",
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(9.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        brandCell.Controls.Add(lblBrand)

        globalBar.Controls.Add(btnF1, 0, 0)
        globalBar.Controls.Add(btnF11, 1, 0)
        globalBar.Controls.Add(btnF12, 2, 0)
        globalBar.Controls.Add(btnEsc, 3, 0)
        globalBar.Controls.Add(btnEnter, 4, 0)
        globalBar.Controls.Add(brandCell, 5, 0)

        globalBar.Height = 62
        globalBar.Dock = DockStyle.Bottom
        statusGrid.Height = 28
        statusGrid.Dock = DockStyle.Top
        container.Controls.Add(globalBar)
        container.Controls.Add(statusGrid)
        Return container
    End Function

    Private Shared Function MakeFooterButton(fKey As String, subText As String) As GlassButton
        Dim b As New GlassButton With {
            .Text = fKey & Environment.NewLine & subText,
            .Dock = DockStyle.Fill,
            .Font = UiTheme.Font(8.9F, FontStyle.Bold),
            .CornerRadius = 9,
            .Margin = New Padding(2)
        }
        b.SetTone(UiTheme.BrandBlue, UiTheme.BrandNavy)
        Return b
    End Function

    Private Sub ShowBrandHelpDialog()
        UiTheme.Info(
            "دليل اختصارات شاشة البيع (DOCTOR TECH RETAIL POS):" & Environment.NewLine &
            "• F2 مسح الباركود | F3 قائمة الأصناف | F4 بحث الأصناف | F5 عميل جديد." & Environment.NewLine &
            "• F6 بطاقة الصنف والصلاحية | F7 الوردية | F8 تعليق الفاتورة | F9 ترحيل الفاتورة." & Environment.NewLine &
            "• F10 طباعة إيصال 80مم | F11 حذف السطر | F12 إلغاء المعاملة | Esc إغلاق الشاشة." & Environment.NewLine &
            "• الشريط الجانبي يجمع بحث الصنف، العميل، الولاء، الكوبون، القسيمة، الخصم، والربحية.",
            "مساعدة DOCTOR TECH")
    End Sub

    ' --------------------------------------------------------------- helpers --
    Private Shared Function StripLabel(text As String, width As Integer) As Label
        Return New Label With {
            .Text = text, .Width = width, .Height = 24, .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(8.5F, FontStyle.Bold), .Margin = New Padding(2, 5, 2, 2)
        }
    End Function

    Private Shared Function GreenCaption(text As String, width As Integer) As Label
        Return New Label With {
            .Text = text, .Width = width, .Height = 24, .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(8.5F, FontStyle.Bold), .ForeColor = Color.White,
            .Margin = New Padding(3, 5, 2, 2)
        }
    End Function

    Private Shared Sub AddGreenText(box As TextBox, width As Integer)
        box.Width = width
        box.Font = UiTheme.Font(9.0F)
        box.RightToLeft = RightToLeft.Yes
        box.TextAlign = HorizontalAlignment.Right
        box.Margin = New Padding(2, 4, 4, 2)
    End Sub

    Private Shared Function GreenValue(label As Label, width As Integer) As Label
        label.AutoSize = False
        label.Width = width
        label.Height = 24
        label.TextAlign = ContentAlignment.MiddleCenter
        label.BackColor = Color.White
        label.Font = UiTheme.Font(9.0F, FontStyle.Bold)
        label.ForeColor = Color.FromArgb(40, 40, 45)
        label.Margin = New Padding(2, 4, 4, 2)
        Return label
    End Function

    Private Shared Function SmallButton(text As String, width As Integer) As GlassButton
        Dim b As New GlassButton With {
            .Text = text, .Size = New Size(width, 28), .CornerRadius = 8,
            .BackColor = Color.White, .ForeColor = UiTheme.Ink,
            .Font = UiTheme.Font(8.2F, FontStyle.Bold), .Cursor = Cursors.Hand, .Margin = New Padding(2, 3, 2, 3)
        }
        b.SetTone(UiTheme.BrandLight, Color.White)
        Return b
    End Function

    Private Shared Function ActionButton(text As String, width As Integer, backColor As Color, foreColor As Color) As GlassButton
        Dim b As New GlassButton With {
            .Text = text, .Width = width, .Height = 40, .Margin = New Padding(2),
            .CornerRadius = 10, .ForeColor = foreColor,
            .Font = UiTheme.Font(8.8F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        b.SetTone(backColor)
        Return b
    End Function

    ' ------------------------------------------- inline dropdown & lookups --
    Private Sub RefreshDropdownItems(term As String)
        Try
            _dropdownItems = ItemService.Search(_session, If(term, String.Empty).Trim(), False, 150)
            cboItemDrop.BeginUpdate()
            cboItemDrop.DataSource = Nothing
            cboItemDrop.DataSource = _dropdownItems
            cboItemDrop.EndUpdate()
            SizeItemDropdown()
        Catch ex As Exception
            AppLog.Warn("Dropdown items load failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>عرض القائمة المنسدلة ثابت افتراضيًا، والأسماء العربية الطويلة تُقطع في آخرها.</summary>
    Private Sub SizeItemDropdown()
        Dim widest As Integer = 520
        For Each it In _dropdownItems.Take(80)
            Dim w As Integer = CInt(TextRenderer.MeasureText(it.DropdownDisplay, cboItemDrop.Font).Width) + 36
            If w > widest Then widest = w
        Next
        Dim limit As Integer = Math.Max(520, Screen.FromControl(cboItemDrop).WorkingArea.Width - 80)
        cboItemDrop.DropDownWidth = Math.Min(widest, limit)
    End Sub

    ''' <summary>فتح القائمة المنسدلة برمجياً يُخفي مؤشر الماوس حتى تنتهي الرسالة؛ نعيد المؤشر بعد ذلك.</summary>
    Private Sub OpenDropdown(cbo As ComboBox)
        cbo.DroppedDown = True
        Cursor.Current = Cursors.Arrow
        If IsHandleCreated Then BeginInvoke(New MethodInvoker(AddressOf RepaintCursor))
    End Sub

    ''' <summary>إعادة نفس موضع المؤبر تجبر ويندوز على إعادة رسم السهم بعد فتح القائمة برمجياً.</summary>
    Private Sub RepaintCursor()
        Cursor.Current = Cursors.Arrow
        Dim p As Point = Cursor.Position
        Cursor.Position = p
    End Sub

    ''' <summary>لو ظل المؤشر مخفياً بعد إغلاق القائمة، أي حركة ماوس داخل النافذة تُعيد السهم.</summary>
    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        If Cursor.Current Is Nothing Then Cursor.Current = Cursors.Arrow
    End Sub

    Private Sub AddSelectedDropdownItem()
        Dim selected = TryCast(cboItemDrop.SelectedItem, ItemSummary)
        If selected Is Nothing AndAlso _dropdownItems.Count > 0 Then
            selected = _dropdownItems(0)
        End If
        If selected Is Nothing Then
            UiTheme.Warn("لا يوجد صنف مطابق في القائمة المنسدلة.")
            Return
        End If
        If Not selected.IsActive Then
            UiTheme.Warn("الصنف «" & selected.NameAr & "» غير مفعّل.")
            Return
        End If

        Dim qtyToAdd As Decimal = numDropQty.Value
        If qtyToAdd <= 0D Then qtyToAdd = 1D

        _editor.AddItem(selected, qtyToAdd)
        Dim grid = _editor.GridControl
        If grid.Rows.Count > 0 Then
            grid.CurrentCell = grid.Rows(grid.Rows.Count - 1).Cells(0)
        End If
        lblPicked.Text = selected.NameAr
        lblQty.Text = qtyToAdd.ToString("0.##")
        numDropQty.Value = 1D

        _suppressDropFilter = True
        txtDropFilter.Clear()
        _suppressDropFilter = False

        UpdateSelectedItem()
        UpdateQuantities()
    End Sub

    Private Sub LoadLookups(Optional selectCustomerId As Long = 0)
        Try
            _customers = CustomerService.Search(_session, String.Empty, "customer")
            BindCustomersCombo(_customers, selectCustomerId)

            cboSeller.DataSource = BuildEmployeeRows("seller")
            cboDelivery.DataSource = BuildEmployeeRows("delivery")

            Dim methods = UiLookup.PaymentMethods(_session)
            cboPayment.DataSource = methods
            For i As Integer = 0 To methods.Count - 1
                If methods(i).Name.IndexOf("كاش", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    cboPayment.SelectedIndex = i
                    Exit For
                End If
            Next
            If cboPayment.SelectedIndex < 0 AndAlso methods.Count > 0 Then cboPayment.SelectedIndex = 0
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub BindCustomersCombo(sourceList As List(Of CustomerSummary), Optional selectCustomerId As Long = 0)
        Dim customerRows As New List(Of CustomerSummary) From {
            New CustomerSummary With {.Id = 0, .Name = "عميل نقدي"}
        }
        If sourceList IsNot Nothing Then customerRows.AddRange(sourceList)
        cboCustomer.DataSource = customerRows
        If selectCustomerId > 0 Then
            For i As Integer = 0 To customerRows.Count - 1
                If customerRows(i).Id = selectCustomerId Then
                    cboCustomer.SelectedIndex = i
                    Return
                End If
            Next
        End If
        If customerRows.Count > 0 Then cboCustomer.SelectedIndex = 0
    End Sub

    ''' <summary>بحث فوري عن العميل برقم العميل أو كود العميل أو الموبايل أو التليفون أو الاسم.</summary>
    Private Sub FilterCustomersLive(term As String)
        Try
            Dim clean As String = If(term, String.Empty).Trim()
            If clean.Length = 0 Then
                BindCustomersCombo(_customers, 0)
                Return
            End If
            Dim matches = CustomerService.Search(_session, clean, "customer")
            Dim firstMatchId As Long = If(matches.Count > 0, matches(0).Id, 0L)
            BindCustomersCombo(matches, firstMatchId)
        Catch
        End Try
    End Sub

    Private Function BuildEmployeeRows(employeeType As String) As List(Of LookupItem)
        Dim rows As New List(Of LookupItem) From {
            New LookupItem With {.Id = 0, .Name = "— بدون —"}
        }
        For Each employee In HrEmployeeService.Search(_session, String.Empty, employeeType, False)
            rows.Add(New LookupItem With {.Id = employee.Id, .Name = employee.Name})
        Next
        Return rows
    End Function

    Private Sub RefreshShift()
        Try
            Dim shift = ShiftService.Current(_session)
            lblShift.Text = If(shift Is Nothing, "⚠️ لا توجد وردية مفتوحة",
                               "🟢 الوردية " & shift.ShiftNo & " | النقدية: " &
                               UiTheme.Money(ShiftService.ExpectedCash(_session, shift.Id)))
        Catch
            lblShift.Text = String.Empty
        End Try
    End Sub

    Private Sub RefreshCustomerExtras()
        Try
            Dim cust = TryCast(cboCustomer.SelectedItem, CustomerSummary)
            If cust Is Nothing OrElse cust.Id <= 0 Then
                lblCustomerPoints.Text = "0"
                lblCustomerMeta.Text = "عميل نقدي | النقاط: 0 | المديونية: 0.00"
            lblBrandStatusCust.Text = "Customer: عميل نقدي"
                Return
            End If
            Dim pts As Decimal = LoyaltyService.Points(_session, cust.Id)
            Dim bal As Decimal = CustomerService.Balance(_session, cust.Id)
            lblCustomerPoints.Text = pts.ToString("0.##")
            If cust IsNot Nothing Then lblBrandStatusCust.Text = "Customer: " & cust.Name & " (" & pts.ToString("0") & " ن)"
            Dim codeText As String = If(String.IsNullOrWhiteSpace(cust.Code), "#" & cust.Id.ToString(), cust.Code)
            lblCustomerMeta.Text = codeText & " | ⭐ النقاط: " & pts.ToString("0.##") &
                                   " | المديونية: " & UiTheme.Money(bal) &
                                   If(cust.GroupDiscountPercent > 0D, " | خصم مجموعة: " & cust.GroupDiscountPercent.ToString("0.#") & "%", "")
        Catch
            lblCustomerPoints.Text = "0"
        End Try
    End Sub

    Private Sub OpenQuickCustomerDialog()
        Using dlg As New QuickCustomerDialog(_session)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.CreatedCustomerId > 0 Then
                LoadLookups(dlg.CreatedCustomerId)
                RefreshCustomerExtras()
            End If
        End Using
    End Sub

    Private Sub QuickAwardPointsToCustomer()
        Dim cust = TryCast(cboCustomer.SelectedItem, CustomerSummary)
        If cust Is Nothing OrElse cust.Id <= 0 Then
            UiTheme.Warn("اختر عميلاً مسجلاً أولاً أو اضغط «عميل جديد + نقاط» لتسجيل عميل جديد.")
            Return
        End If
        Dim input As String = Microsoft.VisualBasic.Interaction.InputBox(
            "أدخل عدد نقاط الولاء المراد إضافتها للعميل «" & cust.Name & "»:",
            "إضافة نقاط ولاء للعميل", "10")
        If String.IsNullOrWhiteSpace(input) Then Return
        Dim pts As Decimal = UiTheme.ToMoney(input)
        If pts <= 0D Then
            UiTheme.Warn("أدخل عدد نقاط أكبر من صفر.")
            Return
        End If
        Try
            Dim newBal As Decimal = LoyaltyService.Adjust(_session, cust.Id, pts, "earn", "إضافة نقاط من شاشة الكاشير")
            RefreshCustomerExtras()
            UiTheme.Info("تمت إضافة " & pts.ToString("0.##") & " نقطة للعميل «" & cust.Name & "». الرصيد الجديد: " & newBal.ToString("0.##") & " نقطة.")
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub AddCustomer()
        Using dialog As New CustomersForm(_session, "customer")
            dialog.ShowDialog(Me)
        End Using
        LoadLookups()
    End Sub

    Private Sub AddByScan()
        Dim term As String = txtScan.Text.Trim()
        If term.Length = 0 Then
            cboItemDrop.Focus()
            OpenDropdown(cboItemDrop)
            Return
        End If
        Try
            Dim item = ItemService.FindForSale(_session, term)
            If item Is Nothing Then
                Dim matches = ItemService.Search(_session, term, False, 50)
                If matches.Count = 1 Then
                    AddPicked(matches(0))
                ElseIf matches.Count > 1 Then
                    _dropdownItems = matches
                    cboItemDrop.DataSource = _dropdownItems
                    SizeItemDropdown()
                    cboItemDrop.Focus()
                    OpenDropdown(cboItemDrop)
                Else
                    UiTheme.Warn("لم يتم العثور على صنف بالكود أو الباركود: " & term)
                End If
            Else
                If Not item.IsActive Then
                    UiTheme.Warn("الصنف «" & item.NameAr & "» غير مفعّل.")
                    Return
                End If
                AddPicked(item)
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub AddPicked(item As ItemSummary)
        _editor.AddItem(item)
        Dim grid = _editor.GridControl
        If grid.Rows.Count > 0 Then
            grid.CurrentCell = grid.Rows(grid.Rows.Count - 1).Cells(0)
        End If
        lblPicked.Text = item.NameAr
        lblQty.Text = "1"
        txtScan.Clear()
        txtScan.Focus()
        UpdateSelectedItem()
        UpdateQuantities()
    End Sub

    ''' <summary>تحديث اللوحة الجانبية بسعر الشراء وسعر البيع وربحية الصنف ونسبة خصم الصنف لحظيًا.</summary>
    Private Sub UpdateSelectedItem()
        Dim line = SelectedLine()
        If line Is Nothing Then
            lblSideItemName.Text = "اختر صنفًا من الفاتورة لعرض ربحيته"
            lblCost.Text = "0.00"
            lblSale.Text = "0.00"
            lblWholesale.Text = "0.00 / 0.00"
            lblLineDiscountVal.Text = "0.00"
            lblDiscountPct.Text = "0 %"
            lblUnitProfit.Text = "0.00"
            lblLineProfit.Text = "0.00 (0 %)"
            Return
        End If

        lblSideItemName.Text = line.ItemName & " [" & line.Code & "] — متاح: " & line.QtyOnHand.ToString("0.##")
        lblQty.Text = line.Qty.ToString("0.##")

        If line.CostPrice <= 0D AndAlso line.WholesalePrice <= 0D Then
            Try
                Dim record = ItemService.GetById(_session, line.ItemId)
                If record IsNot Nothing Then
                    line.CostPrice = record.CostPrice
                    line.WholesalePrice = record.WholesalePrice
                    line.MinPrice = record.MinPrice
                End If
            Catch
            End Try
        End If

        lblCost.Text = UiTheme.Money(line.CostPrice)
        lblSale.Text = UiTheme.Money(line.UnitPrice)
        lblWholesale.Text = UiTheme.Money(line.WholesalePrice) & " / أدنى: " & UiTheme.Money(line.MinPrice)
        lblLineDiscountVal.Text = UiTheme.Money(line.Discount)
        lblDiscountPct.Text = line.DiscountPct.ToString("0.##") & " %"
        lblUnitProfit.Text = UiTheme.Money(line.UnitProfit)
        lblLineProfit.Text = UiTheme.Money(line.LineProfit) & " (" & line.ProfitMarginPct.ToString("0.#") & " %)"

        Dim profitColor As Color = If(line.LineProfit >= 0D, GreenDark, Color.FromArgb(200, 30, 30))
        lblUnitProfit.ForeColor = profitColor
        lblLineProfit.ForeColor = profitColor
    End Sub

    Private Sub ApplyWholesaleToSelectedLine()
        Dim line = SelectedLine()
        If line Is Nothing Then
            UiTheme.Warn("اختر صنفًا من الفاتورة أولاً.")
            Return
        End If
        If line.WholesalePrice <= 0D Then
            UiTheme.Warn("هذا الصنف ليس له سعر جملة مسجل.")
            Return
        End If
        line.UnitPrice = line.WholesalePrice
        _editor.RefreshTotals()
        UpdateSelectedItem()
    End Sub

    Private Sub ApplyLineDiscountPct(pct As Decimal)
        Dim line = SelectedLine()
        If line Is Nothing Then
            UiTheme.Warn("اختر صنفًا من الفاتورة أولاً.")
            Return
        End If
        line.DiscountPct = pct
        Dim gross As Decimal = Money.Round2(line.Qty * line.UnitPrice)
        line.Discount = Money.Round2(gross * (pct / 100D))
        _editor.RefreshTotals()
        UpdateSelectedItem()
    End Sub

    ''' <summary>يفتح شاشة إدارة الأصناف من داخل الفاتورة، ثم يعيد تحميل قائمة الأصناف بعد الإغلاق.</summary>
    Private Sub OpenItemManagement()
        Using f As New ItemsForm(_session)
            f.ShowDialog(Me)
        End Using
        LoadLookups()
        RefreshDropdownItems(txtDropFilter.Text)
    End Sub

    ''' <summary>كارت الصنف يعمل على السطر المحدد، أو على الصنف المختار في القائمة/المسح قبل إضافته للفاتورة.</summary>
    Private Sub ShowItemCardForContext()
        Dim line = SelectedLine()
        If line IsNot Nothing Then
            ShowItemFullCard(line)
            Return
        End If

        Dim candidate = TryCast(cboItemDrop.SelectedItem, ItemSummary)
        If candidate Is Nothing AndAlso _dropdownItems.Count > 0 Then candidate = _dropdownItems(0)
        If candidate Is Nothing AndAlso txtScan.Text.Trim().Length > 0 Then
            Dim matches = ItemService.Search(_session, txtScan.Text.Trim(), True, 5)
            If matches.Count > 0 Then candidate = matches(0)
        End If

        If candidate Is Nothing Then
            UiTheme.Warn("اختر صنفًا من القائمة أو امسح الباركود أولًا ثم اضغط كارت الصنف.")
            Return
        End If
        ShowItemFullCard(LineFromSummary(candidate))
    End Sub

    Private Shared Function LineFromSummary(item As ItemSummary) As InvoiceEditorLine
        Return New InvoiceEditorLine With {
            .ItemId = item.Id,
            .Code = item.Code,
            .ItemName = item.NameAr,
            .Qty = 1D,
            .UnitPrice = item.SalePrice,
            .CostPrice = item.CostPrice,
            .WholesalePrice = item.WholesalePrice,
            .MinPrice = item.MinPrice,
            .QtyOnHand = item.QtyOnHand,
            .TaxCategoryId = item.TaxCategoryId,
            .TaxRate = item.TaxRate
        }
    End Function

    Private Sub ShowItemFullCard(line As InvoiceEditorLine)
        If line Is Nothing Then Return
        Using dlg As New ItemFullCardDialog(_session, line)
            dlg.ShowDialog(Me)
            _editor.RefreshTotals()
            UpdateSelectedItem()
        End Using
    End Sub

    Private Function SelectedLine() As InvoiceEditorLine
        Dim row = _editor.GridControl.CurrentRow
        If row Is Nothing Then Return Nothing
        Return TryCast(row.DataBoundItem, InvoiceEditorLine)
    End Function

    ' -------------------------------------------------------------- totals --
    Private Sub EditorTotalsChanged(sender As Object, e As EventArgs)
        Dim totals = _editor.Totals
        Dim netPayable As Decimal = EffectiveNetTotal()
        lblTotal.Text = UiTheme.Money(netPayable)
        lblTax.Text = UiTheme.Money(totals.TaxTotal)
        lblSubtotal.Text = UiTheme.Money(totals.Subtotal)
        lblSummaryTax.Text = UiTheme.Money(totals.TaxTotal)
        lblNet.Text = UiTheme.Money(netPayable)

        Dim discountTotal As Decimal = totals.LineDiscount + totals.HeaderDiscount + _couponPreviewDiscount + _loyaltyPreviewDiscount
        lblTotalDiscount.Text = UiTheme.Money(discountTotal)
        lblBandSubtotal.Text = UiTheme.Money(totals.Subtotal)
        lblBandDiscount.Text = UiTheme.Money(discountTotal)

        Dim totalInvoiceProfit As Decimal = 0D
        For Each l In _editor.SnapshotLines()
            totalInvoiceProfit += l.LineProfit
        Next
        totalInvoiceProfit = Money.Round2(totalInvoiceProfit - totals.HeaderDiscount)
        lblInvoiceProfit.Text = UiTheme.Money(totalInvoiceProfit)
        lblInvoiceProfit.ForeColor = If(totalInvoiceProfit >= 0D, Indigo, Color.FromArgb(200, 30, 30))

        Dim earnedPreview As Decimal = Money.Round2(totals.Total * 0.01D)
        lblEarnedPointsPreview.Text = "+" & earnedPreview.ToString("0.##")

        If Not _paidTouched Then
            _updatingPaid = True
            txtPaid.Text = totals.Total.ToString("0.00")
            _updatingPaid = False
        End If
        UpdateSelectedItem()
        UpdatePaymentLabels()
        UpdateQuantities()
    End Sub

    Private Sub UpdatePaymentLabels()
        Dim netTotal As Decimal = EffectiveNetTotal()
        Dim voucherAmt As Decimal = UiTheme.ToMoney(txtVoucherAmount.Text)
        Dim paid As Decimal = UiTheme.ToMoney(txtPaid.Text)
        Dim due As Decimal = Math.Max(0D, netTotal - (paid + voucherAmt))
        lblDue.Text = UiTheme.Money(due)
        lblRemain.Text = lblDue.Text

        Dim received As Decimal = UiTheme.ToMoney(txtCashReceived.Text)
        Dim changeDue As Decimal = If(received > paid AndAlso paid > 0D, Money.Round2(received - paid), 0D)
        lblChangeDue.Text = UiTheme.Money(changeDue)
    End Sub

    Private Sub ApplyHeaderDiscountFromAmount()
        If _updatingHeaderDiscount Then Return
        _updatingHeaderDiscount = True
        Try
            Dim amt As Decimal = UiTheme.ToMoney(txtDiscount.Text)
            _editor.HeaderDiscount = amt
            Dim baseAmt As Decimal = _editor.Totals.Subtotal - _editor.Totals.LineDiscount
            txtDiscountPctHeader.Text = If(baseAmt > 0D, Money.Round2((amt / baseAmt) * 100D).ToString("0.##"), "0")
        Finally
            _updatingHeaderDiscount = False
        End Try
    End Sub

    Private Sub ApplyHeaderDiscountFromPercent()
        If _updatingHeaderDiscount Then Return
        _updatingHeaderDiscount = True
        Try
            Dim pct As Decimal = Math.Max(0D, Math.Min(100D, UiTheme.ToMoney(txtDiscountPctHeader.Text)))
            Dim baseAmt As Decimal = Math.Max(0D, _editor.Totals.Subtotal - _editor.Totals.LineDiscount)
            Dim amt As Decimal = Money.Round2(baseAmt * (pct / 100D))
            txtDiscount.Text = amt.ToString("0.00")
            _editor.HeaderDiscount = amt
        Finally
            _updatingHeaderDiscount = False
        End Try
    End Sub


    Private Function EffectiveNetTotal() As Decimal
        Dim totals = _editor.Totals
        Dim redeemPts As Decimal = Math.Max(0D, UiTheme.ToMoney(txtRedeemPoints.Text))
        _loyaltyPreviewDiscount = Money.Round2(redeemPts)
        Return Math.Max(0D, Money.Round2(totals.Total - _couponPreviewDiscount - _loyaltyPreviewDiscount))
    End Function

    Private Sub PayFull()
        _updatingPaid = True
        Dim netPayable As Decimal = EffectiveNetTotal()
        Dim voucherAmt As Decimal = Math.Max(0D, UiTheme.ToMoney(txtVoucherAmount.Text))
        txtPaid.Text = Math.Max(0D, netPayable - voucherAmt).ToString("0.00")
        _updatingPaid = False
        _paidTouched = False
        UpdatePaymentLabels()
    End Sub

    Private Sub UpdateQuantities()
        Dim count As Integer = _editor.LineCount
        Dim qty As Decimal = 0D
        Dim totalProfit As Decimal = 0D
        For Each line In _editor.SnapshotLines()
            qty += line.Qty
            totalProfit += line.LineProfit
        Next
        totalProfit = Money.Round2(totalProfit - _editor.Totals.HeaderDiscount)
        lblQuantities.Text = "عدد الأصناف: " & count.ToString() &
                             "   |   إجمالي القطع: " & qty.ToString("0.##") &
                             "   |   صافي ربح الفاتورة المتوقع: " & UiTheme.Money(totalProfit)
    End Sub

    Private Sub ApplyCouponPreview()
        _couponPreviewDiscount = 0D
        Dim codeStr = txtCouponCode.Text.Trim()
        If Not String.IsNullOrWhiteSpace(codeStr) Then
            Try
                Dim gross As Decimal = _editor.Totals.Subtotal - _editor.Totals.LineDiscount
                Dim disc As Decimal = CouponService.ValidateForSale(_session, codeStr, gross)
                _couponPreviewDiscount = disc
                UiTheme.Info("الكوبون صالح — قيمة الخصم: " & UiTheme.Money(disc) & " (مطبّق لحظياً في الفاتورة).")
            Catch ex As Exception
                UiTheme.Handle(ex)
            End Try
        End If
        EditorTotalsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub ApplyVoucherPreview()
        _voucherPreviewAmount = 0D
        Dim codeStr = txtVoucherCode.Text.Trim()
        If Not String.IsNullOrWhiteSpace(codeStr) Then
            Try
                Dim voucher = PurchaseVoucherService.FindActiveByCode(_session, codeStr)
                Dim needed As Decimal = EffectiveNetTotal()
                Dim useAmt As Decimal = Math.Min(voucher.RemainingAmount, needed)
                _voucherPreviewAmount = useAmt
                txtVoucherAmount.Text = useAmt.ToString("0.00")
                If Not _paidTouched Then
                    _updatingPaid = True
                    txtPaid.Text = Math.Max(0D, needed - useAmt).ToString("0.00")
                    _updatingPaid = False
                End If
                UpdatePaymentLabels()
                UiTheme.Info("القسيمة صالحة — الرصيد المتاح: " & UiTheme.Money(voucher.RemainingAmount) &
                             " | المبلغ المطبّق: " & UiTheme.Money(useAmt))
            Catch ex As Exception
                UiTheme.Handle(ex)
            End Try
        End If
        EditorTotalsChanged(Me, EventArgs.Empty)
    End Sub

    ' -------------------------------------------------------------- keypad --
    Private Sub KeypadPress(key As String)
        If txtScan.Focused Then
            If key = "C" Then txtScan.Clear() Else txtScan.Text &= key
            Return
        End If

        Dim line = SelectedLine()
        If line Is Nothing Then Return
        If key = "C" Then
            _keypadBuffer = String.Empty
            line.Qty = 1D
            lblQty.Text = "1"
            _editor.RefreshTotals()
            Return
        End If
        If key = "." Then
            If _keypadBuffer.Contains(".") Then Return
            _keypadBuffer = If(_keypadBuffer.Length = 0, "0.", _keypadBuffer & ".")
        Else
            _keypadBuffer = If(_keypadBuffer = "0", key, _keypadBuffer & key)
        End If
        Dim parsed As Decimal
        If Decimal.TryParse(_keypadBuffer, NumberStyles.Any, CultureInfo.InvariantCulture, parsed) AndAlso parsed > 0D Then
            line.Qty = parsed
            lblQty.Text = parsed.ToString("0.##")
            _editor.RefreshTotals()
        End If
    End Sub

    ' -------------------------------------------------- multi-hold & post --
    Private Sub RefreshHeldCombo()
        Try
            _heldSlots = SalesService.ListHeldInvoices(_session)
            cboHeldInvoices.DataSource = Nothing
            cboHeldInvoices.DataSource = _heldSlots
            If cboHeldInvoices.Items.Count > 0 Then cboHeldInvoices.SelectedIndex = cboHeldInvoices.Items.Count - 1
        Catch ex As Exception
            AppLog.Warn("Held invoices refresh warning: " & ex.Message)
        End Try
    End Sub

    Private Sub HoldInvoice()
        If _editor.LineCount = 0 Then
            UiTheme.Warn("لا توجد بنود لتعليقها.")
            Return
        End If
        Try
            Dim custName As String = Convert.ToString(cboCustomer.Text)
            Dim linesSnapshot = _editor.SnapshotLines()
            Dim slot As New HeldInvoiceRecord With {
                .SlotTitle = "#" & (_heldSlots.Count + 1).ToString() & " — " & custName & " (" &
                             EffectiveNetTotal().ToString("0.00") & " ج.م — " & Date.Now.ToString("HH:mm") & ")",
                .BranchId = _session.BranchId,
                .UserId = _session.UserId,
                .CustomerId = SelectedId(cboCustomer),
                .HeaderDiscount = _editor.HeaderDiscount,
                .CouponCode = txtCouponCode.Text.Trim(),
                .VoucherCode = txtVoucherCode.Text.Trim(),
                .RedeemPoints = UiTheme.ToMoney(txtRedeemPoints.Text),
                .Notes = txtNotes.Text.Trim(),
                .LinesJson = JsonSerializer.Serialize(linesSnapshot)
            }
            SalesService.SaveHeldInvoice(_session, slot)
            RefreshHeldCombo()
            ResetInvoice(False)
            UiTheme.Info("تم تعليق الفاتورة في قاعدة البيانات بنجاح (" & slot.SlotTitle & ") — لن تضيع عند إغلاق البرنامج.")
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر تعليق الفاتورة")
        End Try
    End Sub

    Private Sub ResumeHeld()
        RefreshHeldCombo()
        If _heldSlots.Count = 0 Then
            UiTheme.Warn("لا توجد فواتير معلّقة حاليًا في قاعدة البيانات.")
            Return
        End If
        Dim chosen As HeldInvoiceRecord = TryCast(cboHeldInvoices.SelectedItem, HeldInvoiceRecord)
        If chosen Is Nothing Then chosen = _heldSlots(_heldSlots.Count - 1)

        Try
            Dim restoredLines = JsonSerializer.Deserialize(Of List(Of InvoiceEditorLine))(chosen.LinesJson)
            If restoredLines IsNot Nothing Then _editor.LoadLines(restoredLines)
            _editor.HeaderDiscount = chosen.HeaderDiscount
            txtDiscount.Text = chosen.HeaderDiscount.ToString("0.00")
            txtCouponCode.Text = If(chosen.CouponCode, String.Empty)
            txtVoucherCode.Text = If(chosen.VoucherCode, String.Empty)
            txtRedeemPoints.Text = If(chosen.RedeemPoints > 0D, chosen.RedeemPoints.ToString("0.##"), String.Empty)
            txtNotes.Text = If(chosen.Notes, String.Empty)
            If chosen.CustomerId.HasValue AndAlso chosen.CustomerId.Value > 0 Then
                BindCustomersCombo(_customers, chosen.CustomerId.Value)
            End If
            SalesService.DeleteHeldInvoice(_session, chosen.Id)
            RefreshHeldCombo()
            EditorTotalsChanged(Me, EventArgs.Empty)
            UpdateQuantities()
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر استئناف الفاتورة المعلقة")
        End Try
    End Sub

    Private Sub OpenInvoices()
        Using dialog As New ReturnsForm(_session, "sale")
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Sub PostSale()
        If _isPosting Then Return
        If _editor.LineCount = 0 Then
            UiTheme.Warn("أضف صنفًا واحدًا على الأقل إلى الفاتورة.")
            txtScan.Focus()
            Return
        End If

        Dim totals = _editor.Totals
        Dim netPayable As Decimal = EffectiveNetTotal()
        Dim paid As Decimal = UiTheme.ToMoney(txtPaid.Text)
        Dim voucherAmt As Decimal = UiTheme.ToMoney(txtVoucherAmount.Text)
        If paid < 0D Then
            UiTheme.Warn("المدفوع لا يمكن أن يكون سالبًا.")
            Return
        End If
        ' P0 #5 Fix: Never bypass the overpayment check when coupons or loyalty points are used!
        If paid + voucherAmt > netPayable + 0.004D Then
            UiTheme.Warn("إجمالي المدفوع (" & UiTheme.Money(paid + voucherAmt) & ") أكبر من صافي الفاتورة المطلوب (" & UiTheme.Money(netPayable) & ").")
            Return
        End If
        If _editor.HeaderDiscount > totals.Subtotal - totals.LineDiscount Then
            UiTheme.Warn("خصم الفاتورة أكبر من قيمة البنود.")
            Return
        End If

        Dim request As New InvoiceRequest With {
            .ClientEventId = _draftEventId,
            .CustomerId = SelectedId(cboCustomer),
            .SellerId = SelectedId(cboSeller),
            .DeliveryId = SelectedId(cboDelivery),
            .PaymentMethodId = If(SelectedId(cboPayment).HasValue, SelectedId(cboPayment).Value, 0L),
            .Paid = paid,
            .HeaderDiscountAmount = _editor.HeaderDiscount,
            .CouponCode = If(String.IsNullOrWhiteSpace(txtCouponCode.Text), Nothing, txtCouponCode.Text.Trim()),
            .RedeemLoyaltyPoints = UiTheme.ToMoney(txtRedeemPoints.Text),
            .VoucherCode = If(String.IsNullOrWhiteSpace(txtVoucherCode.Text), Nothing, txtVoucherCode.Text.Trim()),
            .VoucherAmount = voucherAmt,
            .Notes = txtNotes.Text.Trim(),
            .BusinessDate = Date.Today,
            .Lines = _editor.BuildLineRequests()
        }
        If request.CustomerId.HasValue AndAlso request.CustomerId.Value = 0 Then request.CustomerId = Nothing
        Dim customer = TryCast(cboCustomer.SelectedItem, CustomerSummary)
        If customer IsNot Nothing AndAlso customer.Id > 0 Then request.BuyerName = customer.Name

        _isPosting = True
        Try
            Cursor = Cursors.WaitCursor
            Dim shift = ShiftService.Current(_session)
            If shift IsNot Nothing Then request.ShiftId = shift.Id

            Dim result = SalesService.PostSale(_session, request)
            _lastResult = result
            _lastReceipt = BuildReceiptFromResult(result, _editor.SnapshotLines())
            Cursor = Cursors.Default
            Using dialog As New InvoiceResultForm(_session, result)
                dialog.ShowDialog(Me)
            End Using
            ResetInvoice(False)
            RefreshDropdownItems(txtDropFilter.Text)
            RefreshCustomerExtras()
            RefreshShift()
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex)
        Finally
            _isPosting = False
        End Try
    End Sub

    Private Shared Function SelectedId(combo As ComboBox) As Long?
        Dim item = TryCast(combo.SelectedItem, LookupItem)
        If item IsNot Nothing Then Return If(item.Id = 0, CType(Nothing, Long?), item.Id)
        Dim customer = TryCast(combo.SelectedItem, CustomerSummary)
        If customer IsNot Nothing Then Return If(customer.Id = 0, CType(Nothing, Long?), customer.Id)
        Return Nothing
    End Function

    Private Sub ResetInvoice(confirm As Boolean)
        If confirm AndAlso _editor.LineCount > 0 AndAlso
           Not UiTheme.Confirm("بدء فاتورة جديدة وإفراغ البنود الحالية؟") Then Return
        _editor.Clear()
        _editor.HeaderDiscount = 0D
        _draftEventId = Uuid7.NewId()
        _couponPreviewDiscount = 0D
        _loyaltyPreviewDiscount = 0D
        _voucherPreviewAmount = 0D
        txtDiscount.Clear()
        txtDiscountPctHeader.Clear()
        txtCashReceived.Clear()
        txtCouponCode.Clear()
        txtRedeemPoints.Text = "0"
        txtVoucherCode.Clear()
        txtVoucherAmount.Text = "0.00"
        txtNotes.Clear()
        txtPaid.Clear()
        txtScan.Clear()
        _paidTouched = False
        _keypadBuffer = String.Empty
        lblPicked.Text = "—"
        EditorTotalsChanged(Me, EventArgs.Empty)
        RefreshShift()
        txtScan.Focus()
    End Sub

    ' -------------------------------------------------------------- printing --
    Private Sub PrintTicket(receipt As Boolean)
        If _lastReceipt Is Nothing AndAlso _editor.LineCount = 0 Then
            UiTheme.Warn("لا توجد بنود للطباعة.")
            Return
        End If
        Try
            Using document As New PrintDocument()
                document.DocumentName = "فاتورة مبيعات"
                If receipt Then
                    Dim width As Integer = CInt(80 / 25.4 * 100)
                    document.DefaultPageSettings.PaperSize = New PaperSize("Receipt80", width, 900)
                    document.DefaultPageSettings.Margins = New Margins(10, 10, 10, 10)
                End If
                AddHandler document.PrintPage, Sub(s, e) PrintPage(e, receipt)
                Using preview As New PrintPreviewDialog()
                    preview.Document = document
                    preview.Width = 820
                    preview.Height = 720
                    preview.ShowDialog(Me)
                End Using
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّرت الطباعة")
        End Try
    End Sub

    Private Function CurrentReceipt() As ReceiptSnapshot
        If _editor.LineCount = 0 Then Return _lastReceipt

        Dim totals = _editor.Totals
        Dim redeemPts As Decimal = Math.Max(0D, UiTheme.ToMoney(txtRedeemPoints.Text))
        Dim voucherAmt As Decimal = Math.Max(0D, UiTheme.ToMoney(txtVoucherAmount.Text))
        Dim paidTotal As Decimal = UiTheme.ToMoney(txtPaid.Text) + voucherAmt
        Dim netTotal As Decimal = EffectiveNetTotal()

        Dim snap As New ReceiptSnapshot With {
            .InvoiceNo = Nothing,
            .CustomerName = Convert.ToString(cboCustomer.Text),
            .Subtotal = totals.Subtotal,
            .Discount = totals.LineDiscount + totals.HeaderDiscount + _couponPreviewDiscount + Money.Round2(redeemPts),
            .Tax = totals.TaxTotal,
            .NetTotal = netTotal,
            .Paid = paidTotal,
            .Due = netTotal - paidTotal,
            .CouponDiscount = _couponPreviewDiscount,
            .LoyaltyPoints = redeemPts,
            .VoucherAmount = voucherAmt
        }
        For Each line In _editor.SnapshotLines()
            snap.Lines.Add($"{line.ItemName}  x{line.Qty:0.##}  = {line.DisplayTotal:N2}")
        Next
        Return snap
    End Function

    Private Function BuildReceiptFromResult(result As InvoiceResult, lines As List(Of InvoiceEditorLine)) As ReceiptSnapshot
        Dim snap As New ReceiptSnapshot With {
            .InvoiceNo = result.InvoiceNo,
            .CustomerName = Convert.ToString(cboCustomer.Text),
            .Subtotal = result.Subtotal,
            .Discount = result.LineDiscount + result.HeaderDiscount,
            .Tax = result.TaxTotal,
            .NetTotal = result.Total,
            .Paid = result.Paid,
            .Due = result.Due,
            .CouponDiscount = result.CouponDiscount,
            .LoyaltyPoints = result.LoyaltyRedeemedPoints,
            .VoucherAmount = result.VoucherPaidAmount
        }
        If lines IsNot Nothing Then
            For Each line In lines
                snap.Lines.Add($"{line.ItemName}  x{line.Qty:0.##}  = {line.DisplayTotal:N2}")
            Next
        End If
        Return snap
    End Function

    Private Sub PrintPage(e As PrintPageEventArgs, receipt As Boolean)
        Dim data = CurrentReceipt()
        If data Is Nothing Then Return

        Dim g = e.Graphics
        Dim y As Single = e.MarginBounds.Top
        Dim font As Font = UiTheme.Font(If(receipt, 9.0F, 10.0F))
        Dim bold As Font = UiTheme.Font(If(receipt, 10.0F, 12.0F), FontStyle.Bold)
        Dim sf As New StringFormat With {.Alignment = StringAlignment.Far, .FormatFlags = StringFormatFlags.DirectionRightToLeft}
        Dim bounds As New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 26)

        g.DrawString(_session.BranchName, bold, Brushes.Black, bounds, sf) : y += 28
        g.DrawString("فاتورة مبيعات — " & Date.Now.ToString("yyyy-MM-dd HH:mm"), font, Brushes.Black, bounds, sf) : y += 24
        If Not String.IsNullOrWhiteSpace(data.InvoiceNo) Then
            g.DrawString("رقم الفاتورة: " & data.InvoiceNo, font, Brushes.Black, bounds, sf) : y += 24
        End If
        g.DrawString("العميل: " & If(String.IsNullOrWhiteSpace(data.CustomerName), "—", data.CustomerName), font, Brushes.Black, bounds, sf) : y += 24
        g.DrawLine(Pens.Gray, e.MarginBounds.Left, y, e.MarginBounds.Right, y) : y += 8

        For Each lineText In data.Lines
            g.DrawString(lineText, font, Brushes.Black, New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 22), sf)
            y += 22
            If y > e.MarginBounds.Bottom - 170 Then Exit For
        Next
        y += 6
        g.DrawLine(Pens.Gray, e.MarginBounds.Left, y, e.MarginBounds.Right, y) : y += 10

        Dim captions As New List(Of String) From {
            "الإجمالي قبل الضريبة: " & UiTheme.Money(data.Subtotal),
            "إجمالي الخصومات (بنود + فاتورة + كوبون + نقاط): " & UiTheme.Money(data.Discount),
            "الضريبة: " & UiTheme.Money(data.Tax)
        }
        If data.CouponDiscount > 0D Then captions.Add("منها خصم كوبون: " & UiTheme.Money(data.CouponDiscount))
        If data.LoyaltyPoints > 0D Then captions.Add("منها نقاط ولاء مستبدلة: " & data.LoyaltyPoints.ToString("0.##") & " نقطة")
        If data.VoucherAmount > 0D Then captions.Add("قسيمة شراء مسدَّدة: " & UiTheme.Money(data.VoucherAmount))
        captions.Add("الصافي المطلوب: " & UiTheme.Money(data.NetTotal))
        captions.Add("المدفوع: " & UiTheme.Money(data.Paid))
        captions.Add("المتبقي: " & UiTheme.Money(data.Due))

        For Each caption In captions
            g.DrawString(caption, bold, Brushes.Black, New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 24), sf)
            y += 24
        Next
        e.HasMorePages = False
    End Sub

    Private Sub SaleKeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.F1
                e.SuppressKeyPress = True
                ShowBrandHelpDialog()
            Case Keys.F2
                e.SuppressKeyPress = True
                txtScan.Focus()
                txtScan.SelectAll()
            Case Keys.F3
                e.SuppressKeyPress = True
                txtDropFilter.Focus()
                txtDropFilter.SelectAll()
                If cboItemDrop.Items.Count > 0 Then OpenDropdown(cboItemDrop)
            Case Keys.F4
                e.SuppressKeyPress = True
                cboItemDrop.Focus()
                If cboItemDrop.Items.Count > 0 Then OpenDropdown(cboItemDrop)
            Case Keys.F5
                e.SuppressKeyPress = True
                OpenQuickCustomerDialog()
            Case Keys.F6
                e.SuppressKeyPress = True
                Dim line = SelectedLine()
                If line Is Nothing Then
                    UiTheme.Warn("اختر صنفًا من الفاتورة أولاً لعرض بطاقته وتواريخ صلاحيته.")
                Else
                    ShowItemFullCard(line)
                End If
            Case Keys.F7
                e.SuppressKeyPress = True
                Using f As New ShiftForm(_session)
                    UiChrome.Apply(f)
                    f.ShowDialog(Me)
                End Using
                RefreshShift()
            Case Keys.F8
                e.SuppressKeyPress = True
                HoldInvoice()
            Case Keys.F9, Keys.Enter
                If e.KeyCode = Keys.Enter AndAlso ActiveControl Is txtScan Then Return
                e.SuppressKeyPress = True
                PostSale()
            Case Keys.F10
                e.SuppressKeyPress = True
                PrintTicket(True)
            Case Keys.F11
                e.SuppressKeyPress = True
                _editor.RemoveSelected()
            Case Keys.F12
                e.SuppressKeyPress = True
                ResetInvoice(True)
            Case Keys.Escape
                e.SuppressKeyPress = True
                Close()
        End Select
    End Sub

    ' --------------------------------------------------------------- drag ----
    Private Sub StartDrag(e As MouseEventArgs)
        _dragging = True
        _dragOffset = e.Location
    End Sub

    Private Sub DoDragForm(e As MouseEventArgs)
        If Not _dragging Then Return
        Location = New Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        txtScan.Focus()
        ' The rail is taller than its viewport, so WinForms can leave it parked at the bottom
        ' after the cards finish auto-sizing; cashiers need the scan box first.
        _railNeedsTopPin = True
        _railScroll.ScrollControlIntoView(_railStack)
    End Sub
End Class

''' <summary>
''' نافذة منبثقة شاملة (Popup) تعرض كل تفاصيل الصنف عند الضغط على زر «ℹ️ تفاصيل» بجوار اسم الصنف في الفاتورة:
''' - الباركود مرسومًا + قائمة الباركودات + طباعة ملصق
''' - الرصيد الحالي بالفرع وفي جميع الفروع الأخرى
''' - جدول تواريخ الصلاحية (الإكسبير) والتشغيلات مع اختيار تشغيلة للسطر
''' - تعليمات الاستخدام والجرعة الدوائية والملاحظات (مع حفظها في قاعدة البيانات)
''' </summary>
Public Class ItemFullCardDialog
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _line As InvoiceEditorLine
    Private _details As ItemFullDetails
    Private ReadOnly picBarcode As New PictureBox()
    Private ReadOnly txtInstructions As New TextBox()
    Private ReadOnly gridBatches As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly gridBranches As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly txtBatchEdit As New TextBox()
    Private ReadOnly dtpExpiryEdit As New DateTimePicker()
    Private ReadOnly chkUseExpiry As New CheckBox()

    Public Sub New(session As AppSession, line As InvoiceEditorLine)
        _session = session
        _line = line
        _details = ItemService.GetFullDetails(session, line.ItemId)
        InitializeUi()
        PopulateData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = True
        Text = "بطاقة الصنف الشاملة — " & _details.Record.NameAr
        ClientSize = New Size(920, 650)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = 64,
            .BackColor = Color.FromArgb(18, 65, 24), .Padding = New Padding(14, 8, 14, 8)
        }
        Dim lblTitle As New Label With {
            .Dock = DockStyle.Fill, .ForeColor = Color.White,
            .Font = UiTheme.Font(12.0F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight,
            .Text = "📦 " & _details.Record.NameAr &
                    If(String.IsNullOrWhiteSpace(_details.Record.NameEn), "", "  (" & _details.Record.NameEn & ")") &
                    "   |   كود: " & _details.Record.Code
        }
        header.Controls.Add(lblTitle)

        ' بطاقات المعلومات السريعة والباركود
        Dim topSplit As New SplitContainer With {
            .Dock = DockStyle.Top, .Height = 195, .SplitterDistance = 560,
            .IsSplitterFixed = True, .BackColor = Color.White
        }

        Dim infoTable As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 5, .Padding = New Padding(10)
        }
        infoTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 24))
        infoTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))
        infoTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 24))
        infoTable.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))

        Dim marginVal As Decimal = Money.Round2(_details.Record.SalePrice - _details.LastCost)
        Dim marginPct As Decimal = If(_details.Record.SalePrice > 0D,
                                      Money.Round2((marginVal / _details.Record.SalePrice) * 100D), 0D)

        AddInfoCell(infoTable, 0, 0, "المجموعة:", If(_details.GroupName, "عام"))
        AddInfoCell(infoTable, 0, 2, "الوحدة:", If(_details.UnitName, "وحدة"))
        AddInfoCell(infoTable, 1, 0, "الرصيد المتاح بالفرع:", _details.CurrentBranchQty.ToString("0.##") & " (حد الطلب: " & _details.Record.ReorderLevel.ToString("0.##") & ")")
        AddInfoCell(infoTable, 1, 2, "الضريبة:", If(_details.TaxCategoryName, "—") & " (" & _details.Record.TaxRate.ToString("0.##") & "%)")
        AddInfoCell(infoTable, 2, 0, "سعر الشراء / آخر تكلفة:", UiTheme.Money(_details.LastCost))
        AddInfoCell(infoTable, 2, 2, "سعر البيع للجمهور:", UiTheme.Money(_details.Record.SalePrice))
        AddInfoCell(infoTable, 3, 0, "سعر الجملة:", UiTheme.Money(_details.Record.WholesalePrice))
        AddInfoCell(infoTable, 3, 2, "الحد الأدنى للسعر:", UiTheme.Money(_details.Record.MinPrice))
        AddInfoCell(infoTable, 4, 0, "ربح الوحدة الأساسي:", UiTheme.Money(marginVal) & " (" & marginPct.ToString("0.#") & "%)")
        AddInfoCell(infoTable, 4, 2, "الباركودات المسجلة:", If(_details.Barcodes.Count > 0, String.Join(" , ", _details.Barcodes), _details.Record.Code))

        topSplit.Panel1.Controls.Add(infoTable)

        Dim barcodePanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(8), .BackColor = Color.FromArgb(248, 250, 252)}
        picBarcode.Dock = DockStyle.Top
        picBarcode.Height = 125
        picBarcode.SizeMode = PictureBoxSizeMode.CenterImage
        picBarcode.BackColor = Color.White
        Dim btnPrintBc As Button = UiTheme.MakeButton("🖨️ طباعة ملصقات باركود للصنف", 220)
        btnPrintBc.Dock = DockStyle.Bottom
        AddHandler btnPrintBc.Click, Sub(s, e)
                                         Using f As New BarcodePrintForm(_session)
                                             f.ShowDialog(Me)
                                         End Using
                                     End Sub
        barcodePanel.Controls.Add(picBarcode)
        barcodePanel.Controls.Add(btnPrintBc)
        topSplit.Panel2.Controls.Add(barcodePanel)

        ' التبويبات: (1) الإكسبير والتشغيلات والأرصدة  (2) تعليمات الاستخدام والجرعة الدوائية
        Dim tabs As New TabControl With {.Dock = DockStyle.Fill, .Font = UiTheme.Font(9.0F, FontStyle.Bold)}

        Dim tabExpiry As New TabPage("📅 تواريخ الصلاحية (الإكسبير) وأرصدة الفروع") With {.RightToLeft = RightToLeft.Yes}
        Dim splitGrids As New SplitContainer With {.Dock = DockStyle.Fill, .SplitterDistance = 540}

        Dim batchTopBar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 40, .Padding = New Padding(4), .BackColor = Color.FromArgb(254, 249, 195)}
        batchTopBar.Controls.Add(UiTheme.MakeLabel("التشغيلة للسطر:", True))
        txtBatchEdit.Width = 110
        txtBatchEdit.Text = If(_line.BatchNo, String.Empty)
        batchTopBar.Controls.Add(txtBatchEdit)
        chkUseExpiry.Text = "تحديد إكسبير:"
        chkUseExpiry.AutoSize = True
        chkUseExpiry.Checked = _line.ExpiryDate.HasValue
        batchTopBar.Controls.Add(chkUseExpiry)
        dtpExpiryEdit.Width = 125
        dtpExpiryEdit.Format = DateTimePickerFormat.Short
        If _line.ExpiryDate.HasValue Then dtpExpiryEdit.Value = _line.ExpiryDate.Value
        batchTopBar.Controls.Add(dtpExpiryEdit)

        UiTheme.AddTextColumn(gridBatches, "BatchNo", "رقم التشغيلة", 110)
        UiTheme.AddTextColumn(gridBatches, "ExpiryDate", "تاريخ الصلاحية (الإكسبير)", 135, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(gridBatches, "Qty", "الكمية الواردة", 90, False, "N2")
        UiTheme.AddTextColumn(gridBatches, "UnitCost", "التكلفة", 85, False, "N2")
        UiTheme.AddTextColumn(gridBatches, "InvoiceNo", "الفاتورة", 120, True)
        AddHandler gridBatches.CellDoubleClick, Sub(s, e)
                                                    If e.RowIndex < 0 Then Return
                                                    Dim row = TryCast(gridBatches.Rows(e.RowIndex).DataBoundItem, ItemBatchExpiryRow)
                                                    If row Is Nothing Then Return
                                                    txtBatchEdit.Text = row.BatchNo
                                                    If row.ExpiryDate.HasValue Then
                                                        chkUseExpiry.Checked = True
                                                        dtpExpiryEdit.Value = row.ExpiryDate.Value
                                                    End If
                                                End Sub

        splitGrids.Panel1.Controls.Add(gridBatches)
        splitGrids.Panel1.Controls.Add(batchTopBar)

        UiTheme.AddTextColumn(gridBranches, "BranchName", "الفرع", 170, True)
        UiTheme.AddTextColumn(gridBranches, "QtyOnHand", "الرصيد المتاح", 95, False, "N2")
        UiTheme.AddTextColumn(gridBranches, "LastCost", "آخر تكلفة", 95, False, "N2")
        splitGrids.Panel2.Controls.Add(gridBranches)

        tabExpiry.Controls.Add(splitGrids)

        Dim tabInstructions As New TabPage("📋 تعليمات الصنف والجرعة والملاحظات الدوائية") With {.RightToLeft = RightToLeft.Yes, .Padding = New Padding(10)}
        txtInstructions.Multiline = True
        txtInstructions.Dock = DockStyle.Fill
        txtInstructions.ScrollBars = ScrollBars.Vertical
        txtInstructions.Font = UiTheme.Font(10.5F)
        txtInstructions.PlaceholderText = "اكتب هنا تعليمات استخدام الدواء، الجرعة المعتادة، موانع الاستعمال، أو بدائل الصنف ليراها الكاشير..."
        tabInstructions.Controls.Add(txtInstructions)

        tabs.TabPages.Add(tabExpiry)
        tabs.TabPages.Add(tabInstructions)

        Dim bottom As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .Height = 54, .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(10), .BackColor = Color.FromArgb(241, 245, 249)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("💾 حفظ التعليمات والإكسبير", 195)
        AddHandler btnSave.Click, AddressOf SaveCardChanges
        Dim btnClose As Button = UiTheme.MakeButton("إغلاق", 100)
        AddHandler btnClose.Click, Sub(s, e) Close()
        bottom.Controls.Add(btnSave)
        bottom.Controls.Add(btnClose)

        Controls.Add(tabs)
        Controls.Add(topSplit)
        Controls.Add(header)
        Controls.Add(bottom)
    End Sub

    Private Shared Sub AddInfoCell(t As TableLayoutPanel, row As Integer, col As Integer, caption As String, val As String)
        Dim lblCap As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = UiTheme.Font(8.5F, FontStyle.Bold), .ForeColor = Color.FromArgb(100, 116, 139)
        }
        Dim lblVal As New Label With {
            .Text = val, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = UiTheme.Font(9.0F, FontStyle.Bold), .ForeColor = Color.FromArgb(15, 23, 42)
        }
        t.Controls.Add(lblCap, col, row)
        t.Controls.Add(lblVal, col + 1, row)
    End Sub

    Private Sub PopulateData()
        txtInstructions.Text = If(_details.Instructions, String.Empty)
        If String.IsNullOrWhiteSpace(txtBatchEdit.Text) AndAlso Not String.IsNullOrWhiteSpace(_details.DefaultBatchNo) Then
            txtBatchEdit.Text = _details.DefaultBatchNo
        End If
        If Not _line.ExpiryDate.HasValue AndAlso _details.DefaultExpiryDate.HasValue Then
            chkUseExpiry.Checked = True
            dtpExpiryEdit.Value = _details.DefaultExpiryDate.Value
        End If
        gridBatches.DataSource = New BindingSource With {.DataSource = _details.Batches}
        gridBranches.DataSource = New BindingSource With {.DataSource = _details.BranchStocks}

        Dim codeToRender As String = If(_details.Barcodes.Count > 0, _details.Barcodes(0), _details.Record.Code)
        Try
            If BarcodeRenderer.CanRender(codeToRender) Then
                Dim bmp As New Bitmap(290, 95)
                Using g As Graphics = Graphics.FromImage(bmp)
                    g.Clear(Color.White)
                    BarcodeRenderer.Draw(g, New RectangleF(6.0F, 6.0F, 278.0F, 83.0F), codeToRender, True)
                End Using
                picBarcode.Image = bmp
            End If
        Catch
        End Try
    End Sub

    Private Sub SaveCardChanges(sender As Object, e As EventArgs)
        Try
            _line.BatchNo = If(String.IsNullOrWhiteSpace(txtBatchEdit.Text), Nothing, txtBatchEdit.Text.Trim())
            _line.ExpiryDate = If(chkUseExpiry.Checked, CType(dtpExpiryEdit.Value.Date, Date?), Nothing)
            ItemService.SaveInstructions(_session, _line.ItemId, txtInstructions.Text, _line.BatchNo, _line.ExpiryDate)
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

''' <summary>
''' نافذة سريعة لتسجيل عميل جديد من داخل شاشة البيع ومنحه نقاط ولاء ترحيبية/إضافية فورية.
''' </summary>
Public Class QuickCustomerDialog
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtName As New TextBox()
    Private ReadOnly txtMobile As New TextBox()
    Private ReadOnly txtPhone As New TextBox()
    Private ReadOnly txtAddress As New TextBox()
    Private ReadOnly cboGroup As New ComboBox()
    Private ReadOnly txtInitialPoints As New TextBox()
    Private _createdId As Long

    Public ReadOnly Property CreatedCustomerId As Long
        Get
            Return _createdId
        End Get
    End Property

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = True
        Text = "➕ تسجيل عميل جديد سريع + نقاط ولاء"
        ClientSize = New Size(460, 360)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(16)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 145))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        AddRow(t, "اسم العميل *", txtName)
        AddRow(t, "رقم الموبايل *", txtMobile)
        AddRow(t, "التليفون الأرضي", txtPhone)
        AddRow(t, "العنوان", txtAddress)

        cboGroup.DropDownStyle = ComboBoxStyle.DropDownList
        Dim groups = CustomerGroupService.Search(_session, String.Empty, False)
        Dim groupItems As New List(Of LookupItem) From {New LookupItem With {.Id = 0, .Name = "— بدون مجموعة —"}}
        For Each g In groups
            groupItems.Add(New LookupItem With {.Id = g.Id, .Name = g.Name & " (" & g.DiscountPercent.ToString("0.#") & "%)"})
        Next
        cboGroup.DataSource = groupItems
        cboGroup.DisplayMember = "Name"
        AddRow(t, "مجموعة العميل", cboGroup)

        txtInitialPoints.Text = "0"
        AddRow(t, "⭐ نقاط ولاء ترحيبية", txtInitialPoints)

        Dim btns As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .Height = 56,
            .FlowDirection = FlowDirection.LeftToRight, .Padding = New Padding(12)
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("حفظ واختيار العميل", 155)
        AddHandler btnSave.Click, AddressOf SaveCustomer
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 95)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        btns.Controls.Add(btnSave)
        btns.Controls.Add(btnCancel)

        Controls.Add(t)
        Controls.Add(btns)
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, caption As String, editor As Control)
        Dim r As Integer = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        t.Controls.Add(UiTheme.MakeLabel(caption, True), 0, r)
        editor.Dock = DockStyle.Fill
        t.Controls.Add(editor, 1, r)
    End Sub

    Private Sub SaveCustomer(sender As Object, e As EventArgs)
        Try
            Dim grpItem = TryCast(cboGroup.SelectedItem, LookupItem)
            Dim grpId As Long? = If(grpItem IsNot Nothing AndAlso grpItem.Id > 0, grpItem.Id, CType(Nothing, Long?))
            Dim newId As Long = CustomerService.Save(
                _session, Nothing, txtName.Text.Trim(), "customer",
                txtMobile.Text.Trim(), txtPhone.Text.Trim(), Nothing,
                txtAddress.Text.Trim(), Nothing, 0D, 0D, True, grpId)

            Dim bonusPts As Decimal = UiTheme.ToMoney(txtInitialPoints.Text)
            If bonusPts > 0D Then
                LoyaltyService.Adjust(_session, newId, bonusPts, "earn", "نقاط ترحيبية عند تسجيل العميل")
            End If

            _createdId = newId
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class


''' <summary>
''' DOCTOR TECH — Customer transaction history screen.
''' Displays all posted transactions for the linked customer from the database (invoices table)
''' and allows opening or returning any transaction.
''' </summary>
Public Class CustomerHistoryDialog
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _customerId As Long
    Private ReadOnly _customerName As String
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly lblSummary As New Label()

    Public Sub New(session As AppSession, customerId As Long, customerName As String)
        _session = session
        _customerId = customerId
        _customerName = customerName
        InitializeUi()
        LoadHistory()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — سجل معاملات العميل: " & _customerName
        ClientSize = New Size(860, 520)
        StartPosition = FormStartPosition.CenterParent

        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = 54,
            .BackColor = UiTheme.BrandNavy, .Padding = New Padding(14, 8, 14, 8)
        }
        Dim lblTitle As New Label With {
            .Text = "🔷 سجل فواتير ومسحوبات العميل: " & _customerName,
            .Dock = DockStyle.Fill, .ForeColor = Color.White,
            .Font = UiTheme.Font(11.5F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(lblTitle)

        UiTheme.AddTextColumn(grid, "InvoiceNo", "رقم الفاتورة", 150)
        UiTheme.AddTextColumn(grid, "TypeArabic", "نوع المعاملة", 120)
        UiTheme.AddTextColumn(grid, "BusinessDate", "التاريخ", 120, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(grid, "Total", "الإجمالي", 120, False, "N2")
        UiTheme.AddTextColumn(grid, "Paid", "المدفوع", 120, False, "N2")
        UiTheme.AddTextColumn(grid, "Due", "المتبقي", 120, True, "N2")

        Dim bottom As New Panel With {
            .Dock = DockStyle.Bottom, .Height = 52,
            .BackColor = UiTheme.BrandLight, .Padding = New Padding(10, 8, 10, 8)
        }
        lblSummary.Dock = DockStyle.Fill
        lblSummary.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        lblSummary.ForeColor = UiTheme.BrandNavy
        lblSummary.TextAlign = ContentAlignment.MiddleRight

        Dim btnClose As Button = UiTheme.PrimaryButton("F2 / Done — إغلاق", 150)
        btnClose.Dock = DockStyle.Left
        AddHandler btnClose.Click, Sub(s, e) Close()

        bottom.Controls.Add(lblSummary)
        bottom.Controls.Add(btnClose)

        Controls.Add(grid)
        Controls.Add(header)
        Controls.Add(bottom)
    End Sub

    Private Sub LoadHistory()
        Try
            Dim rows = CustomerService.ListHistory(_session, _customerId, 200)
            grid.DataSource = New BindingSource With {.DataSource = rows}
            Dim totalSales As Decimal = rows.Where(Function(r) r.InvoiceType = "sale").Sum(Function(r) r.Total)
            Dim totalDue As Decimal = CustomerService.Balance(_session, _customerId)
            lblSummary.Text = $"إجمالي عدد الفواتير: {rows.Count}   |   إجمالي المبيعات: {UiTheme.Money(totalSales)}   |   الرصيد الحالي على العميل: {UiTheme.Money(totalDue)}"
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

''' <summary>
''' DOCTOR TECH — Post Void confirmation.
''' Allows voiding/reversing a posted sale transaction in the database by creating a full reversal return
''' with a formal reason code, restoring inventory and reversing cash/accounting.
''' </summary>
Public Class PostVoidConfirmDialog
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly cboInvoices As New ComboBox()
    Private ReadOnly lblTransType As New Label()
    Private ReadOnly lblTransAmount As New Label()
    Private ReadOnly cboReason As New ComboBox()
    Private _recentInvoices As New List(Of InvoiceListRow)()

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadRecentInvoices()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — إلغاء معاملة مرحّلة"
        ClientSize = New Size(560, 360)
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim prompt As New Panel With {
            .Dock = DockStyle.Top, .Height = 52,
            .BackColor = UiTheme.BrandLight, .Padding = New Padding(12, 8, 12, 8)
        }
        Dim lblPrompt As New Label With {
            .Text = "اختر رقم الفاتورة وسبب الإلغاء ثم اضغط Enter/Next — Choose a void reason code and press Next.",
            .Dock = DockStyle.Fill, .BackColor = Color.White,
            .ForeColor = UiTheme.BrandNavy, .Font = UiTheme.Font(9.5F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleRight, .Padding = New Padding(8, 0, 8, 0)
        }
        prompt.Controls.Add(lblPrompt)

        Dim t As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(24, 18, 24, 12)
        }
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        cboInvoices.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler cboInvoices.SelectedIndexChanged, Sub(s, e) UpdateSelectedInvoiceInfo()

        lblTransType.Text = "SALE (فاتورة مبيعات)"
        lblTransType.Font = UiTheme.Font(10.0F, FontStyle.Bold)

        lblTransAmount.Text = "0.00"
        lblTransAmount.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        lblTransAmount.ForeColor = UiTheme.DangerRed

        cboReason.DropDownStyle = ComboBoxStyle.DropDownList
        cboReason.Items.AddRange(New Object() {
            "Customer Change Mind — غيّر العميل رأيه",
            "Cashier Entry Error — خطأ إدخال من الكاشير",
            "Wrong Item Scanned — مسح صنف بالخطأ",
            "Price Dispute — اعتراض على السعر"
        })
        cboReason.SelectedIndex = 0

        AddRow(t, "رقم المعاملة (Transaction No):", cboInvoices)
        AddRow(t, "نوع المعاملة (Transaction Type):", lblTransType)
        AddRow(t, "إجمالي المبلغ (Amount):", lblTransAmount)
        AddRow(t, "سبب الإلغاء (Reason Code) *:", cboReason)

        Dim bottom As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .Height = 56,
            .FlowDirection = FlowDirection.LeftToRight,
            .BackColor = UiTheme.BrandNavy, .Padding = New Padding(10, 8, 10, 8)
        }
        Dim btnConfirm As Button = UiTheme.DangerButton("Enter / Next — تأكيد الإلغاء وعكس الحركة", 250)
        AddHandler btnConfirm.Click, AddressOf ExecutePostVoid
        Dim btnCancel As Button = UiTheme.MakeButton("F12 / Cancel — تراجع", 140)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        bottom.Controls.Add(btnConfirm)
        bottom.Controls.Add(btnCancel)

        Controls.Add(t)
        Controls.Add(prompt)
        Controls.Add(bottom)
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, caption As String, ctl As Control)
        Dim r As Integer = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 46))
        t.Controls.Add(UiTheme.MakeLabel(caption, True), 0, r)
        ctl.Dock = DockStyle.Fill
        t.Controls.Add(ctl, 1, r)
    End Sub

    Private Sub LoadRecentInvoices()
        Try
            _recentInvoices = SalesService.ListInvoices(_session, Date.Today.AddDays(-7), Date.Today, "sale")
            cboInvoices.DataSource = _recentInvoices
            cboInvoices.DisplayMember = "InvoiceNo"
            cboInvoices.ValueMember = "Id"
            UpdateSelectedInvoiceInfo()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub UpdateSelectedInvoiceInfo()
        Dim row = TryCast(cboInvoices.SelectedItem, InvoiceListRow)
        If row Is Nothing Then
            lblTransAmount.Text = "0.00"
        Else
            lblTransAmount.Text = UiTheme.Money(row.Total) & " ج.م (المدفوع: " & UiTheme.Money(row.Paid) & ")"
        End If
    End Sub

    Private Sub ExecutePostVoid(sender As Object, e As EventArgs)
        Dim row = TryCast(cboInvoices.SelectedItem, InvoiceListRow)
        If row Is Nothing Then
            UiTheme.Warn("اختر فاتورة أولاً.")
            Return
        End If

        Try
            Dim shift = ShiftService.Current(_session)
            Dim returnable As List(Of ReturnableLine)
            Using db = DbSession.Open(_session.ConnectionString)
                returnable = InvoicesRepository.GetReturnableLines(db.Connection, row.Id, Nothing)
            End Using

            Dim linesToReturn As New List(Of InvoiceLineRequest)()
            For Each r In returnable
                Dim remQty As Decimal = r.Qty - r.ReturnedQty
                If remQty > 0D Then
                    linesToReturn.Add(New InvoiceLineRequest With {
                        .ItemId = r.ItemId,
                        .OriginalLineId = r.LineId,
                        .Qty = remQty,
                        .UnitPrice = r.UnitPrice,
                        .TaxCategoryId = r.TaxCategoryId
                    })
                End If
            Next

            If linesToReturn.Count = 0 Then
                UiTheme.Warn("هذه الفاتورة تم إرجاع/إلغاء جميع بنودها مسبقًا.")
                Return
            End If

            If Not UiTheme.Confirm("تأكيد إلغاء وعكس الفاتورة رقم «" & row.InvoiceNo & "» بالكامل وإعادة الأرصدة للمخزن؟") Then Return

            Dim req As New InvoiceRequest With {
                .ShiftId = If(shift Is Nothing, CType(Nothing, Long?), shift.Id),
                .Notes = "Post Void (" & Convert.ToString(cboReason.SelectedItem) & ") — أصل: " & row.InvoiceNo,
                .Lines = linesToReturn
            }

            Dim reasonText As String = Convert.ToString(cboReason.SelectedItem)
            Dim shiftIdVal As Long? = If(shift Is Nothing, CType(Nothing, Long?), shift.Id)
            Dim res = SalesService.VoidSaleInvoice(_session, row.Id, reasonText, shiftIdVal)
            UiTheme.Info("تم إلغاء الفاتورة وتحديث حالتها إلى (ملغاة / Voided) وعكس حركتها المخزنية والمحاسبية بنجاح." & Environment.NewLine &
                         "رقم مستند الإلغاء العكسي: " & res.InvoiceNo, "إلغاء معاملة")
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class


Friend Class LookupItem
    Public Property Id As Long
    Public Property Name As String
    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class
