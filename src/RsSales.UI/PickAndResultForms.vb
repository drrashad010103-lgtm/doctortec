Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Windows.Forms

Public Class ItemPickerForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly _items As New List(Of ItemSummary)()
    Private _selectedItem As ItemSummary

    Public ReadOnly Property SelectedItem As ItemSummary
        Get
            Return _selectedItem
        End Get
    End Property

    Public Sub New(session As AppSession, Optional term As String = Nothing)
        _session = session
        InitializeUi()
        txtSearch.Text = If(term, String.Empty)
        Search()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "اختيار صنف"
        ClientSize = New Size(880, 520)

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        txtSearch.Width = 380
        txtSearch.PlaceholderText = "اكتب اسم الصنف أو الكود أو الباركود"
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 90)
        AddHandler btnSearch.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnSearch)
        Dim btnChoose As Button = UiTheme.PrimaryButton("إضافة للسلة", 140)
        AddHandler btnChoose.Click, Sub(s, e) Choose()
        toolbar.Controls.Add(btnChoose)

        UiTheme.AddTextColumn(grid, "Code", "الكود", 110)
        UiTheme.AddTextColumn(grid, "NameAr", "الصنف", 300, True)
        UiTheme.AddTextColumn(grid, "SalePrice", "السعر", 90, False, "N2")
        UiTheme.AddTextColumn(grid, "QtyOnHand", "المتاح", 90, False, "N2")
        UiTheme.AddTextColumn(grid, "TaxRate", "ض%", 60, False, "N0")
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                             If e.RowIndex >= 0 Then Choose()
                                         End Sub
        AddHandler txtSearch.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.SuppressKeyPress = True
                                              Search()
                                          End If
                                      End Sub

        Controls.Add(grid)
        Controls.Add(toolbar)
        AcceptButton = btnSearch
    End Sub

    Private Sub Search()
        Try
            _items.Clear()
            _items.AddRange(ItemService.Search(_session, txtSearch.Text, False, 300))
            grid.DataSource = New BindingSource With {.DataSource = _items}
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub Choose()
        If grid.CurrentRow Is Nothing Then Return
        _selectedItem = TryCast(grid.CurrentRow.DataBoundItem, ItemSummary)
        If _selectedItem IsNot Nothing Then
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub
End Class

Public Class CustomerPickerForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _type As String
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private _selectedCustomerId As Long?
    Private _selectedCustomerName As String

    Public ReadOnly Property SelectedCustomerId As Long?
        Get
            Return _selectedCustomerId
        End Get
    End Property

    Public ReadOnly Property SelectedCustomerName As String
        Get
            Return _selectedCustomerName
        End Get
    End Property

    Public Sub New(session As AppSession, customerType As String)
        _session = session
        _type = customerType
        InitializeUi()
        Search()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_type = "supplier", "اختيار مورد", "اختيار عميل")
        ClientSize = New Size(760, 480)

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        txtSearch.Width = 350
        txtSearch.PlaceholderText = "الاسم أو الموبايل"
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 90)
        AddHandler btnSearch.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnSearch)
        Dim btnChoose As Button = UiTheme.PrimaryButton("اختيار", 110)
        AddHandler btnChoose.Click, Sub(s, e) Choose()
        toolbar.Controls.Add(btnChoose)

        UiTheme.AddTextColumn(grid, "Name", "الاسم", 260, True)
        UiTheme.AddTextColumn(grid, "Mobile", "الموبايل", 130)
        UiTheme.AddTextColumn(grid, "TaxNo", "الرقم الضريبي", 140)
        UiTheme.AddTextColumn(grid, "CreditLimit", "حد الائتمان", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "Balance", "الرصيد", 100, False, "N2")
        AddHandler grid.CellDoubleClick, Sub(s, e)
                                             If e.RowIndex >= 0 Then Choose()
                                         End Sub

        Controls.Add(grid)
        Controls.Add(toolbar)
    End Sub

    Private Sub Search()
        Try
            Dim rows = CustomerService.Search(_session, txtSearch.Text, _type)
            grid.DataSource = New BindingSource With {.DataSource = rows}
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub Choose()
        If grid.CurrentRow Is Nothing Then Return
        Dim row = TryCast(grid.CurrentRow.DataBoundItem, CustomerSummary)
        If row Is Nothing Then Return
        _selectedCustomerId = row.Id
        _selectedCustomerName = row.Name
        DialogResult = DialogResult.OK
        Close()
    End Sub
End Class

