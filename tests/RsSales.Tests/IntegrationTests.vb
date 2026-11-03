Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Dapper
Imports Xunit

<Collection("Database")>
Public Class SalesIntegrationTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function UtcAt(day As Date, hour As Integer) As Date
        Return New Date(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc)
    End Function

    Private Shared Function BuildSale(itemId As Long, qty As Decimal, price As Decimal,
                                      businessDate As Date, Optional paid As Decimal? = Nothing) As InvoiceRequest
        Return New InvoiceRequest With {
            .OccurredAt = UtcAt(businessDate, 9),
            .BusinessDate = businessDate,
            .PaymentMethodId = 1,
            .Paid = If(paid.HasValue, paid.Value, qty * price),
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = qty, .UnitPrice = price}
            }
        }
    End Function

    <Fact>
    Public Sub SalePostsInvoiceStockJournalTaxAndOutbox()
        Dim day = _db.NextTestDate()
        Dim code As String = "S-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 50D, 10D, occurredAt:=UtcAt(day, 8))

        Dim result = SalesService.PostSale(_db.Session(), BuildSale(itemId, 2D, 114D, day, 228D))

        Assert.True(result.InvoiceId > 0)
        Assert.StartsWith("MAIN-S-", result.InvoiceNo)
        Assert.Equal(228D, result.Subtotal)
        Assert.Equal(200D, result.TaxBaseTotal)
        Assert.Equal(28D, result.TaxTotal)
        Assert.Equal(228D, result.Total)
        Assert.Equal(0D, result.Due)
        Assert.False(String.IsNullOrWhiteSpace(result.QrPayload))
        Assert.Single(result.Lines)
        Assert.Equal(50D, result.Lines(0).CostAtSale)

        Assert.Equal(8D, _db.QtyOf(itemId))
        Assert.Equal("pending", _db.OutboxStatus(result.EventId))
        Assert.True(_db.JournalIsBalanced())

        ' The movement must carry the same cost the line and the COGS journal use,
        ' otherwise value-from-moves reports disagree with the inventory account.
        Assert.Equal(50D, _db.ScalarDecimal(
            "SELECT unit_cost FROM stock_moves WHERE ref_type='invoice' AND ref_id=@id AND move_type='sale'",
            New With {.id = result.InvoiceId}))

        Assert.Equal(1L, _db.ScalarLong(
            "SELECT COUNT(*) FROM invoices WHERE id=@id AND invoice_type='sale' AND status='posted'",
            New With {.id = result.InvoiceId}))

        Dim taxReport = ReportService.TaxReport(_db.Session(), day, day)
        Assert.Equal(200D, taxReport.OutputBase)
        Assert.Equal(28D, taxReport.OutputTax)
        Assert.Equal(taxReport.NetPayable, taxReport.LedgerTaxMovement)

        ' CashSummary groups payments by method; its aliases must be quoted to parse.
        Dim cash = ReportService.CashSummary(_db.Session(), day, day)
        Assert.Contains(cash, Function(kv) kv.Key = "نقد" AndAlso kv.Value = 228D)
    End Sub

    <Fact>
    Public Sub SaleExceedingBalanceIsRejectedWithoutSideEffects()
        Dim day = _db.NextTestDate()
        Dim code As String = "O-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 40D, 3D)
        Dim before As Long = _db.ScalarLong("SELECT COUNT(*) FROM invoices")

        Dim ex = Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSale(_db.Session(), BuildSale(itemId, 4D, 100D, day)))

        Assert.Contains("الرصيد غير كافٍ", ex.Message)
        Assert.Equal(before, _db.ScalarLong("SELECT COUNT(*) FROM invoices"))
        Assert.Equal(3D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub SaleOfItemWithoutAnyStockRowIsRejected()
        Dim day = _db.NextTestDate()
        Dim code As String = "N-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 40D, 0D)
        Assert.Equal(0D, _db.QtyOf(itemId))

        Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSale(_db.Session(), BuildSale(itemId, 1D, 100D, day)))
    End Sub

    <Fact>
    Public Sub SaleReturnRestoresStockAndProducesNegativeInvoice()
        Dim day = _db.NextTestDate()
        Dim code As String = "R-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))

        Dim sale = SalesService.PostSale(_db.Session(), BuildSale(itemId, 3D, 114D, day))
        Assert.Equal(7D, _db.QtyOf(itemId))

        Dim request As New InvoiceRequest With {
            .OccurredAt = UtcAt(day, 11),
            .BusinessDate = day,
            .PaymentMethodId = 1,
            .Paid = 114D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = 1D, .OriginalLineId = sale.Lines(0).LineId
                }
            }
        }

        Dim result = SalesService.PostSaleReturn(_db.Session(), sale.InvoiceId, request)

        Assert.StartsWith("MAIN-SR-", result.InvoiceNo)
        Assert.Equal(-114D, result.Total)
        Assert.Equal(-14D, result.TaxTotal)
        Assert.Equal(-114D, result.Paid)
        Assert.Equal(8D, _db.QtyOf(itemId))
        Assert.True(_db.JournalIsBalanced())

        Using db = DbSession.Open(_db.ConnectionString)
            Dim header = InvoicesRepository.GetById(db.Connection, result.InvoiceId)
            Assert.Equal("sale_return", header.InvoiceType)
            Assert.Equal(-114D, header.Total)
        End Using
    End Sub

    <Fact>
    Public Sub CashSaleAndReturnReconcileWithShift()
        Dim day = _db.NextTestDate()
        Dim code As String = "C-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))
        Dim session = _db.Session()

        Dim shift = ShiftService.Open(session, 500D)
        Dim saleRequest = BuildSale(itemId, 2D, 114D, day, 228D)
        saleRequest.ShiftId = shift.Id
        Dim sale = SalesService.PostSale(session, saleRequest)

        ' The seeded cash method is named "كاش"; it must reach the drawer.
        Assert.Equal(228D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(amount),0) FROM cash_moves WHERE shift_id=@id AND direction='in'",
            New With {.id = shift.Id}))

        Dim returnRequest As New InvoiceRequest With {
            .OccurredAt = UtcAt(day, 12),
            .BusinessDate = day,
            .ShiftId = shift.Id,
            .PaymentMethodId = 1,
            .Paid = 114D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = 1D, .OriginalLineId = sale.Lines(0).LineId
                }
            }
        }
        SalesService.PostSaleReturn(session, sale.InvoiceId, returnRequest)

        Assert.Equal(114D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(amount),0) FROM cash_moves WHERE shift_id=@id AND direction='out'",
            New With {.id = shift.Id}))

        ' 500 opening + 228 cash sale - 114 refund; counted matches, so variance is zero.
        Assert.Equal(0D, ShiftService.Close(session, shift.Id, 614D, "إغلاق اختبار"))
        Assert.Equal(9D, _db.QtyOf(itemId))
        Assert.True(_db.JournalIsBalanced())
    End Sub

    <Fact>
    Public Sub ReturningMoreThanSoldIsRejected()
        Dim day = _db.NextTestDate()
        Dim code As String = "X-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 114D, 60D, 10D)
        Dim sale = SalesService.PostSale(_db.Session(), BuildSale(itemId, 1D, 114D, day))

        Dim request As New InvoiceRequest With {
            .BusinessDate = day,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = 2D, .OriginalLineId = sale.Lines(0).LineId
                }
            }
        }

        Dim ex = Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSaleReturn(_db.Session(), sale.InvoiceId, request))
        Assert.Contains("أكبر من المتاح", ex.Message)
        Assert.Equal(9D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub DiscountBeyondLineValueIsRejected()
        Dim day = _db.NextTestDate()
        Dim code As String = "D-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 50D, 5D)

        Dim request = BuildSale(itemId, 1D, 100D, day, 0D)
        request.Lines(0).LineDiscount = 150D

        Assert.Throws(Of BusinessException)(Function() SalesService.PostSale(_db.Session(), request))
    End Sub

    <Fact>
    Public Sub MissingPermissionBlocksSale()
        Dim day = _db.NextTestDate()
        Dim code As String = "P-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 50D, 5D)

        Dim session = _db.Session()
        session.Permissions.Clear()
        session.Permissions.Add("reports.view")

        Dim ex = Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSale(session, BuildSale(itemId, 1D, 100D, day)))
        Assert.Contains("صلاحية", ex.Message)
        Assert.Equal(5D, _db.QtyOf(itemId))
    End Sub
