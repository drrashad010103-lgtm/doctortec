Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Dapper
Imports MySqlConnector
Imports Xunit

''' <summary>
''' Creates a throwaway database (rs_sales_test_it) once per test collection,
''' applies the real migrations and seeds the minimum reference data. Every
''' integration test then works on its own items so tests stay independent
''' without truncating tables.
''' </summary>
Public Class DbFixture
    Implements IDisposable

    Public Const TestDatabase As String = "rs_sales_test_it"

    Private ReadOnly _serverConnectionString As String
    Private ReadOnly _databaseConnectionString As String

    Public ReadOnly Property BranchId As Long
    Public ReadOnly Property Tax14Id As Long
    Public ReadOnly Property Tax0Id As Long
    Public ReadOnly Property CashMethodId As Long

    Public Sub New()
        Dim server As String = Environment.GetEnvironmentVariable("RSSALES_TEST_SERVER")
        If String.IsNullOrWhiteSpace(server) Then
            server = "Server=localhost;Port=3306;User ID=root;Password=;SslMode=None;Allow User Variables=True"
        End If
        _serverConnectionString = server
        _databaseConnectionString = server & ";Database=" & TestDatabase

        ResetDatabase()
        ApplyMigrations()
        Seed()

        Using db = DbSession.Open(ConnectionString)
            BranchId = db.Connection.ExecuteScalar(Of Long)("SELECT id FROM branches WHERE code='MAIN'")
            Tax14Id = db.Connection.ExecuteScalar(Of Long)("SELECT id FROM tax_categories WHERE code='vat14'")
            Tax0Id = db.Connection.ExecuteScalar(Of Long)("SELECT id FROM tax_categories WHERE code='vat0'")
            CashMethodId = db.Connection.ExecuteScalar(Of Long)("SELECT id FROM payment_methods WHERE name='نقد'")
        End Using
    End Sub

    Public ReadOnly Property ConnectionString As String
        Get
            Return _databaseConnectionString
        End Get
    End Property

    Private Sub ResetDatabase()
        Using conn As New MySqlConnection(_serverConnectionString)
            conn.Open()
            conn.Execute("DROP DATABASE IF EXISTS " & TestDatabase)
            conn.Execute("CREATE DATABASE " & TestDatabase &
                         " CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
        End Using
    End Sub

    Private Sub ApplyMigrations()
        Dim directory As String = FindMigrationsDirectory()
        SchemaMigrator.Migrate(_databaseConnectionString, directory)
    End Sub

    Private Shared Function FindMigrationsDirectory() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "db", "migrations")
            If Directory.Exists(candidate) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("db/migrations was not found above " & AppContext.BaseDirectory)
    End Function

    Private Sub Seed()
        Using db = DbSession.Open(_databaseConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim c = db.Connection

            c.Execute(
                "INSERT INTO branches(code, name, timezone, address, phone, tax_no) " &
                "VALUES('MAIN','الفرع الرئيسي','Africa/Cairo','القاهرة','01000000000','100000000000003')", Nothing, tx)

            c.Execute("INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, effective_from) VALUES " &
                      "('vat14','ضريبة 14%','VAT 14%',14,0,'2024-01-01')," &
                      "('vat0','ضريبة صفرية','Zero',0,0,'2024-01-01')," &
                      "('exempt','معفى','Exempt',0,1,'2024-01-01')", Nothing, tx)

            c.Execute("INSERT INTO cashboxes(branch_id, name) VALUES(1,'الصندوق الرئيسي')", Nothing, tx)
            c.Execute("INSERT INTO payment_methods(name, requires_reference) VALUES('نقد',0)", Nothing, tx)

            Dim accounts As String() = {
                "1010|النقدية|asset", "1020|البنك|asset", "1100|العملاء|asset", "1200|المخزون|asset",
                "1250|مستحق من الفروع|asset", "1300|ضريبة مدخلات|asset", "2100|الموردون|liability",
                "2150|مستحق للفروع|liability", "2200|ضريبة مخرجات|liability",
                "3000|رأس المال|equity", "4000|المبيعات|revenue", "4100|مرتجع مبيعات|revenue",
                "5000|تكلفة المبيعات|expense", "5100|الخصومات|expense", "5200|تسويات المخزون|expense"
            }
            For Each account In accounts
                Dim parts = account.Split("|"c)
                c.Execute("INSERT INTO accounts(code, name, nature, is_postable) VALUES(@c,@n,@nat,1)",
                          New With {.c = parts(0), .n = parts(1), .nat = parts(2)}, tx)
            Next

            Dim settings As String() = {
                "tax.enabled|1", "tax.prices_include_tax|1", "tax.recoverable|1",
                "tax.default_category|vat14", "company.name|صيدلية الاختبار",
                "company.tax_no|100000000000003", "stock.allow_negative|0"
            }
            For Each setting In settings
                Dim parts = setting.Split("|"c)
                c.Execute("INSERT INTO settings(k, v) VALUES(@k,@v)",
                          New With {.k = parts(0), .v = parts(1)}, tx)
            Next

            c.Execute("INSERT INTO roles(code, name) VALUES('admin','مدير')", Nothing, tx)
            Dim roleId As Long = c.ExecuteScalar(Of Long)("SELECT id FROM roles WHERE code='admin'", Nothing, tx)

            Dim permissions As String() = {
                "items.manage", "sales.create", "sales.return", "sales.discount", "sales.below_min_price",
                "purchases.create", "purchases.return", "customers.manage", "stock.adjust", "stock.transfer",
                "stock.opening", "tax.manage", "settings.manage", "reports.view", "reports.profit",
                "reports.all_branches", "shifts.open", "shifts.close", "sync.manage", "hr.manage"
            }
            For Each code In permissions
                c.Execute("INSERT INTO permissions(code) VALUES(@c) ON DUPLICATE KEY UPDATE code=VALUES(code)",
                          New With {.c = code}, tx)
                Dim permissionId As Long = c.ExecuteScalar(Of Long)(
                    "SELECT id FROM permissions WHERE code=@c", New With {.c = code}, tx)
                c.Execute("INSERT IGNORE INTO role_permissions(role_id, permission_id) VALUES(@r,@p)",
                          New With {.r = roleId, .p = permissionId}, tx)
            Next

            c.Execute("INSERT INTO users(username, password_hash, full_name) VALUES('tester', @h, 'مستخدم الاختبار')",
                      New With {.h = PasswordHasher.Hash("Test@12345")}, tx)
            Dim userId As Long = c.ExecuteScalar(Of Long)("SELECT id FROM users WHERE username='tester'", Nothing, tx)
            c.Execute("INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r)", New With {.u = userId, .r = roleId}, tx)
            c.Execute("INSERT INTO user_branch_access(user_id, branch_id) VALUES(@u,1)", New With {.u = userId}, tx)

            db.Commit()
        End Using
    End Sub

    ''' <summary>Full-permission session for the seeded test user.</summary>
    Public Function Session(Optional username As String = "tester") As AppSession
        Dim result As New AppSession With {
            .ConnectionString = ConnectionString,
            .UserId = 1,
            .Username = username,
            .FullName = "مستخدم الاختبار",
            .BranchId = BranchId,
            .BranchName = "الفرع الرئيسي",
            .BranchTaxNo = "100000000000003",
            .PricesIncludeTax = True,
            .TaxEnabled = True
        }
        Using db = DbSession.Open(ConnectionString)
            result.UserId = db.Connection.ExecuteScalar(Of Long)(
                "SELECT id FROM users WHERE username=@u", New With {.u = username})
            For Each permission In UsersRepository.GetPermissions(db.Connection, result.UserId)
                result.Permissions.Add(permission)
            Next
        End Using
        Return result
    End Function

    ''' <summary>Creates an item and gives it opening stock at the given cost.</summary>
    Public Function CreateItemWithStock(code As String, salePrice As Decimal, cost As Decimal,
                                        qty As Decimal, Optional taxCategoryId As Long = 0,
                                        Optional occurredAt As Date? = Nothing) As Long
        If taxCategoryId = 0 Then taxCategoryId = Tax14Id
        Using db = DbSession.Open(ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            db.Connection.Execute(
                "INSERT INTO items(uuid, code, name_ar, cost_price, sale_price, tax_category_id) " &
                "VALUES(@uuid,@code,@name,@cost,@sale,@tax)",
                New With {.uuid = Uuid7.NewId(), .code = code, .name = "صنف " & code,
                          .cost = cost, .sale = salePrice, .tax = taxCategoryId}, tx)
            Dim itemId As Long = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)

            If qty <> 0D Then
                Dim moment As Date = If(occurredAt.HasValue, occurredAt.Value, Date.UtcNow)
                StockRepository.AddMove(db.Connection, tx, Uuid7.NewId(), BranchId, itemId, "opening",
                                        qty, cost, "manual", Nothing, Nothing, moment, moment.Date,
                                        1, Nothing, "test opening")
            End If
            db.Commit()
            Return itemId
        End Using
    End Function

    Private _dateCounter As Integer

    ''' <summary>
    ''' Returns a date no other test uses, so date-range reports stay isolated.
    ''' </summary>
    Public Function NextTestDate() As Date
        Dim n As Integer = Threading.Interlocked.Increment(_dateCounter)
        Return New Date(2020, 1, 1).AddDays(n)
    End Function

    Public Function QtyOf(itemId As Long) As Decimal
        Using db = DbSession.Open(ConnectionString)
            Return StockRepository.GetQty(db.Connection, BranchId, itemId)
        End Using
    End Function

    Public Function ScalarLong(sql As String, Optional param As Object = Nothing) As Long
        Using db = DbSession.Open(ConnectionString)
            Return db.Connection.ExecuteScalar(Of Long)(sql, param)
        End Using
    End Function

    Public Function ScalarDecimal(sql As String, Optional param As Object = Nothing) As Decimal
        Using db = DbSession.Open(ConnectionString)
            Return db.Connection.ExecuteScalar(Of Decimal)(sql, param)
        End Using
    End Function

    Public Function JournalIsBalanced() As Boolean
        Dim difference As Decimal = ScalarDecimal(
            "SELECT COALESCE(SUM(jl.debit)-SUM(jl.credit),0) FROM journal_lines jl " &
            "JOIN journal_entries je ON je.id=jl.entry_id WHERE je.status='posted'")
        Return Math.Abs(difference) < 0.005D
    End Function

    Public Function OutboxStatus(eventId As String) As String
        Using db = DbSession.Open(ConnectionString)
            Return db.Connection.ExecuteScalar(Of String)(
                "SELECT status FROM sync_outbox WHERE event_id=@id", New With {.id = eventId})
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        ' Kept for inspection; the next run drops and recreates it.
    End Sub
End Class

<CollectionDefinition("Database")>
Public Class DatabaseCollection
    Implements ICollectionFixture(Of DbFixture)
End Class
