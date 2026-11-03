Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public Class BranchRecord
    Public Property Id As Long
    Public Property Code As String
    Public Property Name As String
    Public Property Timezone As String
    Public Property Address As String
    Public Property Phone As String
    Public Property TaxNo As String
    Public Property IsActive As Boolean
End Class

Public NotInheritable Class BranchesRepository
    Private Sub New()
    End Sub

    Public Shared Function ListAll(conn As IDbConnection, Optional onlyActive As Boolean = True,
                                   Optional tx As IDbTransaction = Nothing) As List(Of BranchRecord)
        Dim sql As String = "SELECT id, code, name, timezone, address, phone, tax_no, is_active FROM branches "
        If onlyActive Then sql &= "WHERE is_active=1 "
        sql &= "ORDER BY code"
        Return conn.Query(Of BranchRecord)(sql, Nothing, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long, Optional tx As IDbTransaction = Nothing) As BranchRecord
        Return conn.QueryFirstOrDefault(Of BranchRecord)(
            "SELECT id, code, name, timezone, address, phone, tax_no, is_active FROM branches WHERE id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, code As String, name As String,
                                  timezone As String, address As String, phone As String, taxNo As String) As Long
        conn.Execute(
            "INSERT INTO branches(code, name, timezone, address, phone, tax_no) " &
            "VALUES(@code, @name, @tz, @address, @phone, @taxNo)",
            New With {.code = code, .name = name, .tz = timezone, .address = address,
                      .phone = phone, .taxNo = taxNo}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, name As String,
                             address As String, phone As String, taxNo As String, isActive As Boolean)
        conn.Execute(
            "UPDATE branches SET name=@name, address=@address, phone=@phone, tax_no=@taxNo, is_active=@active " &
            "WHERE id=@id",
            New With {.id = id, .name = name, .address = address, .phone = phone,
                      .taxNo = taxNo, .active = isActive}, tx)
    End Sub
End Class

Public Class ShiftRecord
    Public Property Id As Long
    Public Property BranchId As Long
    Public Property ShiftNo As String
    Public Property UserId As Long
    Public Property UserName As String
    Public Property OpenedAt As Date
    Public Property ClosedAt As Date?
    Public Property OpeningCash As Decimal
    Public Property ExpectedCash As Decimal
    Public Property CountedCash As Decimal?
    Public Property Variance As Decimal?
    Public Property Status As String
    Public Property Notes As String

    Public ReadOnly Property StatusName As String
        Get
            Return If(Status = "open", "مفتوحة", "مغلقة")
        End Get
    End Property
End Class

