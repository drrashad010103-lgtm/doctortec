Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Shared Oracle Retail Point-of-Service (Green Edition) base screen for all ERP/Management modules:
''' Implements the 5 standard Oracle POS Screen Regions from the User Guide:
''' 1. Prompt & Response Region (Top)
''' 2. Work Area (Center DataGridView in Oracle Green)
''' 3. Local Navigation Bar (Right vertical F2-F9 buttons synced with screen actions)
''' 4. Status Region (Bottom 5-panel status strip: Register ID | Date | User | Screen | Online)
''' 5. Global Navigation Bar (Bottom F1/Help, F11/Delete, F12/Cancel, Esc/Undo, Enter/Next, ORACLE logo)
''' </summary>
Public MustInherit Class SectionFormBase
    Inherits Form

    Protected ReadOnly Session As AppSession
    Protected ReadOnly ToolBar As New FlowLayoutPanel()
    Protected ReadOnly Grid As DataGridView = UiTheme.MakeGrid()
    Protected ReadOnly Footer As New Label()

    Private ReadOnly _localNavPanel As New FlowLayoutPanel()
    Private ReadOnly _lblPromptInstruction As New Label()
    Private ReadOnly _lblResponseBadge As New Label()
    Private _searchBox As TextBox
    Private _searchAction As Action

    Protected Sub New(appSession As AppSession, title As String, subtitle As String,
                      accent As Color, formSize As Size)
        Session = appSession
        UiTheme.Apply(Me)
        Me.Text = title
        ClientSize = New Size(Math.Max(formSize.Width, 1080), Math.Max(formSize.Height, 660))
        MinimumSize = New Size(900, 560)
        StartPosition = FormStartPosition.CenterParent
        DoubleBuffered = True
        KeyPreview = True

        ' === 1. ORACLE PROMPT & RESPONSE REGION (Top) ===
        Dim promptResponseBar As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 68,
            .BackColor = UiTheme.BrandLight,
            .ColumnCount = 2,
            .RowCount = 1,
            .Padding = New Padding(8, 6, 8, 6)
        }
        promptResponseBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68.0F))
        promptResponseBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32.0F))

        Dim promptBox As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .Padding = New Padding(12, 6, 12, 6),
            .Margin = New Padding(3)
        }
        Dim lblTitle As New Label With {
            .Text = "🟢 " & title,
            .Font = UiTheme.Font(12.5F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .Dock = DockStyle.Top,
            .Height = 26,
            .TextAlign = ContentAlignment.MiddleRight
        }
        _lblPromptInstruction.Text = If(String.IsNullOrWhiteSpace(subtitle), "اختر سجلًا من الجدول أو استخدم أزرار أوراكل الجانبية (F2 - F9).", subtitle)
        _lblPromptInstruction.Font = UiTheme.Font(9.0F, FontStyle.Regular)
        _lblPromptInstruction.ForeColor = UiTheme.Muted
        _lblPromptInstruction.Dock = DockStyle.Fill
        _lblPromptInstruction.TextAlign = ContentAlignment.MiddleRight
        promptBox.Controls.Add(_lblPromptInstruction)
        promptBox.Controls.Add(lblTitle)

        Dim responseBox As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = UiTheme.BrandNavy,
            .Padding = New Padding(10, 6, 10, 6),
            .Margin = New Padding(3)
        }
        Dim lblRespHeader As New Label With {
            .Text = "منطقة الاستجابة السريعة (Response Region)",
            .Dock = DockStyle.Top,
            .Height = 18,
            .Font = UiTheme.Font(8.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandGold,
            .TextAlign = ContentAlignment.MiddleRight
        }
        _lblResponseBadge.Text = "قاعدة البيانات متصلة — جاهز للاستعلام والتعديل"
        _lblResponseBadge.Dock = DockStyle.Fill
        _lblResponseBadge.Font = UiTheme.Font(9.5F, FontStyle.Bold)
        _lblResponseBadge.ForeColor = Color.White
        _lblResponseBadge.TextAlign = ContentAlignment.MiddleCenter
        responseBox.Controls.Add(_lblResponseBadge)
        responseBox.Controls.Add(lblRespHeader)

        promptResponseBar.Controls.Add(promptBox, 0, 0)
        promptResponseBar.Controls.Add(responseBox, 1, 0)

        ' === Filter & Action Toolbar (Below Prompt Region) ===
        ToolBar.Dock = DockStyle.Top
        ToolBar.Height = 50
        ToolBar.FlowDirection = FlowDirection.LeftToRight
        ToolBar.WrapContents = False
        ToolBar.AutoScroll = True
        ToolBar.BackColor = Color.FromArgb(238, 247, 239)
        ToolBar.Padding = New Padding(6)

        ' === 3. ORACLE LOCAL NAVIGATION BAR (Right Vertical Bar F2-F9) ===
        Dim rightNavContainer As New Panel With {
            .Dock = DockStyle.Right,
            .Width = 175,
            .BackColor = Color.FromArgb(228, 242, 229),
            .Padding = New Padding(6, 6, 6, 6)
        }
        Dim lblLocalHeader As New Label With {
            .Text = "أوامر الشاشة (Local Nav)",
            .Dock = DockStyle.Top,
            .Height = 24,
            .Font = UiTheme.Font(8.5F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _localNavPanel.Dock = DockStyle.Fill
        _localNavPanel.FlowDirection = FlowDirection.TopDown
        _localNavPanel.WrapContents = False
        _localNavPanel.AutoScroll = True
        _localNavPanel.BackColor = Color.Transparent

        rightNavContainer.Controls.Add(_localNavPanel)
        rightNavContainer.Controls.Add(lblLocalHeader)

        ' === 4 & 5. ORACLE STATUS REGION + GLOBAL NAVIGATION BAR (Bottom) ===
        Dim bottomBrandContainer As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = 108,
            .BackColor = UiTheme.BrandLight
        }

        Footer.Dock = DockStyle.Top
        Footer.Height = 26
        Footer.TextAlign = ContentAlignment.MiddleRight
        Footer.Padding = New Padding(8, 0, 8, 0)
        Footer.BackColor = Color.White
        Footer.ForeColor = UiTheme.BrandNavy
        Footer.Font = UiTheme.Font(9.5F, FontStyle.Bold)

        Dim statusStrip As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 28,
            .ColumnCount = 5,
            .RowCount = 1,
            .BackColor = Color.FromArgb(220, 237, 222),
            .Padding = New Padding(2)
        }
        For i As Integer = 0 To 4
            statusStrip.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20.0F))
        Next
        Dim brName As String = If(Session IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Session.BranchName), Session.BranchName, "الفرع الرئيسي")
        Dim usrName As String = If(Session IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Session.Username), Session.Username, "admin")
        statusStrip.Controls.Add(MakeStatusCell("نقطة: REG-" & If(Session IsNot Nothing, Session.BranchId.ToString("00"), "01") & " (" & brName & ")"), 0, 0)
        statusStrip.Controls.Add(MakeStatusCell("التاريخ: " & Date.Today.ToString("yyyy-MM-dd")), 1, 0)
        statusStrip.Controls.Add(MakeStatusCell("المستخدم: " & usrName), 2, 0)
        statusStrip.Controls.Add(MakeStatusCell("الشاشة: " & title), 3, 0)
        statusStrip.Controls.Add(MakeStatusCell("🟢 Online — متصل بقاعدة البيانات", True), 4, 0)

        Dim globalBar As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 6,
            .RowCount = 1,
            .BackColor = UiTheme.BrandNavy,
            .Padding = New Padding(4, 3, 4, 3)
        }
        For i As Integer = 0 To 4
            globalBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.5F))
        Next
        globalBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 17.5F))

        Dim btnF1 = MakeGlobalBtn("F1 / Help", "مساعدة", UiTheme.BrandBlue)
        AddHandler btnF1.Click, Sub(s, e) UiTheme.Info("شاشة «" & title & "» — تعمل بنظام أوراكل (DOCTOR TECH) ومتصلة مباشرة بقاعدة البيانات." & Environment.NewLine & "استخدم الأزرار الجانبية (F2 - F9) أو شريط الأدوات لتنفيذ العمليات.", "مساعدة أوراكل")

        Dim btnF11 = MakeGlobalBtn("F11 / Delete", "حذف / مسح", UiTheme.DangerRed)
        AddHandler btnF11.Click, Sub(s, e) TriggerToolbarButtonByKeyword("حذف", "إلغاء")

        Dim btnF12 = MakeGlobalBtn("F12 / Cancel", "تحديث البيانات", UiTheme.BrandBlue)
        AddHandler btnF12.Click, Sub(s, e) Reload()

        Dim btnEsc = MakeGlobalBtn("Esc / Undo", "رجوع وإغلاق", UiTheme.BrandBlue)
        AddHandler btnEsc.Click, Sub(s, e) Close()

        Dim btnEnter = MakeGlobalBtn("Enter / Next", "تنفيذ / حفظ", Color.FromArgb(46, 125, 50))
        AddHandler btnEnter.Click, Sub(s, e) TriggerPrimaryAction()

        Dim pnlBrandLogo As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .Margin = New Padding(3)
        }
        Dim lblLogo As New Label With {
            .Text = "ORACLE®" & Environment.NewLine & "RETAIL POS",
            .Dock = DockStyle.Fill,
            .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
            .ForeColor = UiTheme.BrandNavy,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        pnlBrandLogo.Controls.Add(lblLogo)

        globalBar.Controls.Add(btnF1, 0, 0)
        globalBar.Controls.Add(btnF11, 1, 0)
        globalBar.Controls.Add(btnF12, 2, 0)
        globalBar.Controls.Add(btnEsc, 3, 0)
        globalBar.Controls.Add(btnEnter, 4, 0)
        globalBar.Controls.Add(pnlBrandLogo, 5, 0)

        bottomBrandContainer.Controls.Add(globalBar)
        bottomBrandContainer.Controls.Add(statusStrip)
        bottomBrandContainer.Controls.Add(Footer)

        Grid.Dock = DockStyle.Fill

        Controls.Add(Grid)
        Controls.Add(rightNavContainer)
        Controls.Add(ToolBar)
        Controls.Add(promptResponseBar)
        Controls.Add(bottomBrandContainer)

        AddHandler Me.Shown, Sub(s, e) RebuildBrandLocalNavButtons()
        AddHandler Me.KeyDown, AddressOf SectionKeyDown
    End Sub

    Private Shared Function MakeStatusCell(text As String, Optional isOnline As Boolean = False) As Label
        Return New Label With {
            .Text = text,
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .ForeColor = If(isOnline, UiTheme.BrandNavy, UiTheme.Ink),
            .Font = UiTheme.Font(8.5F, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Margin = New Padding(2, 1, 2, 1)
        }
    End Function

    Private Shared Function MakeGlobalBtn(fKey As String, caption As String, back As Color) As Button
        Dim b As New Button With {
            .Text = fKey & Environment.NewLine & caption,
            .Dock = DockStyle.Fill,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = back,
            .ForeColor = Color.White,
            .Font = UiTheme.Font(8.8F, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(3, 2, 3, 2)
        }
        b.FlatAppearance.BorderColor = Color.FromArgb(120, 200, 125)
        b.FlatAppearance.BorderSize = 1
        Return b
    End Function

    ''' <summary>Mirrors the screen's ToolBar buttons into the right-hand Oracle F2-F9 Local Navigation Bar.</summary>
    Private Sub RebuildBrandLocalNavButtons()
        _localNavPanel.SuspendLayout()
        _localNavPanel.Controls.Clear()

        Dim fIndex As Integer = 2
        For Each ctl As Control In ToolBar.Controls
            If TypeOf ctl Is Button AndAlso fIndex <= 8 Then
                Dim srcBtn = DirectCast(ctl, Button)
                Dim fKey As String = "F" & fIndex.ToString()
                Dim navBtn As New Button With {
                    .Text = fKey & " / " & srcBtn.Text,
                    .Width = 158,
                    .Height = 44,
                    .FlatStyle = FlatStyle.Flat,
                    .BackColor = If(srcBtn.BackColor = UiTheme.DangerRed, UiTheme.DangerRed, UiTheme.BrandBlue),
                    .ForeColor = Color.White,
                    .Font = UiTheme.Font(9.5F, FontStyle.Bold),
                    .Cursor = Cursors.Hand,
                    .Margin = New Padding(2, 3, 2, 3)
                }
                navBtn.FlatAppearance.BorderColor = UiTheme.BrandBorder
                Dim targetBtn As Button = srcBtn
                AddHandler navBtn.Click, Sub(s, e) targetBtn.PerformClick()
                _localNavPanel.Controls.Add(navBtn)
                fIndex += 1
            End If
        Next

        ' Always provide CSV Export on F9
        Dim btnCsv As New Button With {
            .Text = "F9 / تصدير CSV",
            .Width = 158,
            .Height = 44,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = UiTheme.BrandNavy,
            .ForeColor = UiTheme.BrandGold,
            .Font = UiTheme.Font(9.5F, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(2, 3, 2, 3)
        }
        btnCsv.FlatAppearance.BorderColor = UiTheme.BrandBorder
        AddHandler btnCsv.Click, Sub(s, e) ExportCsv(Me.Text & "_" & Date.Today.ToString("yyyyMMdd") & ".csv")
        _localNavPanel.Controls.Add(btnCsv)

        _localNavPanel.ResumeLayout()
    End Sub

    Private Sub TriggerToolbarButtonByKeyword(ParamArray keywords() As String)
        For Each ctl As Control In ToolBar.Controls
            Dim b = TryCast(ctl, Button)
            If b IsNot Nothing Then
                For Each kw In keywords
                    If b.Text.Contains(kw) Then
                        b.PerformClick()
                        Return
                    End If
                Next
            End If
        Next
    End Sub

    Private Sub TriggerPrimaryAction()
        For Each ctl As Control In ToolBar.Controls
            Dim b = TryCast(ctl, Button)
            If b IsNot Nothing AndAlso (b.Text.Contains("جديد") OrElse b.Text.Contains("إضافة") OrElse b.Text.Contains("حفظ") OrElse b.Text.Contains("إنشاء") OrElse b.Text.Contains("فتح")) Then
                b.PerformClick()
                Return
            End If
        Next
        Reload()
    End Sub

    Private Sub SectionKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode >= Keys.F2 AndAlso e.KeyCode <= Keys.F8 Then
            Dim idx As Integer = e.KeyCode - Keys.F2
            If idx >= 0 AndAlso idx < _localNavPanel.Controls.Count Then
                Dim b = TryCast(_localNavPanel.Controls(idx), Button)
                If b IsNot Nothing Then
                    b.PerformClick()
                    e.Handled = True
                End If
            End If
        ElseIf e.KeyCode = Keys.F9 Then
            ExportCsv(Me.Text & "_" & Date.Today.ToString("yyyyMMdd") & ".csv")
            e.Handled = True
        ElseIf e.KeyCode = Keys.F12 Then
            Reload()
            e.Handled = True
        End If
    End Sub

    Protected MustOverride Sub LoadData()

    Protected Sub Reload()
        Try
            Cursor = Cursors.WaitCursor
            LoadData()
            _lblResponseBadge.Text = "تم التحديث من قاعدة البيانات (" & Grid.Rows.Count.ToString("N0") & " سجل)"
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Protected Sub AddSearch(placeholder As String, handler As Action)
        _searchAction = handler
        _searchBox = New TextBox With {
            .Width = 270,
            .PlaceholderText = placeholder,
            .Margin = New Padding(4)
        }
        ToolBar.Controls.Add(UiTheme.MakeLabel("بحث فوري:", True))
        ToolBar.Controls.Add(_searchBox)

        Dim btnSearch As Button = UiTheme.PrimaryButton("بحث", 85)
        AddHandler btnSearch.Click, Sub(s, e) handler()
        AddHandler _searchBox.KeyDown, Sub(s, e)
                                           If e.KeyCode = Keys.Enter Then
                                               e.SuppressKeyPress = True
                                               handler()
                                           End If
                                       End Sub
        ToolBar.Controls.Add(btnSearch)
    End Sub

    Protected ReadOnly Property SearchText As String
        Get
            Return If(_searchBox Is Nothing, String.Empty, _searchBox.Text)
        End Get
    End Property

    Protected Sub AddRefresh(handler As Action)
        Dim btn As Button = UiTheme.MakeButton("تحديث", 90)
        AddHandler btn.Click, Sub(s, e) handler()
        ToolBar.Controls.Add(btn)
    End Sub

    Protected Sub SetFooter(text As String)
        Footer.Text = text
    End Sub

    Protected Function SelectedItem(Of T As Class)() As T
        If Grid.CurrentRow Is Nothing Then Return Nothing
        Return TryCast(Grid.CurrentRow.DataBoundItem, T)
    End Function

    Protected Sub ExportCsv(defaultFileName As String)
        If Grid.Rows.Count = 0 Then
            UiTheme.Warn("لا توجد بيانات للتصدير.")
            Return
        End If
        Using dialog As New SaveFileDialog With {
            .Filter = "ملف CSV|*.csv",
            .FileName = defaultFileName,
            .AddExtension = True
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim sb As New StringBuilder()
            Dim headers As New List(Of String)()
            For Each column As DataGridViewColumn In Grid.Columns
                headers.Add(QuoteCsv(column.HeaderText))
            Next
            sb.AppendLine(String.Join(",", headers.ToArray()))
            For Each row As DataGridViewRow In Grid.Rows
                If row.IsNewRow Then Continue For
                Dim values As New List(Of String)()
                For Each cell As DataGridViewCell In row.Cells
                    values.Add(QuoteCsv(Convert.ToString(cell.FormattedValue)))
                Next
                sb.AppendLine(String.Join(",", values.ToArray()))
            Next
            File.WriteAllText(dialog.FileName, sb.ToString(), New UTF8Encoding(True))
        End Using
        UiTheme.Info("تم تصدير البيانات بنجاح.")
    End Sub

    Private Shared Function QuoteCsv(value As String) As String
        Return """" & If(value, String.Empty).Replace("""", """""") & """"
    End Function
End Class
