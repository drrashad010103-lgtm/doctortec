Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Public Class TransferForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly cboTo As New ComboBox()
    Private ReadOnly txtNotes As New TextBox()
    Private ReadOnly txtScan As New TextBox()
    Private ReadOnly gridLines As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly gridList As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly tab As New TabControl()
    Private ReadOnly _lines As New BindingList(Of TrLine)()
    Private _transferId As Long

    Public Sub New(session As AppSession, Optional openListTab As Boolean = False)
        _session = session
        InitializeUi()
        LoadTransferData()
        If openListTab AndAlso tab.TabPages.Count > 1 Then
            tab.SelectedIndex = 1
        End If
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — تحويل المخزون بين الفروع"
        ClientSize = New Size(1020, 680)
        StartPosition = FormStartPosition.CenterParent
        KeyPreview = True
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        Dim p1 As New TabPage("تحويل جديد")
        Dim top As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 54, .BackColor = UiTheme.IceBlue, .Padding = New Padding(8)}
        top.Controls.Add(UiTheme.MakeLabel("إلى فرع:", True))
        cboTo.Width = 200
        cboTo.DropDownStyle = ComboBoxStyle.DropDownList
        top.Controls.Add(cboTo)
        top.Controls.Add(UiTheme.MakeLabel("ملاحظات:"))
        txtNotes.Width = 240
        top.Controls.Add(txtNotes)
        top.Controls.Add(UiTheme.MakeLabel("صنف:"))
        txtScan.Width = 180
        AddHandler txtScan.KeyDown, AddressOf Scan
        top.Controls.Add(txtScan)
        Dim btnAdd As Button = UiTheme.MakeButton("إضافة", 85)
        AddHandler btnAdd.Click, AddressOf AddFromPicker
        top.Controls.Add(btnAdd)
        Dim btnCreate As Button = UiTheme.PrimaryButton("إنشاء تحويل", 130)
        AddHandler btnCreate.Click, AddressOf CreateClick
        top.Controls.Add(btnCreate)
        Dim btnSend As Button = UiTheme.MakeButton("إرسال", 95)
        AddHandler btnSend.Click, AddressOf SendClick
        top.Controls.Add(btnSend)
        Dim btnRecv As Button = UiTheme.MakeButton("استلام", 95)
        AddHandler btnRecv.Click, AddressOf ReceiveClick
        top.Controls.Add(btnRecv)

        UiTheme.AddTextColumn(gridLines, "Code", "الكود", 110)
        UiTheme.AddTextColumn(gridLines, "NameAr", "الصنف", 280, True)
        UiTheme.AddTextColumn(gridLines, "Qty", "الكمية", 110, False, "N2")
        gridLines.Columns("Code").ReadOnly = True
        gridLines.Columns("NameAr").ReadOnly = True

        p1.Controls.Add(gridLines)
        p1.Controls.Add(top)

        Dim p2 As New TabPage("قائمة وسجل التحويلات")
        UiTheme.AddTextColumn(gridList, "TransferNo", "رقم التحويل", 150)
        UiTheme.AddTextColumn(gridList, "FromBranch", "من فرع", 100)
        UiTheme.AddTextColumn(gridList, "ToBranch", "إلى فرع", 100)
        UiTheme.AddTextColumn(gridList, "Status", "الحالة", 110)
        UiTheme.AddTextColumn(gridList, "CreatedAt", "تاريخ الإنشاء", 160)
        AddHandler gridList.CellClick, AddressOf SelectTransfer
        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 95)
        btnRefresh.Dock = DockStyle.Bottom
        AddHandler btnRefresh.Click, Sub(s, e) LoadList()
        p2.Controls.Add(gridList)
        p2.Controls.Add(btnRefresh)

        tab.Dock = DockStyle.Fill
        tab.Controls.Add(p1)
        tab.Controls.Add(p2)
        Controls.Add(tab)

        AddHandler KeyDown, Sub(s, e)
                                If e.KeyCode = Keys.F11 AndAlso gridLines.CurrentRow IsNot Nothing Then
                                    Dim row = TryCast(gridLines.CurrentRow.DataBoundItem, TrLine)
                                    If row IsNot Nothing Then _lines.Remove(row)
                                End If
                            End Sub
    End Sub

    Private Sub LoadTransferData()
        Try
            Dim branches = UiLookup.Branches(_session)
            For Each b In branches
                If b.Id <> _session.BranchId Then cboTo.Items.Add(New BranchItem With {.Id = b.Id, .Name = b.Name})
            Next
            cboTo.DisplayMember = "Name"
            cboTo.ValueMember = "Id"
            If cboTo.Items.Count > 0 Then cboTo.SelectedIndex = 0
            LoadList()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub LoadList()
        Try
            Dim rows = StockService.ListTransfers(_session, 200)
            gridList.DataSource = New BindingSource With {.DataSource = rows}
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub SelectTransfer(s As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        Dim r = TryCast(gridList.Rows(e.RowIndex).DataBoundItem, TransferRecord)
        If r IsNot Nothing Then _transferId = r.Id
    End Sub

    Private Sub Scan(s As Object, e As KeyEventArgs)
        If e.KeyCode <> Keys.Enter Then Return
        e.SuppressKeyPress = True
        Try
            Dim item = ItemService.FindForSale(_session, txtScan.Text.Trim())
            If item IsNot Nothing Then AddLine(item)
            txtScan.Clear()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub AddFromPicker(sender As Object, e As EventArgs)
        Using p As New ItemPickerForm(_session, txtScan.Text)
            If p.ShowDialog(Me) = DialogResult.OK AndAlso p.SelectedItem IsNot Nothing Then
                AddLine(p.SelectedItem)
                txtScan.Clear()
            End If
        End Using
    End Sub

    Private Sub AddLine(item As ItemSummary)
        For Each l In _lines
            If l.ItemId = item.Id Then
                l.Qty += 1D
                gridLines.Refresh()
                Return
            End If
        Next
        _lines.Add(New TrLine With {.ItemId = item.Id, .Code = item.Code, .NameAr = item.NameAr, .Qty = 1D})
        gridLines.DataSource = New BindingSource With {.DataSource = _lines}
    End Sub

    Private Sub CreateClick(sender As Object, e As EventArgs)
        If cboTo.SelectedItem Is Nothing OrElse _lines.Count = 0 Then
            UiTheme.Warn("اختر الفرع وأضف أصنافًا.")
            Return
        End If
        Dim bi = DirectCast(cboTo.SelectedItem, BranchItem)
        Dim list As New List(Of TransferLineInput)()
        For Each l In _lines
            If l.Qty <= 0 Then UiTheme.Warn("تحقق من الكميات.") : Return
            list.Add(New TransferLineInput With {.ItemId = l.ItemId, .Qty = l.Qty})
        Next
        Try
            _transferId = StockService.CreateTransfer(_session, bi.Id, txtNotes.Text.Trim(), list)
            UiTheme.Info("تم إنشاء التحويل رقم " & _transferId.ToString())
            _lines.Clear()
            gridLines.DataSource = Nothing
            LoadList()
            tab.SelectedIndex = 1
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub SendClick(sender As Object, e As EventArgs)
        If _transferId <= 0 Then UiTheme.Warn("اختر تحويلًا.") : Return
        If Not UiTheme.Confirm("إرسال التحويل؟ سيتم خصم الرصيد من الفرع الحالي.") Then Return
        Try
            StockService.SendTransfer(_session, _transferId)
            UiTheme.Info("تم إرسال التحويل.")
            LoadList()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub ReceiveClick(sender As Object, e As EventArgs)
        If _transferId <= 0 Then UiTheme.Warn("اختر تحويلًا.") : Return
        If Not UiTheme.Confirm("استلام التحويل؟ سيُضاف الرصيد لهذا الفرع.") Then Return
        Try
            StockService.ReceiveTransfer(_session, _transferId)
            UiTheme.Info("تم استلام التحويل.")
            LoadList()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Class BranchItem
        Public Property Id As Long
        Public Property Name As String
    End Class

    Private Class TrLine
        Public Property ItemId As Long
        Public Property Code As String
        Public Property NameAr As String
        Public Property Qty As Decimal
    End Class
End Class
