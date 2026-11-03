Imports System.Collections.Generic
Imports System.Linq

''' <summary>
''' Shared, read-only lookup lists used by the data-entry screens.
''' Every lookup respects the caller's database connection and (for branches)
''' the branches the signed-in user is allowed to access.
''' </summary>
Friend NotInheritable Class UiLookup
    Private Sub New()
    End Sub

    Friend Shared Function Branches(session As AppSession) As List(Of BranchRecord)
        If session Is Nothing Then Return New List(Of BranchRecord)()
        Using db = DbSession.Open(session.ConnectionString)
            Dim allBranches = BranchesRepository.ListAll(db.Connection)
            Dim allowed = UsersRepository.GetBranches(db.Connection, session.UserId)
            If allowed Is Nothing OrElse allowed.Count = 0 Then Return allBranches
            Return allBranches.Where(Function(b) allowed.Contains(b.Id)).ToList()
        End Using
    End Function

    Friend Shared Function PaymentMethods(session As AppSession) As List(Of LookupItem)
        If session Is Nothing Then Return New List(Of LookupItem)()
        Using db = DbSession.Open(session.ConnectionString)
            Dim field As New DefinitionField With {
                .LookupTable = "payment_methods",
                .LookupTextColumn = "name",
                .LookupFilter = "is_active=1"
            }
            Return DefinitionsRepository.Lookup(db.Connection, field).
                Select(Function(row) New LookupItem With {.Id = row.Id, .Name = row.Name}).ToList()
        End Using
    End Function

    Friend Shared Function TaxCategories(session As AppSession) As List(Of LookupItem)
        If session Is Nothing Then Return New List(Of LookupItem)()
        Using db = DbSession.Open(session.ConnectionString)
            Return TaxRepository.ListActive(db.Connection).
                Select(Function(tax) New LookupItem With {
                    .Id = tax.Id,
                    .Name = If(String.IsNullOrWhiteSpace(tax.NameAr), tax.NameEn, tax.NameAr)
                }).ToList()
        End Using
    End Function

    Friend Shared Function Groups(session As AppSession) As List(Of LookupItem)
        If session Is Nothing Then Return New List(Of LookupItem)()
        Using db = DbSession.Open(session.ConnectionString)
            Return ItemsRepository.ListGroups(db.Connection).
                Select(Function(item) New LookupItem With {.Id = item.Key, .Name = item.Value}).ToList()
        End Using
    End Function

    Friend Shared Function Units(session As AppSession) As List(Of LookupItem)
        If session Is Nothing Then Return New List(Of LookupItem)()
        Using db = DbSession.Open(session.ConnectionString)
            Return ItemsRepository.ListUnits(db.Connection).
                Select(Function(item) New LookupItem With {.Id = item.Key, .Name = item.Value}).ToList()
        End Using
    End Function
End Class
