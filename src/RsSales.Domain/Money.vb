''' <summary>Decimal helpers. All money in the application is Decimal.</summary>
Public NotInheritable Class Money
    Private Sub New()
    End Sub

    Public Const Scale As Integer = 2
    Public Const QtyScale As Integer = 4

    Public Shared Function Round2(value As Decimal) As Decimal
        Return Math.Round(value, Scale, MidpointRounding.AwayFromZero)
    End Function

    Public Shared Function Round4(value As Decimal) As Decimal
        Return Math.Round(value, QtyScale, MidpointRounding.AwayFromZero)
    End Function

    Public Shared Function Clamp(value As Decimal, minValue As Decimal, maxValue As Decimal) As Decimal
        If value < minValue Then Return minValue
        If value > maxValue Then Return maxValue
        Return value
    End Function
End Class
