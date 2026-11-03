Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Dapper
Imports MySqlConnector

Module Program

    Sub Main(args As String())
        Console.OutputEncoding = Text.Encoding.UTF8
        Try
            If args.Length = 0 Then
                PrintUsage()
                Return
            End If

            Dim command As String = args(0).Trim().ToLowerInvariant()
            Dim options As Dictionary(Of String, String) = ParseOptions(args.Skip(1).ToArray())

            Select Case command
                Case "migrate"
                    RunMigrate(options)
                Case "rollback"
                    RunRollback(options)
                Case "seed"
                    RunSeed(options)
                Case "create-admin"
                    RunCreateAdmin(options)
                Case "import-legacy"
                    RunImportLegacy(options)
                Case "verify"
                    RunVerify(options)
                Case "status"
                    RunStatus(options)
                Case "help", "--help", "-h", "/?"
                    PrintUsage()
                Case Else
                    Console.Error.WriteLine("Unknown command: " & command)
                    PrintUsage()
                    Environment.ExitCode = 1
            End Select
        Catch ex As Exception
            Console.Error.WriteLine("ERROR: " & ex.Message)
            If Environment.GetEnvironmentVariable("RSSALES_DEBUG") = "1" Then
                Console.Error.WriteLine(ex.ToString())
            End If
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub PrintUsage()
        Console.WriteLine("RS Sales database tool")
        Console.WriteLine()
        Console.WriteLine("  migrate        [--conn CS] [--dir db/migrations]")
        Console.WriteLine("  rollback       [--conn CS] [--dir db/rollback] [--version V001]")
        Console.WriteLine("  seed           [--conn CS] [--branch MAIN] [--tax-no 123] [--admin admin] [--password ...]")
        Console.WriteLine("                 (--password is required when the admin user does not exist yet)")
        Console.WriteLine("  create-admin   [--conn CS] --username u --password p --name ""Full Name"" [--branch 1]")
        Console.WriteLine("  import-legacy  [--conn CS] [--source CS] [--limit N] [--dry-run 1]")
        Console.WriteLine("                 (--source defaults to the legacy rs_pharmacy database;")
        Console.WriteLine("                  --dry-run only counts what would be imported, writing nothing)")
        Console.WriteLine("  verify         [--conn CS]")
        Console.WriteLine("  status         [--conn CS]")
        Console.WriteLine()
        Console.WriteLine("Connection string can also come from RSSALES_CONNECTION.")
    End Sub

    Private Function ParseOptions(args As String()) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim i As Integer = 0
        While i < args.Length
            Dim token As String = args(i)
            If token.StartsWith("--") Then
                Dim name As String = token.Substring(2)
                Dim value As String = "1"
                If i + 1 < args.Length AndAlso Not args(i + 1).StartsWith("--") Then
                    value = args(i + 1)
                    i += 1
                End If
                result(name) = value
            End If
            i += 1
        End While
        Return result
    End Function

    Private Function GetOption(options As Dictionary(Of String, String), name As String, defaultValue As String) As String
        Dim value As String = Nothing
        If options.TryGetValue(name, value) AndAlso Not String.IsNullOrWhiteSpace(value) Then Return value
        Return defaultValue
    End Function

    Private Function ConnectionString(options As Dictionary(Of String, String)) As String
        Dim conn As String = GetOption(options, "conn", Nothing)
        If String.IsNullOrWhiteSpace(conn) Then conn = Environment.GetEnvironmentVariable("RSSALES_CONNECTION")
        If String.IsNullOrWhiteSpace(conn) Then
            ' Same appsettings.json the desktop app reads, so the tool targets the database the cashier is using.
            conn = AppConfig.Load().ConnectionString
        End If
        Return conn
    End Function

    Private Function ProjectRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "RsSales.slnx")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Return Directory.GetCurrentDirectory()
    End Function

    Private Sub RunMigrate(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Dim dir = GetOption(options, "dir", Path.Combine(ProjectRoot(), "db", "migrations"))
        Console.WriteLine("Migrating from " & dir)

        Dim results = SchemaMigrator.Migrate(conn, dir)
        For Each r In results
            Console.WriteLine("  " & r.Version & If(r.Applied, " applied", " already applied"))
        Next
        If results.Count = 0 Then Console.WriteLine("  (no migration files found)")
        Console.WriteLine("Done.")
    End Sub

    Private Sub RunRollback(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Dim dir = GetOption(options, "dir", Path.Combine(ProjectRoot(), "db", "rollback"))
        Dim version = GetOption(options, "version", Nothing)

        Console.WriteLine("Rolling back " & If(String.IsNullOrWhiteSpace(version), "all applied migrations", version))
        Dim applied = SchemaMigrator.Rollback(conn, dir, version)
        For Each v In applied
            Console.WriteLine("  " & v & " rolled back")
        Next
        If applied.Count = 0 Then Console.WriteLine("  (nothing to roll back)")
    End Sub

    Private Sub RunSeed(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Dim branchCode = GetOption(options, "branch", "MAIN")
        Dim taxNo = GetOption(options, "tax-no", Nothing)
        Dim adminUser = GetOption(options, "admin", "admin")
        Dim adminPassword = GetOption(options, "password", Nothing)
        If Not String.IsNullOrWhiteSpace(adminPassword) Then
            PasswordHasher.ValidateStrength(adminPassword)
        End If
        Dim createAdmin As Boolean = False

        Using db = DbSession.Open(conn)
            db.BeginTransaction()
            Dim tx = db.Transaction

            Dim adminCount As Long = db.Connection.ExecuteScalar(Of Long)(
                "SELECT COUNT(*) FROM users WHERE username=@u", New With {.u = adminUser}, tx)
            If adminCount = 0 AndAlso String.IsNullOrWhiteSpace(adminPassword) Then
                Throw New ArgumentException(
                    "--password is required to create the first admin (min 8 chars, letter and digit).")
            End If
            createAdmin = (adminCount = 0)

            Dim branchId As Long
            Dim existingBranch As Long = db.Connection.ExecuteScalar(Of Long)(
                "SELECT id FROM branches WHERE code=@c", New With {.c = branchCode}, tx)
            If existingBranch = 0 Then
                db.Connection.Execute(
                    "INSERT INTO branches(code, name, timezone, phone, tax_no) VALUES(@c, @n, 'Africa/Cairo', @p, @t)",
                    New With {.c = branchCode, .n = "الفرع الرئيسي", .p = Nothing, .t = taxNo}, tx)
                branchId = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
                Console.WriteLine("Branch created: " & branchCode)
            Else
                branchId = existingBranch
                If Not String.IsNullOrWhiteSpace(taxNo) Then
                    db.Connection.Execute("UPDATE branches SET tax_no=@t WHERE id=@id",
                                          New With {.t = taxNo, .id = branchId}, tx)
                End If
                Console.WriteLine("Branch exists: " & branchCode)
            End If

            Dim categories As New List(Of Tuple(Of String, String, Decimal, Boolean)) From {
                Tuple.Create("vat14", "ضريبة قيمة مضافة 14%", 14D, False),
                Tuple.Create("vat0", "ضريبة صفرية", 0D, False),
                Tuple.Create("exempt", "معفى من الضريبة", 0D, True)
            }
            For Each cat In categories
                Dim exists As Long = db.Connection.ExecuteScalar(Of Long)(
                    "SELECT COUNT(*) FROM tax_categories WHERE code=@c", New With {.c = cat.Item1}, tx)
                If exists = 0 Then
                    db.Connection.Execute(
                        "INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, effective_from) " &
                        "VALUES(@c, @n, @n, @r, @e, '2024-01-01')",
                        New With {.c = cat.Item1, .n = cat.Item2, .r = cat.Item3, .e = cat.Item4}, tx)
                End If
            Next

            If db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM cashboxes WHERE branch_id=@b",
                                                    New With {.b = branchId}, tx) = 0 Then
                db.Connection.Execute("INSERT INTO cashboxes(branch_id, name) VALUES(@b, 'الصندوق الرئيسي')",
                                      New With {.b = branchId}, tx)
            End If

            Dim methods As New List(Of Tuple(Of String, Boolean)) From {
                Tuple.Create("كاش", True), Tuple.Create("شبكة", False),
                Tuple.Create("محفظة", False), Tuple.Create("آجل", False)
            }
            For Each m In methods
                db.Connection.Execute(
                    "INSERT INTO payment_methods(name, requires_reference) VALUES(@n, @r) " &
                    "ON DUPLICATE KEY UPDATE name=VALUES(name)",
                    New With {.n = m.Item1, .r = m.Item2}, tx)
            Next

            SeedAccounts(db.Connection, tx)
            SeedRoles(db.Connection, tx, branchId, adminUser, adminPassword)
            SeedSettings(db.Connection, tx, branchCode, taxNo)

            db.Commit()
        End Using

        Console.WriteLine("Seed complete.")
        Console.WriteLine("  admin user: " & adminUser)
        If createAdmin Then Console.WriteLine("  password: (set from --password)")
    End Sub

    Private Sub SeedAccounts(conn As MySqlConnection, tx As MySqlTransaction)
        Dim accounts As Object(,) = {
            {"1010", "النقدية", "asset", 0},
            {"1020", "البنك", "asset", 0},
            {"1100", "العملاء (مدينون)", "asset", 0},
            {"1200", "المخزون", "asset", 0},
            {"1250", "مستحق من الفروع", "asset", 0},
            {"1300", "ضريبة القيمة المضافة - مدخلات", "asset", 0},
            {"2100", "الموردون (دائنون)", "liability", 0},
            {"2150", "مستحق للفروع", "liability", 0},
            {"2200", "ضريبة القيمة المضافة - مخرجات", "liability", 0},
            {"3000", "رأس المال", "equity", 0},
            {"4000", "المبيعات", "revenue", 0},
            {"4100", "مرتجع المبيعات", "revenue", 0},
            {"5000", "تكلفة المبيعات", "expense", 0},
            {"2160", "قسائم مشتريات مقدمة", "liability", 0},
            {"5100", "الخصومات", "expense", 0},
            {"5200", "تسويات المخزون", "expense", 0},
            {"5300", "الرواتب والأجور", "expense", 0}
        }

        For i As Integer = 0 To accounts.GetLength(0) - 1
            conn.Execute(
                "INSERT INTO accounts(code, name, nature, is_postable) VALUES(@c, @n, @nat, 1) " &
                "ON DUPLICATE KEY UPDATE name=VALUES(name), nature=VALUES(nature)",
                New With {.c = CStr(accounts(i, 0)), .n = CStr(accounts(i, 1)), .nat = CStr(accounts(i, 2))}, tx)
        Next
    End Sub

    Private Sub SeedRoles(conn As MySqlConnection, tx As MySqlTransaction, branchId As Long,
                          adminUsername As String, adminPassword As String)
        Dim permissions As String() = {
            "items.manage", "sales.create", "sales.return", "sales.discount", "sales.below_min_price",
            "purchases.create", "purchases.return", "customers.manage", "stock.adjust", "stock.transfer",
            "stock.opening", "tax.manage", "settings.manage", "reports.view", "reports.all_branches",
            "shifts.open", "shifts.close", "users.manage", "sync.manage", "reports.profit", "hr.manage"
        }
        For Each code In permissions
            conn.Execute("INSERT INTO permissions(code) VALUES(@c) ON DUPLICATE KEY UPDATE code=VALUES(code)",
                         New With {.c = code}, tx)
        Next

        Dim roles As New List(Of Tuple(Of String, String, String())) From {
            Tuple.Create("admin", "مدير النظام", permissions),
            Tuple.Create("manager", "مدير فرع",
                New String() {"items.manage", "sales.create", "sales.return", "sales.discount", "sales.below_min_price",
                              "purchases.create", "purchases.return", "customers.manage", "stock.adjust",
                              "stock.transfer", "stock.opening", "reports.view", "reports.profit", "shifts.open", "shifts.close", "hr.manage"}),
            Tuple.Create("cashier", "كاشير",
                New String() {"sales.create", "sales.return", "shifts.open", "shifts.close", "reports.view"}),
            Tuple.Create("accountant", "محاسب",
                New String() {"reports.view", "reports.profit", "reports.all_branches", "tax.manage", "customers.manage"})
        }

        For Each role In roles
            conn.Execute("INSERT INTO roles(code, name) VALUES(@c, @n) ON DUPLICATE KEY UPDATE name=VALUES(name)",
                         New With {.c = role.Item1, .n = role.Item2}, tx)
            Dim roleId As Long = conn.ExecuteScalar(Of Long)("SELECT id FROM roles WHERE code=@c",
                                                             New With {.c = role.Item1}, tx)
            For Each permissionCode In role.Item3
                Dim permissionId As Long = conn.ExecuteScalar(Of Long)(
                    "SELECT id FROM permissions WHERE code=@c", New With {.c = permissionCode}, tx)
                conn.Execute("INSERT IGNORE INTO role_permissions(role_id, permission_id) VALUES(@r, @p)",
                             New With {.r = roleId, .p = permissionId}, tx)
            Next
        Next

        Dim userId As Long = conn.ExecuteScalar(Of Long)(
            "SELECT id FROM users WHERE username=@u", New With {.u = adminUsername}, tx)
        If userId = 0 Then
            conn.Execute(
                "INSERT INTO users(username, password_hash, full_name) VALUES(@u, @h, 'مدير النظام')",
                New With {.u = adminUsername, .h = PasswordHasher.Hash(adminPassword)}, tx)
            userId = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)

            Dim adminRoleId As Long = conn.ExecuteScalar(Of Long)("SELECT id FROM roles WHERE code='admin'", Nothing, tx)
            conn.Execute("INSERT IGNORE INTO user_roles(user_id, role_id) VALUES(@u, @r)",
                         New With {.u = userId, .r = adminRoleId}, tx)
            conn.Execute("INSERT IGNORE INTO user_branch_access(user_id, branch_id) VALUES(@u, @b)",
                         New With {.u = userId, .b = branchId}, tx)
        Else
            conn.Execute("INSERT IGNORE INTO user_branch_access(user_id, branch_id) VALUES(@u, @b)",
                         New With {.u = userId, .b = branchId}, tx)
        End If
    End Sub

    Private Sub SeedSettings(conn As MySqlConnection, tx As MySqlTransaction, branchCode As String, taxNo As String)
        Dim settings As New List(Of Tuple(Of String, String)) From {
            Tuple.Create("tax.enabled", "1"),
            Tuple.Create("tax.prices_include_tax", "1"),
            Tuple.Create("tax.recoverable", "1"),
            Tuple.Create("tax.default_category", "vat14"),
            Tuple.Create("company.name", "صيدلية"),
            Tuple.Create("company.branch_code", branchCode),
            Tuple.Create("company.tax_no", If(taxNo, String.Empty)),
            Tuple.Create("sales.receipt_footer", "شكرًا لتعاملكم معنا"),
            Tuple.Create("stock.allow_negative", "0"),
            Tuple.Create("sync.batch_size", "50")
        }
        For Each setting In settings
            conn.Execute(
                "INSERT INTO settings(k, v) VALUES(@k, @v) ON DUPLICATE KEY UPDATE v=VALUES(v)",
                New With {.k = setting.Item1, .v = setting.Item2}, tx)
        Next
    End Sub

    Private Sub RunCreateAdmin(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Dim username = GetOption(options, "username", Nothing)
        Dim password = GetOption(options, "password", Nothing)
        Dim fullName = GetOption(options, "name", username)
        If String.IsNullOrWhiteSpace(username) OrElse String.IsNullOrWhiteSpace(password) Then
            Throw New ArgumentException("--username and --password are required.")
        End If

        Dim branchId As Long = 1
        Long.TryParse(GetOption(options, "branch", "1"), branchId)

        Dim id = AuthService.CreateInitialAdmin(conn, username, password, fullName, branchId)
        Console.WriteLine("Admin created with id " & id.ToString())
    End Sub

    Private Sub RunImportLegacy(options As Dictionary(Of String, String))
        Dim targetConn = ConnectionString(options)
        Dim sourceConn = GetOption(options, "source",
            "Server=localhost;Port=3306;Database=rs_pharmacy;User ID=root;Password=;SslMode=None")
        Dim limit As Integer = 0
        Integer.TryParse(GetOption(options, "limit", "0"), limit)

        If GetOption(options, "dry-run", "0") = "1" Then
            ReportLegacyPreview(sourceConn, targetConn)
            Return
        End If

        Console.WriteLine("Importing legacy items into the new database...")

        Using source As New MySqlConnection(sourceConn)
            source.Open()
            Using target = DbSession.Open(targetConn)
                target.BeginTransaction()
                Dim tx = target.Transaction

                ' company identity (M-7: full company profile + branch metadata)
                Dim company = source.QueryFirstOrDefault(Of LegacyCompanyRow)(
                    "SELECT name, Tel, Mobile, Address, tax_no FROM companies WHERE IS_Deleted=0 ORDER BY id LIMIT 1")
                If company IsNot Nothing Then
                    Dim companyName As String = If(company.Name, String.Empty).Trim()
                    If companyName.Length > 0 Then
                        UpsertSetting(target.Connection, tx, "company.name", companyName)
                        UpsertSetting(target.Connection, tx, "company.name_ar", companyName)
                    End If
                    If Not String.IsNullOrWhiteSpace(company.TaxNo) Then
                        UpsertSetting(target.Connection, tx, "company.tax_no", company.TaxNo.Trim())
                    End If
                    If Not String.IsNullOrWhiteSpace(company.Address) Then
                        UpsertSetting(target.Connection, tx, "company.address", company.Address.Trim())
                    End If
                    If Not String.IsNullOrWhiteSpace(company.Tel) Then
                        UpsertSetting(target.Connection, tx, "company.phone", company.Tel.Trim())
                    End If
                    If Not String.IsNullOrWhiteSpace(company.Mobile) Then
                        UpsertSetting(target.Connection, tx, "company.mobile", company.Mobile.Trim())
                    End If
                    target.Connection.Execute(
                        "UPDATE branches SET phone=COALESCE(@phone, phone), address=COALESCE(@addr, address), " &
                        "tax_no=COALESCE(@tax, tax_no) WHERE id=1",
                        New With {.phone = company.Tel, .addr = company.Address, .tax = company.TaxNo}, tx)
                End If

                ' groups (M-3: two-pass import to preserve parent_id hierarchy safely)
                Dim groupCount As Integer = 0
                Dim legacyGroups = source.Query(Of LegacyGroupRow)("SELECT id, name, parent_id AS ParentId FROM groups").AsList()
                Dim validGroupIds As New HashSet(Of Long)()
                For Each g In legacyGroups
                    target.Connection.Execute(
                        "INSERT INTO item_groups(id, name, parent_id) VALUES(@id, @name, NULL) " &
                        "ON DUPLICATE KEY UPDATE name=VALUES(name)",
                        New With {.id = g.Id, .name = g.Name}, tx)
                    validGroupIds.Add(g.Id)
                    groupCount += 1
                Next
                For Each g In legacyGroups
                    If g.ParentId.HasValue AndAlso g.ParentId.Value > 0 AndAlso g.ParentId.Value <> g.Id AndAlso
                       validGroupIds.Contains(g.ParentId.Value) Then
                        target.Connection.Execute(
                            "UPDATE item_groups SET parent_id=@pid WHERE id=@id",
                            New With {.pid = g.ParentId.Value, .id = g.Id}, tx)
                    End If
                Next

                ' units
                For Each u In source.Query(Of LegacyUnitRow)("SELECT id, name FROM units")
                    target.Connection.Execute(
                        "INSERT INTO units(id, name) VALUES(@id, @name) ON DUPLICATE KEY UPDATE name=VALUES(name)",
                        New With {.id = u.Id, .name = u.Name}, tx)
                Next

                Dim vat0 As Long = target.Connection.ExecuteScalar(Of Long)(
                    "SELECT id FROM tax_categories WHERE code='vat0'", Nothing, tx)
                If vat0 = 0 Then
                    target.Connection.Execute(
                        "INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, effective_from) " &
                        "VALUES('vat0','ضريبة صفرية','Zero rated',0,0,'2024-01-01')", Nothing, tx)
                    vat0 = target.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
                End If

                Dim sql As String =
                    "SELECT id, symbol, nameEN, no, group_id, unit AS UnitId, IS_Deleted, purch_price, " &
                    "       sale_price, whole_price, minprice, `limit` AS ReorderLevel " &
                    "FROM currencies WHERE IS_Deleted=0 ORDER BY id"
                If limit > 0 Then sql &= " LIMIT " & limit.ToString()

                Dim imported As Integer = 0
                For Each row In source.Query(Of LegacyItemRow)(sql)
                    Dim legacyId As Long = row.Id
                    Dim code As String = "L" & legacyId.ToString("000000")
                    Dim nameAr As String = If(row.Symbol, "").Trim()
                    If nameAr.Length = 0 Then nameAr = "صنف " & legacyId.ToString()

                    Dim cost As Decimal = ToDecimal(row.PurchPrice)
                    Dim sale As Decimal = ToDecimal(row.SalePrice)
                    Dim whole As Decimal = ToDecimal(row.WholePrice)
                    Dim minPrice As Decimal = ToDecimal(row.MinPrice)
                    Dim reorder As Decimal = ToDecimal(row.ReorderLevel)
                    If cost < 0D Then cost = 0D
                    If sale < 0D Then sale = 0D

                    If nameAr.Length > 255 Then nameAr = nameAr.Substring(0, 255)

                    target.Connection.Execute(
                        "INSERT INTO items(id, uuid, code, name_ar, name_en, group_id, base_unit_id, cost_price, " &
                        "sale_price, wholesale_price, min_price, reorder_level, tax_category_id, is_active) " &
                        "VALUES(@id, @uuid, @code, @nameAr, @nameEn, @groupId, @unitId, @cost, @sale, @whole, " &
                        "@minPrice, @reorder, @taxId, 1) " &
                        "ON DUPLICATE KEY UPDATE name_ar=VALUES(name_ar), name_en=VALUES(name_en), " &
                        "group_id=VALUES(group_id), base_unit_id=VALUES(base_unit_id), cost_price=VALUES(cost_price), " &
                        "sale_price=VALUES(sale_price), wholesale_price=VALUES(wholesale_price), " &
                        "min_price=VALUES(min_price), reorder_level=VALUES(reorder_level)",
                        New With {
                            .id = legacyId, .uuid = Uuid7.NewId(), .code = code, .nameAr = nameAr,
                            .nameEn = row.NameEn, .groupId = row.GroupId, .unitId = row.UnitId,
                            .cost = cost, .sale = sale, .whole = whole, .minPrice = minPrice,
                            .reorder = reorder, .taxId = vat0
                        }, tx)

                    imported += 1
                    If imported Mod 1000 = 0 Then Console.WriteLine("  items: " & imported.ToString())
                Next

                ' barcodes (M-2: detect duplicate barcode conflicts across different items)
                Dim barcodeCount As Integer = 0
                Dim barcodeConflicts As Integer = 0
                For Each b In source.Query(Of LegacyBarcodeRow)("SELECT curr, barcode FROM curr_barcodes")
                    Dim barcode As String = If(b.Barcode, "").Trim()
                    If barcode.Length = 0 OrElse barcode.Length > 64 Then Continue For
                    Dim itemExists As Long = target.Connection.ExecuteScalar(Of Long)(
                        "SELECT COUNT(*) FROM items WHERE id=@id", New With {.id = b.Curr}, tx)
                    If itemExists = 0 Then Continue For

                    Dim existingOwner As Object = target.Connection.ExecuteScalar(
                        "SELECT item_id FROM item_barcodes WHERE barcode=@b", New With {.b = barcode}, tx)
                    If existingOwner IsNot Nothing AndAlso existingOwner IsNot DBNull.Value Then
                        Dim ownerId As Long = Convert.ToInt64(existingOwner)
                        If ownerId <> b.Curr Then
                            barcodeConflicts += 1
                            Console.WriteLine($"  WARN duplicate barcode '{barcode}' on item {b.Curr} (already assigned to item {ownerId}) — skipped.")
                            Continue For
                        End If
                    End If

                    target.Connection.Execute(
                        "INSERT INTO item_barcodes(item_id, barcode, is_primary) VALUES(@i, @b, 1) " &
                        "ON DUPLICATE KEY UPDATE is_primary=VALUES(is_primary)",
                        New With {.i = b.Curr, .b = barcode}, tx)
                    barcodeCount += 1
                Next

                ' legacy customers (M-1: deduplicate by name + mobile so re-runs are idempotent)
                Dim customerCount As Integer = 0
                For Each c In source.Query(Of LegacyCustomerRow)(
                        "SELECT id, name, mobile, tel, email, tax_no, maxdepit AS MaxDebit, IS_Deleted AS IsDeleted FROM customers")
                    If c.IsDeleted = 1 Then Continue For
                    Dim name As String = If(c.Name, "").Trim()
                    If name.Length = 0 Then Continue For
                    If name.Length > 255 Then name = name.Substring(0, 255)
                    Dim mobileClean As String = If(c.Mobile, "").Trim()

                    Dim existingCustId As Object = target.Connection.ExecuteScalar(
                        "SELECT id FROM customers WHERE name=@name AND COALESCE(mobile,'')=@mob LIMIT 1",
                        New With {.name = name, .mob = mobileClean}, tx)

                    If existingCustId IsNot Nothing AndAlso existingCustId IsNot DBNull.Value Then
                        target.Connection.Execute(
                            "UPDATE customers SET phone=@phone, email=@email, tax_no=@taxNo, credit_limit=@credit WHERE id=@id",
                            New With {
                                .id = Convert.ToInt64(existingCustId), .phone = c.Tel,
                                .email = c.Email, .taxNo = c.TaxNo, .credit = ToDecimal(c.MaxDebit)
                            }, tx)
                    Else
                        target.Connection.Execute(
                            "INSERT INTO customers(uuid, name, type, mobile, phone, email, tax_no, credit_limit) " &
                            "VALUES(@uuid, @name, 'customer', @mobile, @phone, @email, @taxNo, @credit)",
                            New With {
                                .uuid = Uuid7.NewId(), .name = name,
                                .mobile = If(mobileClean.Length = 0, Nothing, mobileClean),
                                .phone = c.Tel, .email = c.Email,
                                .taxNo = c.TaxNo, .credit = ToDecimal(c.MaxDebit)
                            }, tx)
                    End If
                    customerCount += 1
                Next

                ' M-11: write audit log entry for the legacy migration run
                AuditRepository.Log(
                    target.Connection, tx, Uuid7.NewId(), 1L, Nothing, "dbtool",
                    "legacy_import", "migration", "rs_pharmacy", Nothing,
                    $"items={imported},barcodes={barcodeCount},conflicts={barcodeConflicts},customers={customerCount},groups={groupCount}",
                    Nothing)

                target.Commit()
                Console.WriteLine($"Imported items: {imported}, barcodes: {barcodeCount} (conflicts skipped: {barcodeConflicts}), customers: {customerCount}, groups: {groupCount}")
            End Using
        End Using
    End Sub

    ''' <summary>Counts what import-legacy would move, reading the legacy database only — no writes.</summary>
    Private Sub ReportLegacyPreview(sourceConn As String, targetConn As String)
        Using source As New MySqlConnection(sourceConn)
            source.Open()
            Console.WriteLine("LEGACY SOURCE (read-only)")
            Console.WriteLine("  items (currencies) : " & source.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM currencies WHERE IS_Deleted=0").ToString())
            Console.WriteLine("  barcodes           : " & source.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM curr_barcodes").ToString())
            Console.WriteLine("  groups             : " & source.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM groups").ToString())
            Console.WriteLine("  units              : " & source.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM units").ToString())
            Console.WriteLine("  customers          : " & source.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM customers WHERE IS_Deleted=0").ToString())
        End Using
        Using target = DbSession.Open(targetConn)
            Console.WriteLine("TARGET DATABASE")
            Console.WriteLine("  items now          : " & target.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM items").ToString())
            Console.WriteLine("  customers now      : " & target.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM customers").ToString())
        End Using
    End Sub

    Private Sub UpsertSetting(conn As MySqlConnection, tx As MySqlTransaction, key As String, value As String)
        conn.Execute(
            "INSERT INTO settings(k, v) VALUES(@k, @v) ON DUPLICATE KEY UPDATE v=VALUES(v)",
            New With {.k = key, .v = value}, tx)
    End Sub

    Private Function ToDecimal(value As Object) As Decimal
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    Private Sub RunStatus(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Using db = DbSession.Open(conn)
            Dim branchCount = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM branches")
            Dim itemCount = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM items")
            Dim customerCount = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM customers")
            Dim invoiceCount = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM invoices")
            Dim moveCount = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM stock_moves")
            Dim stats = SyncStore.Stats(db.Connection)

            Console.WriteLine("Branches  : " & branchCount.ToString())
            Console.WriteLine("Items     : " & itemCount.ToString())
            Console.WriteLine("Customers : " & customerCount.ToString())
            Console.WriteLine("Invoices  : " & invoiceCount.ToString())
            Console.WriteLine("Moves     : " & moveCount.ToString())
            Console.WriteLine("Outbox    : pending=" & stats.Pending.ToString() & " failed=" & stats.Failed.ToString() &
                              " dead=" & stats.Dead.ToString() & " ackedToday=" & stats.AckedToday.ToString())
        End Using
    End Sub

    Private Sub RunVerify(options As Dictionary(Of String, String))
        Dim conn = ConnectionString(options)
        Dim failures As New List(Of String)()

        Using db = DbSession.Open(conn)
            Console.WriteLine("Verifying database integrity...")

            Dim mismatched = db.Connection.Query(Of VerifyBalanceRow)(
                "SELECT sb.branch_id, sb.item_id, sb.qty_on_hand, COALESCE(m.total,0) AS expected " &
                "FROM stock_balances sb LEFT JOIN (" &
                "  SELECT branch_id, item_id, SUM(qty_delta) AS total FROM stock_moves GROUP BY branch_id, item_id" &
                ") m ON m.branch_id=sb.branch_id AND m.item_id=sb.item_id " &
                "WHERE ABS(sb.qty_on_hand - COALESCE(m.total,0)) > 0.00005").AsList()
            For Each row In mismatched
                failures.Add($"stock balance branch={row.BranchId} item={row.ItemId} cached={row.QtyOnHand} moves={row.Expected}")
            Next
            Console.WriteLine($"  stock balance mismatches: {mismatched.Count}")

            Dim unbalanced = db.Connection.Query(Of VerifyJournalRow)(
                "SELECT je.id, je.entry_no, SUM(jl.debit) AS debit, SUM(jl.credit) AS credit " &
                "FROM journal_entries je JOIN journal_lines jl ON jl.entry_id=je.id " &
                "GROUP BY je.id, je.entry_no HAVING ABS(SUM(jl.debit)-SUM(jl.credit)) > 0.004").AsList()
            For Each row In unbalanced
                failures.Add($"journal unbalanced {row.EntryNo}: debit={row.Debit} credit={row.Credit}")
            Next
            Console.WriteLine($"  unbalanced journal entries: {unbalanced.Count}")

            Dim headerMismatch = db.Connection.Query(Of VerifyInvoiceRow)(
                "SELECT v.id, v.invoice_no, v.tax_total, COALESCE(SUM(l.tax_amount),0) AS lines_tax, " &
                "       v.total, COALESCE(SUM(l.line_total),0) AS lines_total " &
                "FROM invoices v LEFT JOIN invoice_lines l ON l.invoice_id=v.id " &
                "WHERE v.status='posted' GROUP BY v.id, v.invoice_no, v.tax_total, v.total " &
                "HAVING ABS(v.tax_total - COALESCE(SUM(l.tax_amount),0)) > 0.004 " &
                "   OR ABS(v.total - COALESCE(SUM(l.line_total),0)) > 0.02").AsList()
            For Each row In headerMismatch
                failures.Add($"invoice totals {row.InvoiceNo}: tax {row.TaxTotal}/{row.LinesTax} total {row.Total}/{row.LinesTotal}")
            Next
            Console.WriteLine($"  invoice total mismatches: {headerMismatch.Count}")

            Dim negative = db.Connection.Query(Of VerifyBalanceRow)(
                "SELECT branch_id, item_id, qty_on_hand FROM stock_balances WHERE qty_on_hand < 0").AsList()
            For Each row In negative
                failures.Add($"negative balance branch={row.BranchId} item={row.ItemId} qty={row.QtyOnHand}")
            Next
            Console.WriteLine($"  negative balances: {negative.Count}")

            Dim missingMoves = db.Connection.Query(Of VerifyInvoiceRow)(
                "SELECT v.id, v.invoice_no FROM invoices v " &
                "WHERE v.status='posted' AND v.invoice_type IN ('sale','purchase','sale_return','purchase_return','opening') " &
                "  AND NOT EXISTS (SELECT 1 FROM stock_moves m WHERE m.ref_type='invoice' AND m.ref_id=v.id AND m.move_type <> 'adjustment')").AsList()
            For Each row In missingMoves
                failures.Add($"invoice without stock moves: {row.InvoiceNo}")
            Next
            Console.WriteLine($"  invoices without stock moves: {missingMoves.Count}")

            ' Movements are the source of truth: each branch's stock account must
            ' equal the value of its movements. Catches one-sided postings.
            Dim drift = AccountingRepository.InventoryLedgerDrift(db.Connection, 0.05D)
            For Each row In drift
                failures.Add($"inventory account branch={row.BranchCode} account={row.LedgerValue} movements={row.StockValue}")
            Next
            Console.WriteLine($"  inventory account mismatches: {drift.Count}")

            Dim stats = SyncStore.Stats(db.Connection)
            Console.WriteLine($"  outbox pending={stats.Pending} dead={stats.Dead}")
        End Using

        Console.WriteLine()
        If failures.Count = 0 Then
            Console.WriteLine("VERIFY OK — no integrity problems found.")
        Else
            Console.WriteLine("VERIFY FAILED — " & failures.Count.ToString() & " problem(s):")
            For Each failure In failures.Take(50)
                Console.WriteLine("  - " & failure)
            Next
            Environment.ExitCode = 2
        End If
    End Sub

End Module

' ---- typed projections for legacy import and verification -------------------

Friend Class LegacyCompanyRow
    Public Property Name As String
    Public Property Tel As String
    Public Property Mobile As String
    Public Property Address As String
    Public Property TaxNo As String
End Class

Friend Class LegacyGroupRow
    Public Property Id As Long
    Public Property Name As String
    Public Property ParentId As Long?
End Class

Friend Class LegacyUnitRow
    Public Property Id As Long
    Public Property Name As String
End Class

Friend Class LegacyItemRow
    Public Property Id As Long
    Public Property Symbol As String
    Public Property NameEn As String
    Public Property CompanyNo As String
    Public Property GroupId As Long?
    Public Property UnitId As Long?
    Public Property IsDeleted As Integer
    Public Property PurchPrice As Decimal
    Public Property SalePrice As Decimal
    Public Property WholePrice As Decimal
    Public Property MinPrice As Decimal
    Public Property ReorderLevel As Integer
End Class

Friend Class LegacyBarcodeRow
    Public Property Curr As Long
    Public Property Barcode As String
End Class

Friend Class LegacyCustomerRow
    Public Property Id As Long
    Public Property Name As String
    Public Property Mobile As String
    Public Property Tel As String
    Public Property Email As String
    Public Property TaxNo As String
    Public Property MaxDebit As Decimal
    Public Property IsDeleted As Integer
End Class

Friend Class VerifyBalanceRow
    Public Property BranchId As Long
    Public Property ItemId As Long
    Public Property QtyOnHand As Decimal
    Public Property Expected As Decimal
End Class

Friend Class VerifyJournalRow
    Public Property Id As Long
    Public Property EntryNo As String
    Public Property Debit As Decimal
    Public Property Credit As Decimal
End Class

Friend Class VerifyInvoiceRow
    Public Property Id As Long
    Public Property InvoiceNo As String
    Public Property TaxTotal As Decimal
    Public Property LinesTax As Decimal
    Public Property Total As Decimal
    Public Property LinesTotal As Decimal
End Class
