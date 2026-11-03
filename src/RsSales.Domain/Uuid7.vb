Imports System
Imports System.Security.Cryptography

''' <summary>RFC 9562 UUID version 7 (time-ordered, text representation).</summary>
Public NotInheritable Class Uuid7
    Private Sub New()
    End Sub

    Public Shared Function NewId() As String
        Dim unixMs As Long = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        Dim bytes(15) As Byte

        ' 48-bit big-endian timestamp
        bytes(0) = CByte((unixMs >> 40) And CLng(&HFF))
        bytes(1) = CByte((unixMs >> 32) And CLng(&HFF))
        bytes(2) = CByte((unixMs >> 24) And CLng(&HFF))
        bytes(3) = CByte((unixMs >> 16) And CLng(&HFF))
        bytes(4) = CByte((unixMs >> 8) And CLng(&HFF))
        bytes(5) = CByte(unixMs And CLng(&HFF))

        Dim randomBytes(9) As Byte
        RandomNumberGenerator.Fill(randomBytes)
        Array.Copy(randomBytes, 0, bytes, 6, 9)

        ' version 7
        bytes(6) = CByte((bytes(6) And &HF) Or &H70)
        ' variant RFC 4122 (10xxxxxx)
        bytes(8) = CByte((bytes(8) And &H3F) Or &H80)

        Return FormatBytes(bytes)
    End Function

    Private Shared Function FormatBytes(b As Byte()) As String
        Dim hex As String = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant()
        Return hex.Substring(0, 8) & "-" &
               hex.Substring(8, 4) & "-" &
               hex.Substring(12, 4) & "-" &
               hex.Substring(16, 4) & "-" &
               hex.Substring(20, 12)
    End Function
End Class
