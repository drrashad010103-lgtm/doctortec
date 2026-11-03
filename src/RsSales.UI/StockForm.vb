Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Public Class InventoryCountForm
    Inherits StockForm

    Public Sub New(session As AppSession)
        MyBase.New(session, True, "counts")
    End Sub
End Class

' <summary>
' DOCTOR TECH — إدارة المخزون، الجرد الفعلي بالسكانر، وتنبؤ المخزون الذكي وغرفة الطلبية (Smart Forecast & Order Room)
' </summary>
Public Class StockForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtSearch As New TextBox()
    Private ReadOnly chkLow As New CheckBox()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly tab As New TabControl()

    ' عناصر تبويب الجرد الفعلي (سكانر)
    Private ReadOnly cboCounts As New ComboBox()
    Private ReadOnly txtCountScan As New TextBox()
    Private ReadOnly txtCountedQty As New TextBox()
    Private ReadOnly gridCount As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly _scannedItems As New HashSet(Of Long)()
    Private _scannedCountId As Long

    ' عناصر تبويب تنبؤ المخزون الذكي وغرفة الطلبية (Smart Forecast & Order Room)
    Private ReadOnly txtForecastSearch As New TextBox()
    Private ReadOnly numLookbackDays As New NumericUpDown With {.Minimum = 7, .Maximum = 365, .Value = 30, .Width = 65}
    Private ReadOnly numCoverageDays As New NumericUpDown With {.Minimum = 3, .Maximum = 180, .Value = 14, .Width = 65}
    Private ReadOnly chkOnlyActionable As New CheckBox With {.Text = "عرض الأصناف المطلوب شراؤها فقط (غرفة الطلبية)", .AutoSize = True, .Checked = False}
    Private ReadOnly gridForecast As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly lblForecastSummary As New Label()

    Public Sub New(session As AppSession, Optional openCountTab As Boolean = False, Optional initialTabKey As String = "balances")
        _session = session
        InitializeUi()
        Search()
        LoadCountsList()
        LoadForecastData()

        If openCountTab OrElse String.Equals(initialTabKey, "counts", StringComparison.OrdinalIgnoreCase) Then
            tab.SelectedIndex = 1
        ElseIf String.Equals(initialTabKey, "forecast", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(initialTabKey, "order_room", StringComparison.OrdinalIgnoreCase) Then
            If String.Equals(initialTabKey, "order_room", StringComparison.OrdinalIgnoreCase) Then
                chkOnlyActionable.Checked = True
            End If
            tab.SelectedIndex = 2
            LoadForecastData()
        End If
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — أرصدة المخزون، الجرد بالسكانر، وتنبؤ المخزون الذكي وغرفة الطلبية"
        ClientSize = New Size(1220, 740)
        StartPosition = FormStartPosition.CenterParent
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        ' ---------------- 1. تبويب الأرصدة الحالية ----------------
        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        txtSearch.Width = 260
        txtSearch.PlaceholderText = "كود / اسم الصنف"
        toolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        toolbar.Controls.Add(txtSearch)
        Dim btnSearch As Button = UiTheme.MakeButton("بحث", 85)
        AddHandler btnSearch.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnSearch)
        chkLow.Text = "فقط تحت حد الطلب"
        chkLow.AutoSize = True
        chkLow.Margin = New Padding(12, 10, 4, 4)
        AddHandler chkLow.CheckedChanged, Sub(s, e) Search()
        toolbar.Controls.Add(chkLow)
        Dim btnAdjust As Button = UiTheme.MakeButton("تسوية مخزون", 125)
        AddHandler btnAdjust.Click, AddressOf AdjustClick
        toolbar.Controls.Add(btnAdjust)
        Dim btnOpenForecast As Button = UiTheme.MakeGlassButton("🔮 تنبؤ المخزون وغرفة الطلبية", 210, 34, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 10)
        AddHandler btnOpenForecast.Click, Sub(s, e)
                                              tab.SelectedIndex = 2
                                              LoadForecastData()
                                          End Sub
        toolbar.Controls.Add(btnOpenForecast)
        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث", 85)
        AddHandler btnRefresh.Click, Sub(s, e) Search()
        toolbar.Controls.Add(btnRefresh)

        UiTheme.AddTextColumn(grid, "Code", "الكود", 110)
        UiTheme.AddTextColumn(grid, "NameAr", "الصنف", 320, True)
        UiTheme.AddTextColumn(grid, "QtyOnHand", "الرصيد", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "LastCost", "آخر تكلفة", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "SalePrice", "سعر البيع", 110, False, "N2")
        UiTheme.AddTextColumn(grid, "ReorderLevel", "حد الطلب", 110, False, "N2")

        Dim page1 As New TabPage("📊 أرصدة المخزون")
        page1.RightToLeft = RightToLeft.Yes
        page1.Controls.Add(grid)
        page1.Controls.Add(toolbar)

        ' ---------------- 2. تبويب الجرد الفعلي بالسكانر ----------------
        Dim countToolbar As FlowLayoutPanel = UiTheme.Toolbar()
        countToolbar.Controls.Add(UiTheme.MakeLabel("محضر الجرد:"))
        cboCounts.Width = 230
        cboCounts.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler cboCounts.SelectedIndexChanged, Sub(s, e) RefreshSelectedCountLines()
        countToolbar.Controls.Add(cboCounts)

        Dim btnNewCount As Button = UiTheme.PrimaryButton("محضر جرد جديد +", 130)
        AddHandler btnNewCount.Click, Sub(s, e) CreateNewCount()
        countToolbar.Controls.Add(btnNewCount)

        countToolbar.Controls.Add(UiTheme.MakeLabel("باركود/كود:"))
        txtCountScan.Width = 170
        txtCountScan.PlaceholderText = "امسح الباركود ثم Enter"
        AddHandler txtCountScan.KeyDown, AddressOf CountScanKeyDown
        countToolbar.Controls.Add(txtCountScan)

        countToolbar.Controls.Add(UiTheme.MakeLabel("الكمية الفعلية:"))
        txtCountedQty.Width = 75
        txtCountedQty.Text = "1"
        countToolbar.Controls.Add(txtCountedQty)

        Dim btnAddScan As Button = UiTheme.MakeButton("إدراج بالجرد", 105)
        AddHandler btnAddScan.Click, Sub(s, e) AddScannedCountItem()
        countToolbar.Controls.Add(btnAddScan)

        Dim btnPostCount As Button = UiTheme.PrimaryButton("اعتماد وترحيل الفروق", 160)
        AddHandler btnPostCount.Click, Sub(s, e) PostSelectedCount()
        countToolbar.Controls.Add(btnPostCount)

        UiTheme.AddTextColumn(gridCount, "Code", "الكود", 110)
        UiTheme.AddTextColumn(gridCount, "NameAr", "الصنف", 280, True)
        UiTheme.AddTextColumn(gridCount, "SystemQty", "الرصيد الدفتري", 120, False, "N2")
        UiTheme.AddTextColumn(gridCount, "CountedQty", "الجرد الفعلي", 120, False, "N2")
        UiTheme.AddTextColumn(gridCount, "VarianceQty", "الفرق (عجز/زيادة)", 130, False, "N2")
        UiTheme.AddTextColumn(gridCount, "UnitCost", "التكلفة", 110, False, "N2")

        Dim page2 As New TabPage("🧮 الجرد الفعلي (سكانر)")
        page2.RightToLeft = RightToLeft.Yes
        page2.Controls.Add(gridCount)
        page2.Controls.Add(countToolbar)

        ' ---------------- 3. تبويب تنبؤ المخزون الذكي وغرفة الطلبية ----------------
        Dim forecastToolbar As FlowLayoutPanel = UiTheme.Toolbar()
        forecastToolbar.Height = 52
        txtForecastSearch.Width = 180
        txtForecastSearch.PlaceholderText = "بحث كود / اسم الصنف..."
        AddHandler txtForecastSearch.TextChanged, Sub(s, e) LoadForecastData()

        AddHandler numLookbackDays.ValueChanged, Sub(s, e) LoadForecastData()
        AddHandler numCoverageDays.ValueChanged, Sub(s, e) LoadForecastData()
        AddHandler chkOnlyActionable.CheckedChanged, Sub(s, e) LoadForecastData()

        Dim btnCalcForecast As Button = UiTheme.MakeGlassButton("🔮 تحديث التنبؤ الذكي", 155, 34, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 10)
        AddHandler btnCalcForecast.Click, Sub(s, e) LoadForecastData()

        Dim btnExportOrder As Button = UiTheme.MakeGlassButton("📥 تصدير كشف الطلبية (CSV)", 180, 34, UiTheme.SoftTechBlue, UiTheme.DeepTechNavy, Color.White, 10)
        AddHandler btnExportOrder.Click, Sub(s, e) ExportOrderRoomCsv()

        Dim btnOpenPurchase As Button = UiTheme.MakeGlassButton("🛒 فتح فاتورة شراء للطلبية", 180, 34, UiTheme.DigitalBlue, UiTheme.DeepTechNavy, Color.White, 10)
        AddHandler btnOpenPurchase.Click, Sub(s, e)
                                              Using pf As New PurchaseForm(_session)
                                                  UiChrome.Apply(pf)
                                                  pf.ShowDialog(Me)
                                              End Using
                                              Search()
                                              LoadForecastData()
                                          End Sub

        forecastToolbar.Controls.Add(UiTheme.MakeLabel("بحث:"))
        forecastToolbar.Controls.Add(txtForecastSearch)
        forecastToolbar.Controls.Add(UiTheme.MakeLabel("فترة تحليل المبيعات (يوم):"))
        forecastToolbar.Controls.Add(numLookbackDays)
        forecastToolbar.Controls.Add(UiTheme.MakeLabel("أيام التغطية المطلوبة:"))
        forecastToolbar.Controls.Add(numCoverageDays)
        forecastToolbar.Controls.Add(chkOnlyActionable)
        forecastToolbar.Controls.Add(btnCalcForecast)
        forecastToolbar.Controls.Add(btnExportOrder)
        forecastToolbar.Controls.Add(btnOpenPurchase)

        lblForecastSummary.Dock = DockStyle.Bottom
        lblForecastSummary.Height = 36
        lblForecastSummary.BackColor = UiTheme.IceBlue
        lblForecastSummary.ForeColor = UiTheme.DeepTechNavy
        lblForecastSummary.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        lblForecastSummary.TextAlign = ContentAlignment.MiddleRight
        lblForecastSummary.Padding = New Padding(12, 0, 12, 0)

        UiTheme.AddTextColumn(gridForecast, "Code", "الكود", 95)
        UiTheme.AddTextColumn(gridForecast, "NameAr", "اسم الصنف", 230, True)
        UiTheme.AddTextColumn(gridForecast, "QtyOnHand", "الرصيد الحالي", 105, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "SoldLast30Days", "المباع بالفترة", 105, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "DailyBurnRate", "الاستهلاك اليومي", 110, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "DaysOfSupply", "أيام الكفاية", 95, False, "N1")
        UiTheme.AddTextColumn(gridForecast, "ReorderLevel", "حد الطلب", 90, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "SuggestedOrderQty", "الكمية المقترحة للطلب", 135, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "EstimatedOrderCost", "التكلفة التقديرية", 125, False, "N2")
        UiTheme.AddTextColumn(gridForecast, "StatusText", "حالة التنبؤ والأولوية", 185)

        Dim page3 As New TabPage("🔮 تنبؤ المخزون الذكي وغرفة الطلبية")
        page3.RightToLeft = RightToLeft.Yes
        page3.Controls.Add(gridForecast)
        page3.Controls.Add(lblForecastSummary)
        page3.Controls.Add(forecastToolbar)

        tab.Dock = DockStyle.Fill
        tab.RightToLeftLayout = True
        tab.Controls.Add(page1)
        tab.Controls.Add(page2)
        tab.Controls.Add(page3)
        Controls.Add(tab)
    End Sub

    Private Sub Search()
        Try
            Cursor = Cursors.WaitCursor
            Dim rows = StockService.Levels(_session, txtSearch.Text, chkLow.Checked)
            grid.DataSource = New BindingSource With {.DataSource = rows}
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub LoadForecastData()
        Try
            Dim rows = StockService.ForecastOrderRoom(_session, txtForecastSearch.Text,
                                                      Convert.ToInt32(numLookbackDays.Value),
                                                      Convert.ToInt32(numCoverageDays.Value),
                                                      chkOnlyActionable.Checked)
            gridForecast.DataSource = New BindingSource With {.DataSource = rows}
            Dim needOrderCount As Integer = rows.Where(Function(r) r.SuggestedOrderQty > 0D).Count()
            Dim totalOrderCost As Decimal = rows.Sum(Function(r) r.EstimatedOrderCost)
            lblForecastSummary.Text = $"🔮 نتائج التنبؤ الذكي وغرفة الطلبية: إجمالي الأصناف ({rows.Count}) | أصناف تحتاج طلب شراء عاجل ({needOrderCount}) | التكلفة التقديرية للطلبية: {UiTheme.Money(totalOrderCost)} ج.م"
        Catch ex As Exception
            lblForecastSummary.Text = "تنبيه التنبؤ: " & ex.Message
        End Try
    End Sub

    Private Sub ExportOrderRoomCsv()
        If gridForecast.Rows.Count = 0 Then
            UiTheme.Warn("لا توجد أصناف في غرفة الطلبية لتصديرها.")
            Return
        End If
        Using dlg As New SaveFileDialog With {
            .Filter = "ملف CSV|*.csv",
            .FileName = $"doctortech_order_room_{Date.Today:yyyyMMdd}.csv"
        }
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim sb As New StringBuilder()
            sb.AppendLine("الكود,الصنف,الرصيد الحالي,المباع بالفترة,الاستهلاك اليومي,أيام الكفاية,حد الطلب,الكمية المقترحة للطلب,التكلفة التقديرية,الحالة")
            For Each row As DataGridViewRow In gridForecast.Rows
                If row.IsNewRow Then Continue For
                Dim r = TryCast(row.DataBoundItem, StockForecastRow)
                If r Is Nothing Then Continue For
                sb.AppendLine($"""{r.Code}"",""{r.NameAr}"",{r.QtyOnHand},{r.SoldLast30Days},{r.DailyBurnRate},{r.DaysOfSupply},{r.ReorderLevel},{r.SuggestedOrderQty},{r.EstimatedOrderCost},""{r.StatusText}""")
            Next
            File.WriteAllText(dlg.FileName, sb.ToString(), New UTF8Encoding(True))
            UiTheme.Info("تم تصدير كشف غرفة الطلبية بنجاح إلى: " & dlg.FileName)
        End Using
    End Sub

    Private Function Selected() As StockSummary
        If grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(grid.CurrentRow.DataBoundItem, StockSummary)
    End Function

    Private Sub Adjust()
        Dim sRow = Selected()
        If sRow Is Nothing Then UiTheme.Warn("اختر صنفًا.") : Return
        Using dlg As New StockAdjustForm(_session, sRow)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Search()
                LoadForecastData()
            End If
        End Using
    End Sub

    Private Sub AdjustClick(sender As Object, e As EventArgs)
        Adjust()
    End Sub

    ' ---------------- وظائف الجرد الفعلي بالسكانر ----------------
    Private Sub LoadCountsList(Optional selectId As Long = 0)
        Try
            Dim list = StockService.ListInventoryCounts(_session)
            cboCounts.DataSource = Nothing
            cboCounts.DisplayMember = "DisplayTitle"
            cboCounts.ValueMember = "Id"
            cboCounts.DataSource = list
            If selectId > 0 Then
                For i As Integer = 0 To list.Count - 1
                    If list(i).Id = selectId Then
                        cboCounts.SelectedIndex = i
                        Exit For
                    End If
                Next
            ElseIf list.Count > 0 Then
                cboCounts.SelectedIndex = 0
            End If
            RefreshSelectedCountLines()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Function SelectedCountHeader() As InventoryCountSummary
        Return TryCast(cboCounts.SelectedItem, InventoryCountSummary)
    End Function

    Private Sub RefreshSelectedCountLines()
        Dim h = SelectedCountHeader()
        If h Is Nothing Then
            gridCount.DataSource = Nothing
            Return
        End If
        Try
            Dim lines = StockService.GetInventoryCountLines(_session, h.Id)
            gridCount.DataSource = New BindingSource With {.DataSource = lines}
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub CreateNewCount()
        Try
            Dim newId = StockService.CreateInventoryCount(_session, "جرد مخزني بالسكانر - " & Date.Now.ToString("yyyy-MM-dd HH:mm"))
            LoadCountsList(newId)
            txtCountScan.Focus()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub CountScanKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            e.SuppressKeyPress = True
            AddScannedCountItem()
        End If
    End Sub

    Private Sub AddScannedCountItem()
        Dim h = SelectedCountHeader()
        If h Is Nothing Then
            CreateNewCount()
            h = SelectedCountHeader()
            If h Is Nothing Then Return
        End If
        If h.Status = "posted" Then
            UiTheme.Warn("محضر الجرد الحالي مرحّل بالفعل. أنشئ محضر جرد جديدًا.")
            Return
        End If

        Dim code = txtCountScan.Text.Trim()
        If String.IsNullOrWhiteSpace(code) Then Return

        Try
            Dim item = ItemService.FindForSale(_session, code)
            If item Is Nothing Then
                Using p As New ItemPickerForm(_session, code)
                    If p.ShowDialog(Me) = DialogResult.OK AndAlso p.SelectedItem IsNot Nothing Then
                        item = p.SelectedItem
                    End If
                End Using
            End If
            If item Is Nothing Then Return

            Dim qty As Decimal = UiTheme.ToMoney(txtCountedQty.Text)
            If qty < 0D Then
                UiTheme.Warn("الكمية لا يمكن أن تكون سالبة.")
                Return
            End If

            ' The count draft preloads counted_qty = system_qty for every item, so the first
            ' scan of an item must replace that placeholder; later scans of the same item
            ' accumulate, otherwise scanning 3 pieces of a 10-piece item kept reporting 1.
            If _scannedCountId <> h.Id Then
                _scannedItems.Clear()
                _scannedCountId = h.Id
            End If
            Dim targetQty As Decimal = qty
            If _scannedItems.Contains(item.Id) Then
                Dim existing = StockService.GetInventoryCountLines(_session, h.Id).
                    FirstOrDefault(Function(l) l.ItemId = item.Id)
                If existing IsNot Nothing Then targetQty = existing.CountedQty + qty
            End If

            StockService.UpsertInventoryCountLine(_session, h.Id, item.Id, targetQty)
            _scannedItems.Add(item.Id)
            txtCountScan.Clear()
            txtCountedQty.Text = "1"
            RefreshSelectedCountLines()
            txtCountScan.Focus()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    Private Sub PostSelectedCount()
        Dim h = SelectedCountHeader()
        If h Is Nothing Then Return
        If h.Status = "posted" Then
            UiTheme.Warn("محضر الجرد مرحّل بالفعل.")
            Return
        End If
        If Not UiTheme.Confirm("هل أنت متأكد من اعتماد وترحيل محضر الجرد رقم " & h.CountNo & "؟ سيتم تعديل أرصدة المخزون وإنشاء قيود التسوية تلقائيًا.") Then Return

        Try
            Cursor = Cursors.WaitCursor
            Dim netVariance = StockService.PostInventoryCount(_session, h.Id)
            Cursor = Cursors.Default
            UiTheme.Info("تم اعتماد محضر الجرد بنجاح. صافي قيمة فروق الجرد: " & UiTheme.Money(netVariance) & " ج.م")
            LoadCountsList(h.Id)
            Search()
            LoadForecastData()
        Catch ex As Exception
            Cursor = Cursors.Default
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class

