Imports System
Imports System.Collections.Generic

Public NotInheritable Class ReportService
    Private Sub New()
    End Sub

    Public Shared Function SalesByDay(session As AppSession, fromDate As Date, toDate As Date) As List(Of SalesDayRow)
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.SalesByDay(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function SalesByType(session As AppSession, fromDate As Date, toDate As Date) As List(Of KeyValuePair(Of String, Decimal))
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.SalesByType(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function Profit(session As AppSession, fromDate As Date, toDate As Date) As List(Of ProfitRow)
        session.RequirePermission("reports.profit")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.Profit(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function ProfitByGroup(session As AppSession, fromDate As Date, toDate As Date) As List(Of ProfitRow)
        session.RequirePermission("reports.profit")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.ProfitByGroup(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function TaxReport(session As AppSession, fromDate As Date, toDate As Date) As TaxReportTotals
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Dim totals = ReportsRepository.TaxReport(db.Connection, session.BranchId, fromDate, toDate)
            Dim ledger = ReportsRepository.TaxAccountMovement(db.Connection, session.BranchId, fromDate, toDate)
            totals.NetPayable = Money.Round2(totals.NetPayable)
            totals.LedgerTaxMovement = ledger
            Return totals
        End Using
    End Function

    Public Shared Function StockValue(session As AppSession) As List(Of StockValueRow)
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.StockValue(db.Connection, session.BranchId)
        End Using
    End Function

    Public Shared Function TopItems(session As AppSession, fromDate As Date, toDate As Date,
                                    Optional limit As Integer = 50) As List(Of ProfitRow)
        session.RequirePermission("reports.profit")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.TopItems(db.Connection, session.BranchId, fromDate, toDate, limit)
        End Using
    End Function

    Public Shared Function CashSummary(session As AppSession, fromDate As Date, toDate As Date) As List(Of KeyValuePair(Of String, Decimal))
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.CashSummary(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function CustomerDebt(session As AppSession, Optional limit As Integer = 200) As List(Of CustomerSummary)
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.CustomerDebt(db.Connection, session.BranchId, limit)
        End Using
    End Function

    Public Shared Function TrialBalance(session As AppSession, fromDate As Date, toDate As Date) As List(Of AccountBalanceRow)
        session.RequirePermission("reports.profit")
        Using db = DbSession.Open(session.ConnectionString)
            Return AccountingRepository.TrialBalance(db.Connection, session.BranchId, fromDate, toDate)
        End Using
    End Function

    Public Shared Function CashMovesReport(session As AppSession, fromDate As Date, toDate As Date,
                                           Optional direction As String = "") As List(Of CashMoveReportRow)
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.CashMovesReport(db.Connection, session.BranchId, fromDate, toDate, direction)
        End Using
    End Function

    Public Shared Function AuditLogReport(session As AppSession, fromDate As Date, toDate As Date,
                                          Optional limit As Integer = 300) As List(Of AuditReportRow)
        session.RequirePermission("reports.view")
        Using db = DbSession.Open(session.ConnectionString)
            Return ReportsRepository.AuditLogReport(db.Connection, session.BranchId, fromDate, toDate, limit)
        End Using
    End Function
End Class