Public NotInheritable Class ShiftsRepository
    Private Sub New()
    End Sub

    Public Shared Function GetOpen(conn As IDbConnection, branchId As Long, userId As Long,
                                   Optional tx As IDbTransaction = Nothing) As ShiftRecord
        Return conn.QueryFirstOrDefault(Of ShiftRecord)(
            "SELECT id, branch_id, shift_no, user_id, opened_at, closed_at, opening_cash, expected_cash, " &
            "counted_cash, variance, status, notes FROM shifts " &
            "WHERE branch_id=@b AND user_id=@u AND status='open' ORDER BY id DESC LIMIT 1",
            New With {.b = branchId, .u = userId}, tx)
    End Function

    ''' <summary>History for the branch, newest first, optionally limited to one cashier.</summary>
    Public Shared Function List(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                Optional userId As Long? = Nothing, Optional limit As Integer = 500,
                                Optional tx As IDbTransaction = Nothing) As List(Of ShiftRecord)
        Dim sql As String =
            "SELECT s.id, s.branch_id, s.shift_no, s.user_id, s.opened_at, s.closed_at, " &
            "       s.opening_cash, s.expected_cash, s.counted_cash, s.variance, s.status, s.notes, " &
            "       COALESCE(u.full_name, u.username, '') AS user_name " &
            "FROM shifts s JOIN users u ON u.id=s.user_id " &
            "WHERE s.branch_id=@b AND DATE(s.opened_at) BETWEEN @from AND @to "
        If userId.HasValue AndAlso userId.Value > 0 Then sql &= " AND s.user_id=@u "
        sql &= "ORDER BY s.id DESC LIMIT @limit"

        Return conn.Query(Of ShiftRecord)(sql,
            New With {.b = branchId, .from = fromDate, .to = toDate, .u = userId, .limit = limit},
            tx).AsList()
    End Function

    Public Shared Function GetExpectedCash(conn As IDbConnection, shiftId As Long,
                                           Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT opening_cash + COALESCE((SELECT SUM(CASE WHEN direction='in' THEN amount ELSE -amount END) " &
            "FROM cash_moves WHERE shift_id=@id),0) FROM shifts WHERE id=@id",
            New With {.id = shiftId}, tx)
        If value Is Nothing OrElse IsDBNull(value) Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    Public Shared Function Open(conn As IDbConnection, tx As IDbTransaction, branchId As Long, userId As Long,
                                openingCash As Decimal) As ShiftRecord
        Dim period As String = Date.UtcNow.ToString("yyyy")
        Dim no As Long = SequencesRepository.NextNo(conn, tx, branchId, "shift", period)
        Dim shiftNo As String = "SH-" & period & "-" & no.ToString("000000")

        conn.Execute(
            "INSERT INTO shifts(branch_id, shift_no, user_id, opening_cash, expected_cash, status) " &
            "VALUES(@b, @no, @u, @cash, @cash, 'open')",
            New With {.b = branchId, .no = shiftNo, .u = userId, .cash = openingCash}, tx)

        Dim id As Long = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
        Return conn.QueryFirstOrDefault(Of ShiftRecord)(
            "SELECT id, branch_id, shift_no, user_id, opened_at, opening_cash, expected_cash, status " &
            "FROM shifts WHERE id=@id", New With {.id = id}, tx)
    End Function

    Public Shared Sub Close(conn As IDbConnection, tx As IDbTransaction, shiftId As Long,
                            countedCash As Decimal, notes As String)
        Dim expected As Decimal = conn.ExecuteScalar(Of Decimal)(
            "SELECT opening_cash + COALESCE((SELECT SUM(CASE WHEN direction='in' THEN amount ELSE -amount END) " &
            "FROM cash_moves WHERE shift_id=@id),0) FROM shifts WHERE id=@id",
            New With {.id = shiftId}, tx)

        conn.Execute(
            "UPDATE shifts SET status='closed', closed_at=UTC_TIMESTAMP(6), expected_cash=@expected, " &
            "counted_cash=@counted, variance=@variance, notes=@notes WHERE id=@id AND status='open'",
            New With {.id = shiftId, .expected = expected, .counted = countedCash,
                      .variance = Money.Round2(countedCash - expected), .notes = notes}, tx)
    End Sub

    Public Shared Sub AddCashMove(conn As IDbConnection, tx As IDbTransaction, eventId As String,
                                  cashboxId As Long, shiftId As Long?, direction As String, amount As Decimal,
                                  refType As String, refId As Long?, userId As Long)
        conn.Execute(
            "INSERT INTO cash_moves(event_id, cashbox_id, shift_id, direction, amount, ref_type, ref_id, created_by) " &
            "VALUES(@eventId, @cashbox, @shift, @direction, @amount, @refType, @refId, @uid)",
            New With {.eventId = eventId, .cashbox = cashboxId, .shift = shiftId, .direction = direction,
                      .amount = amount, .refType = refType, .refId = refId, .uid = userId}, tx)
    End Sub

    Public Shared Function GetDefaultCashbox(conn As IDbConnection, branchId As Long,
                                             Optional tx As IDbTransaction = Nothing) As Long?
        Dim value As Object = conn.ExecuteScalar(
            "SELECT id FROM cashboxes WHERE branch_id=@b AND is_active=1 ORDER BY id LIMIT 1",
            New With {.b = branchId}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return Nothing
        Return Convert.ToInt64(value)
    End Function
End Class
