Imports System.Collections.Generic
Imports System.Data
Imports System.Text.Json
Imports Dapper

''' <summary>Shared posting helpers (numbering, settings, cash methods).</summary>
Friend NotInheritable Class PostingHelper
    Private Sub New()
    End Sub

    Friend Shared Function BranchCode(conn As IDbConnection, branchId As Long, tx As IDbTransaction) As String
        Dim code As String = conn.ExecuteScalar(Of String)(
            "SELECT code FROM branches WHERE id=@b", New With {.b = branchId}, tx)
        If String.IsNullOrWhiteSpace(code) Then Throw New BusinessException("الفرع غير موجود.")
        Return code
    End Function

    ''' <summary>Number per branch, document type and year (DB-03).</summary>
    Friend Shared Function NextDocumentNo(conn As IDbConnection, tx As IDbTransaction, branchId As Long,
                                          docType As String, prefix As String, businessDate As Date) As String
        Dim period As String = businessDate.Year.ToString()
        Dim seq As Long = SequencesRepository.NextNo(conn, tx, branchId, docType, period)
        Return BranchCode(conn, branchId, tx) & "-" & prefix & "-" & period & "-" & seq.ToString("000000")
    End Function

    Friend Shared Function IsCashMethod(conn As IDbConnection, methodId As Long, tx As IDbTransaction) As Boolean
        If methodId <= 0 Then Return False
        Dim name As String = conn.ExecuteScalar(Of String)(
            "SELECT name FROM payment_methods WHERE id=@id", New With {.id = methodId}, tx)
        If String.IsNullOrWhiteSpace(name) Then Return False
        ' Seeded method is "كاش"; installations may use "نقد" or "cash".
        Return name.IndexOf("نقد", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               name.IndexOf("كاش", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               name.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>
    ''' Rejects a shift that belongs to another branch: its drawer and cash moves
    ''' must never be posted from the wrong branch.
    ''' </summary>
    Friend Shared Sub EnsureShiftInBranch(conn As IDbConnection, tx As IDbTransaction,
                                          shiftId As Long?, branchId As Long)
        If Not shiftId.HasValue OrElse shiftId.Value <= 0 Then Return
        Dim owner As Object = conn.ExecuteScalar(
            "SELECT branch_id FROM shifts WHERE id=@id", New With {.id = shiftId.Value}, tx)
        If owner Is Nothing OrElse IsDBNull(owner) Then Throw New BusinessException("الوردية غير موجودة.")
        If CLng(owner) <> branchId Then
            Throw New BusinessException("الوردية تخص فرعًا آخر ولا يمكن استخدامها من هذا الفرع.")
        End If
    End Sub

    Friend Shared Function DefaultPaymentMethod(conn As IDbConnection, tx As IDbTransaction) As Long
        Dim id As Long = conn.ExecuteScalar(Of Long)(
            "SELECT id FROM payment_methods WHERE is_active=1 ORDER BY id LIMIT 1", Nothing, tx)
        If id = 0 Then Throw New BusinessException("لا توجد طريقة دفع مفعّلة. راجع الإعدادات.")
        Return id
    End Function

    Friend Shared Function CashboxOrThrow(conn As IDbConnection, branchId As Long, tx As IDbTransaction) As Long
        Dim id As Long? = ShiftsRepository.GetDefaultCashbox(conn, branchId, tx)
        If Not id.HasValue Then Throw New BusinessException("لا يوجد صندوق نقدية مفعّل لهذا الفرع.")
        Return id.Value
    End Function

    Friend Shared Function BuildQr(session As AppSession, sellerName As String, taxNo As String,
                                   occurredAt As Date, total As Decimal, taxTotal As Decimal) As String
        If Not session.TaxEnabled OrElse taxTotal = 0D Then Return Nothing
        Return TaxCalculator.BuildQrPayload(
            sellerName,
            If(String.IsNullOrWhiteSpace(taxNo), "000000000000000", taxNo),
            New DateTimeOffset(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)),
            total, taxTotal)
    End Function

    Friend Shared Function LocalBusinessDate(session As AppSession, occurredAtUtc As Date) As Date
        Return occurredAtUtc.ToLocalTime().Date
    End Function

    Friend Shared Function AsUtc(value As Date) As Date
        Select Case value.Kind
            Case DateTimeKind.Utc
                Return value
            Case DateTimeKind.Local
                Return value.ToUniversalTime()
            Case Else
                Return DateTime.SpecifyKind(value, DateTimeKind.Utc)
        End Select
    End Function

    ''' <summary>Serialises an event payload for the outbox.</summary>
    Friend Shared Function Serialize(payload As Object) As String
        Return JsonSerializer.Serialize(payload, New JsonSerializerOptions With {.WriteIndented = False})
    End Function
End Class