End Class

<Collection("Database")>
Public Class PurchaseIntegrationTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Function NewItem(code As String) As Long
        ' Insert with cost 0 and no stock row, then buy.
        Return _db.CreateItemWithStock(code, 171D, 0D, 0D)
    End Function

    Private Shared Function Purchase(itemId As Long, qty As Decimal, unitCost As Decimal,
                                     occurredAt As Date) As InvoiceRequest
        Return New InvoiceRequest With {
            .OccurredAt = occurredAt,
            .BusinessDate = occurredAt.Date,
            .PaymentMethodId = 1,
            .Paid = 0D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = qty, .UnitPrice = unitCost}
            }
        }
    End Function

    <Fact>
    Public Sub PurchaseStoresNetCostAndUpdatesLastCost()
        Dim itemId = NewItem("B-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Dim moment As New Date(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc)

        Dim result = PurchaseService.PostPurchase(_db.Session(), Purchase(itemId, 10D, 114D, moment))

        Assert.Equal(1140D, result.Total)
        Assert.Equal(1000D, result.TaxBaseTotal)
        Assert.Equal(140D, result.TaxTotal)
        Assert.Equal(1140D, result.Due)
        Assert.Equal(10D, _db.QtyOf(itemId))
        Assert.Equal(100D, _db.ScalarDecimal(
            "SELECT last_cost FROM stock_balances WHERE item_id=@i", New With {.i = itemId}))
        Assert.True(_db.JournalIsBalanced())

        ' Selling afterwards must use the purchase cost, not the item's old cost_price.
        Dim sale As New InvoiceRequest With {
            .OccurredAt = moment.AddHours(2),
            .BusinessDate = moment.Date,
            .PaymentMethodId = 1,
            .Paid = 342D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = 2D, .UnitPrice = 171D}
            }
        }
        Dim saleResult = SalesService.PostSale(_db.Session(), sale)
        Assert.Equal(100D, saleResult.Lines(0).CostAtSale)
        Assert.Equal(8D, _db.QtyOf(itemId))
    End Sub

    <Fact>
    Public Sub LastCostUsesPurchaseEffectiveAtSaleDate()
        Dim itemId = NewItem("L-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        PurchaseService.PostPurchase(_db.Session(), Purchase(itemId, 10D, 114D,
                                                             New Date(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)))
        PurchaseService.PostPurchase(_db.Session(), Purchase(itemId, 10D, 136.8D,
                                                             New Date(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)))

        ' A sale back-dated to January must use January's cost (100), not the latest (120).
        Dim sale As New InvoiceRequest With {
            .OccurredAt = New Date(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            .BusinessDate = New Date(2026, 1, 15),
            .PaymentMethodId = 1,
            .Paid = 200D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = 2D, .UnitPrice = 100D}
            }
        }
        Dim result = SalesService.PostSale(_db.Session(), sale)
        Assert.Equal(100D, result.Lines(0).CostAtSale)
    End Sub

    <Fact>
    Public Sub PurchaseReturnCannotExceedStock()
        Dim itemId = NewItem("PR-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Dim moment As New Date(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)
        Dim purchaseResult = PurchaseService.PostPurchase(_db.Session(), Purchase(itemId, 2D, 114D, moment))

        Dim tooMany As New InvoiceRequest With {
            .OccurredAt = moment,
            .BusinessDate = moment.Date,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = 3D, .OriginalLineId = purchaseResult.Lines(0).LineId
                }
            }
        }
        Assert.Throws(Of BusinessException)(
            Function() PurchaseService.PostPurchaseReturn(_db.Session(), purchaseResult.InvoiceId, tooMany))

        Dim valid As New InvoiceRequest With {
            .OccurredAt = moment,
            .BusinessDate = moment.Date,
            .PaymentMethodId = 1,
            .Paid = 0D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {
                    .ItemId = itemId, .Qty = 1D, .OriginalLineId = purchaseResult.Lines(0).LineId
                }
            }
        }
        Dim result = PurchaseService.PostPurchaseReturn(_db.Session(), purchaseResult.InvoiceId, valid)
        Assert.Equal(-114D, result.Total)
        Assert.Equal(1D, _db.QtyOf(itemId))
        Assert.True(_db.JournalIsBalanced())
    End Sub

    <Fact>
    Public Sub OpeningStockCreatesBalanceAndCapitalEntry()
        Dim itemId = NewItem("OP-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Dim request As New InvoiceRequest With {
            .OccurredAt = New Date(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
            .BusinessDate = New Date(2026, 6, 1),
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = 4D, .UnitPrice = 25D}
            }
        }
        Dim result = PurchaseService.PostOpeningStock(_db.Session(), request)
        Assert.Equal(100D, result.Total)
        Assert.Equal(4D, _db.QtyOf(itemId))
        Assert.True(_db.JournalIsBalanced())

        ' The invoice header must carry the same totals as its lines (verify checks this).
        Assert.Equal(100D, _db.ScalarDecimal("SELECT total FROM invoices WHERE id=@id", New With {.id = result.InvoiceId}))
        Assert.Equal(100D, _db.ScalarDecimal("SELECT SUM(line_total) FROM invoice_lines WHERE invoice_id=@id", New With {.id = result.InvoiceId}))
    End Sub

    <Fact>
    Public Sub StockAdjustmentRespectsNonNegativeRule()
        Dim code As String = "A-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 20D, 5D)

        Dim after = StockService.Adjust(_db.Session(), itemId, 3D, 20D, "جرد")
        Assert.Equal(8D, after)

        Assert.Throws(Of BusinessException)(
            Function() StockService.Adjust(_db.Session(), itemId, -10D, 20D, "جرد"))
        Assert.Equal(8D, _db.QtyOf(itemId))
        Assert.True(_db.JournalIsBalanced())
    End Sub

    <Fact>
    Public Sub MissingAdjustmentReasonIsRejected()
        Dim code As String = "AR-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 20D, 5D)
        Assert.Throws(Of BusinessException)(
            Function() StockService.Adjust(_db.Session(), itemId, 1D, Nothing, ""))
    End Sub

    <Fact>
    Public Sub TransferMovesStockBetweenBranches()
        Dim code As String = "T-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(code, 100D, 30D, 10D)

        Dim secondBranchId As Long
        Using db = DbSession.Open(_db.ConnectionString)
            db.Connection.Execute(
                "INSERT INTO branches(code, name, timezone) VALUES('BR2','الفرع الثاني','Africa/Cairo')")
            secondBranchId = db.Connection.ExecuteScalar(Of Long)(
                "SELECT id FROM branches WHERE code='BR2'")
            db.Connection.Execute("INSERT INTO cashboxes(branch_id, name) VALUES(@b,'صندوق 2')",
                                  New With {.b = secondBranchId})
            db.Connection.Execute(
                "INSERT INTO user_branch_access(user_id, branch_id) " &
                "SELECT id, @b FROM users WHERE username='tester'", New With {.b = secondBranchId})
        End Using

        Dim transferId = StockService.CreateTransfer(_db.Session(), secondBranchId, "نقل اختبار",
            New List(Of TransferLineInput) From {
                New TransferLineInput With {.ItemId = itemId, .Qty = 4D}
            })
        StockService.SendTransfer(_db.Session(), transferId)
        Assert.Equal(6D, _db.QtyOf(itemId))

        Dim targetSession = _db.Session()
        targetSession.BranchId = secondBranchId
        StockService.ReceiveTransfer(targetSession, transferId)
        Using db = DbSession.Open(_db.ConnectionString)
            Assert.Equal(4D, StockRepository.GetQty(db.Connection, secondBranchId, itemId))
            Assert.Equal(6D, StockRepository.GetQty(db.Connection, _db.BranchId, itemId))
        End Using
    End Sub
