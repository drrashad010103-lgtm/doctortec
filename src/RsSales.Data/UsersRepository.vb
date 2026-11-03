Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public NotInheritable Class UsersRepository
    Private Sub New()
    End Sub

    Public Shared Function FindByUsername(conn As IDbConnection, username As String, Optional tx As IDbTransaction = Nothing) As UserRecord
        Return conn.QueryFirstOrDefault(Of UserRecord)(
            "SELECT id, username, password_hash, full_name, is_active, must_change_password, failed_attempts, locked_until " &
            "FROM users WHERE username=@u LIMIT 1",
            New With {.u = username}, tx)
    End Function

    Public Shared Function GetPermissions(conn As IDbConnection, userId As Long, Optional tx As IDbTransaction = Nothing) As List(Of String)
        Return conn.Query(Of String)(
            "SELECT DISTINCT p.code FROM permissions p " &
            "JOIN role_permissions rp ON rp.permission_id=p.id " &
            "JOIN user_roles ur ON ur.role_id=rp.role_id " &
            "WHERE ur.user_id=@id", New With {.id = userId}, tx).AsList()
    End Function

    Public Shared Function GetBranches(conn As IDbConnection, userId As Long, Optional tx As IDbTransaction = Nothing) As List(Of Long)
        Return conn.Query(Of Long)(
            "SELECT branch_id FROM user_branch_access WHERE user_id=@id ORDER BY branch_id",
            New With {.id = userId}, tx).AsList()
    End Function

    Public Shared Sub RegisterFailure(conn As IDbConnection, userId As Long, lockMinutes As Integer, tx As IDbTransaction)
        conn.Execute(
            "UPDATE users SET failed_attempts=failed_attempts+1, " &
            "locked_until=CASE WHEN failed_attempts+1>=5 THEN DATE_ADD(UTC_TIMESTAMP(6), INTERVAL @m MINUTE) ELSE locked_until END " &
            "WHERE id=@id", New With {.id = userId, .m = lockMinutes}, tx)
    End Sub

    Public Shared Sub RegisterSuccess(conn As IDbConnection, userId As Long, tx As IDbTransaction)
        conn.Execute(
            "UPDATE users SET failed_attempts=0, locked_until=NULL WHERE id=@id",
            New With {.id = userId}, tx)
    End Sub
End Class
