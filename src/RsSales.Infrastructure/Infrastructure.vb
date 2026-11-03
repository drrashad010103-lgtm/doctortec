Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json

''' <summary>Application configuration for DOCTOR TECH POS & ERP.</summary>
Public Class AppConfig
    Public Property ConnectionString As String
    Public Property ServerUrl As String
    Public Property DeviceKey As String
    ''' <summary>DPAPI-protected DeviceKey (base64). Takes precedence over DeviceKey.</summary>
    Public Property DeviceKeyProtected As String
    Public Property LogDirectory As String
    Public Property SyncBatchSize As Integer = 50
    Public Property SyncMaxAttempts As Integer = 10

    Public Shared Function Load(Optional path As String = Nothing) As AppConfig
        If String.IsNullOrWhiteSpace(path) Then
            path = FindDefaultFile()
        End If

        Dim cfg As AppConfig = Nothing
        If Not String.IsNullOrWhiteSpace(path) AndAlso IO.File.Exists(path) Then
            Try
                Dim json As String = IO.File.ReadAllText(path, Encoding.UTF8)
                If Not String.IsNullOrWhiteSpace(json) Then
                    cfg = JsonSerializer.Deserialize(Of AppConfig)(json, New JsonSerializerOptions With {
                        .PropertyNameCaseInsensitive = True
                    })
                End If
            Catch
            End Try
        End If

        If cfg Is Nothing Then cfg = New AppConfig()

        If Not String.IsNullOrWhiteSpace(cfg.DeviceKeyProtected) Then
            cfg.DeviceKey = SecretStore.Unprotect(cfg.DeviceKeyProtected)
        End If

        Dim envConnection As String = Environment.GetEnvironmentVariable("RSSALES_CONNECTION")
        If Not String.IsNullOrWhiteSpace(envConnection) Then cfg.ConnectionString = envConnection

        Dim envServer As String = Environment.GetEnvironmentVariable("RSSALES_SERVER_URL")
        If Not String.IsNullOrWhiteSpace(envServer) Then cfg.ServerUrl = envServer

        Dim envDeviceKey As String = Environment.GetEnvironmentVariable("RSSALES_DEVICE_KEY")
        If Not String.IsNullOrWhiteSpace(envDeviceKey) Then cfg.DeviceKey = envDeviceKey

        If String.IsNullOrWhiteSpace(cfg.ConnectionString) Then
            cfg.ConnectionString = "Server=localhost;Port=3306;Database=rs_sales;User ID=root;Password=;SslMode=None;Allow User Variables=True"
        End If

        If String.IsNullOrWhiteSpace(cfg.LogDirectory) Then
            cfg.LogDirectory = IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RsSales", "logs")
        End If

        Return cfg
    End Function

    ''' <summary>Searches current directory, base directory, and parent root directories for appsettings.json.</summary>
    Private Shared Function FindDefaultFile() As String
        Dim searchDirs As New System.Collections.Generic.List(Of String)()
        Try
            Dim cur As String = IO.Directory.GetCurrentDirectory()
            For i As Integer = 0 To 5
                If String.IsNullOrWhiteSpace(cur) Then Exit For
                searchDirs.Add(cur)
                Dim parent = IO.Directory.GetParent(cur)
                cur = If(parent IsNot Nothing, parent.FullName, Nothing)
            Next
        Catch
        End Try

        Try
            Dim baseDir As String = AppContext.BaseDirectory
            For i As Integer = 0 To 5
                If String.IsNullOrWhiteSpace(baseDir) Then Exit For
                If Not searchDirs.Contains(baseDir) Then searchDirs.Add(baseDir)
                Dim parent = IO.Directory.GetParent(baseDir.TrimEnd(IO.Path.DirectorySeparatorChar))
                baseDir = If(parent IsNot Nothing, parent.FullName, Nothing)
            Next
        Catch
        End Try

        searchDirs.Add(IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RsSales"))

        For Each d In searchDirs
            Dim candidate As String = IO.Path.Combine(d, "appsettings.json")
            If IO.File.Exists(candidate) Then Return candidate
        Next
        Return Nothing
    End Function
End Class

''' <summary>Simple thread-safe file logger with daily rotation.</summary>
Public NotInheritable Class AppLog
    Private Shared ReadOnly SyncRoot As New Object()

    Private Sub New()
    End Sub

    Public Shared Property Directory As String = "."

    Public Shared Sub Info(message As String)
        Write("INFO", message, Nothing)
    End Sub

    Public Shared Sub Warn(message As String)
        Write("WARN", message, Nothing)
    End Sub

    Public Shared Sub [Error](message As String, ex As Exception)
        Write("ERROR", message & If(ex Is Nothing, String.Empty, " :: " & ex.ToString()), Nothing)
    End Sub

    Private Shared Sub Write(level As String, message As String, ex As Exception)
        Try
            SyncLock SyncRoot
                IO.Directory.CreateDirectory(Directory)
                Dim file As String = IO.Path.Combine(Directory, "rssales-" & Date.UtcNow.ToString("yyyyMMdd") & ".log")
                Dim line As String = Date.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") & "Z [" & level & "] " & message &
                                     If(ex Is Nothing, String.Empty, " | " & ex.Message) & Environment.NewLine
                IO.File.AppendAllText(file, line, Encoding.UTF8)
            End SyncLock
        Catch
        End Try
    End Sub
End Class

''' <summary>DPAPI-backed secret storage for terminal secrets (e.g. DeviceKey).</summary>
Public NotInheritable Class SecretStore
    Private Sub New()
    End Sub

    Public Shared Function Protect(plain As String) As String
        If String.IsNullOrEmpty(plain) Then Return String.Empty
        If Not OperatingSystem.IsWindows() Then Return plain
        Dim bytes As Byte() = Text.Encoding.UTF8.GetBytes(plain)
        Dim protectedBytes As Byte() = System.Security.Cryptography.ProtectedData.Protect(
            bytes, Nothing, System.Security.Cryptography.DataProtectionScope.CurrentUser)
        Return Convert.ToBase64String(protectedBytes)
    End Function

    Public Shared Function Unprotect(protectedValue As String) As String
        If String.IsNullOrWhiteSpace(protectedValue) Then Return String.Empty
        If Not OperatingSystem.IsWindows() Then Return protectedValue
        Try
            Dim bytes As Byte() = Convert.FromBase64String(protectedValue)
            Dim plainBytes As Byte() = System.Security.Cryptography.ProtectedData.Unprotect(
                bytes, Nothing, System.Security.Cryptography.DataProtectionScope.CurrentUser)
            Return Text.Encoding.UTF8.GetString(plainBytes)
        Catch
            Return protectedValue
        End Try
    End Function
End Class

''' <summary>BCrypt password hashing with a cost suitable for a POS server.</summary>
Public NotInheritable Class PasswordHasher
    Private Sub New()
    End Sub

    Public Const WorkFactor As Integer = 11

    Public Shared Function Hash(password As String) As String
        If String.IsNullOrEmpty(password) Then Throw New ArgumentException("Password is required.", NameOf(password))
        Return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor)
    End Function

    Public Shared Function Verify(password As String, hash As String) As Boolean
        If String.IsNullOrEmpty(password) OrElse String.IsNullOrEmpty(hash) Then Return False
        Try
            Return BCrypt.Net.BCrypt.Verify(password, hash)
        Catch
            Return False
        End Try
    End Function

    Public Shared Sub ValidateStrength(password As String)
        If password Is Nothing OrElse password.Length < 4 Then
            Throw New ArgumentException("كلمة المرور يجب أن تكون 4 أحرف على الأقل.")
        End If
    End Sub
End Class
