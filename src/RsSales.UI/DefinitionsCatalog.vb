Imports System.Collections.Generic

''' <summary>
''' Definition specs for the «البيانات الأساسية» tab. Each spec maps one card
''' to a real MySQL table so the screen can list, add, edit and delete rows.
''' </summary>
Public NotInheritable Class DefinitionsCatalog
    Private Sub New()
    End Sub

    Private Shared Function TextField(column As String, label As String, width As Integer,
                                      Optional required As Boolean = False,
                                      Optional maxLength As Integer = 128) As DefinitionField
        Return New DefinitionField With {
            .Column = column, .Label = label, .Width = width,
            .Required = required, .MaxLength = maxLength
        }
    End Function

    Private Shared Function CheckField(column As String, label As String) As DefinitionField
        Return New DefinitionField With {
            .Column = column, .Label = label, .Width = 80,
            .Kind = DefinitionFieldKind.CheckField
        }
    End Function

    Private Shared Function ComboField(column As String, label As String, width As Integer,
                                       table As String, textColumn As String,
                                       Optional required As Boolean = False,
                                       Optional filter As String = Nothing) As DefinitionField
        Return New DefinitionField With {
            .Column = column, .Label = label, .Width = width,
            .Kind = DefinitionFieldKind.ComboField,
            .LookupTable = table, .LookupTextColumn = textColumn,
            .Required = required, .LookupFilter = filter
        }
    End Function

    Public Shared Function Branches() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "branches", .Title = "تعريف الفروع",
            .Subtitle = "الفروع المسجّلة في النظام — الإضافة والتعديل تُحفظ في قاعدة البيانات",
            .OrderBy = "code", .SearchColumns = {"code", "name", "phone"},
            .RequiredPermission = "settings.manage"
        }
        spec.Fields.Add(TextField("code", "كود الفرع", 90, True, 16))
        spec.Fields.Add(TextField("name", "اسم الفرع", 240, True))
        spec.Fields.Add(TextField("phone", "الهاتف", 130, False, 32))
        spec.Fields.Add(TextField("address", "العنوان", 240, False, 255))
        spec.Fields.Add(TextField("tax_no", "الرقم الضريبي", 150, False, 64))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        spec.Defaults("timezone") = "Africa/Cairo"
        Return spec
    End Function

    Public Shared Function Cashboxes() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "cashboxes", .Title = "تعريف الخزن",
            .Subtitle = "خزائن النقدية لكل فرع — تُستخدم في الورديات وحركة الخزينة",
            .OrderBy = "name", .SearchColumns = {"name"},
            .RequiredPermission = "settings.manage"
        }
        spec.Fields.Add(ComboField("branch_id", "الفرع", 190, "branches", "name", True))
        spec.Fields.Add(TextField("name", "اسم الخزينة", 250, True))
        spec.Fields.Add(ComboField("account_id", "حساب الخزينة", 230, "accounts", "CONCAT(code, ' - ', name)"))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function ExpenseAccounts() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "accounts", .Title = "تعريف مصروفات",
            .Subtitle = "بنود المصروفات من دليل الحسابات — كل بند يُحفظ كحساب مصروف",
            .Filter = "nature='expense'", .OrderBy = "code", .SearchColumns = {"code", "name"},
            .RequiredPermission = "settings.manage"
        }
        spec.Defaults("nature") = "expense"
        spec.Fields.Add(TextField("code", "كود الحساب", 110, True, 32))
        spec.Fields.Add(TextField("name", "بند المصروف", 250, True))
        spec.Fields.Add(ComboField("parent_id", "الحساب الأب", 230, "accounts", "CONCAT(code, ' - ', name)"))
        spec.Fields.Add(CheckField("is_postable", "قابل للترحيل"))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function ItemGroups() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "item_groups", .Title = "مجموعات الأصناف",
            .Subtitle = "المجموعات الرئيسية والفرعية للأصناف",
            .OrderBy = "name", .SearchColumns = {"name"},
            .RequiredPermission = "items.manage"
        }
        spec.Fields.Add(TextField("name", "اسم المجموعة", 260, True))
        spec.Fields.Add(ComboField("parent_id", "المجموعة الأب", 230, "item_groups", "name"))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function Units() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "units", .Title = "الوحدات",
            .Subtitle = "وحدات القياس المستخدمة في الأصناف",
            .OrderBy = "name", .SearchColumns = {"name"},
            .RequiredPermission = "items.manage"
        }
        spec.Fields.Add(TextField("name", "اسم الوحدة", 240, True, 64))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function Warehouses() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "warehouses", .Title = "تعريف المخازن",
            .Subtitle = "مخازن كل فرع — تُستخدم في الأصناف والحركات المخزنية",
            .OrderBy = "name", .SearchColumns = {"code", "name"},
            .RequiredPermission = "items.manage"
        }
        spec.Fields.Add(ComboField("branch_id", "الفرع", 190, "branches", "name", True))
        spec.Fields.Add(TextField("code", "كود المخزن", 110, True, 32))
        spec.Fields.Add(TextField("name", "اسم المخزن", 240, True))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function Banks() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "banks", .Title = "تعريف البنوك",
            .Subtitle = "حسابات البنوك وبيانات الاتصال — تُربط بحساب من دليل الحسابات",
            .OrderBy = "name", .SearchColumns = {"name", "city", "phone", "mobile"},
            .RequiredPermission = "settings.manage"
        }
        spec.Fields.Add(TextField("name", "اسم البنك", 240, True))
        spec.Fields.Add(TextField("country", "الدولة", 110, False, 64))
        spec.Fields.Add(TextField("city", "المدينة", 110, False, 64))
        spec.Fields.Add(TextField("region", "المنطقة", 110, False, 64))
        spec.Fields.Add(TextField("phone", "تليفون", 120, False, 32))
        spec.Fields.Add(TextField("mobile", "موبايل", 120, False, 32))
        Dim nature As New DefinitionField With {
            .Column = "account_nature", .Label = "طبيعة الحساب", .Width = 110,
            .Kind = DefinitionFieldKind.ComboField, .Required = True
        }
        nature.Options.Add(New KeyValuePair(Of String, String)("debit", "مدين"))
        nature.Options.Add(New KeyValuePair(Of String, String)("credit", "دائن"))
        spec.Fields.Add(nature)
        spec.Fields.Add(ComboField("account_id", "حساب البنك", 220, "accounts", "CONCAT(code, ' - ', name)"))
        spec.Fields.Add(TextField("notes", "ملاحظات", 220, False, 255))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function

    Public Shared Function Areas() As DefinitionSpec
        Dim spec As New DefinitionSpec With {
            .Table = "areas", .Title = "بيانات المناطق",
            .Subtitle = "مناطق العملاء للتوصيل والتقارير",
            .OrderBy = "name", .SearchColumns = {"name", "city"},
            .RequiredPermission = "customers.manage"
        }
        spec.Fields.Add(TextField("name", "اسم المنطقة", 220, True))
        spec.Fields.Add(TextField("city", "المدينة", 140, False, 64))
        spec.Fields.Add(TextField("notes", "ملاحظات", 220, False, 255))
        spec.Fields.Add(CheckField("is_active", "مفعّل"))
        Return spec
    End Function
End Class
