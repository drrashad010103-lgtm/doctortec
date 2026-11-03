Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

' <summary>
' DOCTOR TECH — مركز التقارير والداشبورد المالي الشامل
' يدعم فتح أي تقرير مخصص مباشرة (20 تقريراً حقيقياً من قاعدة البيانات)
' مع تفعيل الفلترة بالتاريخ (dtpFrom / dtpTo) وتصدير CSV / HTML.
' </summary>
Public Class ReportsForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private _activeReportKey As String
    Private ReadOnly lblTodaySummary As New Label()
    Private ReadOnly lblAlerts As New Label()
    Private ReadOnly lblActiveReportTitle As New Label()
    Private ReadOnly grid As DataGridView = UiTheme.MakeGrid()
    Private ReadOnly dtpFrom As New DateTimePicker()
    Private ReadOnly dtpTo As New DateTimePicker()
    Private ReadOnly cboReportType As New ComboBox()
    Private ReadOnly _pillButtons As New Dictionary(Of String, Button)()

    Private Class ReportOption
        Public Property Key As String
        Public Property Title As String
        Public Overrides Function ToString() As String
            Return Title
        End Function
    End Class

    Public Sub New(session As AppSession, Optional initialReportKey As String = "sales_by_day")
        _session = session
        _activeReportKey = If(String.IsNullOrWhiteSpace(initialReportKey), "sales_by_day", initialReportKey.Trim().ToLowerInvariant())
        InitializeUi()
        RunReport(_activeReportKey)
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "DOCTOR TECH — مركز التقارير والداشبورد الشاملة"
        WindowState = FormWindowState.Maximized
        StartPosition = FormStartPosition.CenterScreen
        MinimumSize = New Size(1180, 740)
        BackColor = UiTheme.PageBg
        DoubleBuffered = True
        KeyPreview = True
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        ' شريط علوي بنظام DOCTOR TECH
        Dim header As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 64,
            .BackColor = UiTheme.DeepTechNavy,
            .Padding = New Padding(16, 10, 16, 10)
        }
        UiTheme.SetRoundedRegion(header, 16)

        Dim lblBanner As New Label With {
            .Text = "📊 DOCTOR TECH | مركز التقارير المالية والمخزنية والداشبورد الحية",
            .ForeColor = Color.White,
            .Font = UiTheme.Font(13.5F, FontStyle.Bold),
            .Dock = DockStyle.Right,
            .AutoSize = True
        }

        Dim topActions As New FlowLayoutPanel With {
            .Dock = DockStyle.Left,
            .AutoSize = True,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .BackColor = Color.Transparent
        }

        Dim btnClose As Button = UiTheme.MakeGlassButton("✖ إغلاق (Esc)", 120, 38, UiTheme.DangerRed, Color.FromArgb(175, 30, 30), Color.White, 12)
        AddHandler btnClose.Click, Sub(s, e) Close()

        Dim btnCloud As Button = UiTheme.MakeGlassButton("☁ تصدير وفتح التقرير (HTML/CSV)", 230, 38, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 12)
        AddHandler btnCloud.Click, Sub(s, e) ExportActiveReport(True)

        Dim btnCsv As Button = UiTheme.MakeGlassButton("📥 حفظ CSV (F9)", 140, 38, UiTheme.SoftTechBlue, UiTheme.DigitalBlue, Color.White, 12)
        AddHandler btnCsv.Click, Sub(s, e) ExportActiveReport(False)

        topActions.Controls.Add(btnClose)
        topActions.Controls.Add(btnCloud)
        topActions.Controls.Add(btnCsv)

        header.Controls.Add(topActions)
        header.Controls.Add(lblBanner)

        ' شريط اختيار التقرير والتواريخ (dtpFrom / dtpTo مفعّلان بالكامل)
        Dim filterBar As New FlowLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Padding = New Padding(10, 8, 10, 8),
            .BackColor = UiTheme.IceBlue
        }

        dtpFrom.Format = DateTimePickerFormat.Custom
        dtpFrom.CustomFormat = "yyyy-MM-dd"
        dtpFrom.Value = Date.Today.AddDays(-30)
        dtpFrom.Width = 120

        dtpTo.Format = DateTimePickerFormat.Custom
        dtpTo.CustomFormat = "yyyy-MM-dd"
        dtpTo.Value = Date.Today
        dtpTo.Width = 120

        cboReportType.DropDownStyle = ComboBoxStyle.DropDownList
        cboReportType.Width = 260
        Dim reportOptions As ReportOption() = {
            New ReportOption With {.Key = "sales_by_day", .Title = "1. المبيعات اليومية والداشبورد الحية"},
            New ReportOption With {.Key = "profit", .Title = "2. الأرباح والخسائر التفصيلية للأصناف"},
            New ReportOption With {.Key = "profit_groups", .Title = "3. أرباح المجموعات والأقسام الرئيسية"},
            New ReportOption With {.Key = "trial_balance", .Title = "4. ميزان المراجعة المحاسبي"},
            New ReportOption With {.Key = "tax", .Title = "5. الإقرار الضريبي وضريبة القيمة المضافة"},
            New ReportOption With {.Key = "stock_value", .Title = "6. تقييم المخزون والكميات والتكلفة"},
            New ReportOption With {.Key = "top_items", .Title = "7. الأصناف الأكثر مبيعاً وإيراداً"},
            New ReportOption With {.Key = "cash_summary", .Title = "8. مبيعات طرق الدفع والتحصيل"},
            New ReportOption With {.Key = "customer_debt", .Title = "9. أرصدة ومديونيات العملاء"},
            New ReportOption With {.Key = "sales_invoices", .Title = "10. سجل فواتير المبيعات بالعملاء"},
            New ReportOption With {.Key = "purchase_invoices", .Title = "11. سجل فواتير المشتريات والموردين"},
            New ReportOption With {.Key = "returns_report", .Title = "12. سجل مرتجعات المبيعات والمشتريات"},
            New ReportOption With {.Key = "sales_by_type", .Title = "13. ملخص المبيعات والمشتريات والمرتجعات"},
            New ReportOption With {.Key = "shifts_report", .Title = "14. تقرير الورديات والعجز والزيادة"},
            New ReportOption With {.Key = "expenses_report", .Title = "15. تقرير المصروفات وسندات الصرف"},
            New ReportOption With {.Key = "cash_moves", .Title = "16. حركة الخزينة اليومية (وارد وصادر)"},
            New ReportOption With {.Key = "audit_log", .Title = "17. تحليل الحركات وسجل الرقابة"}
        }
        cboReportType.Items.AddRange(reportOptions)

        AddHandler cboReportType.SelectedIndexChanged, Sub(s, e)
                                                           Dim opt = TryCast(cboReportType.SelectedItem, ReportOption)
                                                           If opt IsNot Nothing Then RunReport(opt.Key)
                                                       End Sub
        AddHandler dtpFrom.ValueChanged, Sub(s, e) RunReport(_activeReportKey)
        AddHandler dtpTo.ValueChanged, Sub(s, e) RunReport(_activeReportKey)

        Dim btnRun As Button = UiTheme.MakeGlassButton("🔍 عرض التقرير", 130, 34, UiTheme.DigitalBlue, UiTheme.DeepTechNavy, Color.White, 10)
        AddHandler btnRun.Click, Sub(s, e) RunReport(_activeReportKey)

        Dim btnTodayOnly As Button = UiTheme.MakeGlassButton("📅 اليوم فقط", 110, 34, UiTheme.SoftTechBlue, UiTheme.DigitalBlue, Color.White, 10)
        AddHandler btnTodayOnly.Click, Sub(s, e)
                                           dtpFrom.Value = Date.Today
                                           dtpTo.Value = Date.Today
                                           RunReport(_activeReportKey)
                                       End Sub

        filterBar.Controls.Add(UiTheme.MakeLabel("نوع التقرير:", True))
        filterBar.Controls.Add(cboReportType)
        filterBar.Controls.Add(UiTheme.MakeLabel("من تاريخ:", True))
        filterBar.Controls.Add(dtpFrom)
        filterBar.Controls.Add(UiTheme.MakeLabel("إلى تاريخ:", True))
        filterBar.Controls.Add(dtpTo)
        filterBar.Controls.Add(btnRun)
        filterBar.Controls.Add(btnTodayOnly)

        ' شريط أزرار الانتقال السريع بين التقارير (من اليسار إلى اليمين)
        Dim pillsBar As New FlowLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .AutoScroll = True,
            .Padding = New Padding(8, 6, 8, 6),
            .BackColor = Color.White
        }

        Dim quickPills As (Key As String, Title As String)() = {
            ("sales_by_day", "📅 مبيعات بالتواريخ"),
            ("profit", "💰 الأرباح والخسائر"),
            ("profit_groups", "🏢 أرباح الأقسام"),
            ("trial_balance", "⚖️ ميزان المراجعة"),
            ("tax", "🧾 الضرائب VAT"),
            ("stock_value", "📦 قيمة المخزون"),
            ("top_items", "🔥 الأكثر مبيعاً"),
            ("cash_summary", "💳 طرق الدفع"),
            ("customer_debt", "👥 مديونيات العملاء"),
            ("sales_invoices", "📝 فواتير العملاء"),
            ("purchase_invoices", "🛒 فواتير المشتريات"),
            ("expenses_report", "💸 المصروفات"),
            ("audit_log", "🔍 تحليل الحركات")
        }

        For Each qp In quickPills
            Dim rk As String = qp.Key
            Dim b As Button = UiTheme.MakeGlassButton(qp.Title, 130, 34, UiTheme.DigitalBlue, UiTheme.DeepTechNavy, Color.White, 12)
            AddHandler b.Click, Sub(s, e) RunReport(rk)
            _pillButtons(rk) = b
            pillsBar.Controls.Add(b)
        Next

        ' كروت الملخص العلوية
        Dim summaryRow As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 82,
            .ColumnCount = 2,
            .RowCount = 1,
            .Padding = New Padding(0, 6, 0, 6)
        }
        summaryRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55.0F))
        summaryRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45.0F))

        Dim cardSalesToday As Panel = CreateSectionCard($"📅 ملخص مبيعات اليوم ({Date.Today:yyyy-MM-dd})", lblTodaySummary)
        Dim cardQuickAlerts As Panel = CreateSectionCard("🔔 حالة التقرير الحالي والتنبيهات", lblAlerts)
        summaryRow.Controls.Add(cardSalesToday, 0, 0)
        summaryRow.Controls.Add(cardQuickAlerts, 1, 0)

        ' جدول التقرير
        Dim gridCard As Panel = UiTheme.MakeCard("نتائج التقرير المفعّل من قاعدة البيانات")
        gridCard.Dock = DockStyle.Fill
        grid.Dock = DockStyle.Fill
        gridCard.Controls.Add(grid)

        Controls.Add(gridCard)
        Controls.Add(summaryRow)
        Controls.Add(pillsBar)
        Controls.Add(filterBar)
        Controls.Add(header)

        AddHandler KeyDown, Sub(s, e)
                                If e.KeyCode = Keys.Escape Then Close()
                                If e.KeyCode = Keys.F9 Then ExportActiveReport(False)
                            End Sub
    End Sub

    Private Shared Function CreateSectionCard(title As String, bodyLbl As Label) As Panel
        Dim p As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(4, 0, 4, 0),
            .BorderStyle = BorderStyle.FixedSingle
        }
        Dim lblHead As New Label With {
            .Text = title,
            .Dock = DockStyle.Top,
            .Height = 24,
            .Font = UiTheme.Font(10.0F, FontStyle.Bold),
            .ForeColor = UiTheme.DeepTechNavy,
            .TextAlign = ContentAlignment.MiddleRight
        }
        bodyLbl.Dock = DockStyle.Fill
        bodyLbl.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        bodyLbl.ForeColor = UiTheme.DigitalBlue
        bodyLbl.TextAlign = ContentAlignment.MiddleRight
        p.Controls.Add(bodyLbl)
        p.Controls.Add(lblHead)
        Return p
    End Function

    Public Sub RunReport(reportKey As String)
        If String.IsNullOrWhiteSpace(reportKey) Then reportKey = "sales_by_day"
        _activeReportKey = reportKey.Trim().ToLowerInvariant()

        ' Sync combo selection without recursion
        For i As Integer = 0 To cboReportType.Items.Count - 1
            Dim opt = TryCast(cboReportType.Items(i), ReportOption)
            If opt IsNot Nothing AndAlso opt.Key = _activeReportKey AndAlso cboReportType.SelectedIndex <> i Then
                cboReportType.SelectedIndex = i
                Exit For
            End If
        Next

        Dim dFrom As Date = dtpFrom.Value.Date
        Dim dTo As Date = dtpTo.Value.Date
        If dTo < dFrom Then
            Dim tmp = dFrom
            dFrom = dTo
            dTo = tmp
        End If

        Try
            Cursor = Cursors.WaitCursor
            RefreshTodayBanner()

            grid.DataSource = Nothing
            grid.Columns.Clear()
            grid.AutoGenerateColumns = False

            Select Case _activeReportKey
                Case "profit"
                    UiTheme.AddTextColumn(grid, "Code", "كود الصنف", 110)
                    UiTheme.AddTextColumn(grid, "NameAr", "اسم الصنف", 260, True)
                    UiTheme.AddTextColumn(grid, "QtySold", "الكمية المباعة", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "RevenueExTax", "صافي الإيراد (بدون ضريبة)", 160, False, "N2")
                    UiTheme.AddTextColumn(grid, "Cost", "إجمالي التكلفة", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Profit", "صافي الربح", 140, False, "N2")
                    Dim rows = ReportService.Profit(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"تقرير الأرباح والخسائر ({dFrom:yyyy-MM-dd} إلى {dTo:yyyy-MM-dd}) — إجمالي الربح: {UiTheme.Money(rows.Sum(Function(x) x.Profit))} ج.م"

                Case "profit_groups"
                    UiTheme.AddTextColumn(grid, "Code", "كود المجموعة", 120)
                    UiTheme.AddTextColumn(grid, "NameAr", "المجموعة / القسم", 260, True)
                    UiTheme.AddTextColumn(grid, "QtySold", "الكمية المباعة", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "RevenueExTax", "صافي الإيراد", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "Cost", "التكلفة", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Profit", "صافي ربح القسم", 150, False, "N2")
                    Dim rows = ReportService.ProfitByGroup(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"أرباح المجموعات والأقسام — إجمالي الربح: {UiTheme.Money(rows.Sum(Function(x) x.Profit))} ج.م"

                Case "trial_balance"
                    UiTheme.AddTextColumn(grid, "Code", "كود الحساب", 110)
                    UiTheme.AddTextColumn(grid, "NameAr", "اسم الحساب", 260, True)
                    UiTheme.AddTextColumn(grid, "AccountType", "طبيعة الحساب", 120)
                    UiTheme.AddTextColumn(grid, "Debit", "مدين (Debit)", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Credit", "دائن (Credit)", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Balance", "الرصيد الصافي", 150, False, "N2")
                    Dim rows = ReportService.TrialBalance(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"ميزان المراجعة — إجمالي المدين: {UiTheme.Money(rows.Sum(Function(x) x.Debit))} | إجمالي الدائن: {UiTheme.Money(rows.Sum(Function(x) x.Credit))}"

                Case "tax"
                    UiTheme.AddTextColumn(grid, "TaxCode", "كود الضريبة", 110)
                    UiTheme.AddTextColumn(grid, "TaxName", "التصنيف الضريبي", 220, True)
                    UiTheme.AddTextColumn(grid, "TaxRate", "النسبة %", 100, False, "N2")
                    UiTheme.AddTextColumn(grid, "OutputBase", "وعاء المخرجات (مبيعات)", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "OutputTax", "ضريبة المخرجات", 135, False, "N2")
                    UiTheme.AddTextColumn(grid, "InputBase", "وعاء المدخلات (مشتريات)", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "InputTax", "ضريبة المدخلات", 135, False, "N2")
                    Dim taxRep = ReportService.TaxReport(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = taxRep.Rows}
                    lblAlerts.Text = $"الإقرار الضريبي — صافي الضريبة المستحقة: {UiTheme.Money(taxRep.NetPayable)} ج.م (مطابقة الدفتر: {UiTheme.Money(taxRep.LedgerTaxMovement)})"

                Case "stock_value"
                    UiTheme.AddTextColumn(grid, "Code", "كود الصنف", 110)
                    UiTheme.AddTextColumn(grid, "NameAr", "اسم الصنف", 280, True)
                    UiTheme.AddTextColumn(grid, "QtyOnHand", "الرصيد الحالي", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "LastCost", "آخر تكلفة", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "Value", "إجمالي قيمة المخزون", 160, False, "N2")
                    Dim rows = ReportService.StockValue(_session)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"تقييم المخزون الحالي ({rows.Count} صنف) — إجمالي القيمة: {UiTheme.Money(rows.Sum(Function(x) x.Value))} ج.م"

                Case "top_items"
                    UiTheme.AddTextColumn(grid, "Code", "كود الصنف", 110)
                    UiTheme.AddTextColumn(grid, "NameAr", "اسم الصنف", 280, True)
                    UiTheme.AddTextColumn(grid, "QtySold", "الكمية المباعة", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "RevenueExTax", "صافي الإيراد", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "Cost", "التكلفة", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Profit", "صافي الربح", 140, False, "N2")
                    Dim rows = ReportService.TopItems(_session, dFrom, dTo, 100)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"الأصناف الأكثر مبيعاً وإيراداً ({rows.Count} صنف) خلال الفترة المحددة"

                Case "cash_summary"
                    UiTheme.AddTextColumn(grid, "Key", "طريقة الدفع", 320, True)
                    UiTheme.AddTextColumn(grid, "Value", "إجمالي المحصل (ج.م)", 200, False, "N2")
                    Dim rows = ReportService.CashSummary(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"مبيعات طرق الدفع — الإجمالي المحصل: {UiTheme.Money(rows.Sum(Function(x) x.Value))} ج.م"

                Case "customer_debt"
                    UiTheme.AddTextColumn(grid, "Name", "اسم العميل / الحساب", 260, True)
                    UiTheme.AddTextColumn(grid, "Mobile", "الموبايل", 140)
                    UiTheme.AddTextColumn(grid, "CreditLimit", "حد الائتمان", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Balance", "المديونية / الرصيد المستحق", 160, False, "N2")
                    Dim rows = ReportService.CustomerDebt(_session, 500)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"أرصدة ومديونيات العملاء ({rows.Count} عميل) — الإجمالي: {UiTheme.Money(rows.Sum(Function(x) x.Balance))} ج.م"

                Case "sales_invoices", "purchase_invoices", "returns_report"
                    Dim invType As String = If(_activeReportKey = "purchase_invoices", "purchase", If(_activeReportKey = "returns_report", "sale_return", "sale"))
                    UiTheme.AddTextColumn(grid, "InvoiceNo", "رقم الفاتورة", 150)
                    UiTheme.AddTextColumn(grid, "BusinessDate", "التاريخ", 120, False, "yyyy-MM-dd")
                    UiTheme.AddTextColumn(grid, "CustomerName", "العميل / المورد", 240, True)
                    UiTheme.AddTextColumn(grid, "Subtotal", "الإجمالي قبل الضريبة", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "HeaderDiscount", "الخصم", 100, False, "N2")
                    UiTheme.AddTextColumn(grid, "TaxTotal", "الضريبة", 100, False, "N2")
                    UiTheme.AddTextColumn(grid, "Total", "الصافي", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "Paid", "المدفوع", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "Due", "المتبقي", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "PaymentStatusName", "الحالة", 110)
                    Dim rows = SalesService.ListInvoices(_session, dFrom, dTo, invType)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"سجل الفواتير ({rows.Count} فاتورة) — الإجمالي: {UiTheme.Money(rows.Sum(Function(x) x.Total))} ج.م"

                Case "sales_by_type"
                    UiTheme.AddTextColumn(grid, "Key", "نوع المعاملة (بيع / شراء / مرتجع)", 320, True)
                    UiTheme.AddTextColumn(grid, "Value", "إجمالي القيمة (ج.م)", 220, False, "N2")
                    Dim rows = ReportService.SalesByType(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"ملخص المبيعات والمشتريات والمرتجعات خلال الفترة ({rows.Count} نوع)"

                Case "shifts_report"
                    UiTheme.AddTextColumn(grid, "ShiftNo", "رقم الوردية", 150)
                    UiTheme.AddTextColumn(grid, "UserName", "الكاشير", 160, True)
                    UiTheme.AddTextColumn(grid, "OpenedAt", "وقت الفتح", 140, False, "yyyy-MM-dd HH:mm")
                    UiTheme.AddTextColumn(grid, "ClosedAt", "وقت الإغلاق", 140, False, "yyyy-MM-dd HH:mm")
                    UiTheme.AddTextColumn(grid, "OpeningCash", "نقدية الافتتاح", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "ExpectedCash", "المتوقع بالدرج", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "CountedCash", "الفعلي المعدود", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "Variance", "العجز / الزيادة", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "StatusName", "الحالة", 100)
                    Dim rows = ShiftService.List(_session, dFrom, dTo, False)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"تقرير الورديات ({rows.Count} وردية) — من {dFrom:yyyy-MM-dd} إلى {dTo:yyyy-MM-dd}"

                Case "expenses_report", "cash_moves"
                    Dim dir As String = If(_activeReportKey = "expenses_report", "out", "")
                    UiTheme.AddTextColumn(grid, "Id", "رقم الحركة", 95)
                    UiTheme.AddTextColumn(grid, "CashboxName", "الخزينة", 170)
                    UiTheme.AddTextColumn(grid, "ShiftNo", "الوردية", 130)
                    UiTheme.AddTextColumn(grid, "DirectionArabic", "نوع الحركة", 120)
                    UiTheme.AddTextColumn(grid, "Amount", "المبلغ (ج.م)", 130, False, "N2")
                    UiTheme.AddTextColumn(grid, "RefType", "المرجع / البيان", 180, True)
                    UiTheme.AddTextColumn(grid, "UserName", "المستخدم", 130)
                    UiTheme.AddTextColumn(grid, "CreatedAt", "التاريخ والوقت", 150, False, "yyyy-MM-dd HH:mm")
                    Dim rows = ReportService.CashMovesReport(_session, dFrom, dTo, dir)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"حركات الخزينة والمصروفات ({rows.Count} حركة) — إجمالي المبالغ: {UiTheme.Money(rows.Sum(Function(x) x.Amount))} ج.م"

                Case "audit_log"
                    UiTheme.AddTextColumn(grid, "Id", "م", 80)
                    UiTheme.AddTextColumn(grid, "Action", "العملية المنفذة", 170)
                    UiTheme.AddTextColumn(grid, "EntityType", "الجدول / الكيان", 140)
                    UiTheme.AddTextColumn(grid, "EntityId", "رقم السجل", 110)
                    UiTheme.AddTextColumn(grid, "Username", "المستخدم", 130)
                    UiTheme.AddTextColumn(grid, "CreatedAt", "وقت التنفيذ", 155, False, "yyyy-MM-dd HH:mm:ss")
                    UiTheme.AddTextColumn(grid, "AfterJson", "التفاصيل", 280, True)
                    Dim rows = ReportService.AuditLogReport(_session, dFrom, dTo, 400)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"تحليل الحركات وسجل الرقابة ({rows.Count} عملية مسجلة)"

                Case Else ' sales_by_day / dashboard
                    UiTheme.AddTextColumn(grid, "BusinessDate", "التاريخ", 130, False, "yyyy-MM-dd")
                    UiTheme.AddTextColumn(grid, "InvoiceCount", "عدد الفواتير", 120)
                    UiTheme.AddTextColumn(grid, "Subtotal", "الإجمالي قبل الضريبة", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "Discounts", "الخصومات", 125, False, "N2")
                    UiTheme.AddTextColumn(grid, "TaxTotal", "الضريبة", 120, False, "N2")
                    UiTheme.AddTextColumn(grid, "Total", "صافي المبيعات", 150, False, "N2")
                    UiTheme.AddTextColumn(grid, "Paid", "المحصل نقداً/بطاقة", 140, False, "N2")
                    UiTheme.AddTextColumn(grid, "Due", "الآجل المتبقي", 140, True, "N2")
                    Dim rows = ReportService.SalesByDay(_session, dFrom, dTo)
                    grid.DataSource = New BindingSource With {.DataSource = rows}
                    lblAlerts.Text = $"تقرير المبيعات اليومية ({dFrom:yyyy-MM-dd} إلى {dTo:yyyy-MM-dd}) — إجمالي المبيعات: {UiTheme.Money(rows.Sum(Function(x) x.Total))} ج.م"
            End Select
        Catch ex As Exception
            lblAlerts.Text = "تنبيه: " & ex.Message
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub RefreshTodayBanner()
        Try
            Dim todayRows = ReportService.SalesByDay(_session, Date.Today, Date.Today)
            Dim totalToday As Decimal = todayRows.Sum(Function(r) r.Total)
            Dim countToday As Long = todayRows.Sum(Function(r) r.InvoiceCount)
            Dim dueToday As Decimal = todayRows.Sum(Function(r) r.Due)
            lblTodaySummary.Text = $"مبيعات اليوم: {UiTheme.Money(totalToday)} ج.م   |   الفواتير: {countToday}   |   الآجل: {UiTheme.Money(dueToday)} ج.م"
        Catch ex As Exception
            lblTodaySummary.Text = "مبيعات اليوم: 0.00 ج.م"
        End Try
    End Sub

    Private Sub ExportActiveReport(openInBrowser As Boolean)
        If grid.Rows.Count = 0 Then
            UiTheme.Warn("لا توجد بيانات في الجدول لتصديرها.")
            Return
        End If

        Try
            If openInBrowser Then
                Dim htmlPath As String = Path.Combine(Path.GetTempPath(), $"doctortech_report_{_activeReportKey}_{DateTime.Now:yyyyMMdd_HHmmss}.html")
                Dim sb As New StringBuilder()
                sb.AppendLine("<!DOCTYPE html><html lang='ar' dir='rtl'><head><meta charset='utf-8'/>")
                sb.AppendLine("<title>DOCTOR TECH Report</title>")
                sb.AppendLine("<style>body{font-family:'Segoe UI',Tahoma,sans-serif;background:#f4f9ff;color:#0D2B45;padding:24px;}h2{color:#0D2B45;}table{width:100%;border-collapse:collapse;background:#fff;}th{background:#0D2B45;color:#fff;padding:10px;border:1px solid #70C7F2;}td{padding:8px 10px;border:1px solid #A8DFFF;text-align:right;}tr:nth-child(even){background:#DDF4FF;}</style></head><body>")
                sb.AppendLine($"<h2>💎 DOCTOR TECH — {cboReportType.Text}</h2>")
                sb.AppendLine($"<p>الفترة من: <b>{dtpFrom.Value:yyyy-MM-dd}</b> إلى: <b>{dtpTo.Value:yyyy-MM-dd}</b> | الفرع: <b>{_session.BranchName}</b></p>")
                sb.AppendLine("<table><thead><tr>")
                For Each col As DataGridViewColumn In grid.Columns
                    sb.AppendLine($"<th>{col.HeaderText}</th>")
                Next
                sb.AppendLine("</tr></thead><tbody>")
                For Each row As DataGridViewRow In grid.Rows
                    If row.IsNewRow Then Continue For
                    sb.AppendLine("<tr>")
                    For Each cell As DataGridViewCell In row.Cells
                        sb.AppendLine($"<td>{Convert.ToString(cell.FormattedValue)}</td>")
                    Next
                    sb.AppendLine("</tr>")
                Next
                sb.AppendLine("</tbody></table></body></html>")
                File.WriteAllText(htmlPath, sb.ToString(), New UTF8Encoding(True))
                Process.Start(New ProcessStartInfo(htmlPath) With {.UseShellExecute = True})
            Else
                Using dlg As New SaveFileDialog With {
                    .Filter = "ملف CSV|*.csv",
                    .FileName = $"doctortech_{_activeReportKey}_{Date.Today:yyyyMMdd}.csv"
                }
                    If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                    Dim sb As New StringBuilder()
                    Dim headers As New List(Of String)()
                    For Each col As DataGridViewColumn In grid.Columns
                        headers.Add("""" & col.HeaderText.Replace("""", """""") & """")
                    Next
                    sb.AppendLine(String.Join(",", headers))
                    For Each row As DataGridViewRow In grid.Rows
                        If row.IsNewRow Then Continue For
                        Dim cells As New List(Of String)()
                        For Each cell As DataGridViewCell In row.Cells
                            cells.Add("""" & Convert.ToString(cell.FormattedValue).Replace("""", """""") & """")
                        Next
                        sb.AppendLine(String.Join(",", cells))
                    Next
                    File.WriteAllText(dlg.FileName, sb.ToString(), New UTF8Encoding(True))
                    UiTheme.Info("تم حفظ ملف التقرير بنجاح: " & dlg.FileName)
                End Using
            End If
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub
End Class
