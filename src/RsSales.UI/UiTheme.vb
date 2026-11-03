Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' DOCTOR TECH shared look and feel.
''' - Executive navy / blue / cyan palette taken from the official company logo.
''' - Rounded glass controls, brand gradients and consistent screen insets.
''' - Keeps the historical public API so every existing screen picks up the new brand.
''' </summary>
Public NotInheritable Class UiTheme
    Private Sub New()
    End Sub

    ' === DOCTOR TECH Brand Palette (sampled from the company logo) ==========
    Public Const ScreenMargin1Cm As Integer = 38
    ''' <summary>0.5 cm at 96 DPI (~19 px) — the standard inset on every screen edge.</summary>
    Public Const ScreenMarginHalfCm As Integer = 19

    Public Shared ReadOnly BrandNavyDark As Color = Color.FromArgb(5, 45, 108)      ' #052D6C
    Public Shared ReadOnly BrandNavy As Color = Color.FromArgb(2, 56, 125)          ' #02387D
    Public Shared ReadOnly BrandBlue As Color = Color.FromArgb(29, 133, 218)        ' #1D85DA
    Public Shared ReadOnly BrandCyan As Color = Color.FromArgb(19, 147, 223)        ' #1393DF
    Public Shared ReadOnly BrandLight As Color = Color.White
    Public Shared ReadOnly BrandMist As Color = Color.FromArgb(245, 247, 250)
    Public Shared ReadOnly BrandBorder As Color = Color.FromArgb(169, 204, 234)    ' #A9CCEA
    Public Shared ReadOnly BrandGold As Color = Color.FromArgb(214, 178, 94)        ' Premium accent
    Public Shared ReadOnly BrandWhite As Color = Color.FromArgb(250, 252, 255)

    Public Shared ReadOnly DangerRed As Color = Color.FromArgb(198, 40, 40)
    Public Shared ReadOnly WarningOrange As Color = Color.FromArgb(210, 110, 20)
    Public Shared ReadOnly SuccessGreen As Color = Color.FromArgb(22, 128, 108)

    ' === Backward compatible aliases =========================================
    Public Shared ReadOnly Navy As Color = BrandNavy
    Public Shared ReadOnly Teal As Color = BrandCyan
    Public Shared ReadOnly Accent As Color = BrandBlue
    Public Shared ReadOnly Danger As Color = DangerRed
    Public Shared ReadOnly Success As Color = SuccessGreen
    Public Shared ReadOnly Surface As Color = Color.White
    Public Shared ReadOnly Ink As Color = Color.FromArgb(16, 35, 63)
    Public Shared ReadOnly Muted As Color = Color.FromArgb(91, 114, 144)
    Public Shared ReadOnly DeepTechNavy As Color = BrandNavyDark
    Public Shared ReadOnly DigitalBlue As Color = BrandNavy
    Public Shared ReadOnly ElectricBlue As Color = BrandBlue
    Public Shared ReadOnly SoftTechBlue As Color = BrandCyan
    Public Shared ReadOnly IceBlue As Color = BrandLight
    Public Shared ReadOnly PageBg As Color = Color.White

    Private Shared ReadOnly UiFontFamily As String = ResolveFontFamily()
    Private Shared _brandLogo As Image

    ''' <summary>First installed family wins; Cairo is the design font, the rest are fallbacks.</summary>
    Private Shared Function ResolveFontFamily() As String
        Dim candidates As String() = {"Cairo", "Tajawal", "Droid Sans Arabic", "Tahoma", "Segoe UI"}
        Try
            Using installed As New System.Drawing.Text.InstalledFontCollection()
                For Each candidate As String In candidates
                    For Each family As System.Drawing.FontFamily In installed.Families
                        If String.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase) Then Return candidate
                    Next
                Next
            End Using
        Catch
        End Try
        Return "Segoe UI"
    End Function

    Public Shared Function Font(size As Single, Optional style As FontStyle = FontStyle.Regular) As Font
        Return New Font(UiFontFamily, size, style, GraphicsUnit.Point)
    End Function

    Public Shared Sub Apply(form As Form)
        form.RightToLeft = RightToLeft.Yes
        form.RightToLeftLayout = True
        form.Font = Font(10.0F)
        form.BackColor = Surface
        form.ForeColor = Ink
        form.StartPosition = FormStartPosition.CenterParent
        ApplyIcon(form)
    End Sub

    ''' <summary>Uses the executable icon so every window matches the desktop shortcut.</summary>
    Public Shared Sub ApplyIcon(form As Form)
        Try
            Dim appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            If appIcon IsNot Nothing Then form.Icon = appIcon
        Catch
            ' Non-Windows or missing icon: keep the default.
        End Try
    End Sub

    ' =========================================================================
    '  BRAND ASSETS
    ' =========================================================================

    ''' <summary>Locates the official logo in the assets folder next to the executable.</summary>
    Public Shared Function FindBrandAsset(fileName As String) As String
        Dim candidates As New List(Of String)()
        Dim baseDir As String = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        Dim dir As String = baseDir
        For i As Integer = 0 To 6
            If dir Is Nothing Then Exit For
            candidates.Add(Path.Combine(dir, "assets", fileName))
            candidates.Add(Path.Combine(dir, fileName))
            dir = Path.GetDirectoryName(dir)
        Next
        Try
            Dim currentDir As String = Directory.GetCurrentDirectory()
            candidates.Add(Path.Combine(currentDir, "assets", fileName))
            candidates.Add(Path.Combine(currentDir, fileName))
        Catch
        End Try
        For Each candidate In candidates
            Try
                If File.Exists(candidate) Then Return candidate
            Catch
            End Try
        Next
        Return Nothing
    End Function

    ''' <summary>Cached official DOCTOR TECH logo, or Nothing when the asset is missing.</summary>
    Public Shared Function LoadBrandLogo() As Image
        If _brandLogo IsNot Nothing Then Return _brandLogo
        Dim path As String = FindBrandAsset("doctor-tech-logo.png")
        If path Is Nothing Then Return Nothing
        Try
            Using stream As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using source As Image = Image.FromStream(stream)
                    _brandLogo = New Bitmap(source)
                End Using
            End Using
        Catch
            _brandLogo = Nothing
        End Try
        Return _brandLogo
    End Function

    ''' <summary>
    ''' Paints the executive navy backdrop used by the login and home screens.
    ''' The gradient, corner glow and streak layers are expensive to redraw on every paint
    ''' (a maximized window repainted them hundreds of times during layout), so they are
    ''' rendered once per size into a cached bitmap and blitted afterwards.
    ''' </summary>
    Public Shared Sub DrawBrandBackdrop(g As Graphics, bounds As Rectangle, Optional tint As Color? = Nothing)
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return

        Dim key As String = bounds.Width.ToString() & "x" & bounds.Height.ToString() & "|" &
                            If(tint.HasValue, tint.Value.ToArgb().ToString(), "none")
        If _backdropCache Is Nothing OrElse Not String.Equals(_backdropCacheKey, key, StringComparison.Ordinal) Then
            If _backdropCache IsNot Nothing Then _backdropCache.Dispose()
            _backdropCache = New Bitmap(bounds.Width, bounds.Height)
            Using cacheGraphics As Graphics = Graphics.FromImage(_backdropCache)
                RenderBrandBackdrop(cacheGraphics, New Rectangle(0, 0, bounds.Width, bounds.Height), tint)
            End Using
            _backdropCacheKey = key
        End If

        g.DrawImageUnscaled(_backdropCache, bounds.Location)
    End Sub

    Private Shared _backdropCache As Bitmap = Nothing
    Private Shared _backdropCacheKey As String = Nothing

    Private Shared Sub RenderBrandBackdrop(g As Graphics, bounds As Rectangle, tint As Color?)
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim top As Color = If(tint.HasValue, Lighten(tint.Value, 0.20F), BrandNavyDark)
        Dim bottom As Color = If(tint.HasValue, Darken(tint.Value, 0.10F), BrandNavy)
        Using brush As New LinearGradientBrush(bounds, top, bottom, LinearGradientMode.ForwardDiagonal)
            g.FillRectangle(brush, bounds)
        End Using

        ' Soft blue glow toward the top-left corner.
        Dim glowRect As New Rectangle(-bounds.Width \ 3, -bounds.Height \ 2, bounds.Width, bounds.Height)
        If glowRect.Width > 0 AndAlso glowRect.Height > 0 Then
            Try
                Using path As New GraphicsPath()
                    path.AddEllipse(glowRect)
                    Using glow As New PathGradientBrush(path)
                        glow.CenterColor = Color.FromArgb(120, BrandCyan)
                        glow.SurroundColors = {Color.FromArgb(0, BrandCyan)}
                        g.FillPath(glow, path)
                    End Using
                End Using
            Catch
            End Try
        End If

        ' Fine diagonal light streaks for depth.
        Using pen As New Pen(Color.FromArgb(20, 255, 255, 255), 1.0F)
            Dim stepSize As Integer = 46
            For x As Integer = -bounds.Height To bounds.Width Step stepSize
                g.DrawLine(pen, x, bounds.Bottom, x + bounds.Height, bounds.Top)
            Next
        End Using
    End Sub

    Public Shared Function Lighten(color As Color, amount As Single) As Color
        amount = Math.Max(0.0F, Math.Min(1.0F, amount))
        Return Color.FromArgb(color.A,
            CInt(color.R + (255 - color.R) * amount),
            CInt(color.G + (255 - color.G) * amount),
            CInt(color.B + (255 - color.B) * amount))
    End Function

    Public Shared Function Darken(color As Color, amount As Single) As Color
        amount = Math.Max(0.0F, Math.Min(1.0F, amount))
        Return Color.FromArgb(color.A,
            CInt(color.R * (1.0F - amount)),
            CInt(color.G * (1.0F - amount)),
            CInt(color.B * (1.0F - amount)))
    End Function

    ''' <summary>Creates the round-rect path used by every branded surface.</summary>
    Public Shared Function RoundedPath(bounds As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)))
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return path
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90)
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90)
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90)
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ' =========================================================================
    '  BUTTONS
    ' =========================================================================

    Public Shared Function MakeButton(text As String, Optional width As Integer = 120,
                                      Optional backColor As Color? = Nothing,
                                      Optional foreColor As Color? = Nothing) As GlassButton
        Dim button As New GlassButton With {
            .Text = text,
            .Width = width,
            .Height = 36,
            .Font = Font(10.0F, FontStyle.Bold),
            .ForeColor = If(foreColor.HasValue, foreColor.Value, Color.White),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4),
            .CornerRadius = 10
        }
        If backColor.HasValue Then
            button.SetTone(backColor.Value)
        Else
            button.SetTone(BrandBlue)
        End If
        Return button
    End Function

    Public Shared Function PrimaryButton(text As String, Optional width As Integer = 140) As GlassButton
        Dim button = MakeButton(text, width)
        button.SetTone(BrandBlue, BrandNavy)
        button.ForeColor = Color.White
        Return button
    End Function

    Public Shared Function DangerButton(text As String, Optional width As Integer = 120) As GlassButton
        Dim button = MakeButton(text, width)
        button.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        button.ForeColor = Color.White
        Return button
    End Function

    Public Shared Function MakeLabel(text As String, Optional bold As Boolean = False,
                                     Optional size As Single = 10.0F) As Label
        Return New Label With {
            .Text = text,
            .AutoSize = True,
            .Font = Font(size, If(bold, FontStyle.Bold, FontStyle.Regular)),
            .ForeColor = Ink,
            .Margin = New Padding(4, 8, 4, 4)
        }
    End Function

    Public Shared Function MakeTextBox(Optional width As Integer = 220) As TextBox
        Return New TextBox With {
            .Width = width,
            .Font = Font(10.0F),
            .Margin = New Padding(4),
            .RightToLeft = RightToLeft.Yes
        }
    End Function

    Public Shared Function MakeCombo(Optional width As Integer = 220) As ComboBox
        Return New ComboBox With {
            .Width = width,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Font = Font(10.0F),
            .Margin = New Padding(4)
        }
    End Function

    Public Shared Function MakeGrid() As DataGridView
        Dim grid As New DataGridView With {
            .Dock = DockStyle.Fill,
            .BackgroundColor = Color.White,
            .BorderStyle = BorderStyle.None,
            .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False,
            .ReadOnly = True,
            .MultiSelect = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .RowHeadersVisible = False,
            .AutoGenerateColumns = False,
            .EnableHeadersVisualStyles = False,
            .Font = Font(9.5F),
            .GridColor = Color.FromArgb(220, 232, 246),
            .ColumnHeadersHeight = 38
        }
        grid.ColumnHeadersDefaultCellStyle.BackColor = BrandNavy
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        grid.ColumnHeadersDefaultCellStyle.Font = Font(9.5F, FontStyle.Bold)
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = BrandNavyDark
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 251, 255)
        grid.DefaultCellStyle.BackColor = Color.White
        grid.DefaultCellStyle.ForeColor = Ink
        grid.DefaultCellStyle.SelectionBackColor = BrandMist
        grid.DefaultCellStyle.SelectionForeColor = Ink
        grid.RowTemplate.Height = 32
        Return grid
    End Function

    Public Shared Function AddTextColumn(grid As DataGridView, name As String, header As String,
                                         Optional width As Integer = 120, Optional fill As Boolean = False,
                                         Optional format As String = Nothing) As DataGridViewTextBoxColumn
        Dim column As New DataGridViewTextBoxColumn With {
            .Name = name,
            .DataPropertyName = name,
            .HeaderText = header,
            .Width = width,
            .SortMode = DataGridViewColumnSortMode.Automatic
        }
        If fill Then
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        End If
        If Not String.IsNullOrEmpty(format) Then
            column.DefaultCellStyle.Format = format
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft
        End If
        grid.Columns.Add(column)
        Return column
    End Function

    Public Shared Function Card(Optional padding As Integer = 12) As TableLayoutPanel
        Return New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.White,
            .Padding = New Padding(padding),
            .ColumnCount = 1,
            .RowCount = 1
        }
    End Function

    Public Shared Function Toolbar() As FlowLayoutPanel
        Return New FlowLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 52,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .BackColor = BrandLight,
            .Padding = New Padding(6)
        }
    End Function

    ' =========================================================================
    '  BRANDED NAVIGATION BUILDERS
    ' =========================================================================

    ''' <summary>Creates a vertical local-navigation button (F2-F9 stack).</summary>
    Public Shared Function MakeNavButton(fKey As String, text As String,
                                               Optional isSpecial As Boolean = False,
                                               Optional height As Integer = 46) As Button
        Dim btn As New GlassButton With {
            .Text = fKey & "  " & text,
            .Dock = DockStyle.Top,
            .Height = height,
            .Font = Font(10.0F, FontStyle.Bold),
            .ForeColor = Color.White,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(3, 2, 3, 2),
            .CornerRadius = 10
        }
        If isSpecial Then
            btn.SetTone(BrandGold, Color.FromArgb(150, 118, 45))
        Else
            btn.SetTone(BrandBlue, BrandNavy)
        End If
        Return btn
    End Function

    ''' <summary>Creates a global-navigation button (F1, F11, F12, Esc, Enter).</summary>
    Public Shared Function MakeGlobalButton(fKey As String, text As String,
                                                  Optional isDanger As Boolean = False,
                                                  Optional isPrimary As Boolean = False,
                                                  Optional width As Integer = 130) As Button
        Dim btn As New GlassButton With {
            .Text = fKey & vbCrLf & text,
            .Width = width,
            .Height = 46,
            .Font = Font(9.5F, FontStyle.Bold),
            .ForeColor = Color.White,
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4, 2, 4, 2),
            .CornerRadius = 10
        }
        If isDanger Then
            btn.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        ElseIf isPrimary Then
            btn.SetTone(BrandCyan, BrandNavyDark)
        Else
            btn.SetTone(BrandBlue, BrandNavy)
        End If
        Return btn
    End Function

    ''' <summary>Creates a standard 5-region summary panel tile.</summary>
    Public Shared Function MakeSummaryTile(header As String, valueLabel As Label, Optional isTotal As Boolean = False) As Panel
        Dim pnl As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = If(isTotal, BrandNavyDark, Color.White),
            .Margin = New Padding(3)
        }
        Dim lblHead As New Label With {
            .Text = header,
            .Dock = DockStyle.Top,
            .Height = 20,
            .Font = Font(8.5F, FontStyle.Bold),
            .ForeColor = If(isTotal, BrandGold, Muted),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        valueLabel.Dock = DockStyle.Fill
        valueLabel.Font = Font(If(isTotal, 14.0F, 11.5F), FontStyle.Bold)
        valueLabel.ForeColor = If(isTotal, Color.White, BrandNavy)
        valueLabel.TextAlign = ContentAlignment.MiddleCenter

        pnl.Controls.Add(valueLabel)
        pnl.Controls.Add(lblHead)
        Return pnl
    End Function

    Public Shared Sub Info(message As String, Optional title As String = "معلومة")
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading Or MessageBoxOptions.RightAlign)
    End Sub

    Public Shared Sub Warn(message As String, Optional title As String = "تنبيه")
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading Or MessageBoxOptions.RightAlign)
    End Sub

    Public Shared Sub ShowError(message As String, Optional title As String = "خطأ")
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading Or MessageBoxOptions.RightAlign)
    End Sub

    Public Shared Function Confirm(message As String, Optional title As String = "تأكيد") As Boolean
        Return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                               MessageBoxDefaultButton.Button2,
                               MessageBoxOptions.RtlReading Or MessageBoxOptions.RightAlign) = DialogResult.Yes
    End Function

    Public Shared Function ToMoney(text As String) As Decimal
        Dim value As Decimal
        If String.IsNullOrWhiteSpace(text) Then Return 0D
        If Decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, value) Then Return value
        If Decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, value) Then Return value
        Return 0D
    End Function

    Public Shared Function ToInt(text As String, Optional fallback As Long = 0) As Long
        Dim value As Long
        If Long.TryParse(text, value) Then Return value
        Return fallback
    End Function

    Public Shared Function Money(value As Decimal) As String
        Return value.ToString("N2", CultureInfo.CurrentCulture)
    End Function

    ''' <summary>Simple modal text prompt (avoids Microsoft.VisualBasic.InputBox).</summary>
    Public Shared Function Prompt(title As String, label As String, Optional defaultValue As String = "") As String
        Using dialog As New Form()
            dialog.Text = title
            Apply(dialog)
            dialog.ClientSize = New Size(400, 178)
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog
            dialog.MaximizeBox = False
            dialog.MinimizeBox = False
            dialog.BackColor = Surface

            Dim caption As Label = MakeLabel(label, True)
            caption.Location = New Point(16, 16)
            Dim input As TextBox = MakeTextBox(360)
            input.Location = New Point(16, 48)
            input.Text = defaultValue

            Dim ok As Button = PrimaryButton("موافق", 110)
            ok.Location = New Point(266, 112)
            ok.DialogResult = DialogResult.OK
            Dim cancel As Button = MakeButton("إلغاء", 110)
            cancel.Location = New Point(148, 112)
            cancel.DialogResult = DialogResult.Cancel

            dialog.Controls.Add(caption)
            dialog.Controls.Add(input)
            dialog.Controls.Add(ok)
            dialog.Controls.Add(cancel)
            dialog.AcceptButton = ok
            dialog.CancelButton = cancel

            If dialog.ShowDialog() = DialogResult.OK Then Return input.Text
            Return Nothing
        End Using
    End Function

    ''' <summary>Shows a service exception with its Arabic message, logs everything else.</summary>
    Public Shared Function Handle(ex As Exception, Optional title As String = "خطأ") As String
        If TypeOf ex Is BusinessException Then
            ShowError(ex.Message, title)
            Return ex.Message
        End If
        AppLog.Error(title, ex)
        ShowError("حدث خطأ غير متوقع: " & ex.Message & Environment.NewLine & "التفاصيل في سجل البرنامج.", title)
        Return ex.Message
    End Function

    Public Shared Sub SetRoundedRegion(ctrl As Control, Optional radius As Integer = 12)
        If ctrl Is Nothing OrElse radius <= 0 Then Return
        Dim applyRegion As Action = Sub()
            If ctrl.Width <= 0 OrElse ctrl.Height <= 0 Then Return
            Try
                Using path As GraphicsPath = RoundedPath(New Rectangle(0, 0, ctrl.Width, ctrl.Height), radius)
                    ctrl.Region = New Region(path)
                End Using
            Catch
            End Try
        End Sub
        applyRegion()
        AddHandler ctrl.Resize, Sub(s, e) applyRegion()
    End Sub

    Public Shared Function MakeGlassButton(text As String,
                                           Optional width As Integer = 140,
                                           Optional height As Integer = 38,
                                           Optional topColor As Color? = Nothing,
                                           Optional bottomColor As Color? = Nothing,
                                           Optional foreColor As Color? = Nothing,
                                           Optional cornerRadius As Integer = 10) As Button
        Dim btn As New GlassButton With {
            .Text = text,
            .Width = width,
            .Height = height,
            .Font = Font(9.5F, FontStyle.Bold),
            .ForeColor = If(foreColor.HasValue, foreColor.Value, Color.White),
            .Cursor = Cursors.Hand,
            .Margin = New Padding(4),
            .CornerRadius = cornerRadius
        }
        btn.SetTone(If(topColor.HasValue, topColor.Value, BrandBlue),
                    If(bottomColor.HasValue, bottomColor.Value, BrandNavy))
        Return btn
    End Function

    Public Shared Function MakeCard(Optional title As String = Nothing, Optional padding As Integer = 12) As Panel
        Dim pnl As New GlassPanel With {
            .Dock = DockStyle.Fill,
            .FillColor = Color.White,
            .BorderColor = BrandBorder,
            .CornerRadius = 16,
            .Padding = New Padding(padding)
        }
        If Not String.IsNullOrWhiteSpace(title) Then
            Dim lbl As New Label With {
                .Text = title,
                .Dock = DockStyle.Top,
                .Height = 30,
                .Font = Font(10.0F, FontStyle.Bold),
                .ForeColor = BrandNavy,
                .TextAlign = ContentAlignment.MiddleRight
            }
            pnl.Controls.Add(lbl)
        End If
        Return pnl
    End Function
End Class
