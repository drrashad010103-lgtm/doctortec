Imports System.Drawing
Imports System.Drawing.Printing
Imports System.IO
Imports QRCoder

Public NotInheritable Class QrHelper
    Private Sub New()
    End Sub

    Public Shared Function CreateImage(payload As String, Optional pixelsPerModule As Integer = 6) As Image
        If String.IsNullOrWhiteSpace(payload) Then Return Nothing
        Try
            Using generator As New QRCodeGenerator()
                Dim data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M)
                Dim png As New PngByteQRCode(data)
                Dim bytes = png.GetGraphic(pixelsPerModule)
                Using stream As New MemoryStream(bytes)
                    Using img = Image.FromStream(stream)
                        Return New Bitmap(img)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppLog.Warn("QR render failed: " & ex.Message)
            Return Nothing
        End Try
    End Function
End Class