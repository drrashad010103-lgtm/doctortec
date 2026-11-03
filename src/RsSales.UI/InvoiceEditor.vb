Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>One editable line shown inside <see cref="InvoiceEditor"/>.</summary>
Public Class InvoiceEditorLine
    Public Property ItemId As Long
    Public Property Code As String
    Public Property ItemName As String
    Public Property InfoButtonText As String = "ℹ️ تفاصيل"
    Public Property Qty As Decimal
    Public Property UnitPrice As Decimal
    Public Property CostPrice As Decimal
    Public Property WholesalePrice As Decimal
    Public Property MinPrice As Decimal
    Public Property QtyOnHand As Decimal
    Public Property DiscountPct As Decimal
    Public Property Discount As Decimal
    Public Property TaxCategoryId As Long
    Public Property TaxRate As Decimal
    Public Property ExpiryDate As Date?
    Public Property BatchNo As String
    Public Property DisplayTotal As Decimal
    Public Property TaxText As String

    Public ReadOnly Property UnitProfit As Decimal
        Get
            If Qty <= 0D Then Return Money.Round2(UnitPrice - CostPrice)
            Dim netUnit As Decimal = UnitPrice - (Discount / Qty)
            Return Money.Round2(netUnit - CostPrice)
        End Get
    End Property

    Public ReadOnly Property LineProfit As Decimal
        Get
            Dim netLine As Decimal = Money.Round2(Qty * UnitPrice) - Discount
            Return Money.Round2(netLine - Money.Round2(Qty * CostPrice))
        End Get
    End Property

    Public ReadOnly Property ProfitMarginPct As Decimal
        Get
            Dim netLine As Decimal = Money.Round2(Qty * UnitPrice) - Discount
            If netLine <= 0D Then Return 0D
            Return Money.Round2((LineProfit / netLine) * 100D)
        End Get
    End Property
End Class

