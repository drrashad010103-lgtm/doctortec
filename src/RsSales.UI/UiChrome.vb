Imports System
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Unified DOCTOR TECH window frame:
''' - Navy branded title bar with the DOCTOR TECH badge and rounded glass close button.
''' - Harmonizes legacy green/purple accents inside child forms into the brand navy palette.
''' - Supports Escape to close, window dragging, and bottom-right resize grip.
''' - Applies the standard 0.5 cm (19 px) inset around the window content.
''' </summary>
Public NotInheritable Class UiChrome
    Public Shared ReadOnly ChromeColor As Color = UiTheme.BrandNavyDark
    Public Shared ReadOnly CloseColor As Color = UiTheme.DangerRed
    Public Shared ReadOnly BorderColor As Color = UiTheme.BrandBorder

    Private Sub New()
    End Sub

    Public Shared Sub Apply(form As Form)
        If form Is Nothing OrElse form.IsDisposed Then Return
        HarmonizeControlTreeToBrand(form)

        If Equals(form.Tag, "rs-chrome") Then Return
        ' Screens that already draw a full branded frame opt out explicitly.
        If Equals(form.Tag, "rs-custom-chrome") Then Return
        form.Tag = "rs-chrome"

        form.FormBorderStyle = FormBorderStyle.None
        form.MaximizeBox = False
        form.BackColor = UiTheme.Surface
        Dim inset As Integer = UiTheme.ScreenMarginHalfCm
        form.Padding = New Padding(inset, inset, inset, inset)

        Dim bar = FindExistingBar(form)
        If bar IsNot Nothing Then
            StyleBar(form, bar)
        Else
            AddBar(form)
        End If

        AddResizeGrip(form)

        AddHandler form.Paint, Sub(s, e) PaintBorder(form, e.Graphics)
        AddHandler form.KeyDown, Sub(s, e)
            If e.KeyCode = Keys.Escape Then form.Close()
        End Sub
    End Sub

    ''' <summary>
    ''' Recursively converts legacy green, purple and dark-blue backgrounds in child controls
    ''' to the DOCTOR TECH palette so every screen in the project matches.
    ''' </summary>
    Public Shared Sub HarmonizeControlTreeToBrand(root As Control)
        If root Is Nothing Then Return
        For Each ctl As Control In root.Controls
            If TypeOf ctl Is DataGridView Then
                Dim g = DirectCast(ctl, DataGridView)
                g.EnableHeadersVisualStyles = False
                g.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.BrandNavy
                g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.BrandNavyDark
                g.DefaultCellStyle.SelectionBackColor = UiTheme.BrandMist
                g.DefaultCellStyle.SelectionForeColor = UiTheme.Ink
                g.GridColor = Color.FromArgb(220, 232, 246)
            ElseIf TypeOf ctl Is GlassButton Then
                ' Already branded; keep its gradient.
            ElseIf TypeOf ctl Is Button Then
                Dim b = DirectCast(ctl, Button)
                If IsLegacyAccent(b.BackColor) Then
                    b.BackColor = UiTheme.BrandBlue
                    b.ForeColor = Color.White
                End If
            ElseIf TypeOf ctl Is Panel OrElse TypeOf ctl Is FlowLayoutPanel OrElse TypeOf ctl Is TableLayoutPanel Then
                If IsLegacyGreen(ctl.BackColor) Then
                    If ctl.Height <= 46 AndAlso ctl.Dock = DockStyle.Top Then
                        ctl.BackColor = UiTheme.BrandNavyDark
                    Else
                        ctl.BackColor = UiTheme.BrandNavy
                    End If
                ElseIf IsLegacyPurple(ctl.BackColor) Then
                    ctl.BackColor = UiTheme.BrandNavy
                End If
            End If
            If ctl.HasChildren Then
                HarmonizeControlTreeToBrand(ctl)
            End If
        Next
    End Sub

    Private Shared Function IsLegacyAccent(c As Color) As Boolean
        If c.A < 200 Then Return False
        Return IsLegacyGreen(c) OrElse IsLegacyPurple(c)
    End Function

    Private Shared Function IsLegacyGreen(c As Color) As Boolean
        If c.A < 200 Then Return False
        ' Saturated green where green clearly dominates red and blue.
        If c.G > c.R + 16 AndAlso c.G > c.B + 10 AndAlso c.G > 70 AndAlso c.B < c.G Then Return True
        ' Very light green tints (238,247,239 / 228,242,229) missed by the rule above.
        If c.G > c.R AndAlso c.G >= c.B AndAlso c.G > 120 AndAlso c.R < 250 Then Return True
        ' Legacy orange used by HR / payroll / loyalty screens (230,126,34).
        If c.R > 150 AndAlso c.G > 60 AndAlso c.G < 180 AndAlso c.B < 90 Then Return True
        ' WhatsApp green (37,211,102).
        If c.G > 150 AndAlso c.R < 100 AndAlso c.B < 160 AndAlso c.G > c.B + 40 Then Return True
        Return False
    End Function

    Private Shared Function IsLegacyPurple(c As Color) As Boolean
        If c.A < 200 Then Return False
        ' Purple / violet tones such as (46,16,56) or (142,68,173).
        Return c.R > 35 AndAlso c.B > 45 AndAlso c.G < 60 AndAlso c.R < c.B + 40
    End Function

    Private Shared Function FindExistingBar(form As Form) As Panel
        For Each ctl As Control In form.Controls
            Dim panel = TryCast(ctl, Panel)
            If panel Is Nothing Then Continue For
            Dim dockedTop = panel.Dock = DockStyle.Top
            Dim floatingTop = panel.Dock = DockStyle.None AndAlso panel.Top <= 4
            If Not dockedTop AndAlso Not floatingTop Then Continue For
            ' Section screens use 68-74px headers; limiting this to 52px made
            ' UiChrome add a second title bar above the existing header.
            If panel.Height > 96 OrElse panel.Height < 24 Then Continue For
            If FindCloseButton(panel) IsNot Nothing Then Return panel
            If IsChromeTone(panel.BackColor) Then Return panel
        Next
        Return Nothing
    End Function

    Private Shared Function FindCloseButton(parent As Control) As Button
        For Each ctl As Control In parent.Controls
            Dim btn = TryCast(ctl, Button)
            If btn IsNot Nothing Then
                If btn.Text.Contains("✕") OrElse btn.Text.Contains("✖") OrElse btn.Text.Contains("×") Then Return btn
            End If
            Dim nested = FindCloseButton(ctl)
            If nested IsNot Nothing Then Return nested
        Next
        Return Nothing
    End Function

    Private Shared Function IsChromeTone(color As Color) As Boolean
        Return (Math.Abs(color.R - ChromeColor.R) < 30 AndAlso
                Math.Abs(color.G - ChromeColor.G) < 30 AndAlso
                Math.Abs(color.B - ChromeColor.B) < 30) OrElse
               (color.R = 46 AndAlso color.G = 16 AndAlso color.B = 56)
    End Function

    Private Shared Sub AddBar(form As Form)
        Const barHeight As Integer = 42
        Dim pad = form.Padding

        Dim bar As New Panel With {
            .Height = barHeight,
            .BackColor = ChromeColor,
            .RightToLeft = RightToLeft.No,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }

        Dim lblBrand As New Label With {
            .Text = "DOCTOR TECH",
            .ForeColor = UiTheme.BrandGold,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .Dock = DockStyle.Left,
            .Width = 150,
            .TextAlign = ContentAlignment.MiddleCenter
        }

        Dim lblTitle As New Label With {
            .Text = "◆ " & form.Text,
            .ForeColor = Color.White,
            .Font = UiTheme.Font(10.5F, FontStyle.Bold),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Padding = New Padding(0, 0, 14, 0)
        }

        Dim btnClose As New GlassButton With {
            .Text = "✖ إغلاق (Esc)",
            .Size = New Size(118, 30),
            .Dock = DockStyle.Left,
            .CornerRadius = 8,
            .Margin = New Padding(0),
            .TabStop = False
        }
        btnClose.SetTone(Color.FromArgb(214, 66, 66), Color.FromArgb(160, 30, 30))
        AddHandler btnClose.Click, Sub(s, e) form.Close()

        bar.Controls.Add(lblTitle)
        bar.Controls.Add(lblBrand)
        bar.Controls.Add(btnClose)
        form.Controls.Add(bar)
        bar.BringToFront()

        form.Padding = New Padding(pad.Left, pad.Top + barHeight, pad.Right, pad.Bottom)
        Dim layoutBar As Action = Sub()
                                  bar.Bounds = New Rectangle(
                                      form.Padding.Left, form.Padding.Top - barHeight,
                                      Math.Max(120, form.ClientSize.Width - form.Padding.Horizontal), barHeight)
                              End Sub
        layoutBar()
        AddHandler form.Resize, Sub(s, e) layoutBar()
        bar.BringToFront()

        AddHandler form.TextChanged, Sub(s, e) lblTitle.Text = "◆ " & form.Text
        MakeDraggable(form, bar, lblTitle, lblBrand)
    End Sub

    Private Shared Sub StyleBar(form As Form, bar As Panel)
        bar.BackColor = ChromeColor
        bar.Height = Math.Max(38, Math.Min(bar.Height, 46))
        Dim btn = FindCloseButton(bar)
        If btn IsNot Nothing Then
            Dim symbolOnly As Boolean = btn.Text.Trim().Length <= 2
            If Not symbolOnly AndAlso Not btn.Text.Contains("إغلاق") Then btn.Text = "✖ إغلاق"
            btn.BackColor = CloseColor
            btn.ForeColor = Color.White
            btn.FlatStyle = FlatStyle.Flat
            btn.FlatAppearance.BorderSize = 0
            btn.Font = UiTheme.Font(9.0F, FontStyle.Bold)
            btn.Cursor = Cursors.Hand
            If Not symbolOnly AndAlso (btn.Width < 84 OrElse btn.AutoSize) Then
                btn.AutoSize = False
                btn.Size = New Size(100, Math.Min(30, bar.Height - 8))
            End If
        End If
        For Each ctl As Control In bar.Controls
            If TypeOf ctl Is Label Then
                Dim lbl = DirectCast(ctl, Label)
                If lbl.Text = form.Text OrElse lbl.Text.Length > 0 Then
                    lbl.ForeColor = Color.White
                    lbl.Font = UiTheme.Font(10.5F, FontStyle.Bold)
                End If
            End If
        Next
        MakeDraggable(form, bar)
    End Sub

    Private Shared Sub MakeDraggable(form As Form, ParamArray parts() As Control)
        Dim dragging As Boolean = False
        Dim grabOffset As Point = Point.Empty
        For Each handle In parts
            AddHandler handle.MouseDown, Sub(s, e)
                If e.Button <> MouseButtons.Left Then Return
                dragging = True
                grabOffset = New Point(Control.MousePosition.X - form.Location.X, Control.MousePosition.Y - form.Location.Y)
            End Sub
            AddHandler handle.MouseMove, Sub(s, e)
                If Not dragging Then Return
                form.Location = New Point(Control.MousePosition.X - grabOffset.X, Control.MousePosition.Y - grabOffset.Y)
            End Sub
            AddHandler handle.MouseUp, Sub(s, e) dragging = False
        Next
    End Sub

    Private Shared Sub AddResizeGrip(form As Form)
        Dim grip As New Panel With {
            .Size = New Size(16, 16),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.SizeNWSE,
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        }
        AddHandler grip.Paint, Sub(s, e)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
            Using pen As New Pen(Color.FromArgb(150, ChromeColor), 1.4F)
                For i As Integer = 0 To 2
                    Dim offset = 4 + i * 4
                    e.Graphics.DrawLine(pen, grip.Width - 2, offset, offset, grip.Height - 2)
                Next
            End Using
        End Sub
        Dim resizing As Boolean = False
        Dim startMouse As Point
        Dim startSize As Size
        AddHandler grip.MouseDown, Sub(s, e)
            resizing = True
            startMouse = Control.MousePosition
            startSize = form.Size
        End Sub
        AddHandler grip.MouseMove, Sub(s, e)
            If Not resizing Then Return
            Dim now = Control.MousePosition
            Dim nextSize As New Size(
                Math.Max(form.MinimumSize.Width, startSize.Width + (now.X - startMouse.X)),
                Math.Max(form.MinimumSize.Height, startSize.Height + (now.Y - startMouse.Y)))
            form.Size = nextSize
        End Sub
        AddHandler grip.MouseUp, Sub(s, e) resizing = False
        form.Controls.Add(grip)
        grip.BringToFront()
        Dim layout As Action = Sub()
                                  grip.Location = New Point(
                                      Math.Max(0, form.ClientSize.Width - grip.Width - 2),
                                      Math.Max(0, form.ClientSize.Height - grip.Height - 2))
                              End Sub
        AddHandler form.Resize, Sub(s, e) layout()
        layout()
    End Sub

    Private Shared Sub PaintBorder(form As Form, g As Graphics)
        If form.WindowState = FormWindowState.Minimized Then Return
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pen As New Pen(BorderColor, 1.5F)
            g.DrawRectangle(pen, 0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1)
        End Using
    End Sub
End Class
