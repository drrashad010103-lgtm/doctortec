Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>Tax categories and tax-related settings.</summary>
Public NotInheritable Class TaxService
    Private Sub New()
    End Sub

    Public Shared Function ListCategories(session As AppSession) As List(Of TaxCategorySummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return db.Connection.Query(Of TaxCategorySummary)(
                "SELECT id, code, name_ar, rate, is_exempt, is_active, effective_from " &
                "FROM tax_categories ORDER BY rate DESC, code").AsList()
        End Using
    End Function

    Public Shared Function SaveCategory(session As AppSession, id As Long?, code As String, nameAr As String,
                                        rate As Decimal, isExempt As Boolean, isActive As Boolean,
                                        effectiveFrom As Date) As Long
        session.RequirePermission("tax.manage")
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("كود التصنيف الضريبي مطلوب.")
        If String.IsNullOrWhiteSpace(nameAr) Then Throw New BusinessException("اسم التصنيف الضريبي مطلوب.")
        If rate < 0D OrElse rate > 100D Then Throw New BusinessException("نسبة الضريبة يجب أن تكون بين 0 و100.")
        If isExempt AndAlso rate <> 0D Then Throw New BusinessException("التصنيف المعفى يجب أن تكون نسبته صفرًا.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                Dim before = db.Connection.QueryFirstOrDefault(Of TaxCategoryRecord)(
                    "SELECT id, code, name_ar, rate, is_exempt, is_active FROM tax_categories WHERE id=@id",
                    New With {.id = id.Value}, db.Transaction)
                If before Is Nothing Then Throw New BusinessException("التصنيف الضريبي غير موجود.")

                db.Connection.Execute(
                    "UPDATE tax_categories SET code=@code, name_ar=@name, rate=@rate, is_exempt=@exempt, " &
                    "is_active=@active, updated_by=@uid WHERE id=@id",
                    New With {.id = id.Value, .code = code.Trim(), .name = nameAr.Trim(), .rate = rate,
                              .exempt = isExempt, .active = isActive, .uid = session.UserId}, db.Transaction)

                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "tax_category_update", "tax_category", id.Value.ToString(),
                                    $"{before.Code}|{before.Rate}|{before.IsExempt}", $"{code}|{rate}|{isExempt}", session.DeviceId)
                newId = id.Value
            Else
                db.Connection.Execute(
                    "INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, is_active, " &
                    "effective_from, updated_by) VALUES(@code, @name, @name, @rate, @exempt, @active, @from, @uid)",
                    New With {.code = code.Trim(), .name = nameAr.Trim(), .rate = rate, .exempt = isExempt,
                              .active = isActive, .from = effectiveFrom, .uid = session.UserId}, db.Transaction)
                newId = db.Connection.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, db.Transaction)

                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "tax_category_create", "tax_category", newId.ToString(),
                                    Nothing, $"{code}|{rate}|{isExempt}", session.DeviceId)
            End If

            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Function EnsureDefaultCategories(session As AppSession) As Integer
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim count As Long = db.Connection.ExecuteScalar(Of Long)("SELECT COUNT(*) FROM tax_categories", Nothing, db.Transaction)
            If count > 0 Then
                db.Commit()
                Return 0
            End If

            db.Connection.Execute(
                "INSERT INTO tax_categories(code, name_ar, name_en, rate, is_exempt, effective_from) VALUES " &
                "('vat14','ضريبة قيمة مضافة 14%','VAT 14%',14.000,0,'2024-01-01')," &
                "('vat0','ضريبة صفرية','Zero rated',0.000,0,'2024-01-01')," &
                "('exempt','معفى من الضريبة','Exempt',0.000,1,'2024-01-01')",
                Nothing, db.Transaction)

            SettingsRepository.[Set](db.Connection, db.Transaction, "tax.default_category", "vat14", session.UserId)
            SettingsRepository.[Set](db.Connection, db.Transaction, "tax.prices_include_tax", "1", session.UserId)
            SettingsRepository.[Set](db.Connection, db.Transaction, "tax.enabled", "1", session.UserId)
            SettingsRepository.[Set](db.Connection, db.Transaction, "tax.recoverable", "1", session.UserId)

            db.Commit()
            Return 3
        End Using
    End Function

    Public Shared Function GetSetting(session As AppSession, settingKey As String, defaultValue As String) As String
        Using db = DbSession.Open(session.ConnectionString)
            Return SettingsRepository.[Get](db.Connection, settingKey, Nothing)
        End Using
    End Function

    Public Shared Sub SetSetting(session As AppSession, settingKey As String, settingValue As String)
        session.RequirePermission("settings.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            SettingsRepository.[Set](db.Connection, db.Transaction, settingKey, settingValue, session.UserId)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "setting_change", "setting", settingKey, Nothing, settingValue, session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class
