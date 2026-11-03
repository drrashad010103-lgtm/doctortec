Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Company identity block printed at the top of invoices and statements: logo,
''' company name (Arabic and English), tax number, phone, address and an extra
''' note. Everything comes from «بيانات الشركة / ترويسة الطباعة» settings.
''' The logo can sit on the right, on the left, centered above the text, or in a
''' separated side column.
''' </summary>
Public NotInheritable Class PrintHeader
    Public Const LayoutRight As String = "right"
    Public Const LayoutLeft As String = "left"
    Public Const LayoutCenter As String = "center"
    Public Const LayoutSide As String = "side"

    Private Sub New()
    End Sub

    Public Shared Function Load(session As AppSession) As CompanyPrintSettings
        Try
            Using db = DbSession.Open(session.ConnectionString)
                Return CompanyPrintRepository.Load(db.Connection)
            End Using
        Catch ex As Exception
            AppLog.Warn("Print header load failed: " & ex.Message)
            Return New CompanyPrintSettings()
        End Try
    End Function

    ''' <summary>Loads the logo into memory so the file is never locked while printing.</summary>
    Public Shared Function LogoImage(settings As CompanyPrintSettings) As Image
        If settings Is Nothing OrElse Not settings.ShowLogo Then Return Nothing
        If String.IsNullOrWhiteSpace(settings.LogoPath) Then Return Nothing
        Try
            If Not File.Exists(settings.LogoPath) Then Return Nothing
            Dim bytes As Byte() = File.ReadAllBytes(settings.LogoPath)
            Using stream As New MemoryStream(bytes)
                Return Image.FromStream(stream)
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Public Shared Function NormaliseLayout(value As String) As String
        Select Case If(value, String.Empty).Trim().ToLowerInvariant()
            Case LayoutLeft : Return LayoutLeft
            Case LayoutCenter : Return LayoutCenter
            Case LayoutSide : Return LayoutSide
            Case Else : Return LayoutRight
        End Select
    End Function

    ''' <summary>
    ''' Draws the header inside <paramref name="bounds"/> and returns the height it used.
    ''' <paramref name="compact"/> is used for narrow receipt printing.
    ''' </summary>
    Public Shared Function Draw(g As Graphics, bounds As Rectangle, settings As CompanyPrintSettings,
                                fallbackName As String, Optional compact As Boolean = False) As Integer
        If settings Is Nothing Then settings = New CompanyPrintSettings()
        Dim name As String = If(settings.HasName, settings.CompanyName,
                                If(String.IsNullOrWhiteSpace(fallbackName), String.Empty, fallbackName))
        Dim layout As String = If(compact, LayoutRight, NormaliseLayout(settings.HeaderLayout))
        Dim logoHeight As Integer = If(compact, 36, 58)
        Dim logoRect As Rectangle = Rectangle.Empty

        Dim logo As Image = LogoImage(settings)
        If logo IsNot Nothing Then
            Using logo
                Dim ratio As Single = logo.Width / CSng(Math.Max(1, logo.Height))
                Dim drawWidth As Integer = Math.Min(settings.LogoWidth, Math.Max(40, bounds.Width \ 4))
                Dim drawHeight As Integer = CInt(drawWidth / ratio)
                If drawHeight > logoHeight Then
                    drawHeight = logoHeight
                    drawWidth = CInt(drawHeight * ratio)
                End If
                Select Case layout
                    Case LayoutCenter
                        logoRect = New Rectangle(bounds.Left + (bounds.Width - drawWidth) \ 2, bounds.Top, drawWidth, drawHeight)
                    Case LayoutLeft
                        logoRect = New Rectangle(bounds.Left, bounds.Top, drawWidth, drawHeight)
                    Case Else
                        logoRect = New Rectangle(bounds.Right - drawWidth, bounds.Top, drawWidth, drawHeight)
                End Select
                g.DrawImage(logo, logoRect)
            End Using
        End If

        Dim centered As Boolean = layout = LayoutCenter
        Dim titleFont As Font = UiTheme.Font(If(compact, 10.0F, 13.0F), FontStyle.Bold)
        Dim smallFont As Font = UiTheme.Font(If(compact, 7.5F, 9.0F))
        Dim rtl As TextFormatFlags = If(centered, TextFormatFlags.HorizontalCenter, TextFormatFlags.Right) Or
                                     TextFormatFlags.RightToLeft Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis
        Dim plain As TextFormatFlags = If(centered, TextFormatFlags.HorizontalCenter, TextFormatFlags.Right) Or
                                       TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis

        Dim textLeft As Integer = bounds.Left
        Dim textRight As Integer = bounds.Right
        Dim y As Integer = bounds.Top
        If centered AndAlso logoRect <> Rectangle.Empty Then y = logoRect.Bottom + 6
        If layout = LayoutLeft AndAlso logoRect <> Rectangle.Empty Then textLeft = logoRect.Right + 14
        If layout = LayoutSide AndAlso logoRect <> Rectangle.Empty Then
            textRight = logoRect.Left - 16
            g.DrawLine(Pens.Gainsboro, logoRect.Left - 10, bounds.Top + 2, logoRect.Left - 10,
                       bounds.Top + Math.Max(logoHeight, 56))
        ElseIf layout = LayoutRight AndAlso logoRect <> Rectangle.Empty Then
            textRight = logoRect.Left - 14
        End If
        Dim textWidth As Integer = Math.Max(40, textRight - textLeft)

        If name.Length > 0 Then
            TextRenderer.DrawText(g, name, titleFont, New Rectangle(textLeft, y, textWidth, 24), Color.Black, rtl)
            y += If(compact, 20, 24)
        End If

        If Not String.IsNullOrWhiteSpace(settings.CompanyNameEn) Then
            TextRenderer.DrawText(g, settings.CompanyNameEn, smallFont, New Rectangle(textLeft, y, textWidth, 16), Color.Black, plain)
            y += 16
        End If

        If Not compact AndAlso Not String.IsNullOrWhiteSpace(settings.HeaderNote) Then
            TextRenderer.DrawText(g, settings.HeaderNote, smallFont, New Rectangle(textLeft, y, textWidth, 16), Color.Black, rtl)
            y += 16
        End If

        If centered Then
            y = DrawCenteredIdentity(g, bounds, y, smallFont, settings)
        Else
            Dim rightEdge As Integer = textRight
            If settings.ShowTaxNo AndAlso Not String.IsNullOrWhiteSpace(settings.TaxNo) Then
                Const labelWidth As Integer = 78
                Const valueWidth As Integer = 130
                TextRenderer.DrawText(g, "الرقم الضريبي:", smallFont, New Rectangle(rightEdge - labelWidth, y, labelWidth, 16), Color.Black, rtl)
                TextRenderer.DrawText(g, settings.TaxNo, smallFont,
                                      New Rectangle(rightEdge - labelWidth - valueWidth, y, valueWidth, 16), Color.Black, plain)
                rightEdge -= labelWidth + valueWidth + 8
            End If
            If Not compact AndAlso Not String.IsNullOrWhiteSpace(settings.Phone) Then
                Const labelWidth As Integer = 42
                Const valueWidth As Integer = 120
                TextRenderer.DrawText(g, "هاتف:", smallFont, New Rectangle(rightEdge - labelWidth, y, labelWidth, 16), Color.Black, rtl)
                TextRenderer.DrawText(g, settings.Phone, smallFont,
                                      New Rectangle(rightEdge - labelWidth - valueWidth, y, valueWidth, 16), Color.Black, plain)
            End If
            y += 18
        End If

        If Not compact AndAlso Not String.IsNullOrWhiteSpace(settings.Address) Then
            TextRenderer.DrawText(g, settings.Address, smallFont, New Rectangle(textLeft, y, textWidth, 16), Color.DimGray, rtl)
            y += 16
        End If

        y += 4
        Dim bottom As Integer = y
        If logoRect <> Rectangle.Empty AndAlso logoRect.Bottom + 4 > bottom Then bottom = logoRect.Bottom + 4
        g.DrawLine(Pens.Gray, bounds.Left, bottom, bounds.Right, bottom)
        Return bottom - bounds.Top + 2
    End Function

    ''' <summary>Tax number and phone, each pair centered as a group.</summary>
    Private Shared Function DrawCenteredIdentity(g As Graphics, bounds As Rectangle, y As Integer,
                                                 font As Font, settings As CompanyPrintSettings) As Integer
        Dim pairs As New List(Of Tuple(Of String, String))()
        If settings.ShowTaxNo AndAlso Not String.IsNullOrWhiteSpace(settings.TaxNo) Then
            pairs.Add(Tuple.Create("الرقم الضريبي:", settings.TaxNo))
        End If
        If Not String.IsNullOrWhiteSpace(settings.Phone) Then
            pairs.Add(Tuple.Create("هاتف:", settings.Phone))
        End If

        For Each pair In pairs
            Dim labelSize = TextRenderer.MeasureText(pair.Item1, font)
            Dim valueSize = TextRenderer.MeasureText(pair.Item2, font)
            Dim totalWidth As Integer = labelSize.Width + valueSize.Width + 6
            Dim x As Integer = bounds.Left + Math.Max(0, (bounds.Width - totalWidth) \ 2)
            TextRenderer.DrawText(g, pair.Item1, font, New Rectangle(x, y, labelSize.Width, 16), Color.Black,
                                  TextFormatFlags.Left Or TextFormatFlags.RightToLeft Or TextFormatFlags.VerticalCenter)
            TextRenderer.DrawText(g, pair.Item2, font, New Rectangle(x + labelSize.Width + 6, y, valueSize.Width + 4, 16),
                                  Color.Black, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
            y += 16
        Next
        Return y
    End Function
End Class
