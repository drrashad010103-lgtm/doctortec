Imports System.Collections.Generic

''' <summary>Validation or business-rule failure with a user-facing Arabic message.</summary>
Public Class BusinessException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, inner As Exception)
        MyBase.New(message, inner)
    End Sub
End Class

''' <summary>
''' Everything a service needs: connection string plus the authenticated context
''' (user, branch, device, tax settings).
''' </summary>
Public Class AppSession
    Public Property ConnectionString As String
    Public Property UserId As Long
    Public Property Username As String
    Public Property FullName As String
    Public Property BranchId As Long
    Public Property BranchName As String
    Public Property BranchTaxNo As String
    Public Property DeviceId As Long?
    Public Property PricesIncludeTax As Boolean = True
    Public Property TaxEnabled As Boolean = True
    Public Property Permissions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Public Function HasPermission(code As String) As Boolean
        If String.IsNullOrWhiteSpace(code) Then Return True
        Return Permissions.Contains(code) OrElse Permissions.Contains("*")
    End Function

    Public Sub RequirePermission(code As String)
        If Not HasPermission(code) Then
            Throw New BusinessException("ليس لديك صلاحية تنفيذ هذه العملية (" & code & ").")
        End If
    End Sub
End Class
