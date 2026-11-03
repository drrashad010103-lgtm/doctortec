Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>One item row on the barcode label screen.</summary>
Public Class BarcodeLabelRow
    Public Property Selected As Boolean
    Public Property ItemId As Long
    Public Property Code As String
    Public Property NameAr As String
    Public Property SalePrice As Decimal
    Public Property Barcode As String
    Public Property Symbology As String
End Class

''' <summary>
''' Barcode label printing: pick items, choose label size and copies, then
''' preview and print real EAN-13 / Code 128 labels via PrintDocument.
''' </summary>
Public Class BarcodePrintForm
    Inherits SectionFormBase

    Private Const MmToInch100 As Single = 100.0F / 25.4F

    Private ReadOnly _cboSize As New ComboBox()
    Private ReadOnly _numCopies As New NumericUpDown()
    Private ReadOnly _chkPrice As New CheckBox()
    Private ReadOnly _lblCurrent As New Label()
    Private _rows As New List(Of BarcodeLabelRow)()
    Private _labels As New List(Of BarcodeLabelRow)()
    Private _printIndex As Integer
    Private _labelWidthMm As Single = 50.0F
    Private _labelHeightMm As Single = 30.0F

    Public Sub New(session As AppSession)
        MyBase.New(session, "طباعة باركود",
                   "توليد ملصقات EAN-13 أو Code 128 للأصناف مع معاينة الطباعة وأحجام ملصقات جاهزة",
                   Color.FromArgb(142, 68, 173), New Size(1180, 650))

        AddSearch("اسم الصنف أو الكود أو الباركود", AddressOf Reload)

        _cboSize.Width = 145
        _cboSize.DropDownStyle = ComboBoxStyle.DropDownList
        _cboSize.Items.AddRange(New Object() {
            "50 × 30 مم", "40 × 25 مم", "58 × 40 مم", "70 × 40 مم"
        })
        _cboSize.SelectedIndex = 0
        ToolBar.Controls.Add(UiTheme.MakeLabel("حجم الملصق:"))
        ToolBar.Controls.Add(_cboSize)

        _numCopies.Width = 60
        _numCopies.Minimum = 1
        _numCopies.Maximum = 50
        _numCopies.Value = 1
        ToolBar.Controls.Add(UiTheme.MakeLabel("عدد النسخ:"))
        ToolBar.Controls.Add(_numCopies)

        _chkPrice.Text = "إظهار السعر"
        _chkPrice.Checked = True
        _chkPrice.Width = 105
        _chkPrice.TextAlign = ContentAlignment.MiddleRight
        ToolBar.Controls.Add(_chkPrice)

        Dim btnAll As Button = UiTheme.MakeButton("تحديد الكل", 110)
        AddHandler btnAll.Click, Sub(s, e) SetAll(True)
        ToolBar.Controls.Add(btnAll)

        Dim btnNone As Button = UiTheme.MakeButton("إلغاء التحديد", 120)
        AddHandler btnNone.Click, Sub(s, e) SetAll(False)
        ToolBar.Controls.Add(btnNone)

        Dim btnPrint As Button = UiTheme.PrimaryButton("معاينة وطباعة", 145)
        btnPrint.BackColor = Color.FromArgb(142, 68, 173)
        AddHandler btnPrint.Click, Sub(s, e) PreviewAndPrint()
        ToolBar.Controls.Add(btnPrint)

        _lblCurrent.AutoSize = True
        _lblCurrent.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        _lblCurrent.ForeColor = UiTheme.Muted
        _lblCurrent.Padding = New Padding(8, 8, 0, 0)
        ToolBar.Controls.Add(_lblCurrent)

        Dim checkColumn As New DataGridViewCheckBoxColumn With {
            .Name = "Selected", .DataPropertyName = "Selected", .HeaderText = "✓",
            .Width = 42, .ReadOnly = False,
            .SortMode = DataGridViewColumnSortMode.NotSortable
        }
        Grid.Columns.Add(checkColumn)
        UiTheme.AddTextColumn(Grid, "Code", "الكود", 105)
        UiTheme.AddTextColumn(Grid, "NameAr", "الصنف", 300, True)
        UiTheme.AddTextColumn(Grid, "SalePrice", "السعر", 95, False, "N2")
        UiTheme.AddTextColumn(Grid, "Barcode", "الباركود", 165)
        UiTheme.AddTextColumn(Grid, "Symbology", "النوع", 95)

        AddHandler Grid.CurrentCellDirtyStateChanged, Sub(s, e)
                                                          If Grid.IsCurrentCellDirty Then
                                                              Grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                                                          End If
                                                      End Sub
        AddHandler Grid.CellValueChanged, Sub(s, e)
                                               If e.ColumnIndex = 0 Then UpdateFooter()
                                           End Sub
        AddHandler Grid.SelectionChanged, Sub(s, e) UpdateCurrent()
        AddHandler Grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then PreviewAndPrint()

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        Dim items = ItemService.Search(Session, SearchText, False, 300)
        _rows = New List(Of BarcodeLabelRow)()
        For Each item In items
            Dim barcode As String = If(String.IsNullOrWhiteSpace(item.Barcode), item.Code, item.Barcode).Trim()
            _rows.Add(New BarcodeLabelRow With {
                .Selected = False, .ItemId = item.Id, .Code = item.Code, .NameAr = item.NameAr,
                .SalePrice = item.SalePrice, .Barcode = barcode,
                .Symbology = BarcodeRenderer.SymbologyName(barcode)
            })
        Next
        Grid.DataSource = New BindingSource With {.DataSource = _rows}
        UpdateFooter()
        UpdateCurrent()
    End Sub

    Private Sub UpdateFooter()
        If _rows Is Nothing Then Return
        Dim selected As Integer = _rows.Where(Function(r) r.Selected).Count()
        Dim ean As Integer = _rows.Where(Function(r) r.Symbology = "EAN-13").Count()
        Dim code128 As Integer = _rows.Where(Function(r) r.Symbology = "CODE128").Count()
        Dim unsupported As Integer = _rows.Where(Function(r) r.Symbology = "—").Count()
        SetFooter("الأصناف: " & _rows.Count.ToString() &
                  "   |   المحدد: " & selected.ToString() &
                  "   |   EAN-13: " & ean.ToString() &
                  "   |   Code 128: " & code128.ToString() &
                  "   |   بدون باركود صالح: " & unsupported.ToString())
    End Sub

    Private Sub UpdateCurrent()
        Dim row = SelectedItem(Of BarcodeLabelRow)()
        If row Is Nothing Then
            _lblCurrent.Text = String.Empty
            Return
        End If
        _lblCurrent.Text = row.Symbology & "  •  " & row.Barcode
        _lblCurrent.ForeColor = If(row.Symbology = "—", UiTheme.Danger, UiTheme.Success)
    End Sub

    Private Sub SetAll(value As Boolean)
        For Each row In _rows
            row.Selected = value
        Next
        Grid.Refresh()
        UpdateFooter()
    End Sub

    Private Sub PreviewAndPrint()
        Dim selected = _rows.Where(Function(r) r.Selected).ToList()
        If selected.Count = 0 Then
            UiTheme.Warn("حدد صنفًا واحدًا على الأقل لطباعة ملصقاته.")
            Return
        End If

        Dim invalid = selected.Where(Function(r) Not BarcodeRenderer.CanRender(r.Barcode)).ToList()
        If invalid.Count > 0 Then
            UiTheme.Warn("هذه الأصناف ليس لها باركود صالح للطباعة:" & Environment.NewLine &
                         String.Join(Environment.NewLine, invalid.Take(8).Select(Function(r) "• " & r.NameAr & " (" & r.Barcode & ")")))
            Return
        End If

        Select Case _cboSize.SelectedIndex
            Case 1 : _labelWidthMm = 40.0F : _labelHeightMm = 25.0F
            Case 2 : _labelWidthMm = 58.0F : _labelHeightMm = 40.0F
            Case 3 : _labelWidthMm = 70.0F : _labelHeightMm = 40.0F
            Case Else : _labelWidthMm = 50.0F : _labelHeightMm = 30.0F
        End Select

        Dim copies As Integer = CInt(_numCopies.Value)
        _labels = New List(Of BarcodeLabelRow)()
        For Each row In selected
            For i As Integer = 1 To copies
                _labels.Add(row)
            Next
        Next
        _printIndex = 0

        Using document As New PrintDocument()
            document.DocumentName = "ملصقات باركود"
            document.DefaultPageSettings.Margins = New Margins(
                CInt(8 * MmToInch100), CInt(8 * MmToInch100),
                CInt(8 * MmToInch100), CInt(8 * MmToInch100))
            AddHandler document.BeginPrint, Sub(s, e) _printIndex = 0
            AddHandler document.PrintPage, AddressOf PrintLabelsPage

            Using preview As New PrintPreviewDialog With {
                .Document = document,
                .UseAntiAlias = True,
                .WindowState = FormWindowState.Maximized,
                .Text = "معاينة ملصقات الباركود — " & _labels.Count.ToString() & " ملصق"
            }
                preview.ShowDialog(Me)
            End Using
        End Using
    End Sub

    Private Sub PrintLabelsPage(sender As Object, e As PrintPageEventArgs)
        Dim labelWidth As Single = _labelWidthMm * MmToInch100
        Dim labelHeight As Single = _labelHeightMm * MmToInch100
        Dim gap As Single = 2.0F * MmToInch100
        Dim area As Rectangle = e.MarginBounds

        Dim columns As Integer = Math.Max(1, CInt(Math.Floor((area.Width + gap) / (labelWidth + gap))))
        Dim rows As Integer = Math.Max(1, CInt(Math.Floor((area.Height + gap) / (labelHeight + gap))))

        For rowIndex As Integer = 0 To rows - 1
            For columnIndex As Integer = 0 To columns - 1
                If _printIndex >= _labels.Count Then Exit For
                Dim x As Single = area.Left + columnIndex * (labelWidth + gap)
                Dim y As Single = area.Top + rowIndex * (labelHeight + gap)
                DrawLabel(e.Graphics, New RectangleF(x, y, labelWidth, labelHeight), _labels(_printIndex))
                _printIndex += 1
            Next
            If _printIndex >= _labels.Count Then Exit For
        Next

        e.HasMorePages = _printIndex < _labels.Count
    End Sub

    Private Sub DrawLabel(g As Graphics, bounds As RectangleF, row As BarcodeLabelRow)
        Dim border As New Pen(Color.FromArgb(190, 190, 190), 1.0F)
        Dim padding As Single = 2.0F * MmToInch100
        Dim priceHeight As Single = If(_chkPrice.Checked, 4.5F * MmToInch100, 0F)
        Dim nameHeight As Single = Math.Min(bounds.Height * 0.22F, 5.0F * MmToInch100)
        Try
            g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height)
            Dim inner As New RectangleF(bounds.X + padding, bounds.Y + padding,
                                        bounds.Width - padding * 2, bounds.Height - padding * 2)

            Using nameFont As New Font("Segoe UI", 7.0F, FontStyle.Bold, GraphicsUnit.Point)
                Dim nameRect As New RectangleF(inner.X, inner.Y, inner.Width, nameHeight)
                Dim format As New StringFormat With {
                    .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center,
                    .Trimming = StringTrimming.EllipsisCharacter, .FormatFlags = StringFormatFlags.NoWrap
                }
                g.DrawString(row.NameAr, nameFont, Brushes.Black, nameRect, format)
                format.Dispose()
            End Using

            Dim barcodeTop As Single = inner.Y + nameHeight
            Dim barcodeHeight As Single = inner.Bottom - priceHeight - barcodeTop
            If barcodeHeight > 8F Then
                BarcodeRenderer.Draw(g, New RectangleF(inner.X, barcodeTop, inner.Width, barcodeHeight), row.Barcode, True)
            End If

            If _chkPrice.Checked Then
                Using priceFont As New Font("Segoe UI", 8.0F, FontStyle.Bold, GraphicsUnit.Point)
                    Dim priceRect As New RectangleF(inner.X, inner.Bottom - priceHeight, inner.Width, priceHeight)
                    Dim format As New StringFormat With {
                        .Alignment = StringAlignment.Far, .LineAlignment = StringAlignment.Center
                    }
                    g.DrawString(UiTheme.Money(row.SalePrice) & " ج.م", priceFont, Brushes.Black, priceRect, format)
                    format.Dispose()
                End Using
            End If
        Finally
            border.Dispose()
        End Try
    End Sub
End Class
