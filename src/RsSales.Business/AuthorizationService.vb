Imports System
Imports System.Collections.Generic

''' <summary>
''' Central permission codes and the single place where UI and business code
''' decide whether an operation is allowed. Keeping the rules here avoids the
''' scattered "if session.HasPermission(...)" checks that let screens bypass
''' authorization.
''' </summary>
Public NotInheritable Class Permissions
    Private Sub New()
    End Sub

    Public Const ItemsManage As String = "items.manage"
    Public Const SalesCreate As String = "sales.create"
    Public Const SalesReturn As String = "sales.return"
    Public Const SalesDiscount As String = "sales.discount"
    Public Const SalesBelowMinPrice As String = "sales.below_min_price"
    Public Const SalesCreditOverride As String = "sales.credit_override"
    Public Const PurchasesCreate As String = "purchases.create"
    Public Const PurchasesReturn As String = "purchases.return"
    Public Const CustomersManage As String = "customers.manage"
    Public Const StockAdjust As String = "stock.adjust"
    Public Const StockTransfer As String = "stock.transfer"
    Public Const StockOpening As String = "stock.opening"
    Public Const TaxManage As String = "tax.manage"
    Public Const SettingsManage As String = "settings.manage"
    Public Const ReportsView As String = "reports.view"
    Public Const ReportsAllBranches As String = "reports.all_branches"
    Public Const ReportsProfit As String = "reports.profit"
    Public Const ShiftsOpen As String = "shifts.open"
    Public Const ShiftsClose As String = "shifts.close"
    Public Const UsersManage As String = "users.manage"
    Public Const SyncManage As String = "sync.manage"
    Public Const HrManage As String = "hr.manage"
    Public Const DefinitionsView As String = "definitions.view"
    Public Const DefinitionsManage As String = "definitions.manage"
    Public Const CompanyManage As String = "company.manage"
End Class

''' <summary>
''' Unified authorization facade. Every screen and service asks this class,
''' never the raw permission set, so aliases and legacy role grants behave the
''' same everywhere.
''' </summary>
Public NotInheritable Class AuthorizationService
    Private Sub New()
    End Sub

    ''' <summary>Permission code -> codes that also satisfy it (backwards compatible).</summary>
    Private Shared ReadOnly Aliases As New Dictionary(Of String, String()) From {
        {Permissions.DefinitionsView, New String() {Permissions.DefinitionsManage, Permissions.SettingsManage, Permissions.ItemsManage}},
        {Permissions.DefinitionsManage, New String() {Permissions.SettingsManage}},
        {Permissions.CompanyManage, New String() {Permissions.SettingsManage}},
        {Permissions.PurchasesReturn, New String() {Permissions.PurchasesCreate}},
        {Permissions.ShiftsClose, New String() {Permissions.ShiftsOpen}}
    }

    Public Shared Function Can(session As AppSession, code As String) As Boolean
        If session Is Nothing Then Return False
        If String.IsNullOrWhiteSpace(code) Then Return True
        If session.HasPermission(code) Then Return True
        Dim alternatives As String() = Nothing
        If Aliases.TryGetValue(code, alternatives) Then
            For Each alternative In alternatives
                If session.HasPermission(alternative) Then Return True
            Next
        End If
        Return False
    End Function

    Public Shared Function CanAny(session As AppSession, ParamArray codes As String()) As Boolean
        If codes Is Nothing OrElse codes.Length = 0 Then Return True
        For Each code In codes
            If Can(session, code) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Throws a user-facing BusinessException when access is denied.</summary>
    Public Shared Sub Require(session As AppSession, code As String)
        If Not Can(session, code) Then
            Throw New BusinessException("ليس لديك صلاحية تنفيذ هذه العملية (" & code & ").")
        End If
    End Sub

    ''' <summary>
    ''' Non-throwing guard for UI event handlers: shows a consistent message and
    ''' returns False so the caller can simply exit.
    ''' </summary>
    Public Shared Function Allow(session As AppSession, code As String) As Boolean
        Return Can(session, code)
    End Function
End Class
