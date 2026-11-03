Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public Class JournalLineInput
    Public Property AccountId As Long
    Public Property Debit As Decimal
    Public Property Credit As Decimal
    Public Property Description As String
End Class

Public Class AccountBalanceRow
    Public Property AccountId As Long
    Public Property Code As String
    Public Property Name As String
    Public Property Nature As String
    Public Property Debit As Decimal
    Public Property Credit As Decimal
End Class

Public Class BranchInventoryDrift
    Public Property BranchId As Long
    Public Property BranchCode As String
    Public Property StockValue As Decimal
    Public Property LedgerValue As Decimal
End Class

Public NotInheritable Class AccountingRepository
    Private Sub New()
    End Sub

    ''' <summary>Well-known account codes created by the seed script.</summary>
    Public Const AccCash As String = "1010"
    Public Const AccBank As String = "1020"
    Public Const AccReceivable As String = "1100"
    Public Const AccInventory As String = "1200"
    Public Const AccDueFromBranches As String = "1250"
    Public Const AccInputTax As String = "1300"
    Public Const AccDueToBranches As String = "2150"
    Public Const AccPayable As String = "2100"
    Public Const AccOutputTax As String = "2200"
    Public Const AccCapital As String = "3000"
    Public Const AccSales As String = "4000"
    Public Const AccSalesReturn As String = "4100"
    Public Const AccCogs As String = "5000"
    Public Const AccDiscount As String = "5100"
    Public Const AccStockAdjust As String = "5200"
    Public Const AccSalaries As String = "5300"

    Public Shared Function GetAccountId(conn As IDbConnection, code As String,
                                        Optional tx As IDbTransaction = Nothing) As Long
        Dim value As Object = conn.ExecuteScalar(
            "SELECT id FROM accounts WHERE code=@c", New With {.c = code}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then
            Throw New InvalidOperationException($"Accounting account {code} is missing. Run the seed script.")
        End If
        Return Convert.ToInt64(value)
    End Function

    ''' <summary>
    ''' Returns the account id for the given code, creating the postable account if
    ''' it was not present in an earlier seed script (e.g. 5300 Salaries Expense).
    ''' </summary>
    Public Shared Function GetOrCreateAccountId(conn As IDbConnection, code As String,
                                                defaultName As String, nature As String,
                                                Optional tx As IDbTransaction = Nothing) As Long
        Dim existing As Object = conn.ExecuteScalar(
            "SELECT id FROM accounts WHERE code=@c", New With {.c = code}, tx)
        If existing IsNot Nothing AndAlso existing IsNot DBNull.Value Then
            Return Convert.ToInt64(existing)
        End If

        conn.Execute(
            "INSERT INTO accounts(code, name, nature, is_postable, is_active) " &
            "VALUES(@c, @n, @nat, 1, 1)",
            New With {.c = code, .n = defaultName, .nat = nature}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Function InsertEntry(conn As IDbConnection, tx As IDbTransaction,
                                       eventId As String, branchId As Long, entryDate As Date,
                                       description As String, sourceType As String, sourceId As Long?,
                                       userId As Long, lines As IList(Of JournalLineInput)) As Long
        If lines Is Nothing OrElse lines.Count = 0 Then
            Throw New ArgumentException("Journal entry needs at least one line.", NameOf(lines))
        End If

        Dim totalDebit As Decimal = 0D
        Dim totalCredit As Decimal = 0D
        For Each line In lines
            If line.Debit < 0D OrElse line.Credit < 0D Then
                Throw New ArgumentException("Debit and credit cannot be negative.")
            End If
            If line.Debit > 0D AndAlso line.Credit > 0D Then
                Throw New ArgumentException("A journal line cannot be both debit and credit.")
            End If
            If line.Debit = 0D AndAlso line.Credit = 0D Then
                Throw New ArgumentException("A journal line needs a debit or a credit.")
            End If
            totalDebit += line.Debit
            totalCredit += line.Credit
        Next

        totalDebit = Money.Round2(totalDebit)
        totalCredit = Money.Round2(totalCredit)
        If totalDebit <> totalCredit Then
            Throw New InvalidOperationException(
                $"Journal entry is not balanced: debit {totalDebit} <> credit {totalCredit}.")
        End If

        Const docType As String = "journal"
        Dim period As String = entryDate.ToString("yyyy")
        Dim entryNo As String = branchId.ToString() & "-JV-" & period & "-" &
                                SequencesRepository.NextNo(conn, tx, branchId, docType, period).ToString("000000")

        conn.Execute(
            "INSERT INTO journal_entries(event_id, branch_id, entry_no, entry_date, description, source_type, " &
            "source_id, status, posted_at, created_by) VALUES(@eventId, @branchId, @entryNo, @entryDate, " &
            "@description, @sourceType, @sourceId, 'posted', UTC_TIMESTAMP(6), @userId)",
            New With {
                .eventId = eventId, .branchId = branchId, .entryNo = entryNo, .entryDate = entryDate,
                .description = description, .sourceType = sourceType, .sourceId = sourceId, .userId = userId
            }, tx)

        Dim entryId As Long = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)

        For Each line In lines
            conn.Execute(
                "INSERT INTO journal_lines(entry_id, account_id, debit, credit, description) " &
                "VALUES(@entryId, @accountId, @debit, @credit, @description)",
                New With {
                    .entryId = entryId, .accountId = line.AccountId,
                    .debit = Money.Round2(line.Debit), .credit = Money.Round2(line.Credit),
                    .description = line.Description
                }, tx)
        Next

        Return entryId
    End Function

    Public Shared Function TrialBalance(conn As IDbConnection, branchId As Long, fromDate As Date, toDate As Date,
                                        Optional tx As IDbTransaction = Nothing) As List(Of AccountBalanceRow)
        ' The branch/date filter must be applied to the summed lines, not only to
        ' the joined entry: a filter in the JOIN still sums every branch's lines.
        Return conn.Query(Of AccountBalanceRow)(
            "SELECT a.id AS account_id, a.code, a.name, a.nature, " &
            "       COALESCE(SUM(CASE WHEN je.status='posted' AND je.branch_id=@b " &
            "                         AND je.entry_date BETWEEN @from AND @to THEN jl.debit ELSE 0 END),0) AS debit, " &
            "       COALESCE(SUM(CASE WHEN je.status='posted' AND je.branch_id=@b " &
            "                         AND je.entry_date BETWEEN @from AND @to THEN jl.credit ELSE 0 END),0) AS credit " &
            "FROM accounts a LEFT JOIN journal_lines jl ON jl.account_id=a.id " &
            "LEFT JOIN journal_entries je ON je.id=jl.entry_id " &
            "WHERE a.is_active=1 AND a.is_postable=1 " &
            "GROUP BY a.id, a.code, a.name, a.nature ORDER BY a.code",
            New With {.b = branchId, .from = fromDate, .to = toDate}, tx).AsList()
    End Function

    Public Shared Function AccountMovement(conn As IDbConnection, accountId As Long, branchId As Long,
                                           fromDate As Date, toDate As Date,
                                           Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT COALESCE(SUM(jl.debit),0)-COALESCE(SUM(jl.credit),0) FROM journal_lines jl " &
            "JOIN journal_entries je ON je.id=jl.entry_id " &
            "WHERE jl.account_id=@a AND je.status='posted' AND je.branch_id=@b " &
            "AND je.entry_date BETWEEN @from AND @to",
            New With {.a = accountId, .b = branchId, .from = fromDate, .to = toDate}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function

    ''' <summary>
    ''' Branches whose stock account does not match the value of their stock
    ''' movements. Movements are the source of truth; this catches postings that
    ''' changed one side only (for example a transfer without a journal entry).
    ''' </summary>
    Public Shared Function InventoryLedgerDrift(conn As IDbConnection, tolerance As Decimal,
                                                Optional branchId As Long? = Nothing,
                                                Optional tx As IDbTransaction = Nothing) As List(Of BranchInventoryDrift)
        Dim sql As String =
            "SELECT * FROM (" &
            "  SELECT b.id AS branch_id, b.code AS branch_code, " &
            "         COALESCE((SELECT SUM(m.qty_delta*m.unit_cost) FROM stock_moves m " &
            "                   WHERE m.branch_id=b.id),0) AS stock_value, " &
            "         COALESCE((SELECT SUM(jl.debit-jl.credit) FROM journal_lines jl " &
            "                   JOIN journal_entries je ON je.id=jl.entry_id " &
            "                   JOIN accounts a ON a.id=jl.account_id " &
            "                   WHERE a.code=@acc AND je.status='posted' AND je.branch_id=b.id),0) AS ledger_value " &
            "  FROM branches b WHERE b.is_active=1 "
        If branchId.HasValue Then sql &= "AND b.id=@b "
        sql &= ") t WHERE ABS(t.stock_value - t.ledger_value) > @tol ORDER BY t.branch_id"

        Return conn.Query(Of BranchInventoryDrift)(sql,
            New With {.acc = AccInventory, .b = If(branchId.HasValue, branchId.Value, 0L), .tol = tolerance},
            tx).AsList()
    End Function

    Public Shared Function ListAccounts(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As List(Of KeyValuePair(Of Long, String))
        Return conn.Query(Of KeyValuePair(Of Long, String))(
            "SELECT id AS `Key`, CONCAT(code, ' - ', name) AS `Value` FROM accounts " &
            "WHERE is_active=1 AND is_postable=1 ORDER BY code", Nothing, tx).AsList()
    End Function

    Public Shared Function ListPostableAccounts(conn As IDbConnection, Optional tx As IDbTransaction = Nothing) As List(Of AccountBalanceRow)
        Return conn.Query(Of AccountBalanceRow)(
            "SELECT id AS account_id, code, name, nature, 0 AS debit, 0 AS credit " &
            "FROM accounts WHERE is_active=1 AND is_postable=1 ORDER BY code", Nothing, tx).AsList()
    End Function
End Class

Public NotInheritable Class AuditRepository
    Private Sub New()
    End Sub

    Public Shared Sub Log(conn As IDbConnection, tx As IDbTransaction, eventId As String, branchId As Long?,
                          userId As Long?, username As String, action As String, entityType As String,
                          entityId As String, beforeJson As String, afterJson As String, deviceId As Long?)
        conn.Execute(
            "INSERT INTO audit_log(event_id, branch_id, user_id, username, action, entity_type, entity_id, " &
            "before_json, after_json, device_id) VALUES(@eventId, @branchId, @userId, @username, @action, " &
            "@entityType, @entityId, @before, @after, @deviceId)",
            New With {
                .eventId = eventId, .branchId = branchId, .userId = userId, .username = username,
                .action = action, .entityType = entityType, .entityId = entityId,
                .before = beforeJson, .after = afterJson, .deviceId = deviceId
            }, tx)
    End Sub

    Public Shared Function Recent(conn As IDbConnection, limit As Integer,
                                  Optional tx As IDbTransaction = Nothing) As List(Of String)
        Return conn.Query(Of String)(
            "SELECT CONCAT(created_at, ' | ', COALESCE(username,'system'), ' | ', action, ' | ', entity_type, ' #', COALESCE(entity_id,'')) " &
            "FROM audit_log ORDER BY id DESC LIMIT @limit", New With {.limit = limit}, tx).AsList()
    End Function
End Class
