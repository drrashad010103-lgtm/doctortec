Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>Read/write access to items, barcodes and price history.</summary>
Public NotInheritable Class ItemsRepository
    Private Sub New()
    End Sub

    Private Const SelectColumns As String =
        "i.id, i.uuid, i.code, i.name_ar, i.name_en, i.group_id, i.base_unit_id, " &
        "i.cost_price, i.sale_price, i.wholesale_price, i.min_price, i.reorder_level, " &
        "i.tax_category_id, COALESCE(t.rate, 0) AS tax_rate, i.is_active"

    Public Shared Function GetById(conn As IDbConnection, id As Long, Optional tx As IDbTransaction = Nothing) As ItemRecord
        Return conn.QueryFirstOrDefault(Of ItemRecord)(
            "SELECT " & SelectColumns & " FROM items i " &
            "LEFT JOIN tax_categories t ON t.id=i.tax_category_id WHERE i.id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function GetByCode(conn As IDbConnection, code As String, Optional tx As IDbTransaction = Nothing) As ItemRecord
        Return conn.QueryFirstOrDefault(Of ItemRecord)(
            "SELECT " & SelectColumns & " FROM items i " &
            "LEFT JOIN tax_categories t ON t.id=i.tax_category_id WHERE i.code=@code",
            New With {.code = code}, tx)
    End Function

    ''' <summary>Resolves a scanned barcode to its item.</summary>
    Public Shared Function GetByBarcode(conn As IDbConnection, barcode As String, Optional tx As IDbTransaction = Nothing) As ItemRecord
        Return conn.QueryFirstOrDefault(Of ItemRecord)(
            "SELECT " & SelectColumns & " FROM items i " &
            "LEFT JOIN tax_categories t ON t.id=i.tax_category_id " &
            "JOIN item_barcodes b ON b.item_id=i.id WHERE b.barcode=@barcode",
            New With {.barcode = barcode}, tx)
    End Function

    Public Shared Function Search(conn As IDbConnection, term As String, Optional limit As Integer = 50,
                                  Optional includeInactive As Boolean = False,
                                  Optional tx As IDbTransaction = Nothing) As List(Of ItemSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT i.id, i.code, i.name_ar, i.name_en, i.sale_price, i.cost_price, " &
            "       i.wholesale_price, i.min_price, " &
            "       i.tax_category_id, COALESCE(t.rate,0) AS tax_rate, COALESCE(t.code,'') AS tax_code, " &
            "       COALESCE((SELECT b.barcode FROM item_barcodes b WHERE b.item_id=i.id ORDER BY b.is_primary DESC, b.id LIMIT 1),'') AS barcode, " &
            "       i.reorder_level, i.is_active " &
            "FROM items i LEFT JOIN tax_categories t ON t.id=i.tax_category_id " &
            "WHERE (@term='' OR i.code LIKE @like OR i.name_ar LIKE @like OR i.name_en LIKE @like " &
            "       OR EXISTS (SELECT 1 FROM item_barcodes b2 WHERE b2.item_id=i.id AND b2.barcode LIKE @like)) "
        If Not includeInactive Then sql &= " AND i.is_active=1 "
        sql &= "ORDER BY i.name_ar LIMIT @limit"

        Return conn.Query(Of ItemSummary)(sql,
            New With {.term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction,
                                  uuid As String, code As String, nameAr As String, nameEn As String,
                                  groupId As Long?, unitId As Long?, costPrice As Decimal, salePrice As Decimal,
                                  wholesalePrice As Decimal, minPrice As Decimal, reorderLevel As Decimal,
                                  taxCategoryId As Long, isActive As Boolean) As Long
        conn.Execute(
            "INSERT INTO items(uuid, code, name_ar, name_en, group_id, base_unit_id, cost_price, sale_price, " &
            "wholesale_price, min_price, reorder_level, tax_category_id, is_active) " &
            "VALUES(@uuid, @code, @nameAr, @nameEn, @groupId, @unitId, @cost, @sale, @wholesale, @minPrice, " &
            "@reorder, @taxId, @active)",
            New With {
                .uuid = uuid, .code = code, .nameAr = nameAr, .nameEn = nameEn,
                .groupId = groupId, .unitId = unitId, .cost = costPrice, .sale = salePrice,
                .wholesale = wholesalePrice, .minPrice = minPrice, .reorder = reorderLevel,
                .taxId = taxCategoryId, .active = isActive
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long,
                             nameAr As String, nameEn As String, groupId As Long?, unitId As Long?,
                             costPrice As Decimal, salePrice As Decimal, wholesalePrice As Decimal,
                             minPrice As Decimal, reorderLevel As Decimal, taxCategoryId As Long,
                             isActive As Boolean)
        conn.Execute(
            "UPDATE items SET name_ar=@nameAr, name_en=@nameEn, group_id=@groupId, base_unit_id=@unitId, " &
            "cost_price=@cost, sale_price=@sale, wholesale_price=@wholesale, min_price=@minPrice, " &
            "reorder_level=@reorder, tax_category_id=@taxId, is_active=@active, row_version=row_version+1 " &
            "WHERE id=@id",
            New With {
                .id = id, .nameAr = nameAr, .nameEn = nameEn, .groupId = groupId, .unitId = unitId,
                .cost = costPrice, .sale = salePrice, .wholesale = wholesalePrice, .minPrice = minPrice,
                .reorder = reorderLevel, .taxId = taxCategoryId, .active = isActive
            }, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE items SET is_active=@a, row_version=row_version+1 WHERE id=@id",
                     New With {.id = id, .a = isActive}, tx)
    End Sub

    Public Shared Sub AddBarcode(conn As IDbConnection, tx As IDbTransaction, itemId As Long,
                                 barcode As String, isPrimary As Boolean)
        conn.Execute(
            "INSERT INTO item_barcodes(item_id, barcode, is_primary) VALUES(@itemId, @barcode, @primary) " &
            "ON DUPLICATE KEY UPDATE item_id=VALUES(item_id), is_primary=VALUES(is_primary)",
            New With {.itemId = itemId, .barcode = barcode, .primary = isPrimary}, tx)
    End Sub

    Public Shared Function ListBarcodes(conn As IDbConnection, itemId As Long,
                                        Optional tx As IDbTransaction = Nothing) As List(Of String)
        Return conn.Query(Of String)(
            "SELECT barcode FROM item_barcodes WHERE item_id=@id ORDER BY is_primary DESC, id",
            New With {.id = itemId}, tx).AsList()
    End Function

    Public Shared Sub AddPriceHistory(conn As IDbConnection, tx As IDbTransaction, itemId As Long,
                                      priceType As String, oldValue As Decimal, newValue As Decimal,
                                      userId As Long, reason As String)
        If oldValue = newValue Then Return
        conn.Execute(
            "INSERT INTO item_price_history(item_id, price_type, old_value, new_value, changed_by, reason) " &
            "VALUES(@itemId, @type, @oldValue, @newValue, @uid, @reason)",
            New With {.itemId = itemId, .type = priceType, .oldValue = oldValue, .newValue = newValue,
                      .uid = userId, .reason = reason}, tx)
    End Sub

    Public Shared Function ListGroups(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As List(Of KeyValuePair(Of Long, String))
        Return conn.Query(Of KeyValuePair(Of Long, String))(
            "SELECT id AS `Key`, name AS `Value` FROM item_groups WHERE is_active=1 ORDER BY name", Nothing, tx).AsList()
    End Function

    Public Shared Function ListUnits(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As List(Of KeyValuePair(Of Long, String))
        Return conn.Query(Of KeyValuePair(Of Long, String))(
            "SELECT id AS `Key`, name AS `Value` FROM units WHERE is_active=1 ORDER BY name", Nothing, tx).AsList()
    End Function

    Public Shared Function CountAll(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As Long
        Return conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM items", Nothing, tx)
    End Function

    Public Shared Function CountActive(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As Long
        Return conn.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM items WHERE is_active=1", Nothing, tx)
    End Function
End Class