''' <summary>
''' Shared invoice cart used by sales, mobile sales, purchases and quotations:
''' tool-bar search, barcode entry, editable grid, inline item card button and live tax totals.
''' </summary>
Public Class InvoiceEditor
    Inherits UserControl

    Private ReadOnly _session As AppSession
    Private ReadOnly _mode As String
    Private ReadOnly _lines As New BindingList(Of InvoiceEditorLine)()
    Private ReadOnly _binding As New BindingSource()
    Private ReadOnly _grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly _toolbar As New FlowLayoutPanel()
    Private ReadOnly _search As New TextBox()
    Private ReadOnly _btnAdd As Button = UiTheme.PrimaryButton("إضافة", 90)
    Private ReadOnly _btnPick As Button = UiTheme.MakeButton("تصفح الأصناف", 120)
    Private ReadOnly _btnRemove As Button = UiTheme.MakeButton("حذف البند", 100)
    Private ReadOnly _btnClear As Button = UiTheme.MakeButton("تفريغ السلة", 110)
    Private _headerDiscount As Decimal
    Private _totals As New InvoiceTotals()
    Private _recalculating As Boolean

    Public Event TotalsChanged As EventHandler
    Public Event ItemInfoRequested As Action(Of InvoiceEditorLine)

    Public Sub New(session As AppSession, mode As String)
        _session = session
        _mode = If(String.Equals(mode, "purchase", StringComparison.OrdinalIgnoreCase), "purchase", "sale")
        Dock = DockStyle.Fill
        BackColor = Color.White
        BuildUi()
        Recalculate()
    End Sub

    Public ReadOnly Property Totals As InvoiceTotals
        Get
            Return _totals
        End Get
    End Property

    Public ReadOnly Property LineCount As Integer
        Get
            Return _lines.Where(Function(l) l.Qty > 0D).Count()
        End Get
    End Property

    Public ReadOnly Property GridControl As DataGridView
        Get
            Return _grid
        End Get
    End Property

    ''' <summary>Allows a host screen to supply its own search/actions bar.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ToolbarVisible As Boolean
        Get
            Return _toolbar.Visible
        End Get
        Set(value As Boolean)
            _toolbar.Visible = value
        End Set
    End Property

    ''' <summary>Snapshot of the current cart so a host screen can suspend/resume it.</summary>
    Public Function SnapshotLines() As List(Of InvoiceEditorLine)
        Return _lines.ToList()
    End Function

    ''' <summary>Header-level discount amount applied on top of the line discounts.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property HeaderDiscount As Decimal
        Get
            Return _headerDiscount
        End Get
        Set(value As Decimal)
            _headerDiscount = Math.Max(0D, Money.Round2(value))
            Recalculate()
        End Set
    End Property

    Private ReadOnly Property IsPurchase As Boolean
        Get
            Return _mode = "purchase"
        End Get
    End Property

    Private Sub BuildUi()
        With _toolbar
            .Dock = DockStyle.Top
            .Height = 48
            .FlowDirection = FlowDirection.LeftToRight
            .WrapContents = False
            .Padding = New Padding(4, 6, 4, 4)
            .BackColor = UiTheme.BrandLight
        End With

        _search.Width = 280
        _search.PlaceholderText = "باركود / كود / اسم الصنف ثم Enter"
        _toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        _toolbar.Controls.Add(_search)
        _toolbar.Controls.Add(_btnAdd)
        _toolbar.Controls.Add(_btnPick)
        _toolbar.Controls.Add(_btnRemove)
        _toolbar.Controls.Add(_btnClear)

        AddHandler _btnAdd.Click, Sub(s, e) AddFromSearch()
        AddHandler _btnPick.Click, Sub(s, e) PickItem()
        AddHandler _btnRemove.Click, Sub(s, e) RemoveSelected()
        AddHandler _btnClear.Click, Sub(s, e)
                                            If _lines.Count = 0 Then Return
                                            If UiTheme.Confirm("تفريغ كل بنود الفاتورة؟") Then Clear()
                                        End Sub
        AddHandler _search.KeyDown, Sub(s, e)
                                        If e.KeyCode = Keys.Enter Then
                                            e.SuppressKeyPress = True
                                            AddFromSearch()
                                        End If
                                    End Sub

        _binding.DataSource = _lines
        _grid.DataSource = _binding
        _grid.EditMode = DataGridViewEditMode.EditOnEnter
        _grid.AllowUserToOrderColumns = False

        UiTheme.AddTextColumn(_grid, "Code", "الكود", 85)
        UiTheme.AddTextColumn(_grid, "ItemName", "الصنف", 230, True)

        ' زر تفاصيل وتعليمات وإكسبير الصنف بجوار اسم الصنف مباشرة
        Dim infoBtnCol As New DataGridViewButtonColumn With {
            .Name = "ColInfoBtn",
            .DataPropertyName = "InfoButtonText",
            .HeaderText = "بطاقة الصنف",
            .Width = 92,
            .FlatStyle = FlatStyle.Popup,
            .UseColumnTextForButtonValue = False
        }
        infoBtnCol.DefaultCellStyle.BackColor = Color.FromArgb(232, 245, 233)
        infoBtnCol.DefaultCellStyle.ForeColor = Color.FromArgb(27, 94, 32)
        infoBtnCol.DefaultCellStyle.Font = New Font("Cairo", 8.5F, FontStyle.Bold)
        _grid.Columns.Add(infoBtnCol)

        Dim qtyColumn = UiTheme.AddTextColumn(_grid, "Qty", "الكمية", 75, False, "N2")
        qtyColumn.ReadOnly = False
        Dim priceColumn = UiTheme.AddTextColumn(_grid, "UnitPrice", If(IsPurchase, "سعر الشراء", "سعر البيع"), 95, False, "N2")
        priceColumn.ReadOnly = False
        Dim discountPctCol = UiTheme.AddTextColumn(_grid, "DiscountPct", "خصم %", 70, False, "N2")
        discountPctCol.ReadOnly = False
        Dim discountColumn = UiTheme.AddTextColumn(_grid, "Discount", "قيمة الخصم", 85, False, "N2")
        discountColumn.ReadOnly = False
        If IsPurchase Then
            Dim batchColumn = UiTheme.AddTextColumn(_grid, "BatchNo", "التشغيلة", 95)
            batchColumn.ReadOnly = False
            Dim expiryColumn = UiTheme.AddTextColumn(_grid, "ExpiryDate", "الصلاحية", 100, False, "yyyy-MM-dd")
            expiryColumn.ReadOnly = False
        End If
        UiTheme.AddTextColumn(_grid, "TaxText", "الضريبة", 80)
        UiTheme.AddTextColumn(_grid, "DisplayTotal", "الإجمالي", 105, False, "N2")

        AddHandler _grid.CellEndEdit, AddressOf CellEndEdit
        AddHandler _grid.KeyDown, Sub(s, e)
                                      If e.KeyCode = Keys.F11 OrElse (e.KeyCode = Keys.Delete AndAlso Not _grid.IsCurrentCellInEditMode) Then
                                          e.SuppressKeyPress = True
                                          RemoveSelected()
                                      End If
                                  End Sub
        AddHandler _grid.CellContentClick, AddressOf GridCellContentClick
        AddHandler _grid.DataError, Sub(s, e)
                                        e.ThrowException = False
                                        UiTheme.Warn("قيمة غير صالحة في هذا الحقل.")
                                        Recalculate()
                                    End Sub

        Controls.Add(_grid)
        Controls.Add(_toolbar)
    End Sub

    Private Sub GridCellContentClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        If _grid.Columns(e.ColumnIndex).Name = "ColInfoBtn" Then
            Dim line = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, InvoiceEditorLine)
            If line IsNot Nothing Then
                RaiseEvent ItemInfoRequested(line)
            End If
        End If
    End Sub

    Public Sub AddItem(item As ItemSummary, Optional qty As Decimal = 1D, Optional unitPrice As Decimal? = Nothing)
        If item Is Nothing Then Return
        Dim price As Decimal = If(unitPrice.HasValue, unitPrice.Value,
                                  If(IsPurchase, item.CostPrice, item.SalePrice))

        If Not IsPurchase Then
            Dim existing = _lines.FirstOrDefault(Function(l) l.ItemId = item.Id AndAlso l.UnitPrice = price)
            If existing IsNot Nothing Then
                existing.Qty += Math.Max(1D, qty)
                If existing.DiscountPct > 0D Then
                    Dim gross As Decimal = Money.Round2(existing.Qty * existing.UnitPrice)
                    existing.Discount = Money.Round2(gross * (existing.DiscountPct / 100D))
                End If
                Recalculate()
                Return
            End If
        End If

        Dim cost As Decimal = item.CostPrice
        Dim wholesale As Decimal = item.WholesalePrice
        Dim minP As Decimal = item.MinPrice
        If wholesale = 0D AndAlso minP = 0D Then
            Try
                Dim fullRec = ItemService.GetById(_session, item.Id)
                If fullRec IsNot Nothing Then
                    cost = fullRec.CostPrice
                    wholesale = fullRec.WholesalePrice
                    minP = fullRec.MinPrice
                End If
            Catch
            End Try
        End If

        Dim defaultBatch As String = Nothing
        Dim defaultExpiry As Date? = Nothing
        Try
            Dim details = ItemService.GetFullDetails(_session, item.Id)
            If details IsNot Nothing Then
                defaultBatch = details.DefaultBatchNo
                defaultExpiry = details.DefaultExpiryDate
                If cost <= 0D AndAlso details.LastCost > 0D Then cost = details.LastCost
                If wholesale <= 0D AndAlso details.Record IsNot Nothing Then wholesale = details.Record.WholesalePrice
                If minP <= 0D AndAlso details.Record IsNot Nothing Then minP = details.Record.MinPrice
            End If
        Catch
        End Try

        _lines.Add(New InvoiceEditorLine With {
            .ItemId = item.Id, .Code = item.Code, .ItemName = item.NameAr,
            .InfoButtonText = "ℹ️ تفاصيل",
            .Qty = Math.Max(1D, qty), .UnitPrice = price,
            .CostPrice = cost, .WholesalePrice = wholesale, .MinPrice = minP,
            .QtyOnHand = item.QtyOnHand,
            .DiscountPct = 0D, .Discount = 0D,
            .BatchNo = defaultBatch, .ExpiryDate = defaultExpiry,
            .TaxCategoryId = item.TaxCategoryId, .TaxRate = item.TaxRate,
            .TaxText = item.TaxRate.ToString("0.##") & "%"
        })
        Recalculate()
    End Sub

    Public Sub LoadLines(lines As IEnumerable(Of InvoiceEditorLine))
        _lines.Clear()
        For Each line In lines
            If String.IsNullOrWhiteSpace(line.InfoButtonText) Then line.InfoButtonText = "ℹ️ تفاصيل"
            _lines.Add(line)
        Next
        Recalculate()
    End Sub

    Public Sub Clear()
        _lines.Clear()
        _headerDiscount = 0D
        Recalculate()
    End Sub

    ''' <summary>Recalculates after a host screen edits a line directly.</summary>
    Public Sub RefreshTotals()
        Recalculate()
    End Sub

    Public Sub RemoveSelected()
        If _grid.CurrentRow Is Nothing Then Return
        Dim line = TryCast(_grid.CurrentRow.DataBoundItem, InvoiceEditorLine)
        If line IsNot Nothing Then
            _lines.Remove(line)
            Recalculate()
        End If
    End Sub

    Public Function BuildLineRequests() As List(Of InvoiceLineRequest)
        Dim result As New List(Of InvoiceLineRequest)()
        For Each line In _lines
            If line.Qty <= 0D Then Continue For
            result.Add(New InvoiceLineRequest With {
                .ItemId = line.ItemId, .Qty = line.Qty, .UnitPrice = line.UnitPrice,
                .LineDiscount = line.Discount, .TaxCategoryId = line.TaxCategoryId,
                .BatchNo = line.BatchNo,
                .ExpiryDate = line.ExpiryDate
            })
        Next
        Return result
    End Function

    Public Sub FocusSearch()
        _search.Focus()
        _search.SelectAll()
    End Sub

    Private Sub AddFromSearch()
        Dim term As String = _search.Text.Trim()
        If term.Length = 0 Then
            PickItem()
            Return
        End If
        Try
            Dim item = ItemService.FindForSale(_session, term)
            If item Is Nothing Then
                Using picker As New ItemPickerForm(_session, term)
                    If picker.ShowDialog(FindForm()) = DialogResult.OK Then AddItem(picker.SelectedItem)
                End Using
            Else
                If Not item.IsActive Then
                    UiTheme.Warn("الصنف «" & item.NameAr & "» غير مفعّل.")
                    Return
                End If
                AddItem(item)
            End If
            _search.Clear()
            FocusSearch()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub PickItem()
        Using picker As New ItemPickerForm(_session, _search.Text)
            If picker.ShowDialog(FindForm()) = DialogResult.OK Then
                AddItem(picker.SelectedItem)
                _search.Clear()
                FocusSearch()
            End If
        End Using
    End Sub

    Private Sub CellEndEdit(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        Dim line = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, InvoiceEditorLine)
        If line Is Nothing Then Return

        line.Qty = Math.Max(0D, Money.Round4(line.Qty))
        line.UnitPrice = Math.Max(0D, Money.Round4(line.UnitPrice))
        Dim gross As Decimal = Money.Round2(line.Qty * line.UnitPrice)

        Dim colName As String = If(e.ColumnIndex >= 0 AndAlso e.ColumnIndex < _grid.Columns.Count,
                                   _grid.Columns(e.ColumnIndex).DataPropertyName, String.Empty)

        If colName = "DiscountPct" Then
            line.DiscountPct = Math.Max(0D, Math.Min(100D, Money.Round2(line.DiscountPct)))
            line.Discount = Money.Round2(gross * (line.DiscountPct / 100D))
        Else
            line.Discount = Math.Max(0D, Money.Round2(line.Discount))
            If line.Discount > gross Then line.Discount = gross
            line.DiscountPct = If(gross > 0D, Money.Round2((line.Discount / gross) * 100D), 0D)
        End If

        Recalculate()
    End Sub

    Public Sub Recalculate()
        If _recalculating Then Return
        _recalculating = True
        Try
            Dim requestLines As New List(Of InvoiceLineInput)()
            For Each line In _lines
                Dim gross As Decimal = Money.Round2(line.Qty * line.UnitPrice)
                If line.Discount > gross Then line.Discount = gross
                line.DiscountPct = If(gross > 0D, Money.Round2((line.Discount / gross) * 100D), 0D)

                requestLines.Add(New InvoiceLineInput With {
                    .Qty = line.Qty, .UnitPrice = line.UnitPrice,
                    .LineDiscount = line.Discount, .TaxRate = line.TaxRate
                })
            Next

            If requestLines.Count = 0 Then
                _totals = New InvoiceTotals()
            Else
                Dim discount As Decimal = _headerDiscount
                Try
                    _totals = InvoiceCalculator.Calculate(requestLines, discount, _session.PricesIncludeTax)
                Catch ex As ArgumentException
                    Try
                        _totals = InvoiceCalculator.Calculate(requestLines, 0D, _session.PricesIncludeTax)
                    Catch
                        _totals = New InvoiceTotals()
                    End Try
                End Try
            End If

            If _totals.Lines.Count = _lines.Count Then
                For i As Integer = 0 To _lines.Count - 1
                    _lines(i).DisplayTotal = _totals.Lines(i).LineTotal
                    _lines(i).TaxText = _lines(i).TaxRate.ToString("0.##") & "% — " &
                                        UiTheme.Money(_totals.Lines(i).TaxAmount)
                Next
            End If
            _binding.ResetBindings(False)
        Finally
            _recalculating = False
        End Try
        RaiseEvent TotalsChanged(Me, EventArgs.Empty)
    End Sub
End Class
