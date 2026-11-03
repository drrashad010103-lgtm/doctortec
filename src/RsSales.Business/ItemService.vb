Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Text.Json
Imports Dapper

Public Class ItemBatchExpiryRow
    Public Property BatchNo As String
    Public Property ExpiryDate As Date?
    Public Property Qty As Decimal
    Public Property UnitCost As Decimal
    Public Property InvoiceNo As String
    Public Property BusinessDate As Date
End Class

Public Class ItemBranchStockRow
    Public Property BranchName As String
    Public Property QtyOnHand As Decimal
    Public Property LastCost As Decimal
End Class

Public Class ItemFullDetails
    Public Property Record As ItemRecord
    Public Property GroupName As String
    Public Property UnitName As String
    Public Property TaxCategoryName As String
    Public Property CurrentBranchQty As Decimal
    Public Property LastCost As Decimal
    Public Property Barcodes As New List(Of String)()
    Public Property Batches As New List(Of ItemBatchExpiryRow)()
    Public Property BranchStocks As New List(Of ItemBranchStockRow)()
    Public Property Instructions As String
    Public Property DefaultBatchNo As String
    Public Property DefaultExpiryDate As Date?
End Class

''' <summary>Item master data, prices, barcodes and full item card lookup.</summary>
Public NotInheritable Class ItemService
    Private Sub New()
    End Sub

    Public Shared Function CountAll(session As AppSession, Optional includeInactive As Boolean = True) As Integer
        Using db = DbSession.Open(session.ConnectionString)
            Dim count As Long = If(includeInactive,
                                   ItemsRepository.CountAll(db.Connection),
                                   ItemsRepository.CountActive(db.Connection))
            Return Convert.ToInt32(count)
        End Using
    End Function

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional includeInactive As Boolean = False,
                                  Optional limit As Integer = 100) As List(Of ItemSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Dim items = ItemsRepository.Search(db.Connection, term, limit, includeInactive)
            ' Attach branch quantity in one query rather than per row.
            Dim ids As New List(Of Long)()
            For Each item In items
                ids.Add(item.Id)
            Next
            If ids.Count > 0 Then
                Dim balances = db.Connection.Query(Of StockSummary)(
                    "SELECT item_id, qty_on_hand, last_cost FROM stock_balances " &
                    "WHERE branch_id=@b AND item_id IN @ids",
                    New With {.b = session.BranchId, .ids = ids}).AsList()
                Dim map As New Dictionary(Of Long, StockSummary)()
                For Each balance In balances
                    map(balance.ItemId) = balance
                Next
                For Each item In items
                    Dim balance As StockSummary = Nothing
                    If map.TryGetValue(item.Id, balance) Then
                        item.QtyOnHand = balance.QtyOnHand
                        If balance.LastCost > 0D Then item.CostPrice = balance.LastCost
                    End If
                Next
            End If
            Return items
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, id As Long) As ItemRecord
        Using db = DbSession.Open(session.ConnectionString)
            Return ItemsRepository.GetById(db.Connection, id)
        End Using
    End Function

    Public Shared Function FindForSale(session As AppSession, codeOrBarcode As String) As ItemSummary
        If String.IsNullOrWhiteSpace(codeOrBarcode) Then Return Nothing
        Using db = DbSession.Open(session.ConnectionString)
            Dim term As String = codeOrBarcode.Trim()
            Dim record = ItemsRepository.GetByBarcode(db.Connection, term)
            If record Is Nothing Then record = ItemsRepository.GetByCode(db.Connection, term)
            If record Is Nothing Then Return Nothing

            Dim qty = StockRepository.GetQty(db.Connection, session.BranchId, record.Id)
            Dim lastCost = StockRepository.LastCostAsOf(db.Connection, session.BranchId, record.Id, Date.UtcNow)
            If lastCost <= 0D Then lastCost = record.CostPrice
            Dim barcodes = ItemsRepository.ListBarcodes(db.Connection, record.Id)

            Return New ItemSummary With {
                .Id = record.Id, .Code = record.Code, .NameAr = record.NameAr, .NameEn = record.NameEn,
                .SalePrice = record.SalePrice, .CostPrice = lastCost,
                .WholesalePrice = record.WholesalePrice, .MinPrice = record.MinPrice,
                .TaxCategoryId = record.TaxCategoryId, .TaxRate = record.TaxRate,
                .Barcode = If(barcodes.Count > 0, barcodes(0), String.Empty),
                .QtyOnHand = qty, .ReorderLevel = record.ReorderLevel, .IsActive = record.IsActive
            }
        End Using
    End Function

    ''' <summary>Loads full item card details: barcodes, batches/expiry dates, stock across branches, and instructions.</summary>
    Public Shared Function GetFullDetails(session As AppSession, itemId As Long) As ItemFullDetails
        Using db = DbSession.Open(session.ConnectionString)
            Dim c = db.Connection
            Dim rec = ItemsRepository.GetById(c, itemId)
            If rec Is Nothing Then Throw New BusinessException("الصنف غير موجود.")

            Dim details As New ItemFullDetails With {.Record = rec}

            If rec.GroupId.HasValue AndAlso rec.GroupId.Value > 0 Then
                details.GroupName = c.ExecuteScalar(Of String)(
                    "SELECT name FROM item_groups WHERE id=@id", New With {.id = rec.GroupId.Value})
            End If
            If rec.BaseUnitId.HasValue AndAlso rec.BaseUnitId.Value > 0 Then
                details.UnitName = c.ExecuteScalar(Of String)(
                    "SELECT name FROM units WHERE id=@id", New With {.id = rec.BaseUnitId.Value})
            End If
            details.TaxCategoryName = c.ExecuteScalar(Of String)(
                "SELECT name_ar FROM tax_categories WHERE id=@id", New With {.id = rec.TaxCategoryId})

            details.CurrentBranchQty = StockRepository.GetQty(c, session.BranchId, itemId)
            details.LastCost = StockRepository.LastCostAsOf(c, session.BranchId, itemId, Date.UtcNow)
            If details.LastCost <= 0D Then details.LastCost = rec.CostPrice

            details.Barcodes = ItemsRepository.ListBarcodes(c, itemId)

            details.Batches = c.Query(Of ItemBatchExpiryRow)(
                "SELECT COALESCE(l.batch_no, 'بدون تشغيلة') AS batch_no, l.expiry_date, " &
                "       l.qty, l.cost_at_sale AS unit_cost, v.invoice_no, v.business_date " &
                "FROM invoice_lines l JOIN invoices v ON v.id=l.invoice_id " &
                "WHERE l.item_id=@itemId AND v.branch_id=@branchId AND v.status='posted' " &
                "  AND (l.expiry_date IS NOT NULL OR (l.batch_no IS NOT NULL AND l.batch_no <> '') OR v.invoice_type IN ('purchase','opening')) " &
                "ORDER BY CASE WHEN l.expiry_date IS NULL THEN 1 ELSE 0 END, l.expiry_date ASC, v.id DESC LIMIT 50",
                New With {.itemId = itemId, .branchId = session.BranchId}).AsList()

            details.BranchStocks = c.Query(Of ItemBranchStockRow)(
                "SELECT b.name AS branch_name, COALESCE(sb.qty_on_hand, 0) AS qty_on_hand, " &
                "       COALESCE(NULLIF(sb.last_cost, 0), @fallbackCost) AS last_cost " &
                "FROM branches b LEFT JOIN stock_balances sb ON sb.branch_id=b.id AND sb.item_id=@itemId " &
                "WHERE b.is_active=1 ORDER BY b.id",
                New With {.itemId = itemId, .fallbackCost = rec.CostPrice}).AsList()

            details.Instructions = SettingsRepository.Get(c, "item.instructions." & itemId.ToString())
            details.DefaultBatchNo = SettingsRepository.Get(c, "item.batch." & itemId.ToString())
            Dim expRaw As String = SettingsRepository.Get(c, "item.expiry." & itemId.ToString())
            Dim parsedExp As Date
            If Not String.IsNullOrWhiteSpace(expRaw) AndAlso Date.TryParse(expRaw, parsedExp) Then
                details.DefaultExpiryDate = parsedExp.Date
            ElseIf details.Batches.Count > 0 Then
                If String.IsNullOrWhiteSpace(details.DefaultBatchNo) Then details.DefaultBatchNo = details.Batches(0).BatchNo
                details.DefaultExpiryDate = details.Batches(0).ExpiryDate
            End If
            Return details
        End Using
    End Function

    Public Shared Sub SaveInstructions(session As AppSession, itemId As Long, instructions As String,
                                       Optional batchNo As String = Nothing, Optional expiryDate As Date? = Nothing)
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            SettingsRepository.Set(db.Connection, tx,
                                   "item.instructions." & itemId.ToString(),
                                   If(instructions, String.Empty).Trim(),
                                   session.UserId)
            If batchNo IsNot Nothing Then
                SettingsRepository.Set(db.Connection, tx,
                                       "item.batch." & itemId.ToString(),
                                       batchNo.Trim(),
                                       session.UserId)
            End If
            If expiryDate.HasValue Then
                SettingsRepository.Set(db.Connection, tx,
                                       "item.expiry." & itemId.ToString(),
                                       expiryDate.Value.ToString("yyyy-MM-dd"),
                                       session.UserId)
            End If
            AuditRepository.Log(db.Connection, tx, Nothing, session.BranchId, session.UserId,
                                session.Username, "item_card_metadata_save", "item", itemId.ToString(),
                                Nothing, $"batch={batchNo};expiry={If(expiryDate.HasValue, expiryDate.Value.ToString("yyyy-MM-dd"), "")}", session.DeviceId)
            db.Commit()
        End Using
    End Sub

    Public Shared Function Save(session As AppSession, itemId As Long?, code As String, nameAr As String,
                                nameEn As String, groupId As Long?, unitId As Long?,
                                costPrice As Decimal, salePrice As Decimal, wholesalePrice As Decimal,
                                minPrice As Decimal, reorderLevel As Decimal, taxCategoryId As Long,
                                isActive As Boolean, barcode As String) As Long
        session.RequirePermission("items.manage")
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("كود الصنف مطلوب.")
        If String.IsNullOrWhiteSpace(nameAr) Then Throw New BusinessException("اسم الصنف مطلوب.")
        If salePrice < 0D OrElse costPrice < 0D Then Throw New BusinessException("الأسعار لا يمكن أن تكون سالبة.")
        If taxCategoryId <= 0 Then Throw New BusinessException("اختر التصنيف الضريبي للصنف.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()

            Dim duplicate = ItemsRepository.GetByCode(db.Connection, code.Trim(), db.Transaction)
            If duplicate IsNot Nothing AndAlso (Not itemId.HasValue OrElse duplicate.Id <> itemId.Value) Then
                Throw New BusinessException("كود الصنف مستخدم بالفعل لصنف آخر.")
            End If

            Dim newId As Long
            If itemId.HasValue AndAlso itemId.Value > 0 Then
                Dim before = ItemsRepository.GetById(db.Connection, itemId.Value, db.Transaction)
                If before Is Nothing Then Throw New BusinessException("الصنف غير موجود.")

                ItemsRepository.Update(db.Connection, db.Transaction, itemId.Value, nameAr.Trim(), nameEn,
                                       groupId, unitId, costPrice, salePrice, wholesalePrice, minPrice,
                                       reorderLevel, taxCategoryId, isActive)
                ItemsRepository.AddPriceHistory(db.Connection, db.Transaction, itemId.Value, "cost", before.CostPrice, costPrice, session.UserId, Nothing)
                ItemsRepository.AddPriceHistory(db.Connection, db.Transaction, itemId.Value, "sale", before.SalePrice, salePrice, session.UserId, Nothing)
                newId = itemId.Value

                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                    session.Username, "item_update", "item", newId.ToString(),
                    JsonSerializer.Serialize(before), Nothing, session.DeviceId)
            Else
                newId = ItemsRepository.Insert(db.Connection, db.Transaction, Uuid7.NewId(), code.Trim(), nameAr.Trim(),
                                               nameEn, groupId, unitId, costPrice, salePrice, wholesalePrice,
                                               minPrice, reorderLevel, taxCategoryId, isActive)

                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                    session.Username, "item_create", "item", newId.ToString(), Nothing, Nothing, session.DeviceId)
            End If

            If Not String.IsNullOrWhiteSpace(barcode) Then
                ItemsRepository.AddBarcode(db.Connection, db.Transaction, newId, barcode.Trim(), True)
            End If

            Dim payload = JsonSerializer.Serialize(New With {
                .id = newId, .code = code.Trim(), .name_ar = nameAr.Trim(), .name_en = nameEn,
                .group_id = groupId, .base_unit_id = unitId, .cost_price = costPrice, .sale_price = salePrice,
                .wholesale_price = wholesalePrice, .min_price = minPrice, .reorder_level = reorderLevel,
                .tax_category_id = taxCategoryId, .is_active = isActive,
                .row_version = If(itemId.HasValue, DateTime.UtcNow.Ticks, 1L)
            })
            SyncStore.AppendChange(db.Connection, db.Transaction, "item", newId.ToString(), "upsert", payload, session.BranchId)
            SyncStore.Enqueue(db.Connection, db.Transaction, Uuid7.NewId(), session.BranchId, session.DeviceId,
                              "item", newId.ToString(), "item.upserted", 1, payload, Date.UtcNow)

            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, itemId As Long, isActive As Boolean)
        session.RequirePermission("items.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            ItemsRepository.SetActive(db.Connection, db.Transaction, itemId, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                session.Username, If(isActive, "item_activate", "item_deactivate"), "item",
                itemId.ToString(), Nothing, Nothing, session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class
