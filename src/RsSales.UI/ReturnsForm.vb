Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>
''' Sales or purchase returns. Shows posted invoices for the selected type and
''' opens a return editor that respects quantities already returned.
''' </summary>
Public Class ReturnsForm
    Inherits SectionFormBase

    Private ReadOnly _kind As String
    Private ReadOnly _fromDate As New DateTimePicker()
    Private ReadOnly _toDate As New DateTimePicker()
    Private _rows As List(Of InvoiceListRow) = New List(Of InvoiceListRow)()

    Public Sub New(session As AppSession, kind As String)
        MyBase.New(session, TitleFor(kind), SubtitleFor(kind), AccentFor(kind), New Size(1120, 650))
        _kind = If(kind = "purchase", "purchase", "sale")

        _fromDate.Format = DateTimePickerFormat.Custom
        _fromDate.CustomFormat = "yyyy-MM-dd"
        _fromDate.Value = Date.Today.AddDays(-30)
        _fromDate.Width = 120
        _toDate.Format = DateTimePickerFormat.Custom
        _toDate.CustomFormat = "yyyy-MM-dd"
        _toDate.Value = Date.Today
        _toDate.Width = 120

        ToolBar.Controls.Add(UiTheme.MakeLabel("من:"))
        ToolBar.Controls.Add(_fromDate)
        ToolBar.Controls.Add(UiTheme.MakeLabel("إلى:"))
        ToolBar.Controls.Add(_toDate)

        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 90)
        AddHandler btnRefresh.Click, Sub(s, e) Reload()
        ToolBar.Controls.Add(btnRefresh)

        Dim btnNew As Button = UiTheme.PrimaryButton("إنشاء مرتجع", 140)
        AddHandler btnNew.Click, Sub(s, e) OpenReturn()
        ToolBar.Controls.Add(btnNew)

        Dim btnShow As Button = UiTheme.MakeButton("عرض البنود", 110)
        AddHandler btnShow.Click, Sub(s, e) OpenReturn()
        ToolBar.Controls.Add(btnShow)

        UiTheme.AddTextColumn(Grid, "InvoiceNo", "رقم الفاتورة", 155)
        UiTheme.AddTextColumn(Grid, "BusinessDate", "التاريخ", 105, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "CustomerName", If(_kind = "purchase", "المورد", "العميل"), 240, True)
        UiTheme.AddTextColumn(Grid, "Total", "الإجمالي", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "Paid", "المدفوع", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "Due", "المتبقي", 105, False, "N2")
        UiTheme.AddTextColumn(Grid, "PaymentStatusName", "حالة السداد", 115)
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OpenReturn()

        Reload()
    End Sub

    Private Shared Function TitleFor(kind As String) As String
        Return If(kind = "purchase", "مرتجع مشتريات", "مرتجع مبيعات")
    End Function

    Private Shared Function SubtitleFor(kind As String) As String
        Return If(kind = "purchase",
                  "إرجاع أصناف إلى المورد مع خصم المخزون وتخفيض المستحق أو استلام نقدية",
                  "إرجاع أصناف من العميل مع إعادة المخزون ورد النقدية أو رصيد دائن")
    End Function

    Private Shared Function AccentFor(kind As String) As Color
        Return If(kind = "purchase", Color.FromArgb(41, 128, 185), Color.FromArgb(39, 174, 96))
    End Function

    Protected Overrides Sub LoadData()
        Dim fromDate As Date = _fromDate.Value.Date
        Dim toDate As Date = _toDate.Value.Date
        If toDate < fromDate Then
            Dim swap = fromDate
            fromDate = toDate
            toDate = swap
        End If

        _rows = SalesService.ListInvoices(Session, fromDate, toDate, _kind)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        Dim total As Decimal = _rows.Sum(Function(r) r.Total)
        Dim paid As Decimal = _rows.Sum(Function(r) r.Paid)
        Dim due As Decimal = _rows.Sum(Function(r) r.Due)
        SetFooter("الفواتير: " & _rows.Count.ToString() &
                  "   |   الإجمالي: " & UiTheme.Money(total) &
                  "   |   المدفوع: " & UiTheme.Money(paid) &
                  "   |   المتبقي: " & UiTheme.Money(due))
    End Sub

    Private Sub OpenReturn()
        Dim row = SelectedItem(Of InvoiceListRow)()
        If row Is Nothing Then
            UiTheme.Warn("اختر فاتورة من القائمة أولًا.")
            Return
        End If
        Using dialog As New ReturnEditForm(Session, _kind, row)
            If dialog.ShowDialog(Me) = DialogResult.OK Then Reload()
        End Using
    End Sub
End Class

Public Class ReturnLineEdit
    Public Property LineId As Long
    Public Property ItemId As Long
    Public Property ItemName As String
    Public Property OriginalQty As Decimal
    Public Property ReturnedQty As Decimal
    Public Property AvailableQty As Decimal
    Public Property ReturnQty As Decimal
    Public Property UnitPrice As Decimal
    Public Property TaxRate As Decimal
    Public Property DisplayTotal As Decimal
End Class

''' <summary>Return editor with returnable-quantity checks and live totals.</summary>
Public Class ReturnEditForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _kind As String
    Private ReadOnly _invoice As InvoiceListRow
    Private ReadOnly _rows As New BindingList(Of ReturnLineEdit)()
    Private ReadOnly _binding As New BindingSource()
    Private ReadOnly _grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly _lblTotal As New Label()
    Private ReadOnly _txtRefund As New TextBox()
    Private ReadOnly _cboPayment As New ComboBox()
    Private ReadOnly _txtNotes As New TextBox()
    Private ReadOnly _btnPost As Button = UiTheme.PrimaryButton("ترحيل المرتجع", 170)
    Private _refundTouched As Boolean
    Private _updatingRefund As Boolean
    Private _isPosting As Boolean
    Private _totals As New InvoiceTotals()

    Public Sub New(session As AppSession, kind As String, invoice As InvoiceListRow)
        _session = session
        _kind = If(kind = "purchase", "purchase", "sale")
        _invoice = invoice
        UiTheme.Apply(Me)
        Text = If(_kind = "purchase", "مرتجع مشتريات", "مرتجع مبيعات")
        ClientSize = New Size(980, 680)
        MinimumSize = New Size(860, 600)
        BuildUi()
        LoadPaymentMethods()
        LoadLines()
    End Sub

    Private ReadOnly Property IsPurchase As Boolean
        Get
            Return _kind = "purchase"
        End Get
    End Property

    Private Sub BuildUi()
        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = 72,
            .BackColor = If(IsPurchase, Color.FromArgb(41, 128, 185), Color.FromArgb(39, 174, 96)),
            .Padding = New Padding(18, 8, 18, 8)
        }
        Dim title As New Label With {
            .Text = If(IsPurchase, "↩️ مرتجع مشتريات — ", "↩️ مرتجع مبيعات — ") & _invoice.InvoiceNo,
            .Font = UiTheme.Font(14.0F, FontStyle.Bold), .ForeColor = Color.White,
            .Dock = DockStyle.Top, .Height = 34, .TextAlign = ContentAlignment.MiddleRight
        }
        Dim subtitle As New Label With {
            .Text = _invoice.CustomerName & "   |   " & _invoice.BusinessDate.ToString("yyyy-MM-dd") &
                    "   |   إجمالي الفاتورة: " & UiTheme.Money(_invoice.Total),
            .Font = UiTheme.Font(9.5F), .ForeColor = Color.FromArgb(235, 245, 250),
            .Dock = DockStyle.Top, .Height = 22, .TextAlign = ContentAlignment.MiddleRight
        }
        header.Controls.Add(subtitle)
        header.Controls.Add(title)

        Dim topBar As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 48, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Padding = New Padding(6), .BackColor = UiTheme.Surface
        }
        Dim btnAll As Button = UiTheme.MakeButton("إرجاع كل المتاح", 150)
        AddHandler btnAll.Click, Sub(s, e) ReturnAll()
        Dim btnZero As Button = UiTheme.MakeButton("تصفير الكميات", 130)
        AddHandler btnZero.Click, Sub(s, e) ZeroAll()
        topBar.Controls.Add(UiTheme.MakeLabel("الكمية القابلة للإرجاع محسوبة بعد المرتجعات السابقة."))
        topBar.Controls.Add(btnAll)
        topBar.Controls.Add(btnZero)

        _binding.DataSource = _rows
        _grid.DataSource = _binding
        _grid.EditMode = DataGridViewEditMode.EditOnEnter
        UiTheme.AddTextColumn(_grid, "ItemName", "الصنف", 270, True)
        UiTheme.AddTextColumn(_grid, "OriginalQty", "كمية الفاتورة", 105, False, "N2")
        UiTheme.AddTextColumn(_grid, "ReturnedQty", "مرتجع سابقًا", 105, False, "N2")
        UiTheme.AddTextColumn(_grid, "AvailableQty", "المتاح", 95, False, "N2")
        Dim returnColumn = UiTheme.AddTextColumn(_grid, "ReturnQty", "كمية الإرجاع", 110, False, "N2")
        returnColumn.ReadOnly = False
        UiTheme.AddTextColumn(_grid, "UnitPrice", "السعر", 95, False, "N2")
        UiTheme.AddTextColumn(_grid, "DisplayTotal", "إجمالي البند", 115, False, "N2")
        AddHandler _grid.CellEndEdit, AddressOf ReturnQtyEdited
        AddHandler _grid.DataError, Sub(s, e)
                                        e.ThrowException = False
                                        UiTheme.Warn("قيمة غير صالحة في كمية الإرجاع.")
                                    End Sub

        Dim footer As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom, .Height = 94, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .Padding = New Padding(10, 8, 10, 8), .BackColor = Color.White
        }
        _lblTotal.Width = 220
        _lblTotal.Height = 62
        _lblTotal.TextAlign = ContentAlignment.MiddleCenter
        _lblTotal.Font = UiTheme.Font(12.0F, FontStyle.Bold)
        _lblTotal.BackColor = Color.FromArgb(255, 248, 225)
        _lblTotal.BorderStyle = BorderStyle.FixedSingle
        footer.Controls.Add(_lblTotal)

        footer.Controls.Add(UiTheme.MakeLabel(If(IsPurchase, "المستلم نقدًا:", "المردود نقدًا:")))
        _txtRefund.Width = 110
        _txtRefund.TextAlign = HorizontalAlignment.Left
        AddHandler _txtRefund.TextChanged, Sub(s, e)
                                                If _updatingRefund Then Return
                                                _refundTouched = True
                                            End Sub
        footer.Controls.Add(_txtRefund)

        footer.Controls.Add(UiTheme.MakeLabel("طريقة الدفع:"))
        _cboPayment.Width = 140
        _cboPayment.DropDownStyle = ComboBoxStyle.DropDownList
        footer.Controls.Add(_cboPayment)

        footer.Controls.Add(UiTheme.MakeLabel("ملاحظات:"))
        _txtNotes.Width = 190
        footer.Controls.Add(_txtNotes)

        AddHandler _btnPost.Click, Sub(s, e) PostReturn()
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 90)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        footer.Controls.Add(_btnPost)
        footer.Controls.Add(btnCancel)

        Controls.Add(_grid)
        Controls.Add(footer)
        Controls.Add(topBar)
        Controls.Add(header)
    End Sub

    Private Sub LoadPaymentMethods()
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

    Private Sub LoadLines()
        Try
            Cursor = Cursors.WaitCursor
            Dim source = InvoiceQueryService.GetReturnableLines(_session, _invoice.Id,
                                                               If(IsPurchase, "purchase", "sale"))
            _rows.Clear()
            For Each line In source
                Dim available As Decimal = line.Qty - line.ReturnedQty
                If available <= 0D Then Continue For
                _rows.Add(New ReturnLineEdit With {
                    .LineId = line.LineId, .ItemId = line.ItemId, .ItemName = line.ItemName,
                    .OriginalQty = line.Qty, .ReturnedQty = line.ReturnedQty, .AvailableQty = available,
                    .ReturnQty = 0D, .UnitPrice = line.UnitPrice, .TaxRate = line.TaxRate
                })
            Next
            _binding.ResetBindings(False)
            Recalculate()
            If _rows.Count = 0 Then
                UiTheme.Warn("كل بنود هذه الفاتورة تم إرجاعها بالفعل.")
                _btnPost.Enabled = False
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
            Close()
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub ReturnAll()
        For Each row In _rows
            row.ReturnQty = row.AvailableQty
        Next
        Recalculate()
    End Sub

    Private Sub ZeroAll()
        For Each row In _rows
            row.ReturnQty = 0D
        Next
        Recalculate()
    End Sub

    Private Sub ReturnQtyEdited(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        Dim row = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, ReturnLineEdit)
        If row Is Nothing Then Return
        row.ReturnQty = Math.Max(0D, Math.Min(row.AvailableQty, Money.Round4(row.ReturnQty)))
        Recalculate()
    End Sub

    Private Sub Recalculate()
        Dim calcLines As New List(Of InvoiceLineInput)()
        For Each row In _rows
            If row.ReturnQty > 0D Then
                calcLines.Add(New InvoiceLineInput With {
                    .Qty = row.ReturnQty, .UnitPrice = row.UnitPrice,
                    .LineDiscount = 0D, .TaxRate = row.TaxRate
                })
            End If
        Next

        If calcLines.Count = 0 Then
            _totals = New InvoiceTotals()
        Else
            Try
                _totals = InvoiceCalculator.Calculate(calcLines, 0D, _session.PricesIncludeTax)
            Catch ex As ArgumentException
                UiTheme.Warn(ex.Message)
                _totals = New InvoiceTotals()
            End Try
        End If

        Dim index As Integer = 0
        For Each row In _rows
            If row.ReturnQty > 0D AndAlso index < _totals.Lines.Count Then
                row.DisplayTotal = _totals.Lines(index).LineTotal
                index += 1
            Else
                row.DisplayTotal = 0D
            End If
        Next
        _binding.ResetBindings(False)

        _lblTotal.Text = "إجمالي المرتجع" & Environment.NewLine & UiTheme.Money(_totals.Total)
        If Not _refundTouched Then
            _updatingRefund = True
            _txtRefund.Text = _totals.Total.ToString("0.00")
            _updatingRefund = False
        End If
    End Sub

    Private Sub PostReturn()
        If _isPosting Then Return
        Dim selected As New List(Of ReturnLineEdit)()
        For Each row In _rows
            If row.ReturnQty > 0D Then
                If row.ReturnQty > row.AvailableQty Then
                    UiTheme.Warn("كمية الإرجاع أكبر من المتاح للصنف «" & row.ItemName & "».")
                    Return
                End If
                selected.Add(row)
            End If
        Next
        If selected.Count = 0 Then
            UiTheme.Warn("حدد كمية إرجاع لصنف واحد على الأقل.")
            Return
        End If

        Dim refund As Decimal = UiTheme.ToMoney(_txtRefund.Text)
        If refund < 0D OrElse refund > _totals.Total + 0.004D Then
            UiTheme.Warn("المبلغ المدخل أكبر من قيمة المرتجع.")
            Return
        End If

        Dim request As New InvoiceRequest With {
            .PaymentMethodId = If(TryCast(_cboPayment.SelectedItem, LookupItem) Is Nothing, 0L,
                                  DirectCast(_cboPayment.SelectedItem, LookupItem).Id),
            .Paid = refund,
            .Notes = _txtNotes.Text.Trim(),
            .BusinessDate = Date.Today
        }
        For Each row In selected
            request.Lines.Add(New InvoiceLineRequest With {
                .ItemId = row.ItemId, .Qty = row.ReturnQty, .OriginalLineId = row.LineId
            })
        Next

        _isPosting = True
        Try
            Cursor = Cursors.WaitCursor
            Dim shift = ShiftService.Current(_session)
            If shift IsNot Nothing Then request.ShiftId = shift.Id

            Dim result As InvoiceResult
            If IsPurchase Then
                result = PurchaseService.PostPurchaseReturn(_session, _invoice.Id, request)
            Else
                result = SalesService.PostSaleReturn(_session, _invoice.Id, request)
            End If
            Cursor = Cursors.Default
            Using dialog As New InvoiceResultForm(_session, result)
                dialog.ShowDialog(Me)
            End Using
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex)
        Finally
            _isPosting = False
        End Try
    End Sub
End Class
