Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports Dapper
Imports MySqlConnector

Public Class MigrationResult
    Public Property Version As String
    Public Property Applied As Boolean
    Public Property Skipped As Boolean
    Public Property Checksum As String
End Class

Public NotInheritable Class SchemaMigrator
    Private Sub New()
    End Sub

    Public Shared Function Migrate(connectionString As String, migrationsDirectory As String) As List(Of MigrationResult)
        If Not Directory.Exists(migrationsDirectory) Then
            Throw New DirectoryNotFoundException(migrationsDirectory)
        End If

        Dim results As New List(Of MigrationResult)()

        Using conn As New MySqlConnection(connectionString)
            conn.Open()
            EnsureMigrationsTable(conn)

            Dim files = Directory.GetFiles(migrationsDirectory, "V*__*.sql").OrderBy(Function(f) f).ToArray()
            For Each filePath In files
                Dim version As String = Path.GetFileName(filePath).Split(New String() {"__"}, StringSplitOptions.None)(0)
                Dim checksum As String = ComputeChecksum(filePath)

                Dim existing As String = CStr(conn.ExecuteScalar(
                    "SELECT checksum FROM schema_migrations WHERE version=@v", New With {.v = version}))

                If Not String.IsNullOrEmpty(existing) Then
                    If Not String.Equals(existing, checksum, StringComparison.OrdinalIgnoreCase) Then
                        Throw New InvalidOperationException(
                            $"Migration {version} was modified after being applied. Create a new migration instead.")
                    End If
                    results.Add(New MigrationResult With {.Version = version, .Skipped = True, .Checksum = checksum})
                    Continue For
                End If

                ExecuteScript(conn, IO.File.ReadAllText(filePath, Encoding.UTF8))

                conn.Execute(
                    "INSERT INTO schema_migrations(version, checksum) VALUES(@v, @c)",
                    New With {.v = version, .c = checksum})

                results.Add(New MigrationResult With {.Version = version, .Applied = True, .Checksum = checksum})
            Next
        End Using

        Return results
    End Function

    Public Shared Function Rollback(connectionString As String, rollbackDirectory As String, Optional version As String = Nothing) As List(Of String)
        Dim applied As New List(Of String)()

        Using conn As New MySqlConnection(connectionString)
            conn.Open()
            EnsureMigrationsTable(conn)

            Dim versions As List(Of String)
            If String.IsNullOrWhiteSpace(version) Then
                versions = conn.Query(Of String)(
                    "SELECT version FROM schema_migrations ORDER BY version DESC").AsList()
            Else
                versions = New List(Of String) From {version}
            End If

            For Each v In versions
                Dim rollbackFile As String = Directory.GetFiles(rollbackDirectory, "R" & v.Substring(1) & "__*.sql").FirstOrDefault()
                If rollbackFile Is Nothing Then
                    Throw New FileNotFoundException($"No rollback script for {v} in {rollbackDirectory}")
                End If

                ExecuteScript(conn, IO.File.ReadAllText(rollbackFile, Encoding.UTF8))
                ' The rollback script may drop schema_migrations itself.
                If TableExists(conn, "schema_migrations") Then
                    conn.Execute("DELETE FROM schema_migrations WHERE version=@v", New With {.v = v})
                End If
                applied.Add(v)
            Next
        End Using

        Return applied
    End Function

    ''' <summary>
    ''' Executes a multi-statement SQL script. Splits on semicolons while ignoring
    ''' semicolons inside quoted strings, quoted identifiers and comments.
    ''' </summary>
    Public Shared Sub ExecuteScript(conn As MySqlConnection, sql As String)
        For Each statement In SplitStatements(sql)
            Using cmd As New MySqlCommand(statement, conn)
                cmd.CommandTimeout = 600
                cmd.ExecuteNonQuery()
            End Using
        Next
    End Sub

    Public Shared Function SplitStatements(sql As String) As List(Of String)
        Dim statements As New List(Of String)()
        If String.IsNullOrWhiteSpace(sql) Then Return statements

        Dim current As New StringBuilder()
        Dim i As Integer = 0
        Dim n As Integer = sql.Length
        Dim quote As Char = ChrW(0)

        While i < n
            Dim c As Char = sql(i)

            If quote <> ChrW(0) Then
                current.Append(c)
                If c = quote Then
                    If i + 1 < n AndAlso sql(i + 1) = quote Then
                        current.Append(sql(i + 1))
                        i += 2
                        Continue While
                    End If
                    If i = 0 OrElse sql(i - 1) <> "\"c Then quote = ChrW(0)
                End If
                i += 1
                Continue While
            End If

            If c = "'"c OrElse c = """"c OrElse c = "`"c Then
                quote = c
                current.Append(c)
                i += 1
                Continue While
            End If

            ' line comment starting with -- or #
            If (c = "-"c AndAlso i + 1 < n AndAlso sql(i + 1) = "-"c) OrElse c = "#"c Then
                While i < n AndAlso sql(i) <> ChrW(10)
                    i += 1
                End While
                current.Append(vbLf)
                Continue While
            End If

            ' block comment
            If c = "/"c AndAlso i + 1 < n AndAlso sql(i + 1) = "*"c Then
                Dim closeIndex As Integer = sql.IndexOf("*/", i + 2, StringComparison.Ordinal)
                If closeIndex < 0 Then
                    i = n
                Else
                    i = closeIndex + 2
                End If
                current.Append(vbLf)
                Continue While
            End If

            If c = ";"c Then
                Dim text As String = current.ToString().Trim()
                If text.Length > 0 Then statements.Add(text)
                current.Clear()
                i += 1
                Continue While
            End If

            current.Append(c)
            i += 1
        End While

        Dim tail As String = current.ToString().Trim()
        If tail.Length > 0 Then statements.Add(tail)
        Return statements
    End Function

    Private Shared Function TableExists(conn As MySqlConnection, tableName As String) As Boolean
        Dim count As Long = conn.ExecuteScalar(Of Long)(
            "SELECT COUNT(*) FROM information_schema.tables " &
            "WHERE table_schema=DATABASE() AND table_name=@t", New With {.t = tableName})
        Return count > 0
    End Function

    Private Shared Sub EnsureMigrationsTable(conn As MySqlConnection)
        conn.Execute(
            "CREATE TABLE IF NOT EXISTS schema_migrations (" &
            "version VARCHAR(32) NOT NULL PRIMARY KEY, " &
            "checksum CHAR(64) NOT NULL, " &
            "applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)" &
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci")
    End Sub

    Private Shared Function ComputeChecksum(path As String) As String
        Using sha As SHA256 = SHA256.Create()
            Dim bytes As Byte() = sha.ComputeHash(IO.File.ReadAllBytes(path))
            Return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
        End Using
    End Function
End Class
