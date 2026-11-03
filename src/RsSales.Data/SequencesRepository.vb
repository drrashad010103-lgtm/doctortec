Imports System.Data
Imports Dapper

''' <summary>
''' Per-branch, per-document, per-year sequences. Runs inside the caller's
''' transaction and locks the row, so two cashiers never share a number.
''' </summary>
Public NotInheritable Class SequencesRepository
    Private Sub New()
    End Sub

    Public Shared Function NextNo(conn As IDbConnection, tx As IDbTransaction, branchId As Long,
                                  docType As String, period As String) As Long
        conn.Execute(
            "INSERT IGNORE INTO document_sequences(branch_id, doc_type, period, next_no) VALUES(@b, @t, @p, 1)",
            New With {.b = branchId, .t = docType, .p = period}, tx)

        Dim current As Long = conn.ExecuteScalar(Of Long)(
            "SELECT next_no FROM document_sequences WHERE branch_id=@b AND doc_type=@t AND period=@p FOR UPDATE",
            New With {.b = branchId, .t = docType, .p = period}, tx)

        conn.Execute(
            "UPDATE document_sequences SET next_no=next_no+1 WHERE branch_id=@b AND doc_type=@t AND period=@p",
            New With {.b = branchId, .t = docType, .p = period}, tx)

        Return current
    End Function

    Public Shared Function Peek(conn As IDbConnection, tx As IDbTransaction, branchId As Long,
                                docType As String, period As String) As Long
        Dim value As Object = conn.ExecuteScalar(
            "SELECT next_no FROM document_sequences WHERE branch_id=@b AND doc_type=@t AND period=@p",
            New With {.b = branchId, .t = docType, .p = period}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 1
        Return Convert.ToInt64(value)
    End Function
End Class
