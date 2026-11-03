Imports System
Imports System.Data
Imports System.IO
Imports Dapper
Imports MySqlConnector

''' <summary>
''' One unit of work: connection + optional transaction. Always disposed.
''' Automatically resolves local root password and ensures database exists.
''' </summary>
Public NotInheritable Class DbSession
    Implements IDisposable

    Private Shared _resolvedConnectionString As String = Nothing
    Private Shared _migrationsChecked As Boolean = False

    Shared Sub New()
        DefaultTypeMap.MatchNamesWithUnderscores = True
    End Sub

    Private _connection As MySqlConnection
    Private _transaction As MySqlTransaction
    Private _disposed As Boolean

    Public ReadOnly Property Connection As MySqlConnection
        Get
            Return _connection
        End Get
    End Property

    Public ReadOnly Property Transaction As MySqlTransaction
        Get
            Return _transaction
        End Get
    End Property

    Private Sub New(connectionString As String)
        Dim targetCs As String = If(_resolvedConnectionString, Normalize(connectionString))
        Try
            _connection = New MySqlConnection(targetCs)
            _connection.Open()
            _resolvedConnectionString = targetCs
        Catch ex As MySqlException When ex.Number = 1045
            ' Never guess credentials: a wrong user/password must fail loudly instead of
            ' silently logging in with a guessed password.
            Throw New InvalidOperationException(
                "تعذّر الاتصال بقاعدة البيانات: اسم المستخدم أو كلمة المرور في سلسلة الاتصال غير صحيحة. راجع ملف appsettings.json.", ex)
        Catch ex As MySqlException When ex.Number = 1049
            Throw New InvalidOperationException(
                "تعذّر الاتصال بقاعدة البيانات: قاعدة البيانات المطلوبة غير موجودة. أنشئها أو شغّل أداة الترحيل (RsSales.DbTool).", ex)
        End Try

        If Not _migrationsChecked AndAlso _connection IsNot Nothing Then
            _migrationsChecked = True
            Try
                Dim migDir As String = FindMigrationsDir()
                If Not String.IsNullOrWhiteSpace(migDir) Then
                    SchemaMigrator.Migrate(_resolvedConnectionString, migDir)
                End If
            Catch
            End Try
        End If
    End Sub

    Private Shared Function FindMigrationsDir() As String
        Try
            Dim cur As DirectoryInfo = New DirectoryInfo(Directory.GetCurrentDirectory())
            For i As Integer = 0 To 5
                If cur Is Nothing Then Exit For
                Dim cand As String = Path.Combine(cur.FullName, "db", "migrations")
                If Directory.Exists(cand) Then Return cand
                cur = cur.Parent
            Next
            Dim baseDir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
            For i As Integer = 0 To 5
                If baseDir Is Nothing Then Exit For
                Dim cand As String = Path.Combine(baseDir.FullName, "db", "migrations")
                If Directory.Exists(cand) Then Return cand
                baseDir = baseDir.Parent
            Next
        Catch
        End Try
        Return Nothing
    End Function

    Public Shared Function Normalize(connectionString As String) As String
        If String.IsNullOrWhiteSpace(connectionString) Then Return connectionString
        Dim builder As New MySqlConnectionStringBuilder(connectionString)
        If Not connectionString.Contains("GuidFormat", StringComparison.OrdinalIgnoreCase) Then
            builder.GuidFormat = MySqlGuidFormat.None
        End If
        Return builder.ConnectionString
    End Function

    Public Shared Function Open(connectionString As String) As DbSession
        Return New DbSession(connectionString)
    End Function

    Public Function BeginTransaction() As MySqlTransaction
        If _transaction IsNot Nothing Then Throw New InvalidOperationException("Transaction already started.")
        _transaction = _connection.BeginTransaction(IsolationLevel.ReadCommitted)
        Return _transaction
    End Function

    Public Sub Commit()
        If _transaction IsNot Nothing Then
            _transaction.Commit()
            _transaction.Dispose()
            _transaction = Nothing
        End If
    End Sub

    Public Sub Rollback()
        If _transaction IsNot Nothing Then
            Try
                _transaction.Rollback()
            Finally
                _transaction.Dispose()
                _transaction = Nothing
            End Try
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Rollback()
        If _connection IsNot Nothing Then _connection.Dispose()
    End Sub
End Class
