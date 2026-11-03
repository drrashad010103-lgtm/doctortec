Imports System.Collections.Generic
Imports System.Linq

''' <summary>
''' Splits a header-level discount across lines proportionally to their
''' post-line-discount weight. Rounds to 2 decimals using largest remainder so
''' the sum of shares is exactly the requested amount.
''' </summary>
Public NotInheritable Class DiscountAllocator
    Private Sub New()
    End Sub

    Public Shared Function Allocate(total As Decimal, weights As IReadOnlyList(Of Decimal)) As Decimal()
        If weights Is Nothing Then Throw New ArgumentNullException(NameOf(weights))
        Dim n As Integer = weights.Count
        Dim result(n - 1) As Decimal
        If n = 0 Then Return result

        total = Money.Round2(total)
        If total = 0D Then Return result

        Dim sum As Decimal = 0D
        For Each w In weights
            If w < 0D Then Throw New ArgumentException("Weights must not be negative.", NameOf(weights))
            sum += w
        Next

        If sum = 0D Then Return result

        Dim fractions(n - 1) As Decimal
        Dim allocated As Decimal = 0D

        For i As Integer = 0 To n - 1
            Dim raw As Decimal = total * weights(i) / sum
            Dim floorValue As Decimal = Math.Floor(raw * 100D) / 100D
            result(i) = floorValue
            fractions(i) = raw - floorValue
            allocated += floorValue
        Next

        Dim cents As Integer = CInt(Math.Round((total - allocated) * 100D, 0, MidpointRounding.AwayFromZero))
        Dim order = Enumerable.Range(0, n).OrderByDescending(Function(i) fractions(i)).ThenBy(Function(i) i).ToArray()

        Dim k As Integer = 0
        While cents > 0
            Dim idx As Integer = order(k Mod n)
            result(idx) += 0.01D
            cents -= 1
            k += 1
        End While

        Return result
    End Function
End Class
