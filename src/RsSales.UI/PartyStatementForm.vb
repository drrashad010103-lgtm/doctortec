Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Printing
Imports System.Windows.Forms

''' <summary>
''' Detailed account statement for one customer or supplier: the balance carried
''' into the period, then every invoice and payment in date order with a running
''' balance, and the closing balance at the end.
''' </summary>
Public Class PartyStatementForm
    Inherits SectionFormBase

    Private ReadOnly _customerId As Long
    Private ReadOnly dtpFrom As New DateTimePicker()
    Private ReadOnly dtpTo As New DateTimePicker()
    Private ReadOnly lblParty As New Label()
    Private _statement As PartyStatement
    Private _rows As New List(Of PartyStatementLine)()
    Private _printIndex As Integer
    Private _printPageNo As Integer
    Private _printHeader As CompanyPrintSettings

    Private Shared ReadOnly PrintColumns As Integer() = {95, 115, 105, 300, 110, 110, 120}
    Private Shared ReadOnly PrintHeaders As String() = {"التاريخ", "المستند", "رقم المستند", "البيان", "مدين", "دائن", "الرصيد"}
    ' Dates, references and amounts must not be mirrored: draw them without RightToLeft.
    Private Shared ReadOnly PrintRtl As Boolean() = {False, True, False, True, False, False, False}
    Private Const PrintRowHeight As Integer = 24

    Public Sub New(session As AppSession, customerId As Long)
        MyBase.New(session, "كشف حساب تفصيلي",
                   "الرصيد الافتتاحي وكل الحركات بالتواريخ مع الرصيد الجاري",
                   Color.FromArgb(24, 58, 92), New Size(1080, 660))
        _customerId = customerId

        lblParty.AutoSize = True
        lblParty.Font = UiTheme.Font(10.0F, FontStyle.Bold)
        lblParty.ForeColor = UiTheme.Navy
        lblParty.Margin = New Padding(8, 9, 4, 4)
        ToolBar.Controls.Add(lblParty)

        ToolBar.Controls.Add(UiTheme.MakeLabel("من:"))
        dtpFrom.Format = DateTimePickerFormat.Custom
        dtpFrom.CustomFormat = "yyyy-MM-dd"
        dtpFrom.Width = 120
        dtpFrom.Margin = New Padding(4, 5, 4, 4)
        dtpFrom.Value = New Date(2000, 1, 1)
        ToolBar.Controls.Add(dtpFrom)

        ToolBar.Controls.Add(UiTheme.MakeLabel("إلى:"))
        dtpTo.Format = DateTimePickerFormat.Custom
        dtpTo.CustomFormat = "yyyy-MM-dd"
        dtpTo.Width = 120
        dtpTo.Margin = New Padding(4, 5, 4, 4)
        dtpTo.Value = Date.Today
        ToolBar.Controls.Add(dtpTo)

        AddRefresh(AddressOf Reload)

        Dim btnExport As Button = UiTheme.MakeButton("تصدير CSV", 110)
        AddHandler btnExport.Click, Sub(s, e) ExportCsv("statement-" & _customerId & "-" & Date.Now.ToString("yyyyMMdd") & ".csv")
        ToolBar.Controls.Add(btnExport)

        Dim btnPrint As Button = UiTheme.PrimaryButton("🖨 طباعة", 120)
        AddHandler btnPrint.Click, Sub(s, e) PrintStatement(False)
        ToolBar.Controls.Add(btnPrint)

        Dim btnPdf As Button = UiTheme.MakeButton("حفظ PDF", 110)
        AddHandler btnPdf.Click, Sub(s, e) PrintStatement(True)
        ToolBar.Controls.Add(btnPdf)

        UiTheme.AddTextColumn(Grid, "EntryDate", "التاريخ", 100, False, "yyyy-MM-dd")
        UiTheme.AddTextColumn(Grid, "DocumentName", "المستند", 120)
        UiTheme.AddTextColumn(Grid, "Reference", "رقم المستند", 130)
        UiTheme.AddTextColumn(Grid, "Description", "البيان", 260, True)
        UiTheme.AddTextColumn(Grid, "Debit", "مدين", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "Credit", "دائن", 110, False, "N2")
        UiTheme.AddTextColumn(Grid, "Balance", "الرصيد", 120, False, "N2")

        Reload()
    End Sub

    Protected Overrides Sub LoadData()
        _statement = CustomerFinanceService.Statement(Session, _customerId, dtpFrom.Value.Date, dtpTo.Value.Date)
        lblParty.Text = $"{_statement.PartyTypeName}: {_statement.PartyName}"

        _rows = New List(Of PartyStatementLine)()
        _rows.Add(New PartyStatementLine With {
            .EntryDate = _statement.FromDate,
            .Kind = "balance",
            .Description = "رصيد ما قبل الفترة",
            .Balance = _statement.OpeningBalance
        })
        _rows.AddRange(_statement.Lines)
        Grid.DataSource = New BindingSource With {.DataSource = _rows}

        SetFooter($"الرصيد الافتتاحي: {UiTheme.Money(_statement.OpeningBalance)}" &
                  $"   |   مدين: {UiTheme.Money(_statement.TotalDebit)}" &
                  $"   |   دائن: {UiTheme.Money(_statement.TotalCredit)}" &
                  $"   |   الرصيد النهائي: {UiTheme.Money(_statement.ClosingBalance)}")
    End Sub

    ' -------------------------------------------------------------- printing --
    Private Sub PrintStatement(toPdf As Boolean)
        If _statement Is Nothing OrElse _rows.Count = 0 Then
            UiTheme.Warn("لا يوجد كشف للطباعة، حدّث البيانات أولًا.")
            Return
        End If
        _printHeader = PrintHeader.Load(Session)
        Try
            Using document As New PrintDocument()
                document.DocumentName = "كشف حساب - " & _statement.PartyName
                document.DefaultPageSettings.Landscape = True
                document.DefaultPageSettings.Margins = New Margins(40, 40, 40, 40)
                AddHandler document.BeginPrint, Sub(s, e)
                                                    _printIndex = 0
                                                    _printPageNo = 0
                                                End Sub
                AddHandler document.PrintPage, AddressOf PrintStatementPage

                If toPdf Then
                    Const pdfPrinter As String = "Microsoft Print to PDF"
                    If Not InstalledPrinters().Contains(pdfPrinter) Then
                        UiTheme.Warn("طابعة «Microsoft Print to PDF» غير مثبتة على هذا الجهاز. استخدم زر «طباعة» ثم اختر حفظ PDF من نافذة الطابعة.")
                        Return
                    End If
                    document.PrinterSettings.PrinterName = pdfPrinter
                    document.Print()
                Else
                    Using preview As New PrintPreviewDialog()
                        preview.Document = document
                        preview.Width = 1040
                        preview.Height = 740
                        preview.ShowDialog(Me)
                    End Using
                End If
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّرت الطباعة")
        End Try
    End Sub

    Private Shared Function InstalledPrinters() As List(Of String)
        Dim names As New List(Of String)()
        For Each printerName As String In PrinterSettings.InstalledPrinters
            names.Add(printerName)
        Next
        Return names
    End Function

    Private Structure PrintSegment
        Public Text As String
        Public Font As Font
        Public Width As Integer
        Public Rtl As Boolean
    End Structure

    Private Shared Function Segment(text As String, font As Font, width As Integer, rtl As Boolean) As PrintSegment
        Return New PrintSegment With {.Text = text, .Font = font, .Width = width, .Rtl = rtl}
    End Function

    ''' <summary>Draws right-aligned pieces from the right edge, each with its own bidi direction.</summary>
    Private Sub DrawSegments(g As Graphics, rightPos As Integer, y As Integer, height As Integer,
                             segments As PrintSegment())
        Dim x As Integer = rightPos
        For Each part In segments
            Dim flags As TextFormatFlags = TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or
                                           TextFormatFlags.EndEllipsis
            If part.Rtl Then flags = flags Or TextFormatFlags.RightToLeft
            TextRenderer.DrawText(g, part.Text, part.Font,
                                  New Rectangle(x - part.Width, y, part.Width, height), Color.Black, flags)
            x -= part.Width
        Next
    End Sub

    Private Sub PrintStatementPage(sender As Object, e As PrintPageEventArgs)
        _printPageNo += 1
        Dim g = e.Graphics
        Dim bold As Font = UiTheme.Font(10.0F, FontStyle.Bold)
        Dim normal As Font = UiTheme.Font(9.0F)
        Dim muted As Font = UiTheme.Font(8.5F)
        Dim rightPos As Integer = e.MarginBounds.Right
        Dim leftPos As Integer = e.MarginBounds.Left
        Dim width As Integer = e.MarginBounds.Width
        Dim y As Integer = e.MarginBounds.Top
        Dim rtl As TextFormatFlags = TextFormatFlags.Right Or TextFormatFlags.RightToLeft Or
                                     TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis

        Dim company As CompanyPrintSettings = If(_printHeader, New CompanyPrintSettings())
        y += PrintHeader.Draw(g, New Rectangle(leftPos, y, width, 82), company, Session.BranchName) + 4
        TextRenderer.DrawText(g, "كشف حساب تفصيلي — " & _statement.PartyTypeName & ": " & _statement.PartyName,
                              bold, New Rectangle(leftPos, y, width, 24), Color.Black, rtl)
        y += 26
        Dim periodSegments As PrintSegment() = {
            Segment("الرصيد الافتتاحي:", bold, 135, True),
            Segment(UiTheme.Money(_statement.OpeningBalance), normal, 110, False),
            Segment("الفترة:", bold, 55, True),
            Segment(_statement.FromDate.ToString("yyyy-MM-dd") & "  ←  " & _statement.ToDate.ToString("yyyy-MM-dd"),
                    normal, 230, False)
        }
        DrawSegments(g, rightPos, y, 22, periodSegments)
        y += 22
        Dim printedAtSegments As PrintSegment() = {
            Segment("تاريخ الطباعة:", muted, 95, True),
            Segment(Date.Now.ToString("yyyy-MM-dd HH:mm"), muted, 120, False),
            Segment("صفحة", muted, 40, True),
            Segment(_printPageNo.ToString(), muted, 30, False)
        }
        DrawSegments(g, rightPos, y, 20, printedAtSegments)
        y += 26
        g.DrawLine(Pens.Gray, leftPos, y, rightPos, y)
        y += 8

        DrawPrintRow(g, rightPos, PrintHeaders, y, bold, Color.FromArgb(232, 235, 242), Nothing)
        y += PrintRowHeight + 2

        While _printIndex < _rows.Count
            If y + PrintRowHeight > e.MarginBounds.Bottom - 42 Then
                e.HasMorePages = True
                Return
            End If
            Dim line As PartyStatementLine = _rows(_printIndex)
            Dim values As String() = {
                line.EntryDate.ToString("yyyy-MM-dd"),
                line.DocumentName,
                If(line.Reference, String.Empty),
                If(line.Description, String.Empty),
                PrintAmount(line.Debit),
                PrintAmount(line.Credit),
                UiTheme.Money(line.Balance)
            }
            DrawPrintRow(g, rightPos, values, y, normal, Color.Transparent, PrintRtl)
            y += PrintRowHeight
            _printIndex += 1
        End While

        y += 8
        g.DrawLine(Pens.Gray, leftPos, y, rightPos, y)
        y += 12
        Dim totalsSegments As PrintSegment() = {
            Segment("الرصيد النهائي:", bold, 125, True),
            Segment(UiTheme.Money(_statement.ClosingBalance), bold, 120, False),
            Segment("|", bold, 16, False),
            Segment("إجمالي دائن:", bold, 110, True),
            Segment(UiTheme.Money(_statement.TotalCredit), bold, 110, False),
            Segment("|", bold, 16, False),
            Segment("إجمالي مدين:", bold, 110, True),
            Segment(UiTheme.Money(_statement.TotalDebit), bold, 110, False)
        }
        DrawSegments(g, rightPos, y, 26, totalsSegments)

        If Not String.IsNullOrWhiteSpace(company.FooterNote) Then
            y += 30
            TextRenderer.DrawText(g, company.FooterNote, muted, New Rectangle(leftPos, y, width, 22), Color.DimGray, rtl)
        End If
        e.HasMorePages = False
    End Sub

    ''' <summary>One table row; rtlFlags selects per-column bidi direction (Nothing = all Arabic).</summary>
    Private Sub DrawPrintRow(g As Graphics, rightPos As Integer, values As String(), y As Integer,
                             font As Font, backColor As Color, rtlFlags As Boolean())
        Dim offset As Integer = 0
        Using brush As New SolidBrush(backColor)
            For index As Integer = 0 To PrintColumns.Length - 1
                Dim columnWidth As Integer = PrintColumns(index)
                Dim cell As New Rectangle(rightPos - offset - columnWidth, y, columnWidth, PrintRowHeight)
                If backColor <> Color.Transparent Then g.FillRectangle(brush, cell)
                g.DrawRectangle(Pens.Silver, cell)
                Dim text As String = If(index < values.Length, values(index), String.Empty)
                Dim flags As TextFormatFlags = TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or
                                               TextFormatFlags.EndEllipsis
                If rtlFlags Is Nothing OrElse rtlFlags(index) Then flags = flags Or TextFormatFlags.RightToLeft
                Dim inner As New Rectangle(cell.X + 4, cell.Y + 3, cell.Width - 8, cell.Height - 6)
                TextRenderer.DrawText(g, text, font, inner, Color.Black, flags)
                offset += columnWidth
            Next
        End Using
    End Sub

    Private Shared Function PrintAmount(value As Decimal) As String
        If value = 0D Then Return "-"
        Return UiTheme.Money(value)
    End Function
End Class
