Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public NotInheritable Class TaxRepository
    Private Sub New()
    End Sub

    Public Shared Function ListActive(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As List(Of TaxCategoryRecord)
        Return conn.Query(Of TaxCategoryRecord)(
            "SELECT id, code, name_ar, name_en, rate, is_exempt, is_active, effective_from, effective_to " &
            "FROM tax_categories WHERE is_active=1 ORDER BY rate DESC, code", Nothing, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long, Optional tx As IDbTransaction = Nothing) As TaxCategoryRecord
        Return conn.QueryFirstOrDefault(Of TaxCategoryRecord)(
            "SELECT id, code, name_ar, name_en, rate, is_exempt, is_active, effective_from, effective_to " &
            "FROM tax_categories WHERE id=@id", New With {.id = id}, tx)
    End Function

    Public Shared Function GetDefault(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As TaxCategoryRecord
        Dim code As String = SettingsRepository.[Get](conn, "tax.default_category", tx)
        If String.IsNullOrWhiteSpace(code) Then code = "vat14"
        Dim row = conn.QueryFirstOrDefault(Of TaxCategoryRecord)(
            "SELECT id, code, name_ar, name_en, rate, is_exempt, is_active, effective_from, effective_to " &
            "FROM tax_categories WHERE code=@c LIMIT 1", New With {.c = code}, tx)
        Return row
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, nameAr As String, code As String,
                                  rate As Decimal, isExempt As Boolean, effectiveFrom As Date, userId As Long) As Long
        conn.Execute(
            "INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, is_active, effective_from, updated_by) " &
            "VALUES(@code, @name, @name, @rate, @exempt, 1, @effFrom, @uid)",
            New With {.code = code, .name = nameAr, .rate = rate, .exempt = isExempt, .effFrom = effectiveFrom, .uid = userId}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, nameAr As String,
                             rate As Decimal, isExempt As Boolean, isActive As Boolean, userId As Long)
        conn.Execute(
            "UPDATE tax_categories SET name_ar=@name, rate=@rate, is_exempt=@exempt, is_active=@active, updated_by=@uid " &
            "WHERE id=@id",
            New With {.id = id, .name = nameAr, .rate = rate, .exempt = isExempt, .active = isActive, .uid = userId}, tx)
    End Sub
End Class

Public NotInheritable Class SettingsRepository
    Private Sub New()
    End Sub

    Public Shared Function [Get](conn As IDbConnection, settingKey As String, Optional tx As IDbTransaction = Nothing) As String
        Return conn.ExecuteScalar(Of String)("SELECT v FROM settings WHERE k=@k", New With {.k = settingKey}, tx)
    End Function

    Public Shared Function GetBool(conn As IDbConnection, settingKey As String, defaultValue As Boolean, Optional tx As IDbTransaction = Nothing) As Boolean
        Dim settingValue As String = [Get](conn, settingKey, tx)
        If String.IsNullOrWhiteSpace(settingValue) Then Return defaultValue
        Return settingValue = "1" OrElse settingValue.Equals("true", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function GetDecimal(conn As IDbConnection, settingKey As String, defaultValue As Decimal, Optional tx As IDbTransaction = Nothing) As Decimal
        Dim settingValue As String = [Get](conn, settingKey, tx)
        Dim parsed As Decimal
        If Decimal.TryParse(settingValue, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, parsed) Then
            Return parsed
        End If
        Return defaultValue
    End Function

    Public Shared Sub [Set](conn As IDbConnection, tx As IDbTransaction, settingKey As String, settingValue As String, userId As Long)
        conn.Execute(
            "INSERT INTO settings(k, v, value_type, updated_by) VALUES(@k, @v, 'string', @uid) " &
            "ON DUPLICATE KEY UPDATE v=VALUES(v), updated_by=VALUES(updated_by)",
            New With {.k = settingKey, .v = settingValue, .uid = userId}, tx)
    End Sub
End Class
