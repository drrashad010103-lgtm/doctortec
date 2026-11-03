Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>List of service quotations with workflow actions and invoice conversion.</summary>
Public Class QuotesForm
    Inherits SectionFormBase

    Private ReadOnly _statusCombo As New ComboBox()
    Private _rows As List(Of QuoteSummary) = New List(Of QuoteSummary)()

    Public Sub New(session As AppSession)
        MyBase.New(session, "عرض سعر خدمات",
                   "عروض أسعار للخدمات والأصناف مع حالات الإرسال والقبول والتحويل إلى فاتورة بيع",
                   Color.FromArgb(230, 126, 34), New Size(1180, 650))

        AddSearch("رقم العرض أو اسم العميل", AddressOf Reload)

        Dim statuses As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("", "كل الحالات"),
            New KeyValuePair(Of String, String)("draft", "مسودة"),
            New KeyValuePair(Of String, String)("sent", "مرسل"),
            New KeyValuePair(Of String, String)("accepted", "مقبول"),
            New KeyValuePair(Of String, String)("rejected", "مرفوض"),
            New KeyValuePair(Of String, String)("converted", "محوّل لفاتورة"),
            New KeyValuePair(Of String, String)("cancelled", "ملغي")
        }
        _statusCombo.Width = 150
        _statusCombo.DropDownStyle = ComboBoxStyle.DropDownList
        _statusCombo.DataSource = statuses
        _statusCombo.DisplayMember = "Value"
        _statusCombo.ValueMember = "Key"
        AddHandler _statusCombo.SelectedIndexChanged, Sub(s, e) Reload()
        ToolBar.Controls.Add(UiTheme.MakeLabel("الحالة:"))
        ToolBar.Controls.Add(_statusCombo)

        Dim btnNew As Button = UiTheme.PrimaryButton("عرض سعر جديد", 150)
        AddHandler btnNew.Click, Sub(s, e) NewQuote()
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 85)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnSend As Button = UiTheme.MakeButton("إرسال", 85)
        AddHandler btnSend.Click, Sub(s, e) ChangeStatus("sent", "إرسال عرض السعر للعميل؟")
        ToolBar.Controls.Add(btnSend)

        Dim btnAccept As Button = UiTheme.MakeButton("قبول", 85)
        AddHandler btnAccept.Click, Sub(s, e) ChangeStatus("accepted", "تأكيد قبول العميل لعرض السعر؟")
        ToolBar.Controls.Add(btnAccept)

        Dim btnReject As Button = UiTheme.MakeButton("رفض", 85)
        AddHandler btnReject.Click, Sub(s, e) ChangeStatus("rejected", "تأكيد رفض عرض السعر؟")
        ToolBar.Controls.Add(btnReject)

        Dim btnConvert As Button = UiTheme.PrimaryButton("تحويل لفاتورة", 130)
        AddHandler btnConvert.Click, Sub(s, e) ConvertSelected()
        ToolBar.Controls.Add(btnConvert)

        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 85)
        AddHandler btnCancel.Click, Sub(s, e) ChangeStatus("cancelled", "إلغاء عرض السعر؟")
        ToolBar.Controls.Add(btnCancel)

        Dim btnDelete As Button = UiTheme.MakeButton("حذف المسودة", 120)
        AddHandler btnDelete.Click, Sub(s, e) DeleteSelected()
        ToolBar.Controls.Add(btnDelete)

        AddRefresh(AddressOf Reload)

        UiTheme.AddTextColumn(Grid, "QuoteNo", "رقم العرض", 165)
        UiTheme.AddTextColumn(Grid, "CustomerName", "العميل", 220, True)
        UiTheme.AddTextColumn(Grid, "CreatedAt", "التاريخ", 105, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "ValidUntil", "صالح حتى", 105, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "Total", "الإجمالي", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "StatusName", "الحالة", 120)
        UiTheme.AddTextColumn(Grid, "ConvertedInvoiceNo", "الفاتورة", 150)
        UiTheme.AddTextColumn(Grid, "CreatedByName", "أنشأه", 150)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then EditSelected()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        Dim status As String = Convert.ToString(_statusCombo.SelectedValue)
        _rows = QuoteService.List(Session, status, SearchText)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}
        Dim converted As Integer = _rows.Where(Function(r) r.Status = "converted").Count()
        SetFooter("العروض: " & _rows.Count.ToString() &
                  "   |   المحوّلة: " & converted.ToString() &
                  "   |   إجمالي القيم: " & UiTheme.Money(_rows.Sum(Function(r) r.Total)))
    End Sub

    Private Function SelectedQuote() As QuoteSummary
        Return SelectedItem(Of QuoteSummary)()
    End Function

    Private Sub NewQuote()
        Using dialog As New QuoteEditForm(Session, Nothing)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub EditSelected()
        Dim row = SelectedQuote()
        If row Is Nothing Then
            UiTheme.Warn("اختر عرض سعر أولًا.")
            Return
        End If
        If row.Status <> "draft" Then
            UiTheme.Warn("لا يمكن تعديل إلا عرض سعر في حالة مسودة.")
            Return
        End If
        Using dialog As New QuoteEditForm(Session, row.Id)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub

    Private Sub ChangeStatus(newStatus As String, question As String)
        Dim row = SelectedQuote()
        If row Is Nothing Then
            UiTheme.Warn("اختر عرض سعر أولًا.")
            Return
        End If
        If Not UiTheme.Confirm(question) Then Return
        Try
            QuoteService.SetStatus(Session, row.Id, newStatus)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ConvertSelected()
        Dim row = SelectedQuote()
        If row Is Nothing Then
            UiTheme.Warn("اختر عرض سعر أولًا.")
            Return
        End If
        If row.Status = "converted" Then
            UiTheme.Warn("تم تحويل هذا العرض إلى فاتورة بالفعل.")
            Return
        End If
        If Not UiTheme.Confirm("تحويل العرض " & row.QuoteNo & " إلى فاتورة بيع مرحّلة؟") Then Return

        Dim methodId As Long = 0
        Using dialog As New QuotePaymentForm(Session, row.Total)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            methodId = dialog.PaymentMethodId
        End Using

        Try
            Cursor = Cursors.WaitCursor
            Dim result = QuoteService.ConvertToSale(Session, row.Id, methodId)
            Cursor = Cursors.Default
            Using dialog As New InvoiceResultForm(Session, result)
                dialog.ShowDialog(Me)
            End Using
            Reload()
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub DeleteSelected()
        Dim row = SelectedQuote()
        If row Is Nothing Then
            UiTheme.Warn("اختر عرض سعر أولًا.")
            Return
        End If
        If Not UiTheme.Confirm("حذف عرض السعر " & row.QuoteNo & " نهائيًا؟") Then Return
        Try
            QuoteService.Delete(Session, row.Id)
            Reload()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

