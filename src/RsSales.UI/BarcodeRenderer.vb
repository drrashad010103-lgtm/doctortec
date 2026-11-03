Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Text

''' <summary>
''' Dependency-free EAN-13 and Code 128 (subset B) renderer used by the
''' barcode label screen. EAN-13 needs a valid check digit; anything else
''' that is printable ASCII falls back to Code 128.
''' </summary>
Public NotInheritable Class BarcodeRenderer
    Private Sub New()
    End Sub

    ''' <summary>ZXing Code 128 symbol patterns (index = symbol value).</summary>
    Private Shared ReadOnly Code128Patterns As Integer()() = {
        New Integer() {2, 1, 2, 2, 2, 2},
        New Integer() {2, 2, 2, 1, 2, 2},
        New Integer() {2, 2, 2, 2, 2, 1},
        New Integer() {1, 2, 1, 2, 2, 3},
        New Integer() {1, 2, 1, 3, 2, 2},
        New Integer() {1, 3, 1, 2, 2, 2},
        New Integer() {1, 2, 2, 2, 1, 3},
        New Integer() {1, 2, 2, 3, 1, 2},
        New Integer() {1, 3, 2, 2, 1, 2},
        New Integer() {2, 2, 1, 2, 1, 3},
        New Integer() {2, 2, 1, 3, 1, 2},
        New Integer() {2, 3, 1, 2, 1, 2},
        New Integer() {1, 1, 2, 2, 3, 2},
        New Integer() {1, 2, 2, 1, 3, 2},
        New Integer() {1, 2, 2, 2, 3, 1},
        New Integer() {1, 1, 3, 2, 2, 2},
        New Integer() {1, 2, 3, 1, 2, 2},
        New Integer() {1, 2, 3, 2, 2, 1},
        New Integer() {2, 2, 3, 2, 1, 1},
        New Integer() {2, 2, 1, 1, 3, 2},
        New Integer() {2, 2, 1, 2, 3, 1},
        New Integer() {2, 1, 3, 2, 1, 2},
        New Integer() {2, 2, 3, 1, 1, 2},
        New Integer() {3, 1, 2, 1, 3, 1},
        New Integer() {3, 1, 1, 2, 2, 2},
        New Integer() {3, 2, 1, 1, 2, 2},
        New Integer() {3, 2, 1, 2, 2, 1},
        New Integer() {3, 1, 2, 2, 1, 2},
        New Integer() {3, 2, 2, 1, 1, 2},
        New Integer() {3, 2, 2, 2, 1, 1},
        New Integer() {2, 1, 2, 1, 2, 3},
        New Integer() {2, 1, 2, 3, 2, 1},
        New Integer() {2, 3, 2, 1, 2, 1},
        New Integer() {1, 1, 1, 3, 2, 3},
        New Integer() {1, 3, 1, 1, 2, 3},
        New Integer() {1, 3, 1, 3, 2, 1},
        New Integer() {1, 1, 2, 3, 1, 3},
        New Integer() {1, 3, 2, 1, 1, 3},
        New Integer() {1, 3, 2, 3, 1, 1},
        New Integer() {2, 1, 1, 3, 1, 3},
        New Integer() {2, 3, 1, 1, 1, 3},
        New Integer() {2, 3, 1, 3, 1, 1},
        New Integer() {1, 1, 2, 1, 3, 3},
        New Integer() {1, 1, 2, 3, 3, 1},
        New Integer() {1, 3, 2, 1, 3, 1},
        New Integer() {1, 1, 3, 1, 2, 3},
        New Integer() {1, 1, 3, 3, 2, 1},
        New Integer() {1, 3, 3, 1, 2, 1},
        New Integer() {3, 1, 3, 1, 2, 1},
        New Integer() {2, 1, 1, 3, 3, 1},
        New Integer() {2, 3, 1, 1, 3, 1},
        New Integer() {2, 1, 3, 1, 1, 3},
        New Integer() {2, 1, 3, 3, 1, 1},
        New Integer() {2, 1, 3, 1, 3, 1},
        New Integer() {3, 1, 1, 1, 2, 3},
        New Integer() {3, 1, 1, 3, 2, 1},
        New Integer() {3, 3, 1, 1, 2, 1},
        New Integer() {3, 1, 2, 1, 1, 3},
        New Integer() {3, 1, 2, 3, 1, 1},
        New Integer() {3, 3, 2, 1, 1, 1},
        New Integer() {3, 1, 4, 1, 1, 1},
        New Integer() {2, 2, 1, 4, 1, 1},
        New Integer() {4, 3, 1, 1, 1, 1},
        New Integer() {1, 1, 1, 2, 2, 4},
        New Integer() {1, 1, 1, 4, 2, 2},
        New Integer() {1, 2, 1, 1, 2, 4},
        New Integer() {1, 2, 1, 4, 2, 1},
        New Integer() {1, 4, 1, 1, 2, 2},
        New Integer() {1, 4, 1, 2, 2, 1},
        New Integer() {1, 1, 2, 2, 1, 4},
        New Integer() {1, 1, 2, 4, 1, 2},
        New Integer() {1, 2, 2, 1, 1, 4},
        New Integer() {1, 2, 2, 4, 1, 1},
        New Integer() {1, 4, 2, 1, 1, 2},
        New Integer() {1, 4, 2, 2, 1, 1},
        New Integer() {2, 4, 1, 2, 1, 1},
        New Integer() {2, 2, 1, 1, 1, 4},
        New Integer() {4, 1, 3, 1, 1, 1},
        New Integer() {2, 4, 1, 1, 1, 2},
        New Integer() {1, 3, 4, 1, 1, 1},
        New Integer() {1, 1, 1, 2, 4, 2},
        New Integer() {1, 2, 1, 1, 4, 2},
        New Integer() {1, 2, 1, 2, 4, 1},
        New Integer() {1, 1, 4, 2, 1, 2},
        New Integer() {1, 2, 4, 1, 1, 2},
        New Integer() {1, 2, 4, 2, 1, 1},
        New Integer() {4, 1, 1, 2, 1, 2},
        New Integer() {4, 2, 1, 1, 1, 2},
        New Integer() {4, 2, 1, 2, 1, 1},
        New Integer() {2, 1, 2, 1, 4, 1},
        New Integer() {2, 1, 4, 1, 2, 1},
        New Integer() {4, 1, 2, 1, 2, 1},
        New Integer() {1, 1, 1, 1, 4, 3},
        New Integer() {1, 1, 1, 3, 4, 1},
        New Integer() {1, 3, 1, 1, 4, 1},
        New Integer() {1, 1, 4, 1, 1, 3},
        New Integer() {1, 1, 4, 3, 1, 1},
        New Integer() {4, 1, 1, 1, 1, 3},
        New Integer() {4, 1, 1, 3, 1, 1},
        New Integer() {1, 1, 3, 1, 4, 1},
        New Integer() {1, 1, 4, 1, 3, 1},
        New Integer() {3, 1, 1, 1, 4, 1},
        New Integer() {4, 1, 1, 1, 3, 1},
        New Integer() {2, 1, 1, 4, 1, 2},
        New Integer() {2, 1, 1, 2, 1, 4},
        New Integer() {2, 1, 1, 2, 3, 2},
        New Integer() {2, 3, 3, 1, 1, 1, 2}
    }

    Private Shared ReadOnly EanL As String() = {
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011"
    }

    Private Shared ReadOnly EanG As String() = {
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111"
    }

    Private Shared ReadOnly EanR As String() = {
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100"
    }

    Private Shared ReadOnly EanParity As String() = {
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"
    }

    ''' <summary>EAN-13 check digit for the first twelve digits.</summary>
    Public Shared Function Ean13CheckDigit(first12 As String) As Integer
        If first12 Is Nothing OrElse first12.Length <> 12 Then
            Throw New ArgumentException("EAN-13 needs twelve digits.", NameOf(first12))
        End If
        Dim sum As Integer = 0
        For i As Integer = 0 To 11
            Dim digit As Integer = AscW(first12(i)) - AscW("0"c)
            If digit < 0 OrElse digit > 9 Then Throw New ArgumentException("EAN-13 must be numeric.", NameOf(first12))
            sum += If(i Mod 2 = 0, digit, digit * 3)
        Next
        Return (10 - (sum Mod 10)) Mod 10
    End Function

    ''' <summary>Returns a valid 13-digit EAN, or Nothing when the value is not EAN-13.</summary>
    Public Shared Function TryNormalizeEan13(value As String) As String
        Dim builder As New StringBuilder()
        For Each ch As Char In If(value, String.Empty)
            If Char.IsDigit(ch) Then builder.Append(ch)
        Next
        Dim digits As String = builder.ToString()
        If digits.Length = 12 Then
            Return digits & Ean13CheckDigit(digits).ToString()
        End If
        If digits.Length = 13 Then
            Dim expected As Integer = Ean13CheckDigit(digits.Substring(0, 12))
            If expected = AscW(digits(12)) - AscW("0"c) Then Return digits
        End If
        Return Nothing
    End Function

    Public Shared Function SymbologyName(value As String) As String
        If TryNormalizeEan13(value) IsNot Nothing Then Return "EAN-13"
        If IsCode128Compatible(value) Then Return "CODE128"
        Return "—"
    End Function

    Public Shared Function CanRender(value As String) As Boolean
        Return TryNormalizeEan13(value) IsNot Nothing OrElse IsCode128Compatible(value)
    End Function

    Private Shared Function IsCode128Compatible(value As String) As Boolean
        Dim text As String = If(value, String.Empty).Trim()
        If text.Length = 0 Then Return False
        For Each ch As Char In text
            If AscW(ch) < 32 OrElse AscW(ch) > 126 Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' Draws bars plus the human-readable value inside <paramref name="bounds"/>.
    ''' Quiet zones are included so the symbol stays scannable.
    ''' </summary>
    Public Shared Sub Draw(g As Graphics, bounds As RectangleF, value As String, Optional showText As Boolean = True)
        If g Is Nothing Then Throw New ArgumentNullException(NameOf(g))
        Dim text As String = If(value, String.Empty).Trim()
        Dim ean As String = TryNormalizeEan13(text)
        Dim modules As Boolean()
        If ean IsNot Nothing Then
            modules = Ean13Modules(ean)
            text = ean
        Else
            If Not IsCode128Compatible(text) Then
                Throw New ArgumentException("قيمة الباركود تحتوي على رموز غير مدعومة.", NameOf(value))
            End If
            modules = Code128Modules(text)
        End If

        If bounds.Width <= 0F OrElse bounds.Height <= 0F Then Return
        g.SmoothingMode = SmoothingMode.None
        g.PixelOffsetMode = PixelOffsetMode.Half

        Const QuietModules As Integer = 10
        Dim totalModules As Integer = modules.Length + QuietModules * 2
        Dim moduleWidth As Single = bounds.Width / totalModules
        Dim textHeight As Single = If(showText, Math.Min(bounds.Height * 0.24F, 22F), 0F)
        Dim barHeight As Single = Math.Max(1F, bounds.Height - textHeight)
        Dim left As Single = bounds.Left + QuietModules * moduleWidth
        Dim black As New SolidBrush(Color.Black)

        Try
            Dim index As Integer = 0
            While index < modules.Length
                If Not modules(index) Then
                    index += 1
                    Continue While
                End If
                Dim runStart As Integer = index
                While index < modules.Length AndAlso modules(index)
                    index += 1
                End While
                Dim runWidth As Single = (index - runStart) * moduleWidth
                g.FillRectangle(black, left + runStart * moduleWidth, bounds.Top, runWidth + 0.05F, barHeight)
            End While

            If showText Then
                Using font As New Font("Consolas", Math.Max(6F, textHeight * 0.72F), FontStyle.Regular, GraphicsUnit.Pixel)
                    Dim format As New StringFormat With {
                        .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center,
                        .Trimming = StringTrimming.None
                    }
                    Dim textRect As New RectangleF(bounds.Left, bounds.Top + barHeight,
                                                   bounds.Width, bounds.Height - barHeight)
                    g.DrawString(text, font, black, textRect, format)
                End Using
            End If
        Finally
            black.Dispose()
        End Try
    End Sub

    Private Shared Function Ean13Modules(ean As String) As Boolean()
        Dim bits As New StringBuilder(95)
        bits.Append("101")
        Dim parity As String = EanParity(AscW(ean(0)) - AscW("0"c))
        For i As Integer = 0 To 5
            Dim digit As Integer = AscW(ean(i + 1)) - AscW("0"c)
            bits.Append(If(parity(i) = "L"c, EanL(digit), EanG(digit)))
        Next
        bits.Append("01010")
        For i As Integer = 0 To 5
            bits.Append(EanR(AscW(ean(i + 7)) - AscW("0"c)))
        Next
        bits.Append("101")
        Return ToModules(bits.ToString())
    End Function

    Private Shared Function Code128Modules(text As String) As Boolean()
        Dim values As New List(Of Integer)()
        Const StartB As Integer = 104
        values.Add(StartB)
        For Each ch As Char In text
            values.Add(AscW(ch) - 32)
        Next

        Dim checksum As Integer = StartB
        For i As Integer = 1 To values.Count - 1
            checksum += values(i) * i
        Next
        values.Add(checksum Mod 103)
        values.Add(106)

        Dim bits As New StringBuilder()
        For Each code In values
            Dim pattern As Integer() = Code128Patterns(code)
            Dim drawBar As Boolean = True
            For Each width In pattern
                Dim ch As Char = If(drawBar, "1"c, "0"c)
                bits.Append(New String(ch, width))
                drawBar = Not drawBar
            Next
        Next
        Return ToModules(bits.ToString())
    End Function

    Private Shared Function ToModules(bits As String) As Boolean()
        Dim modules(bits.Length - 1) As Boolean
        For i As Integer = 0 To bits.Length - 1
            modules(i) = bits(i) = "1"c
        Next
        Return modules
    End Function
End Class
