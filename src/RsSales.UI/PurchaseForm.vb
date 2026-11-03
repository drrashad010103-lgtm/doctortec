Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Real purchase invoice editor: supplier, cost, batch/expiry and posting.</summary>
Public Class PurchaseForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _editor As InvoiceEditor
    Private ReadOnly _cboSupplier As New ComboBox()
    Private ReadOnly _cboPayment As New ComboBox()
    Private ReadOnly _txtPaid As New TextBox()
    Private ReadOnly _txtDiscount As New TextBox()
    Private ReadOnly _txtNotes As New TextBox()
    Private ReadOnly _dtDate As New DateTimePicker()
    Private ReadOnly _lblShift As New Label()
    Private ReadOnly _lblSubtotal As New Label()
    Private ReadOnly _lblDiscount As New Label()
    Private ReadOnly _lblTax As New Label()
    Private ReadOnly _lblTotal As New Label()
    Private ReadOnly _lblPaid As New Label()
    Private ReadOnly _lblDue As New Label()
    Private _paidTouched As Boolean
    Private _updatingPaid As Boolean
    Private _isPosting As Boolean
    Private _draftEventId As String = Uuid7.NewId()

    Public Sub New(session As AppSession)
        _session = session
        _editor = New InvoiceEditor(session, "purchase")
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — فاتورة مشتريات وتوريد مخزني"
        WindowState = FormWindowState.Maximized
        ClientSize = New Size(1280, 760)
        MinimumSize = New Size(1050, 650)
        KeyPreview = True
        BuildUi()
        LoadLookups()
        RefreshShift()
        AddHandler _editor.TotalsChanged, AddressOf EditorTotalsChanged
        AddHandler KeyDown, Sub(s, e)
                                If e.KeyCode = Keys.F2 Then
                                    e.SuppressKeyPress = True
                                    _editor.FocusSearch()
                                ElseIf e.KeyCode = Keys.F9 Then
                                    e.SuppressKeyPress = True
                                    PostPurchase()
                                End If
                            End Sub
        EditorTotalsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub BuildUi()
        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = UiTheme.Surface
        }
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 68))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 84))
        root.Controls.Add(BuildHeader(), 0, 0)
        root.Controls.Add(BuildBody(), 0, 1)
        root.Controls.Add(BuildTotals(), 0, 2)
        Controls.Add(root)
    End Sub

    Private Function BuildHeader() As Panel
        Dim header As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = UiTheme.BrandNavy,
            .Padding = New Padding(18, 8, 18, 8)
        }
        Dim title As New Label With {
            .Text = "🔷 DOCTOR TECH | فاتورة مشتريات وتوريد مخزني", .Font = UiTheme.Font(15.0F, FontStyle.Bold),
            .ForeColor = Color.White, .Dock = DockStyle.Right, .Width = 320,
            .TextAlign = ContentAlignment.MiddleRight
        }
        Dim subtitle As New Label With {
            .Text = "توريد أصناف من الموردين مع التكلفة والصلاحية والدفع الآجل",
            .Font = UiTheme.Font(9.5F), .ForeColor = UiTheme.BrandLight,
            .Dock = DockStyle.Right, .Width = 450, .TextAlign = ContentAlignment.MiddleRight,
            .Padding = New Padding(0, 8, 12, 0)
        }
        _lblShift.Dock = DockStyle.Fill
        _lblShift.TextAlign = ContentAlignment.MiddleLeft
        _lblShift.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        _lblShift.ForeColor = UiTheme.BrandLight
        header.Controls.Add(_lblShift)
        header.Controls.Add(subtitle)
        header.Controls.Add(title)
        Return header
    End Function

    Private Function BuildBody() As Control
        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Padding = New Padding(8), .BackColor = UiTheme.Surface
        }
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 350))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        body.Controls.Add(BuildSidePanel(), 0, 0)
        body.Controls.Add(_editor, 1, 0)
        Return body
    End Function

    Private Function BuildSidePanel() As Control
        Dim card As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.White,
            .Padding = New Padding(10), .Margin = New Padding(0, 0, 8, 0)
        }
        Dim form As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 0,
            .AutoScroll = True, .BackColor = Color.White
        }
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92))
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        _cboSupplier.DropDownStyle = ComboBoxStyle.DropDownList
        _cboSupplier.DisplayMember = "Name"
        Dim supplierRow As New Panel With {.Dock = DockStyle.Fill, .Height = 32}
        _cboSupplier.Dock = DockStyle.Fill
        Dim btnSupplier As Button = UiTheme.MakeButton("جديد", 60)
        btnSupplier.Dock = DockStyle.Right
        AddHandler btnSupplier.Click, Sub(s, e) AddSupplier()
        supplierRow.Controls.Add(_cboSupplier)
        supplierRow.Controls.Add(btnSupplier)
        AddRow(form, "المورد", supplierRow)

        _cboPayment.DropDownStyle = ComboBoxStyle.DropDownList
        _cboPayment.DisplayMember = "Name"
        AddRow(form, "طريقة الدفع", _cboPayment)

        Dim paidRow As New Panel With {.Dock = DockStyle.Fill, .Height = 30}
        _txtPaid.Dock = DockStyle.Fill
        _txtPaid.TextAlign = HorizontalAlignment.Left
        Dim btnFull As Button = UiTheme.MakeButton("الكامل", 65)
        btnFull.Dock = DockStyle.Right
        AddHandler btnFull.Click, Sub(s, e) PayFull()
        AddHandler _txtPaid.TextChanged, Sub(s, e)
                                             If _updatingPaid Then Return
                                             _paidTouched = True
                                             UpdatePaymentLabels()
                                         End Sub
        paidRow.Controls.Add(_txtPaid)
        paidRow.Controls.Add(btnFull)
        AddRow(form, "المدفوع للمورد", paidRow)

        _txtDiscount.TextAlign = HorizontalAlignment.Left
        AddHandler _txtDiscount.Leave, Sub(s, e) ApplyHeaderDiscount()
        AddHandler _txtDiscount.KeyDown, Sub(s, e)
                                             If e.KeyCode = Keys.Enter Then
                                                 e.SuppressKeyPress = True
                                                 ApplyHeaderDiscount()
                                             End If
                                         End Sub
        AddRow(form, "خصم الفاتورة", _txtDiscount)

        _dtDate.Format = DateTimePickerFormat.Custom
        _dtDate.CustomFormat = "yyyy-MM-dd"
        _dtDate.Value = Date.Today
        AddRow(form, "التاريخ", _dtDate)

        _txtNotes.Multiline = True
        _txtNotes.Height = 66
        _txtNotes.ScrollBars = ScrollBars.Vertical
        AddRow(form, "ملاحظات", _txtNotes)

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .Height = 92, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Padding = New Padding(0, 8, 0, 0)
        }
        Dim btnPost As Button = UiTheme.PrimaryButton("ترحيل فاتورة الشراء  F9", 310)
        btnPost.Height = 40
        btnPost.BackColor = UiTheme.BrandNavy
        AddHandler btnPost.Click, Sub(s, e) PostPurchase()
        Dim btnNew As Button = UiTheme.MakeButton("فاتورة جديدة", 310)
        AddHandler btnNew.Click, Sub(s, e) ResetInvoice(True)
        buttons.Controls.Add(btnPost)
        buttons.Controls.Add(btnNew)

        Dim host As New Panel With {.Dock = DockStyle.Fill}
        host.Controls.Add(form)
        host.Controls.Add(buttons)
        card.Controls.Add(host)
        Return card
    End Function

    Private Function BuildTotals() As Control
        Dim bar As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Padding = New Padding(14, 8, 14, 8), .BackColor = Color.White
        }
        AddTotalBox(bar, _lblSubtotal, "الإجمالي قبل الضريبة")
        AddTotalBox(bar, _lblDiscount, "الخصومات")
        AddTotalBox(bar, _lblTax, "الضريبة")
        AddTotalBox(bar, _lblTotal, "إجمالي الفاتورة")
        AddTotalBox(bar, _lblPaid, "المدفوع")
        AddTotalBox(bar, _lblDue, "المستحق للمورد")
        Return bar
    End Function

    Private Shared Sub AddTotalBox(bar As FlowLayoutPanel, label As Label, caption As String)
        label.Width = 176
        label.Height = 62
        label.Margin = New Padding(4)
        label.TextAlign = ContentAlignment.MiddleCenter
        label.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        label.BackColor = UiTheme.BrandLight
        label.ForeColor = UiTheme.Navy
        label.BorderStyle = BorderStyle.FixedSingle
        label.Tag = caption
        bar.Controls.Add(label)
    End Sub

    Private Shared Sub AddRow(table As TableLayoutPanel, caption As String, editor As Control)
        Dim row As Integer = table.RowCount
        table.RowCount += 1
        table.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        Dim label As New Label With {
            .Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = UiTheme.Font(9.5F, FontStyle.Bold), .ForeColor = UiTheme.Ink
        }
        editor.Dock = DockStyle.Fill
        editor.Margin = New Padding(2, 4, 2, 4)
        table.Controls.Add(label, 0, row)
        table.Controls.Add(editor, 1, row)
    End Sub

    Private Sub LoadLookups()
        Try
            Dim suppliers = CustomerService.Search(_session, String.Empty, "supplier")
            Dim rows As New List(Of CustomerSummary) From {
                New CustomerSummary With {.Id = 0, .Name = "— بدون مورد —"}
            }
            rows.AddRange(suppliers)
            _cboSupplier.DataSource = rows
            _cboSupplier.SelectedIndex = 0

            Dim methods = UiLookup.PaymentMethods(_session)
            _cboPayment.DataSource = methods
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

    Private Sub AddSupplier()
        Using dialog As New CustomersForm(_session, "supplier")
            dialog.ShowDialog(Me)
        End Using
        LoadLookups()
    End Sub

    Private Sub RefreshShift()
        Try
            Dim shift = ShiftService.Current(_session)
            If shift Is Nothing Then
                _lblShift.Text = "لا توجد وردية مفتوحة — الدفع النقدي سيُسجّل بدون وردية."
            Else
                _lblShift.Text = "الوردية: " & shift.ShiftNo
            End If
        Catch
            _lblShift.Text = String.Empty
        End Try
    End Sub

    Private Sub EditorTotalsChanged(sender As Object, e As EventArgs)
        Dim totals = _editor.Totals
        _lblSubtotal.Text = CStr(_lblSubtotal.Tag) & Environment.NewLine & UiTheme.Money(totals.Subtotal)
        _lblDiscount.Text = CStr(_lblDiscount.Tag) & Environment.NewLine & UiTheme.Money(totals.LineDiscount + totals.HeaderDiscount)
        _lblTax.Text = CStr(_lblTax.Tag) & Environment.NewLine & UiTheme.Money(totals.TaxTotal)
        _lblTotal.Text = CStr(_lblTotal.Tag) & Environment.NewLine & UiTheme.Money(totals.Total)
        If Not _paidTouched Then
            _updatingPaid = True
            _txtPaid.Text = "0.00"
            _updatingPaid = False
        End If
        UpdatePaymentLabels()
    End Sub

    Private Sub UpdatePaymentLabels()
        Dim paid As Decimal = UiTheme.ToMoney(_txtPaid.Text)
        _lblPaid.Text = CStr(_lblPaid.Tag) & Environment.NewLine & UiTheme.Money(paid)
        _lblDue.Text = CStr(_lblDue.Tag) & Environment.NewLine & UiTheme.Money(_editor.Totals.Total - paid)
    End Sub

    Private Sub ApplyHeaderDiscount()
        _editor.HeaderDiscount = UiTheme.ToMoney(_txtDiscount.Text)
    End Sub

    Private Sub PayFull()
        _updatingPaid = True
        _txtPaid.Text = _editor.Totals.Total.ToString("0.00")
        _updatingPaid = False
        _paidTouched = False
        UpdatePaymentLabels()
    End Sub

    Private Sub PostPurchase()
        If _isPosting Then Return
        If _editor.LineCount = 0 Then
            UiTheme.Warn("أضف صنفًا واحدًا على الأقل.")
            _editor.FocusSearch()
            Return
        End If

        Dim totals = _editor.Totals
        Dim paid As Decimal = UiTheme.ToMoney(_txtPaid.Text)
        If paid < 0D OrElse paid > totals.Total + 0.004D Then
            UiTheme.Warn("المدفوع أكبر من إجمالي الفاتورة.")
            Return
        End If
        If _editor.HeaderDiscount > totals.Subtotal - totals.LineDiscount Then
            UiTheme.Warn("خصم الفاتورة أكبر من قيمة البنود.")
            Return
        End If

        Dim supplier = TryCast(_cboSupplier.SelectedItem, CustomerSummary)
        Dim request As New InvoiceRequest With {
            .ClientEventId = _draftEventId,
            .CustomerId = If(supplier IsNot Nothing AndAlso supplier.Id > 0, CType(supplier.Id, Long?), Nothing),
            .BuyerName = If(supplier IsNot Nothing AndAlso supplier.Id > 0, supplier.Name, Nothing),
            .PaymentMethodId = If(TryCast(_cboPayment.SelectedItem, LookupItem) Is Nothing, 0L,
                                  DirectCast(_cboPayment.SelectedItem, LookupItem).Id),
            .Paid = paid,
            .HeaderDiscountAmount = _editor.HeaderDiscount,
            .Notes = _txtNotes.Text.Trim(),
            .BusinessDate = _dtDate.Value.Date,
            .Lines = _editor.BuildLineRequests()
        }
        _isPosting = True
        Try
            Cursor = Cursors.WaitCursor
            Dim shift = ShiftService.Current(_session)
            If shift IsNot Nothing Then request.ShiftId = shift.Id

            Dim result = PurchaseService.PostPurchase(_session, request)
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
           Not UiTheme.Confirm("بدء فاتورة جديدة وإفراغ البنود الحالية؟") Then Return
        _editor.Clear()
        _draftEventId = Uuid7.NewId()
        _editor.HeaderDiscount = 0D
        _txtDiscount.Clear()
        _txtNotes.Clear()
        _updatingPaid = True
        _txtPaid.Text = "0.00"
        _updatingPaid = False
        _paidTouched = False
        _dtDate.Value = Date.Today
        EditorTotalsChanged(Me, EventArgs.Empty)
        RefreshShift()
        _editor.FocusSearch()
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _editor.FocusSearch()
    End Sub
End Class