Public Class InvoiceResultForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _result As InvoiceResult
    Private ReadOnly picQr As New PictureBox()

    Public Sub New(session As AppSession, result As InvoiceResult)
        _session = session
        _result = result
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "تم ترحيل الفاتورة"
        ClientSize = New Size(520, 640)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim header As New Label With {
            .Text = "فاتورة رقم " & _result.InvoiceNo,
            .Dock = DockStyle.Top,
            .Height = 54,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Font = UiTheme.Font(16.0F, FontStyle.Bold),
            .BackColor = UiTheme.Navy,
            .ForeColor = Color.White
        }

        Dim details As New Label With {
            .Dock = DockStyle.Top,
            .Height = 150,
            .Padding = New Padding(20, 12, 20, 0),
            .Font = UiTheme.Font(11.0F),
            .Text = "الإجمالي قبل الضريبة: " & UiTheme.Money(_result.TaxBaseTotal) & Environment.NewLine &
                    "الضريبة: " & UiTheme.Money(_result.TaxTotal) & Environment.NewLine &
                    "الإجمالي: " & UiTheme.Money(_result.Total) & Environment.NewLine &
                    "المدفوع: " & UiTheme.Money(_result.Paid) & Environment.NewLine &
                    "المتبقي: " & UiTheme.Money(_result.Due)
        }

        picQr.SizeMode = PictureBoxSizeMode.Zoom
        picQr.Dock = DockStyle.Fill
        picQr.BackColor = Color.White
        Dim qrImage As System.Drawing.Image = QrHelper.CreateImage(_result.QrPayload, 8)
        If qrImage IsNot Nothing Then picQr.Image = qrImage

        Dim buttons As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 64,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(16)
        }
        Dim btnPrint As Button = UiTheme.PrimaryButton("طباعة الإيصال", 160)
        AddHandler btnPrint.Click, Sub(s, e) Print()
        Dim btnClose As Button = UiTheme.MakeButton("إغلاق", 110)
        AddHandler btnClose.Click, Sub(s, e) Close()
        buttons.Controls.Add(btnPrint)
        buttons.Controls.Add(btnClose)

        Controls.Add(picQr)
        Controls.Add(details)
        Controls.Add(header)
        Controls.Add(buttons)
    End Sub

    Private Sub Print()
        Try
            Using document As New PrintDocument()
                document.DocumentName = "Invoice " & _result.InvoiceNo
                AddHandler document.PrintPage, AddressOf PrintPage
                Using preview As New PrintPreviewDialog()
                    preview.Document = document
                    preview.Width = 800
                    preview.Height = 700
                    preview.ShowDialog(Me)
                End Using
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّرت الطباعة")
        End Try
    End Sub

    Private Sub PrintPage(sender As Object, e As PrintPageEventArgs)
        Dim g As Graphics = e.Graphics
        Dim x As Single = e.MarginBounds.Right - 20
        Dim y As Single = e.MarginBounds.Top
        Dim format As New StringFormat With {.Alignment = StringAlignment.Far, .FormatFlags = StringFormatFlags.DirectionRightToLeft}
        Dim font As Font = UiTheme.Font(11.0F)

        g.DrawString("فاتورة رقم " & _result.InvoiceNo, UiTheme.Font(13.0F, FontStyle.Bold), Brushes.Black,
                     New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 30), format)
        y += 40
        g.DrawString("التاريخ: " & Date.Now.ToString("yyyy-MM-dd HH:mm"), font, Brushes.Black,
                     New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 24), format)
        y += 30
        g.DrawString("الإجمالي: " & UiTheme.Money(_result.Total) & "    الضريبة: " & UiTheme.Money(_result.TaxTotal),
                     font, Brushes.Black, New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 24), format)
        y += 34

        For Each line In _result.Lines
            g.DrawString(line.ItemId.ToString() & " × " & line.Qty.ToString("0.##") & "  =  " & UiTheme.Money(line.LineTotal),
                         font, Brushes.Black, New RectangleF(e.MarginBounds.Left, y, e.MarginBounds.Width, 22), format)
            y += 22
            If y > e.MarginBounds.Bottom - 160 Then Exit For
        Next

        Dim qrPrint As System.Drawing.Image = QrHelper.CreateImage(_result.QrPayload, 6)
        If qrPrint IsNot Nothing Then
            g.DrawImage(qrPrint, New RectangleF(e.MarginBounds.Left + 40, y + 10, 120, 120))
        End If
        g.DrawString("شكرًا لتعاملكم معنا", font, Brushes.Gray,
                     New RectangleF(e.MarginBounds.Left, y + 150, e.MarginBounds.Width, 24), format)
        e.HasMorePages = False
    End Sub
End Class
