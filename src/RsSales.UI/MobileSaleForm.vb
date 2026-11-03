Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Touch-friendly point-of-sale screen for tablets and mobile workstations.</summary>
Public Class MobileSaleForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _editor As InvoiceEditor
    Private ReadOnly _cboPayment As New ComboBox()
    Private ReadOnly _txtPaid As New TextBox()
    Private ReadOnly _lblCustomer As New Label()
    Private ReadOnly _lblShift As New Label()
    Private ReadOnly _lblTotal As New Label()
    Private ReadOnly _lblTax As New Label()
    Private ReadOnly _lblDue As New Label()
    Private ReadOnly _quickPanel As New FlowLayoutPanel()
    Private _customerId As Long?
    Private _customerName As String = "عميل نقدي"
    Private _paidTouched As Boolean
    Private _updatingPaid As Boolean
    Private _isPosting As Boolean
    Private _draftEventId As String = Uuid7.NewId()

    Public Sub New(session As AppSession)
        _session = session
        _editor = New InvoiceEditor(session, "sale")
        UiTheme.Apply(Me)
        Text = "فاتورة موبايل"
        ClientSize = New Size(1180, 760)
        MinimumSize = New Size(980, 650)
        BuildUi()
        LoadPayments()
        LoadQuickItems()
        RefreshShift()
        AddHandler _editor.TotalsChanged, AddressOf TotalsChanged
        TotalsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub BuildUi()
        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = UiTheme.Surface
        }
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 64))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 76))
        root.Controls.Add(BuildHeader(), 0, 0)
        root.Controls.Add(BuildBody(), 0, 1)
        root.Controls.Add(BuildFooter(), 0, 2)
        Controls.Add(root)
    End Sub

    Private Function BuildHeader() As Panel
        Dim header As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(24, 58, 92)}
        Dim title As New Label With {
            .Text = "📱 فاتورة موبايل سريعة", .Font = UiTheme.Font(16.0F, FontStyle.Bold),
            .ForeColor = Color.White, .Dock = DockStyle.Right, .Width = 360,
            .TextAlign = ContentAlignment.MiddleRight
        }
        _lblShift.Dock = DockStyle.Fill
        _lblShift.TextAlign = ContentAlignment.MiddleLeft
        _lblShift.Padding = New Padding(18, 0, 18, 0)
        _lblShift.Font = UiTheme.Font(10.5F, FontStyle.Bold)
        _lblShift.ForeColor = Color.FromArgb(205, 230, 245)
        header.Controls.Add(_lblShift)
        header.Controls.Add(title)
        Return header
    End Function

    Private Function BuildBody() As Control
        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Padding = New Padding(8)
        }
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 340))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        body.Controls.Add(BuildSide(), 0, 0)
        body.Controls.Add(BuildCenter(), 1, 0)
        Return body
    End Function

    Private Function BuildCenter() As Control
        Dim panel As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .BackColor = Color.White
        }
        panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 94))
        panel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        _quickPanel.Dock = DockStyle.Fill
        _quickPanel.FlowDirection = FlowDirection.LeftToRight
        _quickPanel.WrapContents = False
        _quickPanel.AutoScroll = True
        _quickPanel.Padding = New Padding(4)
        _quickPanel.BackColor = Color.FromArgb(246, 249, 252)
        Dim quickHost As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(2)}
        quickHost.Controls.Add(_quickPanel)
        panel.Controls.Add(quickHost, 0, 0)
        panel.Controls.Add(_editor, 0, 1)
        Return panel
    End Function

    Private Function BuildSide() As Control
        Dim card As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.White,
            .Padding = New Padding(12), .Margin = New Padding(0, 0, 8, 0)
        }
        Dim stack As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .AutoScroll = True
        }

        _lblCustomer.Width = 300
        _lblCustomer.Height = 46
        _lblCustomer.TextAlign = ContentAlignment.MiddleCenter
        _lblCustomer.BackColor = Color.FromArgb(238, 247, 250)
        _lblCustomer.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        _lblCustomer.Text = "👤 " & _customerName
        Dim btnCustomer As Button = UiTheme.MakeButton("تغيير العميل", 300)
        btnCustomer.Height = 38
        AddHandler btnCustomer.Click, Sub(s, e) PickCustomer()

        Dim lblPay As New Label With {
            .Text = "طريقة الدفع", .Width = 300, .Height = 28,
            .Font = UiTheme.Font(10.0F, FontStyle.Bold), .TextAlign = ContentAlignment.MiddleRight
        }
        _cboPayment.Width = 300
        _cboPayment.Font = UiTheme.Font(11.0F)
        _cboPayment.DropDownStyle = ComboBoxStyle.DropDownList

        Dim lblPaid As New Label With {
            .Text = "المدفوع", .Width = 300, .Height = 28,
            .Font = UiTheme.Font(10.0F, FontStyle.Bold), .TextAlign = ContentAlignment.MiddleRight
        }
        Dim paidRow As New Panel With {.Width = 300, .Height = 40}
        _txtPaid.Dock = DockStyle.Fill
        _txtPaid.Font = UiTheme.Font(13.0F, FontStyle.Bold)
        _txtPaid.TextAlign = HorizontalAlignment.Left
        Dim btnFull As Button = UiTheme.MakeButton("الكامل", 80)
        btnFull.Dock = DockStyle.Right
        btnFull.Height = 38
        AddHandler btnFull.Click, Sub(s, e) PayFull()
        AddHandler _txtPaid.TextChanged, Sub(s, e)
                                             If _updatingPaid Then Return
                                             _paidTouched = True
                                             UpdateDue()
                                         End Sub
        paidRow.Controls.Add(_txtPaid)
        paidRow.Controls.Add(btnFull)

        _lblTotal.Width = 300
        _lblTotal.Height = 74
        _lblTotal.TextAlign = ContentAlignment.MiddleCenter
        _lblTotal.BackColor = Color.FromArgb(39, 174, 96)
        _lblTotal.ForeColor = Color.White
        _lblTotal.Font = UiTheme.Font(16.0F, FontStyle.Bold)

        _lblTax.Width = 300
        _lblTax.Height = 38
        _lblTax.TextAlign = ContentAlignment.MiddleCenter
        _lblTax.Font = UiTheme.Font(10.5F, FontStyle.Bold)
        _lblTax.ForeColor = UiTheme.Muted

        _lblDue.Width = 300
        _lblDue.Height = 38
        _lblDue.TextAlign = ContentAlignment.MiddleCenter
        _lblDue.Font = UiTheme.Font(11.0F, FontStyle.Bold)
        _lblDue.ForeColor = UiTheme.Danger

        Dim btnPost As Button = UiTheme.PrimaryButton("💾 ترحيل الفاتورة", 300)
        btnPost.Height = 56
        btnPost.Font = UiTheme.Font(13.0F, FontStyle.Bold)
        AddHandler btnPost.Click, Sub(s, e) PostSale()
        Dim btnNew As Button = UiTheme.MakeButton("فاتورة جديدة", 300)
        btnNew.Height = 40
        AddHandler btnNew.Click, Sub(s, e) ResetInvoice(True)

        stack.Controls.Add(_lblCustomer)
        stack.Controls.Add(btnCustomer)
        stack.Controls.Add(lblPay)
        stack.Controls.Add(_cboPayment)
        stack.Controls.Add(lblPaid)
        stack.Controls.Add(paidRow)
        stack.Controls.Add(_lblTotal)
        stack.Controls.Add(_lblTax)
        stack.Controls.Add(_lblDue)
        stack.Controls.Add(btnPost)
        stack.Controls.Add(btnNew)
        card.Controls.Add(stack)
        Return card
    End Function

    Private Function BuildFooter() As Control
        Dim footer As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.FromArgb(24, 58, 92),
            .Padding = New Padding(18, 10, 18, 10)
        }
        Dim note As New Label With {
            .Text = "امسح الباركود أو ابحث بالاسم ثم اضغط Enter — الأصناف السريعة في الأعلى.",
            .Dock = DockStyle.Fill, .ForeColor = Color.FromArgb(210, 232, 245),
            .TextAlign = ContentAlignment.MiddleRight, .Font = UiTheme.Font(10.0F)
        }
        footer.Controls.Add(note)
        Return footer
    End Function

    Private Sub LoadPayments()
        Try
            Dim methods = UiLookup.PaymentMethods(_session)
            _cboPayment.DataSource = methods
            _cboPayment.DisplayMember = "Name"
            For i As Integer = 0 To methods.Count - 1
                If methods(i).Name.IndexOf("كاش", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    _cboPayment.SelectedIndex = i
                    Exit For
                End If
            Next
            If _cboPayment.SelectedIndex < 0 AndAlso methods.Count > 0 Then _cboPayment.SelectedIndex = 0
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub LoadQuickItems()
        Try
            _quickPanel.Controls.Clear()
            For Each item In ItemService.Search(_session, String.Empty, False, 10)
                Dim button As New Button With {
                    .Width = 168, .Height = 76, .Margin = New Padding(4),
                    .FlatStyle = FlatStyle.Flat, .BackColor = Color.White,
                    .Font = UiTheme.Font(9.5F, FontStyle.Bold), .Cursor = Cursors.Hand,
                    .Text = TrimText(item.NameAr, 26) & Environment.NewLine & UiTheme.Money(item.SalePrice)
                }
                button.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 230)
                Dim captured = item
                AddHandler button.Click, Sub(s, e) _editor.AddItem(captured)
                _quickPanel.Controls.Add(button)
            Next
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Shared Function TrimText(text As String, maxLength As Integer) As String
        If String.IsNullOrEmpty(text) Then Return String.Empty
        Return If(text.Length <= maxLength, text, text.Substring(0, maxLength - 1) & "…")
    End Function

    Private Sub PickCustomer()
        Using picker As New CustomerPickerForm(_session, "customer")
            If picker.ShowDialog(Me) = DialogResult.OK Then
                _customerId = picker.SelectedCustomerId
                _customerName = picker.SelectedCustomerName
                _lblCustomer.Text = "👤 " & _customerName
            End If
        End Using
    End Sub

    Private Sub RefreshShift()
        Try
            Dim shift = ShiftService.Current(_session)
            _lblShift.Text = If(shift Is Nothing, "لا توجد وردية مفتوحة",
                                "الوردية " & shift.ShiftNo & " مفتوحة")
        Catch
            _lblShift.Text = String.Empty
        End Try
    End Sub

    Private Sub TotalsChanged(sender As Object, e As EventArgs)
        Dim totals = _editor.Totals
        _lblTotal.Text = "الإجمالي" & Environment.NewLine & UiTheme.Money(totals.Total)
        _lblTax.Text = "الضريبة: " & UiTheme.Money(totals.TaxTotal)
        If Not _paidTouched Then
            _updatingPaid = True
            _txtPaid.Text = totals.Total.ToString("0.00")
            _updatingPaid = False
        End If
        UpdateDue()
    End Sub

    Private Sub UpdateDue()
        _lblDue.Text = "المتبقي: " & UiTheme.Money(_editor.Totals.Total - UiTheme.ToMoney(_txtPaid.Text))
    End Sub

    Private Sub PayFull()
        _updatingPaid = True
        _txtPaid.Text = _editor.Totals.Total.ToString("0.00")
        _updatingPaid = False
        _paidTouched = False
        UpdateDue()
    End Sub

    Private Sub PostSale()
        If _isPosting Then Return
        If _editor.LineCount = 0 Then
            UiTheme.Warn("أضف صنفًا واحدًا على الأقل.")
            Return
        End If
        Dim paid As Decimal = UiTheme.ToMoney(_txtPaid.Text)
        If paid < 0D OrElse paid > _editor.Totals.Total + 0.004D Then
            UiTheme.Warn("المدفوع غير صالح.")
            Return
        End If

        Dim request As New InvoiceRequest With {
            .ClientEventId = _draftEventId,
            .CustomerId = _customerId,
            .BuyerName = If(_customerId.HasValue, _customerName, Nothing),
            .PaymentMethodId = If(TryCast(_cboPayment.SelectedItem, LookupItem) Is Nothing, 0L,
                                  DirectCast(_cboPayment.SelectedItem, LookupItem).Id),
            .Paid = paid,
            .Notes = "فاتورة موبايل",
            .BusinessDate = Date.Today,
            .Lines = _editor.BuildLineRequests()
        }
        Try
            Dim shift = ShiftService.Current(_session)
            If shift IsNot Nothing Then request.ShiftId = shift.Id

            Cursor = Cursors.WaitCursor
            Dim result = SalesService.PostSale(_session, request)
            Cursor = Cursors.Default
            Using dialog As New InvoiceResultForm(_session, result)
                dialog.ShowDialog(Me)
            End Using
            ResetInvoice(False)
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex)
        Finally
            _isPosting = False
        End Try
    End Sub

    Private Sub ResetInvoice(confirm As Boolean)
        If confirm AndAlso _editor.LineCount > 0 AndAlso
           Not UiTheme.Confirm("بدء فاتورة جديدة؟") Then Return
        _editor.Clear()
        _draftEventId = Uuid7.NewId()
        _customerId = Nothing
        _customerName = "عميل نقدي"
        _lblCustomer.Text = "👤 " & _customerName
        _paidTouched = False
        _txtPaid.Clear()
        TotalsChanged(Me, EventArgs.Empty)
        RefreshShift()
        LoadQuickItems()
        _editor.FocusSearch()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _editor.FocusSearch()
    End Sub
End Class
