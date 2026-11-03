Imports System.Net.Http
Imports System.Text
Imports System.Text.Json

''' <summary>
''' HTTPS transport to the central API. Bearer token is the branch/device key
''' configured on the terminal; TLS is mandatory outside localhost.
''' </summary>
Public Class HttpSyncTransport
    Implements ISyncTransport

    Private ReadOnly _baseUrl As String
    Private ReadOnly _token As String
    Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    }

    Public Sub New(baseUrl As String, token As String)
        If String.IsNullOrWhiteSpace(baseUrl) Then Throw New ArgumentException("Central server URL is required.", NameOf(baseUrl))
        Dim trimmed As String = baseUrl.Trim()
        ' Enforce TLS outside localhost/loopback. Plain HTTP is only allowed for
        ' local development so a misconfigured terminal cannot leak sync payloads.
        Dim uri As New Uri(trimmed, UriKind.Absolute)
        Dim isLocal As Boolean = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) OrElse
                                uri.Host = "127.0.0.1" OrElse uri.Host = "::1"
        If uri.Scheme <> Uri.UriSchemeHttps AndAlso Not isLocal Then
            Throw New ArgumentException("Central server URL must use HTTPS outside localhost.", NameOf(baseUrl))
        End If
        _baseUrl = trimmed.TrimEnd("/"c)
        _token = token
    End Sub

    Public Function Push(batch As SyncBatch) As SyncAck Implements ISyncTransport.Push
        Dim body As String = JsonSerializer.Serialize(batch, JsonOptions)
        Using client = CreateClient()
            Using content As New StringContent(body, Encoding.UTF8, "application/json")
                Using response = client.PostAsync(_baseUrl & "/api/v1/sync/push", content).GetAwaiter().GetResult()
                    Dim text As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    If Not response.IsSuccessStatusCode Then
                        Throw New HttpRequestException(
                            "Central sync failed with HTTP " & CInt(response.StatusCode).ToString() & ": " & text)
                    End If
                    Return JsonSerializer.Deserialize(Of SyncAck)(text, JsonOptions)
                End Using
            End Using
        End Using
    End Function

    Public Function Pull(stream As String, afterSeq As Long, limit As Integer) As PullResponse Implements ISyncTransport.Pull
        Using client = CreateClient()
            Dim url As String = _baseUrl & "/api/v1/sync/pull?stream=" & Uri.EscapeDataString(stream) &
                                "&after=" & afterSeq.ToString() & "&limit=" & limit.ToString()
            Using response = client.GetAsync(url).GetAwaiter().GetResult()
                Dim text As String = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                If Not response.IsSuccessStatusCode Then
                    Throw New HttpRequestException(
                        "Central pull failed with HTTP " & CInt(response.StatusCode).ToString() & ": " & text)
                End If
                Return JsonSerializer.Deserialize(Of PullResponse)(text, JsonOptions)
            End Using
        End Using
    End Function

    Private Function CreateClient() As HttpClient
        Dim client As New HttpClient()
        client.Timeout = TimeSpan.FromSeconds(30)
        client.DefaultRequestHeaders.Add("Accept", "application/json")
        If Not String.IsNullOrWhiteSpace(_token) Then
            client.DefaultRequestHeaders.Authorization =
                New System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token)
        End If
        Return client
    End Function
End Class