End Class

<Collection("Database")>
Public Class AuthAndSyncTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    <Fact>
    Public Sub LoginSucceedsAndLoadsPermissions()
        Dim session = AuthService.Login(_db.ConnectionString, "tester", "Test@12345")
        Assert.Equal(_db.BranchId, session.BranchId)
        Assert.True(session.HasPermission("sales.create"))
        Assert.True(session.HasPermission("reports.profit"))
    End Sub

    <Fact>
    Public Sub WrongPasswordIncrementsFailedAttempts()
        Assert.Throws(Of BusinessException)(
            Function() AuthService.Login(_db.ConnectionString, "tester", "wrong-password"))
        Assert.Equal(1L, _db.ScalarLong(
            "SELECT failed_attempts FROM users WHERE username='tester'"))
    End Sub

    <Fact>
    Public Sub OutboxIsIdempotentAndKeepsFailedEvents()
        Dim eventId As String = "evt-" & Guid.NewGuid().ToString("N")
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            SyncStore.Enqueue(db.Connection, db.Transaction, eventId, _db.BranchId, Nothing,
                              "invoice", "1", "sale.posted", 1, "{}", Date.UtcNow)
            db.Commit()

            db.BeginTransaction()
            SyncStore.Enqueue(db.Connection, db.Transaction, eventId, _db.BranchId, Nothing,
                              "invoice", "1", "sale.posted", 1, "{}", Date.UtcNow)
            db.Commit()

            Assert.Equal(1L, db.Connection.ExecuteScalar(Of Long)(
                "SELECT COUNT(*) FROM sync_outbox WHERE event_id=@id", New With {.id = eventId}))

            db.BeginTransaction()
            Dim claimed = SyncStore.ClaimBatch(db.Connection, db.Transaction, 500)
            db.Commit()
            Assert.Contains(claimed, Function(r) r.EventId = eventId)

            db.BeginTransaction()
            SyncStore.MarkFailed(db.Connection, db.Transaction, eventId, "server down", 10, 1)
            db.Commit()

            Assert.Equal("dead", db.Connection.ExecuteScalar(Of String)(
                "SELECT status FROM sync_outbox WHERE event_id=@id", New With {.id = eventId}))

            db.BeginTransaction()
            SyncStore.PurgeAcked(db.Connection, db.Transaction, 0)
            db.Commit()

            Assert.Equal(1L, db.Connection.ExecuteScalar(Of Long)(
                "SELECT COUNT(*) FROM sync_outbox WHERE event_id=@id", New With {.id = eventId}))
        End Using
    End Sub

    <Fact>
    Public Sub SyncPushAcksAcceptedAndRetriesRejectedEvents()
        Dim accepted As String = "acc-" & Guid.NewGuid().ToString("N")
        Dim rejected As String = "rej-" & Guid.NewGuid().ToString("N")
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            SyncStore.Enqueue(db.Connection, db.Transaction, accepted, _db.BranchId, Nothing,
                              "invoice", "10", "sale.posted", 1, "{}", Date.UtcNow)
            SyncStore.Enqueue(db.Connection, db.Transaction, rejected, _db.BranchId, Nothing,
                              "invoice", "11", "sale.posted", 1, "{}", Date.UtcNow)
            db.Commit()
        End Using

        Dim transport As New FakeTransport()
        transport.Handler = Function(batch)
                              Dim ack As New SyncAck With {.Accepted = False}
                              For Each item In batch.Events
                                  ack.Results.Add(New SyncEventAck With {
                                      .EventId = item.EventId,
                                      .Status = If(item.EventId = accepted, "acked", "rejected"),
                                      .Error = If(item.EventId = accepted, Nothing, "invalid payload")
                                  })
                              Next
                              Return ack
                          End Function

        Dim service As New SyncService(_db.ConnectionString, _db.BranchId, Nothing, transport, 5)
        Dim firstRun = service.PushOnce(500)
        Assert.True(firstRun.Acked >= 1)
        Assert.Equal("acked", _db.OutboxStatus(accepted))
        Assert.Equal("failed", _db.OutboxStatus(rejected))

        ' Make the rejected event due, then let the server accept everything.
        Using db = DbSession.Open(_db.ConnectionString)
            db.Connection.Execute(
                "UPDATE sync_outbox SET next_attempt_at=UTC_TIMESTAMP(6) WHERE event_id=@id",
                New With {.id = rejected})
        End Using
        transport.Handler = Function(batch)
                              Dim ack As New SyncAck With {.Accepted = True}
                              Return ack
                          End Function

        service.PushOnce(500)
        Assert.Equal("acked", _db.OutboxStatus(rejected))
    End Sub

    <Fact>
    Public Sub SyncFailureDoesNotDropEvents()
        Dim eventId As String = "off-" & Guid.NewGuid().ToString("N")
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            SyncStore.Enqueue(db.Connection, db.Transaction, eventId, _db.BranchId, Nothing,
                              "invoice", "20", "sale.posted", 1, "{}", Date.UtcNow)
            db.Commit()
        End Using

        Dim transport As New FakeTransport()
        transport.Handler = Function(batch As SyncBatch) As SyncAck
                              Throw New InvalidOperationException("offline")
                          End Function
        Dim service As New SyncService(_db.ConnectionString, _db.BranchId, Nothing, transport, 5)
        Dim result = service.PushOnce(500)

        Assert.True(result.Failed >= 1)
        Assert.Equal("failed", _db.OutboxStatus(eventId))
        Assert.Equal(1L, _db.ScalarLong("SELECT COUNT(*) FROM sync_outbox WHERE event_id=@id",
                                        New With {.id = eventId}))
    End Sub

    Private Class FakeTransport
        Implements ISyncTransport
        Public Property Handler As Func(Of SyncBatch, SyncAck)

        Public Function Push(batch As SyncBatch) As SyncAck Implements ISyncTransport.Push
            Return Handler(batch)
        End Function

        Public Function Pull(stream As String, afterSeq As Long, limit As Integer) As PullResponse Implements ISyncTransport.Pull
            Return New PullResponse With {.NextSeq = afterSeq}
        End Function
    End Class
