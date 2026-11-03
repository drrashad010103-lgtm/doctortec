Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Linq
Imports Dapper
Imports Xunit

''' <summary>
''' Security, inventory-count and basic performance coverage for the hardening
''' plan: authorization aliases, credit limits, expired stock, closed shifts,
''' physical counts and a smoke budget for posting/reporting.
''' </summary>
<Collection("Database")>
Public Class SecurityAndInventoryTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function UtcAt(day As Date, hour As Integer) As Date
        Return New Date(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc)
    End Function

    Private Shared Function SaleFor(itemId As Long, qty As Decimal, price As Decimal, day As Date,
                                    Optional paid As Decimal? = Nothing, Optional shiftId As Long? = Nothing,
                                    Optional customerId As Long? = Nothing,
                                    Optional expiry As Date? = Nothing) As InvoiceRequest
        Return New InvoiceRequest With {
            .OccurredAt = UtcAt(day, 9),
            .BusinessDate = day,
            .ShiftId = shiftId,
            .CustomerId = customerId,
            .PaymentMethodId = 1,
            .Paid = If(paid.HasValue, paid.Value, qty * price),
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = qty, .UnitPrice = price, .ExpiryDate = expiry
                }
            }
        }
    End Function

    Private Function CreateCustomer(name As String, creditLimit As Decimal) As Long
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            db.Connection.Execute(
                "INSERT INTO customers(uuid, name, type, credit_limit) VALUES(@uuid,@name,'customer',@limit)",
                New With {.uuid = Uuid7.NewId(), .name = name, .limit = creditLimit}, db.Transaction)
            Dim id = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, db.Transaction)
            db.Commit()
            Return id
        End Using
    End Function

    <Fact>
    Public Sub AuthorizationAliasesKeepLegacyGrantsWorking()
        Dim session = _db.Session()
        session.Permissions.Clear()
        session.Permissions.Add(Permissions.SettingsManage)

        Assert.True(AuthorizationService.Can(session, Permissions.DefinitionsView))
        Assert.True(AuthorizationService.Can(session, Permissions.DefinitionsManage))
        Assert.True(AuthorizationService.Can(session, Permissions.CompanyManage))
        Assert.False(AuthorizationService.Can(session, Permissions.SalesCreate))

        session.Permissions.Clear()
        Assert.False(AuthorizationService.Can(session, Permissions.DefinitionsView))
        Assert.Throws(Of BusinessException)(
            Sub() AuthorizationService.Require(session, Permissions.DefinitionsView))
    End Sub

    <Fact>
    Public Sub MissingStockPermissionBlocksInventoryCount()
        Dim session = _db.Session()
        session.Permissions.Clear()
        session.Permissions.Add(Permissions.ReportsView)

        Dim ex = Assert.Throws(Of BusinessException)(
            Function() InventoryCountService.CreateDraft(session, "محاولة بلا صلاحية"))
        Assert.Contains("صلاحية", ex.Message)
        Assert.Equal(0L, _db.ScalarLong("SELECT COUNT(*) FROM inventory_counts WHERE notes=@n",
                                        New With {.n = "محاولة بلا صلاحية"}))
    End Sub

    <Fact>
    Public Sub InventoryCountPostsVarianceStockMovementAndJournal()
        Dim day = _db.NextTestDate()
        Dim code As String = "IC-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))
        Dim session = _db.Session()

        Dim countId = InventoryCountService.CreateDraft(session, "جرد اختبار")
        Dim line = InventoryCountService.GetLines(session, countId).First(Function(l) l.ItemId = itemId)
        Assert.Equal(10D, line.SystemQty)

        InventoryCountService.SaveCounted(session, countId, New List(Of InventoryCountLineInput) From {
            New InventoryCountLineInput With {.LineId = line.Id, .CountedQty = 7D}
        })

        Dim net = InventoryCountService.Post(session, countId)

        Assert.Equal(-180D, net)
        Assert.Equal(7D, _db.QtyOf(itemId))
        Assert.Equal("posted", InventoryCountService.GetById(session, countId).Status)
        Assert.Equal(1L, _db.ScalarLong(
            "SELECT COUNT(*) FROM stock_moves WHERE ref_type='inventory_count' AND ref_id=@id AND move_type='count_loss'",
            New With {.id = countId}))
        Assert.Equal(1L, _db.ScalarLong(
            "SELECT COUNT(*) FROM journal_entries WHERE source_type='inventory_count' AND source_id=@id",
            New With {.id = countId}))
        Assert.True(_db.JournalIsBalanced())

        ' Posting twice must be rejected and must not change stock again.
        Assert.Throws(Of BusinessException)(Sub() InventoryCountService.Post(session, countId))
        Assert.Equal(7D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub InventoryCountRepostsAgainstLiveBalanceNotStaleSnapshot()
        Dim day = _db.NextTestDate()
        Dim code As String = "IL-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))
        Dim session = _db.Session()

        Dim countId = InventoryCountService.CreateDraft(session, "جرد مع حركة")
        Dim line = InventoryCountService.GetLines(session, countId).First(Function(l) l.ItemId = itemId)

        ' Two units arrive after the snapshot was taken.
        StockService.Adjust(session, itemId, 2D, 60D, "توريد أثناء الجرد")
        InventoryCountService.SaveCounted(session, countId, New List(Of InventoryCountLineInput) From {
            New InventoryCountLineInput With {.LineId = line.Id, .CountedQty = 9D}
        })

        Dim net = InventoryCountService.Post(session, countId)

        ' Current is 12, counted is 9, so the posted variance is -3 (not -1).
        Assert.Equal(-180D, net)
        Assert.Equal(9D, _db.QtyOf(itemId))
        Assert.Equal(12D, _db.ScalarDecimal(
            "SELECT system_qty FROM inventory_count_lines WHERE id=@id", New With {.id = line.Id}))
    End Sub

    <Fact>
    Public Sub CreditLimitBlocksThenManagerOverrideAllows()
        Dim day = _db.NextTestDate()
        Dim code As String = "CL-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 40D, 5D)
        Dim customerId = CreateCustomer("عميل ائتمان " & code, 100D)

        Dim session = _db.Session()
        session.Permissions.Remove(Permissions.SalesCreditOverride)

        Dim request = SaleFor(itemId, 1D, 150D, day, paid:=0D, customerId:=customerId)
        Dim ex = Assert.Throws(Of BusinessException)(Function() SalesService.PostSale(session, request))
        Assert.Contains("سقف الائتمان", ex.Message)
        Assert.Equal(5D, _db.QtyOf(itemId))
        Assert.Equal(0L, _db.ScalarLong("SELECT COUNT(*) FROM invoices WHERE customer_id=@c",
                                        New With {.c = customerId}))

        session.Permissions.Add(Permissions.SalesCreditOverride)
        Dim result = SalesService.PostSale(session, request)
        Assert.Equal(150D, result.Due)
        Assert.Equal(1L, _db.ScalarLong("SELECT COUNT(*) FROM invoices WHERE customer_id=@c",
                                        New With {.c = customerId}))
    End Sub

    <Fact>
    Public Sub ExpiredStockCannotBeSold()
        Dim day = _db.NextTestDate()
        Dim code As String = "EX-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D)

        Dim request = SaleFor(itemId, 1D, 114D, day, expiry:=day.AddDays(-1))
        Dim ex = Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSale(_db.Session(), request))
        Assert.Contains("منتهي الصلاحية", ex.Message)
        Assert.Equal(10D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub ClosedShiftCannotBeUsedForANewSale()
        Dim day = _db.NextTestDate()
        Dim code As String = "CS-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D)
        Dim session = _db.Session()

        Dim shift = ShiftService.Open(session, 100D)
        ShiftService.Close(session, shift.Id, 100D, "إغلاق اختبار")

        Dim request = SaleFor(itemId, 1D, 114D, day, shiftId:=shift.Id)
        Dim ex = Assert.Throws(Of BusinessException)(Function() SalesService.PostSale(session, request))
        Assert.Contains("مغلقة", ex.Message)
        Assert.Equal(10D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub PostingAndReportingStayWithinATimeBudget()
        Dim day = _db.NextTestDate()
        Dim session = _db.Session()
        Dim itemIds As New List(Of Long)()

        Dim posting = Stopwatch.StartNew()
        For i As Integer = 0 To 9
            Dim code As String = "PF-" & i.ToString() & "-" & Guid.NewGuid().ToString("N").Substring(0, 6)
            Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 5D, occurredAt:=UtcAt(day, 8))
            itemIds.Add(itemId)
            SalesService.PostSale(session, SaleFor(itemId, 1D, 114D, day))
        Next
        posting.Stop()

        Dim reporting = Stopwatch.StartNew()
        Dim daily = ReportService.SalesByDay(session, day, day)
        Dim byType = ReportService.SalesByType(session, day, day)
        Dim trial = ReportService.TrialBalance(session, day, day)
        reporting.Stop()

        Assert.Equal(10L, daily.Sum(Function(r) r.InvoiceCount))
        Assert.Contains(byType, Function(kv) kv.Key = "sale" AndAlso kv.Value = 1140D)
        Assert.Equal(0D, trial.Sum(Function(r) r.Debit - r.Credit))
        Assert.True(posting.ElapsedMilliseconds < 20000,
                    "posting 10 sales took " & posting.ElapsedMilliseconds.ToString() & " ms")
        Assert.True(reporting.ElapsedMilliseconds < 5000,
                    "loading three reports took " & reporting.ElapsedMilliseconds.ToString() & " ms")
    End Sub
End Class