Public Class StockAdjustForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _row As StockSummary
    Private ReadOnly txtQty As New TextBox()
    Private ReadOnly txtCost As New TextBox()
    Private ReadOnly txtReason As New TextBox()
    Private ReadOnly cboDir As New ComboBox()

    Public Sub New(session As AppSession, row As StockSummary)
        _session = session
        _row = row
        InitializeUi()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — تسوية مخزون: " & _row.NameAr
        ClientSize = New Size(440, 320)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False

        Dim t As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(18)}
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
        t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        AddRow(t, "الرصيد الحالي", UiTheme.MakeLabel(_row.QtyOnHand.ToString("N2")))
        cboDir.Items.Add("زيادة")
        cboDir.Items.Add("نقصان")
        cboDir.SelectedIndex = 0
        cboDir.DropDownStyle = ComboBoxStyle.DropDownList
        AddRow(t, "نوع التسوية", cboDir)
        AddRow(t, "الكمية", txtQty)
        AddRow(t, "التكلفة", txtCost)
        txtCost.Text = _row.LastCost.ToString("0.00")
        AddRow(t, "السبب", txtReason)

        Dim btns As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 58, .FlowDirection = FlowDirection.LeftToRight, .Padding = New Padding(12)}
        Dim bs As Button = UiTheme.PrimaryButton("حفظ", 120)
        AddHandler bs.Click, AddressOf SaveAdjust
        Dim bc As Button = UiTheme.MakeButton("إلغاء", 100)
        AddHandler bc.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        btns.Controls.Add(bs)
        btns.Controls.Add(bc)

        Controls.Add(t)
        Controls.Add(btns)
    End Sub

    Private Shared Sub AddRow(t As TableLayoutPanel, cap As String, ed As Control)
        Dim r = t.RowCount
        t.RowCount += 1
        t.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        t.Controls.Add(UiTheme.MakeLabel(cap, True), 0, r)
        ed.Dock = DockStyle.Fill
        t.Controls.Add(ed, 1, r)
    End Sub

    Private Sub SaveAdjust(sender As Object, e As EventArgs)
        Try
            Dim qty = UiTheme.ToMoney(txtQty.Text)
            If qty <= 0 Then UiTheme.Warn("الكمية يجب أن تكون أكبر من صفر.") : Return
            Dim delta = If(cboDir.SelectedIndex = 0, qty, -qty)
            Dim cost As Decimal? = Nothing
            If Not String.IsNullOrWhiteSpace(txtCost.Text) Then cost = UiTheme.ToMoney(txtCost.Text)
            StockService.Adjust(_session, _row.ItemId, delta, cost, txtReason.Text.Trim())
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
