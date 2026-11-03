Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>
''' Authentication, lockout and permission loading for DOCTOR TECH.
''' Self-heals initial branches, roles, permissions, and root/admin accounts so first login never errors out.
''' </summary>
Public NotInheritable Class AuthService
    Private Sub New()
    End Sub

    Public Shared Sub EnsureDefaultSetup(conn As IDbConnection, Optional tx As IDbTransaction = Nothing)
        Try
            ' 1. Ensure at least one active branch exists
            Dim branchCount As Long = conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM branches WHERE is_active=1", Nothing, tx)
            If branchCount = 0 Then
                conn.Execute(
                    "INSERT IGNORE INTO branches(code, name, timezone, address, phone, tax_no, is_active) " &
                    "VALUES('MAIN', 'الفرع الرئيسي - DOCTOR TECH', 'Africa/Cairo', 'القاهرة', '01000000000', '100-000-000', 1)",
                    Nothing, tx)
            End If

            ' 2. Ensure admin role and all permissions exist
            Dim allPerms As String() = {
                "items.manage", "sales.create", "sales.return", "sales.discount", "sales.below_min_price",
                "purchases.create", "purchases.return", "customers.manage", "stock.adjust", "stock.transfer",
                "stock.opening", "tax.manage", "settings.manage", "reports.view", "reports.all_branches",
                "shifts.open", "shifts.close", "users.manage", "sync.manage", "reports.profit", "hr.manage"
            }
            For Each code In allPerms
                conn.Execute("INSERT IGNORE INTO permissions(code) VALUES(@c)", New With {.c = code}, tx)
            Next

            conn.Execute("INSERT IGNORE INTO roles(code, name) VALUES('admin', 'مدير النظام')", Nothing, tx)
            Dim adminRoleId As Long = conn.ExecuteScalar(Of Long)("SELECT id FROM roles WHERE code='admin' LIMIT 1", Nothing, tx)
            If adminRoleId > 0 Then
                conn.Execute(
                    "INSERT IGNORE INTO role_permissions(role_id, permission_id) SELECT @r, id FROM permissions",
                    New With {.r = adminRoleId}, tx)
            End If

            ' 3. Bootstrap the default accounts only on a brand-new database. Existing
            '    accounts are never re-created, unlocked or have their password reset here.
            Dim userCount As Long = conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM users", Nothing, tx)
            If userCount = 0 Then
                EnsureUserAccount(conn, tx, "root", "root", "مدير النظام (Root)", adminRoleId)
                EnsureUserAccount(conn, tx, "admin", "Admin@12345", "مدير النظام (Admin)", adminRoleId)
            Else
                EnsureUserAccess(conn, tx, "root", adminRoleId)
                EnsureUserAccess(conn, tx, "admin", adminRoleId)
            End If
        Catch
        End Try
    End Sub

    Private Shared Sub EnsureUserAccount(conn As IDbConnection, tx As IDbTransaction,
                                         uname As String, defaultPass As String, fullName As String, adminRoleId As Long)
        Dim uid As Long = conn.ExecuteScalar(Of Long)(
            "SELECT COALESCE((SELECT id FROM users WHERE LOWER(username)=LOWER(@u) LIMIT 1), 0)",
            New With {.u = uname}, tx)
        If uid = 0 Then
            conn.Execute(
                "INSERT INTO users(username, password_hash, full_name, is_active, failed_attempts, locked_until) " &
                "VALUES(@u, @h, @n, 1, 0, NULL)",
                New With {.u = uname, .h = PasswordHasher.Hash(defaultPass), .n = fullName}, tx)
            uid = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
        End If

        If uid > 0 Then
            If adminRoleId > 0 Then
                conn.Execute("INSERT IGNORE INTO user_roles(user_id, role_id) VALUES(@u, @r)", New With {.u = uid, .r = adminRoleId}, tx)
            End If
            conn.Execute("INSERT IGNORE INTO user_branch_access(user_id, branch_id) SELECT @u, id FROM branches", New With {.u = uid}, tx)
        End If
    End Sub

    ''' <summary>Re-links role/branch access for an existing bootstrap account without touching its password, activation or lock state.</summary>
    Private Shared Sub EnsureUserAccess(conn As IDbConnection, tx As IDbTransaction, uname As String, adminRoleId As Long)
        Dim uid As Long = conn.ExecuteScalar(Of Long)(
            "SELECT COALESCE((SELECT id FROM users WHERE LOWER(username)=LOWER(@u) LIMIT 1), 0)",
            New With {.u = uname}, tx)
        If uid = 0 Then Return
        If adminRoleId > 0 Then
            conn.Execute("INSERT IGNORE INTO user_roles(user_id, role_id) VALUES(@u, @r)", New With {.u = uid, .r = adminRoleId}, tx)
        End If
        conn.Execute("INSERT IGNORE INTO user_branch_access(user_id, branch_id) SELECT @u, id FROM branches", New With {.u = uid}, tx)
    End Sub

    Public Shared Function Login(connectionString As String, username As String, password As String,
                                 Optional branchId As Long? = Nothing) As AppSession
        Dim cleanUser As String = If(username, String.Empty).Trim()
        Dim cleanPass As String = If(password, String.Empty).Trim()

        If String.IsNullOrWhiteSpace(cleanUser) OrElse String.IsNullOrEmpty(cleanPass) Then
            Throw New BusinessException("أدخل اسم المستخدم وكلمة المرور.")
        End If

        Using db = DbSession.Open(connectionString)
            EnsureDefaultSetup(db.Connection)

            Dim user = UsersRepository.FindByUsername(db.Connection, cleanUser)
            If user Is Nothing Then
                Throw New BusinessException("اسم المستخدم أو كلمة المرور غير صحيحة.")
            End If

            If Not user.IsActive Then
                Throw New BusinessException("هذا الحساب موقوف. راجع مدير النظام.")
            End If

            If user.LockedUntil.HasValue AndAlso user.LockedUntil.Value > Date.UtcNow Then
                Dim minutesLeft As Integer = Math.Max(1, CInt(Math.Ceiling((user.LockedUntil.Value - Date.UtcNow).TotalMinutes)))
                Throw New BusinessException("تم قفل الحساب مؤقتًا بعد محاولات دخول خاطئة. أعد المحاولة بعد " &
                                            minutesLeft.ToString() & " دقيقة.")
            End If

            If Not PasswordHasher.Verify(cleanPass, user.PasswordHash) Then
                RegisterFailedLogin(db, user)
                Throw New BusinessException("اسم المستخدم أو كلمة المرور غير صحيحة.")
            End If

            Dim permissions = UsersRepository.GetPermissions(db.Connection, user.Id)
            Dim branches = UsersRepository.GetBranches(db.Connection, user.Id)
            If branches.Count = 0 Then
                db.Connection.Execute("INSERT IGNORE INTO user_branch_access(user_id, branch_id) SELECT @u, id FROM branches", New With {.u = user.Id})
                branches = UsersRepository.GetBranches(db.Connection, user.Id)
            End If

            Dim allBranches = BranchesRepository.ListAll(db.Connection)
            Dim selectedBranch As Long = 0
            If branchId.HasValue AndAlso branchId.Value > 0 Then
                selectedBranch = branchId.Value
            ElseIf branches.Count > 0 Then
                selectedBranch = branches(0)
            ElseIf allBranches.Count > 0 Then
                selectedBranch = allBranches(0).Id
            End If

            Dim branch = BranchesRepository.GetById(db.Connection, selectedBranch)
            If branch Is Nothing AndAlso allBranches.Count > 0 Then
                branch = allBranches(0)
            End If
            If branch Is Nothing Then
                Throw New BusinessException("لا يوجد فرع مفعّل في قاعدة البيانات.")
            End If

            Dim session As New AppSession With {
                .ConnectionString = connectionString,
                .UserId = user.Id,
                .Username = user.Username,
                .FullName = user.FullName,
                .BranchId = branch.Id,
                .BranchName = branch.Name,
                .BranchTaxNo = branch.TaxNo,
                .PricesIncludeTax = SettingsRepository.GetBool(db.Connection, "tax.prices_include_tax", True),
                .TaxEnabled = SettingsRepository.GetBool(db.Connection, "tax.enabled", True)
            }
            For Each p In permissions
                session.Permissions.Add(p)
            Next

            db.BeginTransaction()
            UsersRepository.RegisterSuccess(db.Connection, user.Id, db.Transaction)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, branch.Id, user.Id, user.Username,
                "login", "user", user.Id.ToString(), Nothing, Nothing, Nothing)
            db.Commit()

            Return session
        End Using
    End Function

    ''' <summary>Counts a failed attempt and locks the account for 15 minutes after the fifth failure.</summary>
    Private Shared Sub RegisterFailedLogin(db As DbSession, user As UserRecord)
        Try
            db.BeginTransaction()
            UsersRepository.RegisterFailure(db.Connection, user.Id, 15, db.Transaction)
            db.Commit()
        Catch
            db.Rollback()
        End Try
    End Sub

    Public Shared Sub ChangePassword(connectionString As String, userId As Long, oldPassword As String,
                                     newPassword As String, username As String)
        PasswordHasher.ValidateStrength(newPassword)

        Using db = DbSession.Open(connectionString)
            Dim user = UsersRepository.FindByUsername(db.Connection, username)
            If user Is Nothing OrElse user.Id <> userId Then
                Throw New BusinessException("المستخدم غير موجود.")
            End If
            If Not PasswordHasher.Verify(oldPassword, user.PasswordHash) Then
                Throw New BusinessException("كلمة المرور الحالية غير صحيحة.")
            End If

            db.BeginTransaction()
            db.Connection.Execute(
                "UPDATE users SET password_hash=@hash, must_change_password=0 WHERE id=@id",
                New With {.hash = PasswordHasher.Hash(newPassword), .id = userId}, db.Transaction)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, Nothing, userId, username,
                "change_password", "user", userId.ToString(), Nothing, Nothing, Nothing)
            db.Commit()
        End Using
    End Sub

    Public Shared Function CreateInitialAdmin(connectionString As String, username As String, password As String,
                                              fullName As String, branchId As Long) As Long
        PasswordHasher.ValidateStrength(password)

        Using db = DbSession.Open(connectionString)
            db.BeginTransaction()
            Dim existing = UsersRepository.FindByUsername(db.Connection, username, db.Transaction)
            If existing IsNot Nothing Then Throw New BusinessException("اسم المستخدم مستخدم بالفعل.")

            db.Connection.Execute(
                "INSERT INTO users(username, password_hash, full_name) VALUES(@u, @h, @n)",
                New With {.u = username, .h = PasswordHasher.Hash(password), .n = fullName}, db.Transaction)
            Dim id = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, db.Transaction)

            Dim roleId = db.Connection.ExecuteScalar(Of Long)(
                "SELECT id FROM roles WHERE code='admin'", Nothing, db.Transaction)
            If roleId = 0 Then
                conn_InsertAdminRole(db.Connection, db.Transaction, roleId)
            End If

            db.Connection.Execute(
                "INSERT IGNORE INTO user_roles(user_id, role_id) VALUES(@u, @r)",
                New With {.u = id, .r = roleId}, db.Transaction)
            db.Connection.Execute(
                "INSERT IGNORE INTO user_branch_access(user_id, branch_id) VALUES(@u, @b)",
                New With {.u = id, .b = branchId}, db.Transaction)

            db.Commit()
            Return id
        End Using
    End Function

    Private Shared Sub conn_InsertAdminRole(conn As IDbConnection, tx As IDbTransaction, ByRef roleId As Long)
        conn.Execute("INSERT INTO roles(code, name) VALUES('admin','مدير النظام')", Nothing, tx)
        roleId = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Sub
End Class
