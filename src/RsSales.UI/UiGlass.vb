Imports System
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Premium rounded control used across DOCTOR TECH screens:
''' - gradient glass fill, glossy sheen, soft border and animated hover glow.
''' </summary>
Public Class GlassButton
    Inherits Button

    Private _topColor As Color = UiTheme.BrandBlue
    Private _bottomColor As Color = UiTheme.BrandNavy
    Private _hoverAmount As Single
    Private _hoverTarget As Single
    Private _animating As Boolean
    Private _updatingTone As Boolean
    Private ReadOnly _animTimer As New Timer()

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or
                 ControlStyles.SupportsTransparentBackColor, True)
        FlatStyle = FlatStyle.Flat
        FlatAppearance.BorderSize = 0
        UseVisualStyleBackColor = False
        Font = UiTheme.Font(9.5F, FontStyle.Bold)
        ForeColor = Color.White
        Cursor = Cursors.Hand
        TabStop = True
        MyBase.BackColor = UiTheme.BrandNavy

        _animTimer.Interval = 15
        AddHandler _animTimer.Tick, AddressOf AnimateTick
    End Sub

    ''' <summary>Rounded corner radius in pixels.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property CornerRadius As Integer = 10

    ''' <summary>Draws the animated glass glow when the pointer is over the button.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property GlowOnHover As Boolean = True

    ''' <summary>Sets the vertical gradient explicitly (top highlight -> bottom shade).</summary>
    Public Sub SetTone(bottom As Color, Optional top As Color? = Nothing)
        _updatingTone = True
        Try
            _bottomColor = bottom
            _topColor = If(top.HasValue, top.Value, UiTheme.Lighten(bottom, 0.22F))
            MyBase.BackColor = bottom
        Finally
            _updatingTone = False
        End Try
        Invalidate()
    End Sub

    Public Overrides Property BackColor As Color
        Get
            Return MyBase.BackColor
        End Get
        Set(value As Color)
            MyBase.BackColor = value
            If Not _updatingTone Then
                _bottomColor = value
                _topColor = UiTheme.Lighten(value, 0.22F)
                Invalidate()
            End If
        End Set
    End Property

    Private Sub AnimateTick(sender As Object, e As EventArgs)
        Dim diff As Single = _hoverTarget - _hoverAmount
        If Math.Abs(diff) < 0.02F Then
            _hoverAmount = _hoverTarget
            _animTimer.Stop()
            _animating = False
        Else
            _hoverAmount += diff * 0.22F
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        StartHover(1.0F)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        StartHover(0.0F)
    End Sub

    Private Sub StartHover(target As Single)
        If Not GlowOnHover Then Return
        _hoverTarget = target
        If Not _animating Then
            _animating = True
            _animTimer.Start()
        End If
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If Width > 0 AndAlso Height > 0 Then
            Try
                Using path As GraphicsPath = UiTheme.RoundedPath(New Rectangle(0, 0, Width, Height), CornerRadius)
                    Region = New Region(path)
                End Using
            Catch
            End Try
        End If
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality

        Dim parentColor As Color = If(Parent IsNot Nothing, Parent.BackColor, UiTheme.Surface)
        g.Clear(parentColor)

        Dim bounds As New Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1))
        Dim enabledState As Boolean = Enabled

        Dim top As Color = If(enabledState, UiTheme.Lighten(_topColor, 0.10F * _hoverAmount), Color.FromArgb(190, 200, 214))
        Dim bottom As Color = If(enabledState, UiTheme.Lighten(_bottomColor, 0.16F * _hoverAmount), Color.FromArgb(160, 172, 190))

        Using path As GraphicsPath = UiTheme.RoundedPath(bounds, CornerRadius)
            Using body As New LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical)
                g.FillPath(body, path)
            End Using

            ' Glass reflection over the upper half.
            Dim halfHeight As Integer = Math.Max(1, Height \ 2)
            Dim oldClip As Region = g.Clip
            g.SetClip(path, CombineMode.Intersect)
            Using shine As New LinearGradientBrush(
                    New Rectangle(0, 0, Width, halfHeight),
                    Color.FromArgb(CInt(86 + 40 * _hoverAmount), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical)
                g.FillRectangle(shine, 0, 0, Width, halfHeight)
            End Using
            g.Clip = oldClip

            ' Soft inner highlight along the top edge.
            Using edgePen As New Pen(Color.FromArgb(CInt(70 + 40 * _hoverAmount), 255, 255, 255), 1.2F)
                g.DrawLine(edgePen, 6, 1, Math.Max(7, Width - 7), 1)
            End Using

            ' Border / focus glow.
            Dim borderColor As Color
            If Focused Then
                borderColor = UiTheme.BrandGold
            Else
                borderColor = Color.FromArgb(CInt(150 + 50 * _hoverAmount), 255, 255, 255)
            End If
            Using border As New Pen(borderColor, If(Focused, 1.8F, 1.2F))
                g.DrawPath(border, path)
            End Using
        End Using

        ' Text with a subtle shadow for legibility on gradients.
        Dim textColor As Color = If(enabledState, ForeColor, Color.FromArgb(235, 240, 248))
        Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                                       TextFormatFlags.WordBreak Or TextFormatFlags.EndEllipsis
        Dim textRect As New Rectangle(2, 0, Math.Max(1, Width - 4), Height)
        Using shadowBrush As New SolidBrush(Color.FromArgb(80, 0, 0, 0))
            Dim shadowRect = textRect
            shadowRect.Offset(1, 1)
            TextRenderer.DrawText(g, Text, Font, shadowRect, shadowBrush.Color, flags)
        End Using
        TextRenderer.DrawText(g, Text, Font, textRect, textColor, flags)
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        Invalidate()
    End Sub
End Class

''' <summary>
''' Rounded card surface with a soft border, used for panels and login cards.
''' </summary>
Public Class GlassPanel
    Inherits Panel

    Private _fill As Color = Color.White
    Private _border As Color = UiTheme.BrandBorder

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.White
    End Sub

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property CornerRadius As Integer = 16

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property FillColor As Color
        Get
            Return _fill
        End Get
        Set(value As Color)
            _fill = value
            BackColor = value
            Invalidate()
        End Set
    End Property

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property BorderColor As Color
        Get
            Return _border
        End Get
        Set(value As Color)
            _border = value
            Invalidate()
        End Set
    End Property

    ''' <summary>Optional vertical gradient target; when set, the card fades into this color.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property GradientBottom As Color? = Nothing

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ShowInnerGlow As Boolean = True

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        If Width > 0 AndAlso Height > 0 Then
            Try
                Using path As GraphicsPath = UiTheme.RoundedPath(New Rectangle(0, 0, Width, Height), CornerRadius)
                    Region = New Region(path)
                End Using
            Catch
            End Try
        End If
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim parentColor As Color = If(Parent IsNot Nothing, Parent.BackColor, UiTheme.Surface)
        g.Clear(parentColor)

        Dim bounds As New Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1))
        Using path As GraphicsPath = UiTheme.RoundedPath(bounds, CornerRadius)
            If GradientBottom.HasValue Then
                Using body As New LinearGradientBrush(bounds, _fill, GradientBottom.Value, LinearGradientMode.Vertical)
                    g.FillPath(body, path)
                End Using
            Else
                Using body As New SolidBrush(_fill)
                    g.FillPath(body, path)
                End Using
            End If

            If ShowInnerGlow Then
                Dim halfHeight As Integer = Math.Max(1, Height \ 3)
                Dim oldClip As Region = g.Clip
                g.SetClip(path, CombineMode.Intersect)
                Using glow As New LinearGradientBrush(
                        New Rectangle(0, 0, Width, halfHeight),
                        Color.FromArgb(26, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255),
                        LinearGradientMode.Vertical)
                    g.FillRectangle(glow, 0, 0, Width, halfHeight)
                End Using
                g.Clip = oldClip
            End If

            Using border As New Pen(_border, 1.2F)
                g.DrawPath(border, path)
            End Using
        End Using
    End Sub
End Class

''' <summary>Full-bleed DOCTOR TECH gradient host that supports transparent children.</summary>
Public Class BrandBackdropPanel
    Inherits Panel

    Private _tint As Color? = Nothing

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or
                 ControlStyles.SupportsTransparentBackColor, True)
        BackColor = UiTheme.BrandNavyDark
        DoubleBuffered = True
    End Sub

    ''' <summary>Optional tint used for training / re-entry modes.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Tint As Color?
        Get
            Return _tint
        End Get
        Set(value As Color?)
            _tint = value
            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        If ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then Return
        e.Graphics.Clear(UiTheme.BrandNavyDark)
        UiTheme.DrawBrandBackdrop(e.Graphics, ClientRectangle, _tint)
    End Sub
End Class

''' <summary>Small rounded status pill used on the branded footer/status bars.</summary>
Public Class StatusPill
    Inherits Label

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property PillColor As Color = UiTheme.BrandNavy
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property PillRadius As Integer = 9

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        AutoSize = False
        TextAlign = ContentAlignment.MiddleCenter
        Font = UiTheme.Font(8.8F, FontStyle.Bold)
        ForeColor = Color.White
        BackColor = Color.Transparent
        Margin = New Padding(2, 1, 2, 1)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim parentColor As Color = If(Parent IsNot Nothing, Parent.BackColor, UiTheme.Surface)
        g.Clear(parentColor)
        Dim bounds As New Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1))
        Using path As GraphicsPath = UiTheme.RoundedPath(bounds, PillRadius)
            Using brush As New SolidBrush(PillColor)
                g.FillPath(brush, path)
            End Using
            Using pen As New Pen(UiTheme.Lighten(PillColor, 0.35F), 1.0F)
                g.DrawPath(pen, path)
            End Using
        End Using
        TextRenderer.DrawText(g, Text, Font, bounds, ForeColor,
                              TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
    End Sub
End Class