''' <summary>Small dialog that asks for the payment method before converting a quote.</summary>
Public Class QuotePaymentForm
    Inherits Form

    Private ReadOnly _combo As New ComboBox()
    Private _methodId As Long

    Public ReadOnly Property PaymentMethodId As Long
        Get
            Return _methodId
        End Get
    End Property

    Public Sub New(session As AppSession, total As Decimal)
        UiTheme.Apply(Me)
        Text = "تحويل عرض السعر"
        ClientSize = New Size(420, 210)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim title As New Label With {
            .Text = "سيتم ترحيل فاتورة بيع بقيمة " & UiTheme.Money(total),
            .Font = UiTheme.Font(11.0F, FontStyle.Bold),
            .Dock = DockStyle.Top, .Height = 52, .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = UiTheme.Navy, .ForeColor = Color.White
        }
        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(20)}
        Dim caption As New Label With {
            .Text = "طريقة الدفع:", .AutoSize = True, .Location = New Point(20, 24),
            .Font = UiTheme.Font(10.0F, FontStyle.Bold)
        }
        _combo.Location = New Point(20, 52)
        _combo.Width = 360
        _combo.DropDownStyle = ComboBoxStyle.DropDownList
        Dim methods = UiLookup.PaymentMethods(session)
        _combo.DataSource = methods
        _combo.DisplayMember = "Name"
        For i As Integer = 0 To methods.Count - 1
            If methods(i).Name.IndexOf("كاش", StringComparison.OrdinalIgnoreCase) >= 0 Then
                _combo.SelectedIndex = i
                Exit For
            End If
        Next

        Dim ok As Button = UiTheme.PrimaryButton("تحويل", 120)
        ok.Location = New Point(260, 108)
        AddHandler ok.Click, Sub(s, e)
                                 Dim item = TryCast(_combo.SelectedItem, LookupItem)
                                 If item Is Nothing Then
                                     UiTheme.Warn("اختر طريقة الدفع.")
                                     Return
                                 End If
                                 _methodId = item.Id
                                 DialogResult = DialogResult.OK
                             End Sub
        Dim cancel As Button = UiTheme.MakeButton("إلغاء", 100)
        cancel.Location = New Point(150, 108)
        AddHandler cancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        body.Controls.Add(caption)
        body.Controls.Add(_combo)
        body.Controls.Add(ok)
        body.Controls.Add(cancel)
        Controls.Add(body)
        Controls.Add(title)
        AcceptButton = ok
        CancelButton = cancel
    End Sub
End Class

''' <summary>Create or edit a draft quotation.</summary>
Public Class QuoteEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _quoteId As Long?
    Private ReadOnly _editor As InvoiceEditor
    Private ReadOnly _cboCustomer As New ComboBox()
    Private ReadOnly _dtValid As New DateTimePicker()
    Private ReadOnly _txtDiscount As New TextBox()
    Private ReadOnly _txtNotes As New TextBox()
    Private ReadOnly _lblTotal As New Label()
    Private ReadOnly _lblTax As New Label()
    Private ReadOnly _lblDiscount As New Label()
    Private _detail As QuoteDetail
    Private _customers As List(Of CustomerSummary)

    Public Sub New(session As AppSession, quoteId As Long?)
        _session = session
        _quoteId = quoteId
        _editor = New InvoiceEditor(session, "sale")
        UiTheme.Apply(Me)
        Text = If(quoteId.HasValue, "تعديل عرض سعر", "عرض سعر جديد")
        ClientSize = New Size(1200, 720)
        MinimumSize = New Size(1000, 620)
        BuildUi()
        LoadCustomers()
        If quoteId.HasValue Then LoadQuote()
        AddHandler _editor.TotalsChanged, AddressOf TotalsChanged
        TotalsChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub BuildUi()
        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .BackColor = UiTheme.Surface
        }
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 66))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 82))
        root.Controls.Add(BuildHeader(), 0, 0)
        root.Controls.Add(BuildBody(), 0, 1)
        root.Controls.Add(BuildFooter(), 0, 2)
        Controls.Add(root)
    End Sub

    Private Function BuildHeader() As Panel
        Dim header As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.FromArgb(230, 126, 34),
            .Padding = New Padding(18, 8, 18, 8)
        }
        Dim title As New Label With {
            .Text = "📋 عرض سعر خدمات", .Font = UiTheme.Font(15.0F, FontStyle.Bold),
            .ForeColor = Color.White, .Dock = DockStyle.Right, .Width = 320,
            .TextAlign = ContentAlignment.MiddleRight
        }
        Dim subtitle As New Label With {
            .Text = "حدد العميل والبنود، ثم احفظ كمسودة أو أرسله للعميل مباشرة",
            .Font = UiTheme.Font(9.5F), .ForeColor = Color.FromArgb(255, 245, 232),
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Padding = New Padding(0, 8, 12, 0)
        }
        header.Controls.Add(subtitle)
        header.Controls.Add(title)
        Return header
    End Function

    Private Function BuildBody() As Control
        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Padding = New Padding(8)
        }
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 330))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        body.Controls.Add(BuildSide(), 0, 0)
        body.Controls.Add(_editor, 1, 0)
        Return body
    End Function

    Private Function BuildSide() As Control
        Dim card As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.White, .Padding = New Padding(12),
            .Margin = New Padding(0, 0, 8, 0)
        }
        Dim form As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 0, .AutoScroll = True
        }
        form.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        _cboCustomer.Width = 290
        _cboCustomer.DropDownStyle = ComboBoxStyle.DropDownList
        _cboCustomer.DisplayMember = "Name"
        AddSide(form, "العميل", _cboCustomer)
        Dim btnCustomer As Button = UiTheme.MakeButton("إضافة عميل جديد", 290)
        AddHandler btnCustomer.Click, Sub(s, e) AddCustomer()
        form.Controls.Add(btnCustomer)

        _dtValid.Width = 290
        _dtValid.Format = DateTimePickerFormat.Custom
        _dtValid.CustomFormat = "yyyy-MM-dd"
        _dtValid.ShowCheckBox = True
        _dtValid.Checked = False
        AddSide(form, "صالح حتى (اختياري)", _dtValid)

        _txtDiscount.Width = 290
        _txtDiscount.TextAlign = HorizontalAlignment.Left
        AddHandler _txtDiscount.Leave, Sub(s, e) _editor.HeaderDiscount = UiTheme.ToMoney(_txtDiscount.Text)
        AddHandler _txtDiscount.KeyDown, Sub(s, e)
                                              If e.KeyCode = Keys.Enter Then
                                                  e.SuppressKeyPress = True
                                                  _editor.HeaderDiscount = UiTheme.ToMoney(_txtDiscount.Text)
                                              End If
                                          End Sub
        AddSide(form, "خصم العرض", _txtDiscount)

        _txtNotes.Multiline = True
        _txtNotes.Height = 100
        _txtNotes.ScrollBars = ScrollBars.Vertical
        _txtNotes.Width = 290
        AddSide(form, "ملاحظات", _txtNotes)

        Dim spacer As New Panel With {.Height = 8}
        form.Controls.Add(spacer)
        card.Controls.Add(form)
        Return card
    End Function

    Private Shared Sub AddSide(form As TableLayoutPanel, caption As String, editor As Control)
        Dim label As New Label With {
            .Text = caption, .AutoSize = False, .Height = 26, .Dock = DockStyle.Top,
            .TextAlign = ContentAlignment.MiddleRight, .Font = UiTheme.Font(9.5F, FontStyle.Bold),
            .Margin = New Padding(0, 8, 0, 0)
        }
        form.Controls.Add(label)
        form.Controls.Add(editor)
    End Sub

    Private Function BuildFooter() As Control
        Dim bar As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Padding = New Padding(12, 10, 12, 10), .BackColor = Color.White
        }
        _lblTotal.Width = 240
        _lblTotal.Height = 58
        _lblTotal.TextAlign = ContentAlignment.MiddleCenter
        _lblTotal.Font = UiTheme.Font(12.0F, FontStyle.Bold)
        _lblTotal.BackColor = Color.FromArgb(255, 248, 225)
        _lblTotal.BorderStyle = BorderStyle.FixedSingle
        bar.Controls.Add(_lblTotal)

        _lblTax.Width = 190
        _lblTax.Height = 58
        _lblTax.TextAlign = ContentAlignment.MiddleCenter
        _lblTax.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        _lblTax.ForeColor = UiTheme.Muted
        bar.Controls.Add(_lblTax)

        _lblDiscount.Width = 190
        _lblDiscount.Height = 58
        _lblDiscount.TextAlign = ContentAlignment.MiddleCenter
        _lblDiscount.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        _lblDiscount.ForeColor = UiTheme.Muted
        bar.Controls.Add(_lblDiscount)

        Dim btnSave As Button = UiTheme.MakeButton("حفظ كمسودة", 150)
        btnSave.Height = 46
        AddHandler btnSave.Click, Sub(s, e) SaveQuote(False)
        bar.Controls.Add(btnSave)

        Dim btnSend As Button = UiTheme.PrimaryButton("حفظ وإرسال", 160)
        btnSend.Height = 46
        btnSend.BackColor = Color.FromArgb(230, 126, 34)
        AddHandler btnSend.Click, Sub(s, e) SaveQuote(True)
        bar.Controls.Add(btnSend)

        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 100)
        btnCancel.Height = 46
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        bar.Controls.Add(btnCancel)
        Return bar
    End Function

    Private Sub LoadCustomers()
        Try
            _customers = CustomerService.Search(_session, String.Empty, "customer")
            Dim rows As New List(Of CustomerSummary) From {
                New CustomerSummary With {.Id = 0, .Name = "عميل نقدي"}
            }
            rows.AddRange(_customers)
            _cboCustomer.DataSource = rows
            _cboCustomer.SelectedIndex = 0
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub AddCustomer()
        Using dialog As New CustomersForm(_session, "customer")
            dialog.ShowDialog(Me)
        End Using
        LoadCustomers()
    End Sub

    Private Sub LoadQuote()
        Try
            _detail = QuoteService.Get(_session, _quoteId.Value)
            If _detail.Status <> "draft" Then
                UiTheme.Warn("لا يمكن تعديل إلا عرض سعر في حالة مسودة.")
                BeginInvoke(New Action(Sub() Close()))
                Return
            End If
            Text = "تعديل عرض سعر " & _detail.QuoteNo
            If _detail.CustomerId.HasValue Then
                For i As Integer = 0 To _cboCustomer.Items.Count - 1
                    Dim customer = TryCast(_cboCustomer.Items(i), CustomerSummary)
                    If customer IsNot Nothing AndAlso customer.Id = _detail.CustomerId.Value Then
                        _cboCustomer.SelectedIndex = i
                        Exit For
                    End If
                Next
            End If
            If _detail.ValidUntil.HasValue Then
                _dtValid.Value = _detail.ValidUntil.Value
                _dtValid.Checked = True
            End If
            _txtDiscount.Text = _detail.HeaderDiscount.ToString("0.00")
            _editor.HeaderDiscount = _detail.HeaderDiscount
            _txtNotes.Text = _detail.Notes

            Dim lines As New List(Of InvoiceEditorLine)()
            For Each line In _detail.Lines
                lines.Add(New InvoiceEditorLine With {
                    .ItemId = line.ItemId, .Code = line.ItemCode, .ItemName = line.ItemName,
                    .Qty = line.Qty, .UnitPrice = line.UnitPrice, .Discount = line.Discount,
                    .TaxCategoryId = line.TaxCategoryId, .TaxRate = line.TaxRate,
                    .DisplayTotal = line.LineTotal,
                    .TaxText = line.TaxRate.ToString("0.##") & "% — " & UiTheme.Money(line.TaxAmount)
                })
            Next
            _editor.LoadLines(lines)
        Catch ex As Exception
            UiTheme.Handle(ex)
            BeginInvoke(New Action(Sub() Close()))
        End Try
    End Sub

    Private Sub TotalsChanged(sender As Object, e As EventArgs)
        Dim totals = _editor.Totals
        _lblTotal.Text = "إجمالي العرض" & Environment.NewLine & UiTheme.Money(totals.Total)
        _lblTax.Text = "الضريبة" & Environment.NewLine & UiTheme.Money(totals.TaxTotal)
        _lblDiscount.Text = "الخصومات" & Environment.NewLine &
                           UiTheme.Money(totals.LineDiscount + totals.HeaderDiscount)
    End Sub

    Private Sub SaveQuote(sendNow As Boolean)
        If _editor.LineCount = 0 Then
            UiTheme.Warn("أضف بندًا واحدًا على الأقل لعرض السعر.")
            Return
        End If
        Dim totals = _editor.Totals
        If _editor.HeaderDiscount > totals.Subtotal - totals.LineDiscount Then
            UiTheme.Warn("خصم العرض أكبر من قيمة البنود.")
            Return
        End If

        Dim customer = TryCast(_cboCustomer.SelectedItem, CustomerSummary)
        Dim request As New QuoteRequest With {
            .CustomerId = If(customer IsNot Nothing AndAlso customer.Id > 0, CType(customer.Id, Long?), Nothing),
            .ValidUntil = If(_dtValid.Checked, CType(_dtValid.Value.Date, Date?), Nothing),
            .HeaderDiscountAmount = _editor.HeaderDiscount,
            .Notes = _txtNotes.Text.Trim(),
            .Lines = New List(Of QuoteLineRequest)()
        }
        For Each line In _editor.BuildLineRequests()
            request.Lines.Add(New QuoteLineRequest With {
                .ItemId = line.ItemId, .Qty = line.Qty, .UnitPrice = line.UnitPrice,
                .Discount = line.LineDiscount, .TaxCategoryId = line.TaxCategoryId
            })
        Next

        Try
            Dim savedId = QuoteService.Save(_session, _quoteId, request)
            If sendNow Then QuoteService.SetStatus(_session, savedId, "sent")
            UiTheme.Info(If(sendNow, "تم حفظ عرض السعر وإرساله.", "تم حفظ عرض السعر كمسودة."))
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
