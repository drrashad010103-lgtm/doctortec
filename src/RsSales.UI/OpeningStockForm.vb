Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Public Class OpeningStockForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtScan As New TextBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly _lines As New BindingList(Of OpenLine)()
    Private _isPosting As Boolean

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        AddHandler Shown, Sub(s, e) txtScan.Focus()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — أرصدة مخزون أول المدة"
        ClientSize = New Size(940, 620)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        toolbar.Controls.Add(UiTheme.MakeLabel("كود / باركود الصنف:", True))
        txtScan.Width = 250
        AddHandler txtScan.KeyDown, AddressOf ScanKeyDown
        toolbar.Controls.Add(txtScan)
        Dim btnAdd As Button = UiTheme.MakeButton("إضافة", 90)
        AddHandler btnAdd.Click, AddressOf ShowPickerClick
        toolbar.Controls.Add(btnAdd)
        Dim btnDelete As Button = UiTheme.DangerButton("حذف السطر (F11)", 130)
        AddHandler btnDelete.Click, AddressOf DeleteSelectedLineClick
        toolbar.Controls.Add(btnDelete)
        Dim btnPost As Button = UiTheme.PrimaryButton("ترحيل المخزون الأول (F9)", 190)
        AddHandler btnPost.Click, AddressOf PostButtonClick
        toolbar.Controls.Add(btnPost)

        UiTheme.AddTextColumn(grid, "Code", "الكود", 110)
        UiTheme.AddTextColumn(grid, "NameAr", "الصنف", 280, True)
        Dim colQty = UiTheme.AddTextColumn(grid, "Qty", "الكمية", 110, False, "N2")
        colQty.ReadOnly = False
        Dim colCost = UiTheme.AddTextColumn(grid, "UnitCost", "التكلفة للوحدة", 130, False, "N2")
        colCost.ReadOnly = False

        Controls.Add(grid)
        Controls.Add(toolbar)

        AddHandler KeyDown, Sub(s, e)
                                If e.KeyCode = Keys.F11 Then DeleteSelectedLineClick(Me, EventArgs.Empty)
                                If e.KeyCode = Keys.F9 Then PostButtonClick(Me, EventArgs.Empty)
                            End Sub
    End Sub

    Private Sub ScanKeyDown(s As Object, e As KeyEventArgs)
        If e.KeyCode <> Keys.Enter Then Return
        e.SuppressKeyPress = True
        Dim term = txtScan.Text.Trim()
        If term = "" Then Return
        Try
            Dim item = ItemService.FindForSale(_session, term)
            If item Is Nothing Then UiTheme.Warn("الصنف غير موجود.") : Return
            AddItem(item)
            txtScan.Clear()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ShowPickerClick(sender As Object, e As EventArgs)
        Using p As New ItemPickerForm(_session, txtScan.Text)
            If p.ShowDialog(Me) = DialogResult.OK AndAlso p.SelectedItem IsNot Nothing Then
                AddItem(p.SelectedItem)
                txtScan.Clear()
            End If
        End Using
    End Sub

    Private Sub DeleteSelectedLineClick(sender As Object, e As EventArgs)
        If grid.CurrentRow Is Nothing Then Return
        Dim l = TryCast(grid.CurrentRow.DataBoundItem, OpenLine)
        If l IsNot Nothing Then _lines.Remove(l)
    End Sub

    Private Sub AddItem(item As ItemSummary)
        For Each l In _lines
            If l.ItemId = item.Id Then Return
        Next
        _lines.Add(New OpenLine With {.ItemId = item.Id, .Code = item.Code, .NameAr = item.NameAr, .Qty = 1D, .UnitCost = item.CostPrice})
        grid.DataSource = New BindingSource With {.DataSource = _lines}
    End Sub

    Private Sub PostButtonClick(sender As Object, e As EventArgs)
        If _isPosting Then Return
        If _lines.Count = 0 Then UiTheme.Warn("أضف أصنافًا أولاً.") : Return
        Dim req As New InvoiceRequest With {
            .ClientEventId = Uuid7.NewId(),
            .Paid = 0D,
            .PaymentMethodId = 1,
            .OccurredAt = Date.UtcNow,
            .BusinessDate = Date.Today,
            .Notes = "مخزون أول المدة"
        }
        For Each l In _lines
            If l.Qty <= 0 OrElse l.UnitCost < 0 Then UiTheme.Warn("تحقق من الكميات والتكاليف.") : Return
            req.Lines.Add(New InvoiceLineRequest With {.ItemId = l.ItemId, .Qty = l.Qty, .UnitPrice = l.UnitCost})
        Next
        If Not UiTheme.Confirm("ترحيل المخزون الأول؟ سيؤثر على الرصيد وتكلفة الوحدة الأخيرة.") Then Return
        _isPosting = True
        Try
            Cursor = Cursors.WaitCursor
            Dim res = PurchaseService.PostOpeningStock(_session, req)
            Cursor = Cursors.Default
            Using d As New InvoiceResultForm(_session, res)
                d.ShowDialog(Me)
            End Using
            Close()
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex, "تعذّر ترحيل المخزون الأول")
        Finally
            _isPosting = False
        End Try
    End Sub

    Private Class OpenLine
        Public Property ItemId As Long
        Public Property Code As String
        Public Property NameAr As String
        Public Property Qty As Decimal
        Public Property UnitCost As Decimal
    End Class
End Class