End Class

''' <summary>
''' Two branches must never share stock, journal or drawer figures, and a
''' session may not post against another branch's shift.
''' </summary>
<Collection("Database")>
Public Class BranchIsolationTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function UtcAt(day As Date, hour As Integer) As Date
        Return New Date(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc)
    End Function

    Private Shared Function Sale(itemId As Long, qty As Decimal, businessDate As Date,
                                 shiftId As Long?, methodId As Long) As InvoiceRequest
        Return New InvoiceRequest With {
            .OccurredAt = UtcAt(businessDate, 10),
            .BusinessDate = businessDate,
            .ShiftId = shiftId,
            .PaymentMethodId = methodId,
            .Paid = qty * 114D,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = qty, .UnitPrice = 114D}
            }
        }
    End Function

    Private Function SessionFor(branchId As Long, branchName As String) As AppSession
        Dim result = _db.Session()
        result.BranchId = branchId
        result.BranchName = branchName
        Return result
    End Function

    <Fact>
    Public Sub BranchesKeepStockLedgerCashAndTaxSeparate()
        Dim day = _db.NextTestDate()
        Dim branchB As Long
        Dim branchCode As String = "B2-" & Guid.NewGuid().ToString("N").Substring(0, 6)
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            branchB = BranchesRepository.Insert(db.Connection, db.Transaction, branchCode, "فرع العزل",
                                                "Africa/Cairo", "الجيزة", "01011111111", "200000000000003")
            db.Connection.Execute("INSERT INTO cashboxes(branch_id, name) VALUES(@b,'صندوق فرع العزل')",
                                  New With {.b = branchB}, db.Transaction)
            db.Commit()
        End Using

        Dim sessionA = _db.Session()
        Dim sessionB = SessionFor(branchB, "فرع العزل")
        Dim manzil As Long = _db.CashMethodId

        Dim codeItem As String = "I-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(codeItem, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))
        StockService.Adjust(sessionB, itemId, 10D, 60D, "رصيد افتتاحي فرع العزل")

        Dim shiftA = ShiftService.Open(sessionA, 500D)
        Dim shiftB = ShiftService.Open(sessionB, 700D)
        Try

        Dim saleA = SalesService.PostSale(sessionA, Sale(itemId, 2D, day, shiftA.Id, manzil))
        Dim saleB = SalesService.PostSale(sessionB, Sale(itemId, 3D, day, shiftB.Id, manzil))
        Assert.StartsWith("MAIN-S-", saleA.InvoiceNo)
        Assert.StartsWith(branchCode & "-S-", saleB.InvoiceNo)

        ' ---- stock per branch --------------------------------------------
        Assert.Equal(8D, _db.ScalarDecimal(
            "SELECT qty_on_hand FROM stock_balances WHERE branch_id=@b AND item_id=@i",
            New With {.b = _db.BranchId, .i = itemId}))
        Assert.Equal(7D, _db.ScalarDecimal(
            "SELECT qty_on_hand FROM stock_balances WHERE branch_id=@b AND item_id=@i",
            New With {.b = branchB, .i = itemId}))

        ' ---- drawer and payments per branch ------------------------------
        Assert.Equal(228D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(cm.amount),0) FROM cash_moves cm JOIN cashboxes cb ON cb.id=cm.cashbox_id " &
            "WHERE cb.branch_id=@b", New With {.b = _db.BranchId}))
        Assert.Equal(342D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(cm.amount),0) FROM cash_moves cm JOIN cashboxes cb ON cb.id=cm.cashbox_id " &
            "WHERE cb.branch_id=@b", New With {.b = branchB}))
        Assert.Equal(228D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(amount),0) FROM payments WHERE branch_id=@b", New With {.b = _db.BranchId}))
        Assert.Equal(342D, _db.ScalarDecimal(
            "SELECT COALESCE(SUM(amount),0) FROM payments WHERE branch_id=@b", New With {.b = branchB}))

        ' ---- ledger and tax report per branch ----------------------------
        Dim trialA = ReportService.TrialBalance(sessionA, day.AddDays(-1), day.AddDays(1))
        Dim trialB = ReportService.TrialBalance(sessionB, day.AddDays(-1), day.AddDays(1))
        Assert.Equal(200D, trialA.Where(Function(r) r.Code = "4000").Sum(Function(r) r.Credit))
        Assert.Equal(300D, trialB.Where(Function(r) r.Code = "4000").Sum(Function(r) r.Credit))
        Assert.Equal(0D, trialA.Sum(Function(r) r.Debit) - trialA.Sum(Function(r) r.Credit))
        Assert.Equal(0D, trialB.Sum(Function(r) r.Debit) - trialB.Sum(Function(r) r.Credit))

        Dim taxA = ReportService.TaxReport(sessionA, day.AddDays(-1), day.AddDays(1))
        Dim taxB = ReportService.TaxReport(sessionB, day.AddDays(-1), day.AddDays(1))
        Assert.Equal(200D, taxA.OutputBase)
        Assert.Equal(28D, taxA.OutputTax)
        Assert.Equal(300D, taxB.OutputBase)
        Assert.Equal(42D, taxB.OutputTax)
        Assert.Equal(taxB.OutputTax - taxB.InputTax, taxB.LedgerTaxMovement)

        ' ---- a session cannot use another branch's shift ------------------
        Dim before As Long = _db.ScalarLong("SELECT COUNT(*) FROM invoices WHERE branch_id=@b", New With {.b = branchB})
        Assert.Throws(Of BusinessException)(
            Function() SalesService.PostSale(sessionB, Sale(itemId, 1D, day, shiftA.Id, manzil)))
        Assert.Equal(before, _db.ScalarLong("SELECT COUNT(*) FROM invoices WHERE branch_id=@b", New With {.b = branchB}))
        Assert.Throws(Of BusinessException)(
            Function() ShiftService.Close(sessionB, shiftA.Id, 0D, "إغلاق من فرع آخر"))

        ' ---- closing the right shift shows no variance --------------------
        Assert.Equal(0D, ShiftService.Close(sessionA, shiftA.Id, 500D + saleA.Paid, "إغلاق فرع أ"))
        Assert.Equal(0D, ShiftService.Close(sessionB, shiftB.Id, 700D + saleB.Paid, "إغلاق فرع ب"))
        Finally
            ' Leave no open shift behind for the tests that share this database.
            CloseIfOpen(sessionA, shiftA.Id)
            CloseIfOpen(sessionB, shiftB.Id)
        End Try
    End Sub

    <Fact>
    Public Sub TransferMovesStockAndPostsBothBranchEntries()
        Dim day = _db.NextTestDate()
        Dim branchB As Long
        Dim branchCode As String = "TR-" & Guid.NewGuid().ToString("N").Substring(0, 6)
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            branchB = BranchesRepository.Insert(db.Connection, db.Transaction, branchCode, "فرع التحويل",
                                                "Africa/Cairo", "الإسكندرية", "01222222222", "300000000000003")
            db.Connection.Execute("INSERT INTO cashboxes(branch_id, name) VALUES(@b,'صندوق فرع التحويل')",
                                  New With {.b = branchB}, db.Transaction)
            db.Commit()
        End Using

        Dim sessionA = _db.Session()
        Dim sessionB = SessionFor(branchB, "فرع التحويل")
        Dim codeItem As String = "T-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(codeItem, 114D, 60D, 10D, occurredAt:=UtcAt(day, 8))

        Dim transferId = StockService.CreateTransfer(sessionA, branchB, "تحويل اختبار",
            New List(Of TransferLineInput) From {New TransferLineInput With {.ItemId = itemId, .Qty = 4D}})

        ' A draft moves nothing.
        Assert.Equal(10D, QtyIn(_db.BranchId, itemId))
        Assert.Equal(0D, QtyIn(branchB, itemId))

        StockService.SendTransfer(sessionA, transferId)
        Assert.Equal(6D, QtyIn(_db.BranchId, itemId))
        Assert.Equal(0D, QtyIn(branchB, itemId))
        ' Departed stock becomes a due from the receiving branch.
        Assert.Equal(240D, AccountMovement(AccountingRepository.AccDueFromBranches, _db.BranchId))

        StockService.ReceiveTransfer(sessionB, transferId)
        Assert.Equal(4D, QtyIn(branchB, itemId))
        Assert.Equal(-240D, AccountMovement(AccountingRepository.AccDueToBranches, branchB))

        ' Each branch's ledger matches its own stock and stays balanced.
        Assert.Equal(240D, StockValue(branchB, itemId))
        Assert.Equal(360D, StockValue(_db.BranchId, itemId))
        Dim trialA = ReportService.TrialBalance(sessionA, day.AddDays(-1), day.AddDays(1))
        Dim trialB = ReportService.TrialBalance(sessionB, day.AddDays(-1), day.AddDays(1))
        Assert.Equal(0D, trialA.Sum(Function(r) r.Debit - r.Credit))
        Assert.Equal(0D, trialB.Sum(Function(r) r.Debit - r.Credit))

        ' Only the intended branch may receive, and a transfer sends once.
        Assert.Throws(Of BusinessException)(Function() ReceiveAndReturn(sessionA, transferId))
        Assert.Throws(Of BusinessException)(Function() SendAndReturn(sessionA, transferId))
        Assert.Equal(1L, _db.ScalarLong("SELECT COUNT(*) FROM stock_transfers WHERE id=@id AND status='received'",
                                        New With {.id = transferId}))
    End Sub

    <Fact>
    Public Sub InventoryDriftDetectsMovementsWithoutLedgerEntry()
        Dim branchB As Long
        Dim branchCode As String = "DR-" & Guid.NewGuid().ToString("N").Substring(0, 6)
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            branchB = BranchesRepository.Insert(db.Connection, db.Transaction, branchCode, "فرع الفحص",
                                                "Africa/Cairo", "طنطا", "01333333333", "400000000000003")
            db.Commit()
        End Using

        Dim sessionB = SessionFor(branchB, "فرع الفحص")
        Dim codeItem As String = "D-" & Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim itemId = _db.CreateItemWithStock(codeItem, 114D, 60D, 0D)

        ' A normal adjustment posts stock and ledger together, so no drift.
        StockService.Adjust(sessionB, itemId, 5D, 60D, "تسوية توازن")
        Using db = DbSession.Open(_db.ConnectionString)
            Assert.Empty(AccountingRepository.InventoryLedgerDrift(db.Connection, 0.05D, branchB))
        End Using

        ' A movement with no journal entry is exactly the old one-sided posting.
        Using db = DbSession.Open(_db.ConnectionString)
            db.BeginTransaction()
            StockRepository.AddMove(db.Connection, db.Transaction, Uuid7.NewId(), branchB, itemId, "adjustment",
                                    5D, 60D, "manual", Nothing, Nothing, Date.UtcNow, Date.UtcNow.Date,
                                    1, Nothing, "حركة بلا قيد")
            db.Commit()
        End Using

        Using db = DbSession.Open(_db.ConnectionString)
            Dim drift = AccountingRepository.InventoryLedgerDrift(db.Connection, 0.05D, branchB)
            Assert.Single(drift)
            Assert.Equal(600D, drift(0).StockValue)
            Assert.Equal(300D, drift(0).LedgerValue)
        End Using
    End Sub

    Private Function ReceiveAndReturn(session As AppSession, transferId As Long) As Boolean
        StockService.ReceiveTransfer(session, transferId)
        Return True
    End Function

    Private Function SendAndReturn(session As AppSession, transferId As Long) As Boolean
        StockService.SendTransfer(session, transferId)
        Return True
    End Function

    Private Function QtyIn(branchId As Long, itemId As Long) As Decimal
        Return _db.ScalarDecimal("SELECT COALESCE(qty_on_hand,0) FROM stock_balances WHERE branch_id=@b AND item_id=@i",
                                 New With {.b = branchId, .i = itemId})
    End Function

    Private Function StockValue(branchId As Long, itemId As Long) As Decimal
        Return _db.ScalarDecimal("SELECT COALESCE(SUM(qty_delta*unit_cost),0) FROM stock_moves WHERE branch_id=@b AND item_id=@i",
                                 New With {.b = branchId, .i = itemId})
    End Function

    Private Function AccountMovement(code As String, branchId As Long) As Decimal
        Return _db.ScalarDecimal(
            "SELECT COALESCE(SUM(jl.debit)-SUM(jl.credit),0) FROM journal_lines jl " &
            "JOIN journal_entries je ON je.id=jl.entry_id JOIN accounts a ON a.id=jl.account_id " &
            "WHERE je.status='posted' AND je.branch_id=@b AND a.code=@c",
            New With {.b = branchId, .c = code})
    End Function

    Private Sub CloseIfOpen(session As AppSession, shiftId As Long)
        Using db = DbSession.Open(_db.ConnectionString)
            Dim status As String = db.Connection.ExecuteScalar(Of String)(
                "SELECT status FROM shifts WHERE id=@id", New With {.id = shiftId})
            If status = "open" Then
                Dim expected As Decimal = db.Connection.ExecuteScalar(Of Decimal)(
                    "SELECT opening_cash + COALESCE((SELECT SUM(CASE WHEN direction='in' THEN amount ELSE -amount END) " &
                    "FROM cash_moves WHERE shift_id=@id),0) FROM shifts WHERE id=@id", New With {.id = shiftId})
                ShiftService.Close(session, shiftId, expected, "إغلاق تنظيف الاختبار")
            End If
        End Using
    End Sub
End Class
