Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

''' <summary>HR departments master data.</summary>
Public NotInheritable Class HrDepartmentsRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String, includeInactive As Boolean,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of HrDepartmentSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT d.id, d.name, d.notes, d.is_active, " &
            "       (SELECT COUNT(*) FROM hr_sections s WHERE s.department_id=d.id) AS section_count, " &
            "       (SELECT COUNT(*) FROM hr_employees e WHERE e.department_id=d.id) AS employee_count " &
            "FROM hr_departments d " &
            "WHERE (@includeInactive=1 OR d.is_active=1) " &
            "  AND (@term='' OR d.name LIKE @like) " &
            "ORDER BY d.name LIMIT @limit"
        Return conn.Query(Of HrDepartmentSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As HrDepartmentSummary
        Return conn.QueryFirstOrDefault(Of HrDepartmentSummary)(
            "SELECT d.id, d.name, d.notes, d.is_active, " &
            "       (SELECT COUNT(*) FROM hr_sections s WHERE s.department_id=d.id) AS section_count, " &
            "       (SELECT COUNT(*) FROM hr_employees e WHERE e.department_id=d.id) AS employee_count " &
            "FROM hr_departments d WHERE d.id=@id", New With {.id = id}, tx)
    End Function

    Public Shared Function FindByName(conn As IDbConnection, name As String,
                                      Optional tx As IDbTransaction = Nothing) As HrDepartmentSummary
        Return conn.QueryFirstOrDefault(Of HrDepartmentSummary)(
            "SELECT id, name, notes, is_active FROM hr_departments WHERE name=@name",
            New With {.name = name}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, name As String,
                                  notes As String) As Long
        conn.Execute(
            "INSERT INTO hr_departments(name, notes) VALUES(@name, @notes)",
            New With {.name = name, .notes = notes}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, name As String,
                             notes As String, isActive As Boolean)
        conn.Execute(
            "UPDATE hr_departments SET name=@name, notes=@notes, is_active=@active WHERE id=@id",
            New With {.id = id, .name = name, .notes = notes, .active = isActive}, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE hr_departments SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub
End Class

''' <summary>HR sections, each belonging to a department.</summary>
Public NotInheritable Class HrSectionsRepository
    Private Sub New()
    End Sub

    Private Const SelectSql As String =
        "SELECT s.id, s.department_id, d.name AS department_name, s.name, s.notes, s.is_active, " &
        "       (SELECT COUNT(*) FROM hr_employees e WHERE e.section_id=s.id) AS employee_count " &
        "FROM hr_sections s JOIN hr_departments d ON d.id=s.department_id "

    Public Shared Function Search(conn As IDbConnection, term As String, includeInactive As Boolean,
                                  Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of HrSectionSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String = SelectSql &
            "WHERE (@includeInactive=1 OR s.is_active=1) " &
            "  AND (@term='' OR s.name LIKE @like OR d.name LIKE @like) " &
            "ORDER BY d.name, s.name LIMIT @limit"
        Return conn.Query(Of HrSectionSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .term = If(term, String.Empty).Trim(),
                      .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function ListForDepartment(conn As IDbConnection, departmentId As Long,
                                             Optional includeInactive As Boolean = True,
                                             Optional tx As IDbTransaction = Nothing) As List(Of HrSectionSummary)
        Return conn.Query(Of HrSectionSummary)(SelectSql &
            "WHERE s.department_id=@departmentId AND (@includeInactive=1 OR s.is_active=1) ORDER BY s.name",
            New With {.departmentId = departmentId, .includeInactive = If(includeInactive, 1, 0)}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As HrSectionSummary
        Return conn.QueryFirstOrDefault(Of HrSectionSummary)(SelectSql & "WHERE s.id=@id",
                                                             New With {.id = id}, tx)
    End Function

    Public Shared Function FindByName(conn As IDbConnection, departmentId As Long, name As String,
                                      Optional tx As IDbTransaction = Nothing) As HrSectionSummary
        Return conn.QueryFirstOrDefault(Of HrSectionSummary)(
            "SELECT id, department_id, name, notes, is_active FROM hr_sections " &
            "WHERE department_id=@departmentId AND name=@name",
            New With {.departmentId = departmentId, .name = name}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, departmentId As Long,
                                  name As String, notes As String) As Long
        conn.Execute(
            "INSERT INTO hr_sections(department_id, name, notes) VALUES(@departmentId, @name, @notes)",
            New With {.departmentId = departmentId, .name = name, .notes = notes}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, departmentId As Long,
                             name As String, notes As String, isActive As Boolean)
        conn.Execute(
            "UPDATE hr_sections SET department_id=@departmentId, name=@name, notes=@notes, is_active=@active " &
            "WHERE id=@id",
            New With {.id = id, .departmentId = departmentId, .name = name, .notes = notes,
                      .active = isActive}, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE hr_sections SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub
End Class

''' <summary>Employees, sellers and delivery agents.</summary>
Public NotInheritable Class HrEmployeesRepository
    Private Sub New()
    End Sub

    Private Const SelectSql As String =
        "SELECT e.id, e.uuid, e.code, e.name, e.employee_type, e.department_id, d.name AS department_name, " &
        "       e.section_id, s.name AS section_name, e.mobile, e.phone, e.email, e.address, e.hire_date, " &
        "       e.base_salary, e.commission_percent, e.delivery_fee, e.notes, e.is_active " &
        "FROM hr_employees e " &
        "LEFT JOIN hr_departments d ON d.id=e.department_id " &
        "LEFT JOIN hr_sections s ON s.id=e.section_id "

    Public Shared Function Search(conn As IDbConnection, term As String, employeeType As String,
                                  includeInactive As Boolean, Optional limit As Integer = 500,
                                  Optional tx As IDbTransaction = Nothing) As List(Of HrEmployeeSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String = SelectSql &
            "WHERE (@includeInactive=1 OR e.is_active=1) " &
            "  AND (@type='' OR e.employee_type=@type) " &
            "  AND (@term='' OR e.name LIKE @like OR e.code LIKE @like OR e.mobile LIKE @like) " &
            "ORDER BY e.name LIMIT @limit"
        Return conn.Query(Of HrEmployeeSummary)(sql,
            New With {.includeInactive = If(includeInactive, 1, 0), .type = If(employeeType, String.Empty),
                      .term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long,
                                   Optional tx As IDbTransaction = Nothing) As HrEmployeeSummary
        Return conn.QueryFirstOrDefault(Of HrEmployeeSummary)(SelectSql & "WHERE e.id=@id",
                                                             New With {.id = id}, tx)
    End Function

    Public Shared Function FindByCode(conn As IDbConnection, code As String,
                                      Optional tx As IDbTransaction = Nothing) As HrEmployeeSummary
        Return conn.QueryFirstOrDefault(Of HrEmployeeSummary)(SelectSql & "WHERE e.code=@code",
                                                             New With {.code = code}, tx)
    End Function

    Public Shared Function NextCode(conn As IDbConnection, tx As IDbTransaction, prefix As String,
                                    Optional offset As Integer = 0) As String
        Dim nextId As Long = conn.ExecuteScalar(Of Long)(
            "SELECT COALESCE(MAX(id),0)+1 FROM hr_employees", Nothing, tx) + offset
        Return prefix & "-" & nextId.ToString("0000")
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, uuid As String, code As String,
                                  name As String, employeeType As String, departmentId As Long?,
                                  sectionId As Long?, mobile As String, phone As String, email As String,
                                  address As String, hireDate As Date?, baseSalary As Decimal,
                                  commissionPercent As Decimal, deliveryFee As Decimal,
                                  notes As String) As Long
        conn.Execute(
            "INSERT INTO hr_employees(uuid, code, name, employee_type, department_id, section_id, mobile, phone, " &
            "email, address, hire_date, base_salary, commission_percent, delivery_fee, notes) " &
            "VALUES(@uuid, @code, @name, @type, @departmentId, @sectionId, @mobile, @phone, @email, @address, " &
            "@hireDate, @baseSalary, @commissionPercent, @deliveryFee, @notes)",
            New With {
                .uuid = uuid, .code = code, .name = name, .type = employeeType,
                .departmentId = departmentId, .sectionId = sectionId, .mobile = mobile, .phone = phone,
                .email = email, .address = address, .hireDate = hireDate, .baseSalary = baseSalary,
                .commissionPercent = commissionPercent, .deliveryFee = deliveryFee, .notes = notes
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, code As String,
                             name As String, employeeType As String, departmentId As Long?,
                             sectionId As Long?, mobile As String, phone As String, email As String,
                             address As String, hireDate As Date?, baseSalary As Decimal,
                             commissionPercent As Decimal, deliveryFee As Decimal, notes As String,
                             isActive As Boolean)
        conn.Execute(
            "UPDATE hr_employees SET code=@code, name=@name, employee_type=@type, department_id=@departmentId, " &
            "section_id=@sectionId, mobile=@mobile, phone=@phone, email=@email, address=@address, " &
            "hire_date=@hireDate, base_salary=@baseSalary, commission_percent=@commissionPercent, " &
            "delivery_fee=@deliveryFee, notes=@notes, is_active=@active WHERE id=@id",
            New With {
                .id = id, .code = code, .name = name, .type = employeeType,
                .departmentId = departmentId, .sectionId = sectionId, .mobile = mobile, .phone = phone,
                .email = email, .address = address, .hireDate = hireDate, .baseSalary = baseSalary,
                .commissionPercent = commissionPercent, .deliveryFee = deliveryFee, .notes = notes,
                .active = isActive
            }, tx)
    End Sub

    Public Shared Sub SetActive(conn As IDbConnection, tx As IDbTransaction, id As Long, isActive As Boolean)
        conn.Execute("UPDATE hr_employees SET is_active=@active WHERE id=@id",
                     New With {.id = id, .active = isActive}, tx)
    End Sub
End Class

''' <summary>Monthly payroll runs and their editable lines.</summary>
Public NotInheritable Class HrPayrollRepository
    Private Sub New()
    End Sub

    Private Const RunSelectSql As String =
        "SELECT r.id, r.uuid, r.period_year, r.period_month, r.status, r.employee_count, " &
        "       r.total_base, r.total_commission, r.total_deductions, r.total_bonus, r.total_net, " &
        "       r.notes, r.created_at, r.paid_at, COALESCE(u.full_name,'') AS created_by_name " &
        "FROM hr_payroll_runs r LEFT JOIN users u ON u.id=r.created_by "

    Public Shared Function Runs(conn As IDbConnection, term As String, Optional limit As Integer = 200,
                                Optional tx As IDbTransaction = Nothing) As List(Of HrPayrollRunSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Return conn.Query(Of HrPayrollRunSummary)(RunSelectSql &
            "WHERE (@term='' OR r.notes LIKE @like OR " &
            "       CONCAT(r.period_year,'-',LPAD(r.period_month,2,'0')) LIKE @like) " &
            "ORDER BY r.period_year DESC, r.period_month DESC LIMIT @limit",
            New With {.term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function

    Public Shared Function GetRunById(conn As IDbConnection, id As Long,
                                      Optional tx As IDbTransaction = Nothing) As HrPayrollRunSummary
        Return conn.QueryFirstOrDefault(Of HrPayrollRunSummary)(RunSelectSql & "WHERE r.id=@id",
                                                               New With {.id = id}, tx)
    End Function

    Public Shared Function FindRunByPeriod(conn As IDbConnection, periodYear As Integer, periodMonth As Integer,
                                           Optional tx As IDbTransaction = Nothing) As HrPayrollRunSummary
        Return conn.QueryFirstOrDefault(Of HrPayrollRunSummary)(
            RunSelectSql & "WHERE r.period_year=@year AND r.period_month=@month",
            New With {.year = periodYear, .month = periodMonth}, tx)
    End Function

    Public Shared Function InsertRun(conn As IDbConnection, tx As IDbTransaction, uuid As String,
                                     periodYear As Integer, periodMonth As Integer, notes As String,
                                     createdBy As Long) As Long
        conn.Execute(
            "INSERT INTO hr_payroll_runs(uuid, period_year, period_month, notes, created_by) " &
            "VALUES(@uuid, @year, @month, @notes, @createdBy)",
            New With {.uuid = uuid, .year = periodYear, .month = periodMonth, .notes = notes,
                      .createdBy = createdBy}, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Sub UpdateRunTotals(conn As IDbConnection, tx As IDbTransaction, runId As Long)
        conn.Execute(
            "UPDATE hr_payroll_runs r SET " &
            " employee_count=(SELECT COUNT(*) FROM hr_payroll_lines l WHERE l.run_id=r.id), " &
            " total_base=(SELECT COALESCE(SUM(l.base_salary),0) FROM hr_payroll_lines l WHERE l.run_id=r.id), " &
            " total_commission=(SELECT COALESCE(SUM(l.commission_amount),0) FROM hr_payroll_lines l WHERE l.run_id=r.id), " &
            " total_deductions=(SELECT COALESCE(SUM(l.deductions),0) FROM hr_payroll_lines l WHERE l.run_id=r.id), " &
            " total_bonus=(SELECT COALESCE(SUM(l.bonus),0) FROM hr_payroll_lines l WHERE l.run_id=r.id), " &
            " total_net=(SELECT COALESCE(SUM(l.net_amount),0) FROM hr_payroll_lines l WHERE l.run_id=r.id) " &
            "WHERE r.id=@id", New With {.id = runId}, tx)
    End Sub

    Public Shared Function InsertLine(conn As IDbConnection, tx As IDbTransaction, runId As Long,
                                      employeeId As Long, employeeCode As String, employeeName As String,
                                      employeeType As String, departmentName As String, baseSalary As Decimal,
                                      commissionAmount As Decimal, deductions As Decimal, bonus As Decimal,
                                      netAmount As Decimal, notes As String) As Long
        conn.Execute(
            "INSERT INTO hr_payroll_lines(run_id, employee_id, employee_code, employee_name, employee_type, " &
            "department_name, base_salary, commission_amount, deductions, bonus, net_amount, notes) " &
            "VALUES(@runId, @employeeId, @code, @name, @type, @department, @base, @commission, @deductions, " &
            "@bonus, @net, @notes)",
            New With {
                .runId = runId, .employeeId = employeeId, .code = employeeCode, .name = employeeName,
                .type = employeeType, .department = departmentName, .base = baseSalary,
                .commission = commissionAmount, .deductions = deductions, .bonus = bonus,
                .net = netAmount, .notes = notes
            }, tx)
        Return conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
    End Function

    Public Shared Function Lines(conn As IDbConnection, runId As Long,
                                 Optional tx As IDbTransaction = Nothing) As List(Of HrPayrollLineSummary)
        Return conn.Query(Of HrPayrollLineSummary)(
            "SELECT id, run_id, employee_id, employee_code, employee_name, employee_type, department_name, " &
            "       base_salary, commission_amount, deductions, bonus, net_amount, notes " &
            "FROM hr_payroll_lines WHERE run_id=@id ORDER BY employee_name",
            New With {.id = runId}, tx).AsList()
    End Function

    Public Shared Function GetLineById(conn As IDbConnection, lineId As Long,
                                       Optional tx As IDbTransaction = Nothing) As HrPayrollLineSummary
        Return conn.QueryFirstOrDefault(Of HrPayrollLineSummary)(
            "SELECT id, run_id, employee_id, employee_code, employee_name, employee_type, department_name, " &
            "       base_salary, commission_amount, deductions, bonus, net_amount, notes " &
            "FROM hr_payroll_lines WHERE id=@id", New With {.id = lineId}, tx)
    End Function

    Public Shared Sub UpdateLine(conn As IDbConnection, tx As IDbTransaction, lineId As Long,
                                 deductions As Decimal, bonus As Decimal, netAmount As Decimal, notes As String)
        conn.Execute(
            "UPDATE hr_payroll_lines SET deductions=@deductions, bonus=@bonus, net_amount=@net, notes=@notes " &
            "WHERE id=@id",
            New With {.id = lineId, .deductions = deductions, .bonus = bonus, .net = netAmount,
                      .notes = notes}, tx)
    End Sub

    Public Shared Sub SetStatus(conn As IDbConnection, tx As IDbTransaction, runId As Long, status As String,
                                paidAt As Date?, paidBy As Long?)
        conn.Execute(
            "UPDATE hr_payroll_runs SET status=@status, paid_at=@paidAt, paid_by=@paidBy WHERE id=@id",
            New With {.id = runId, .status = status, .paidAt = paidAt, .paidBy = paidBy}, tx)
    End Sub

    Public Shared Sub DeleteRun(conn As IDbConnection, tx As IDbTransaction, runId As Long)
        conn.Execute("DELETE FROM hr_payroll_runs WHERE id=@id", New With {.id = runId}, tx)
    End Sub

    Public Shared Function CountLines(conn As IDbConnection, runId As Long,
                                      Optional tx As IDbTransaction = Nothing) As Integer
        Return conn.ExecuteScalar(Of Integer)(
            "SELECT COUNT(*) FROM hr_payroll_lines WHERE run_id=@id", New With {.id = runId}, tx)
    End Function
End Class

''' <summary>Commission aggregates derived from posted sale invoices.</summary>
Public NotInheritable Class HrCommissionsRepository
    Private Sub New()
    End Sub

    Public Shared Function ForRange(conn As IDbConnection, employeeType As String, fromDate As Date,
                                    toExclusive As Date, term As String,
                                    Optional limit As Integer = 500,
                                    Optional tx As IDbTransaction = Nothing) As List(Of HrCommissionSummary)
        Dim likeTerm As String = "%" & If(term, String.Empty).Trim() & "%"
        Dim sql As String =
            "SELECT e.id AS employee_id, e.code, e.name AS employee_name, e.employee_type, " &
            "       COALESCE(d.name,'') AS department_name, e.mobile, " &
            "       COUNT(i.id) AS invoice_count, COALESCE(SUM(i.total),0) AS sales_total, " &
            "       e.commission_percent, e.delivery_fee, " &
            "       CASE WHEN e.employee_type='seller' " &
            "            THEN ROUND(COALESCE(SUM(i.total),0) * e.commission_percent / 100, 2) " &
            "            ELSE ROUND(COUNT(i.id) * e.delivery_fee, 2) END AS commission_amount " &
            "FROM hr_employees e " &
            "LEFT JOIN hr_departments d ON d.id=e.department_id " &
            "LEFT JOIN invoices i ON i.status='posted' AND i.invoice_type='sale' " &
            "     AND i.business_date >= @fromDate AND i.business_date < @toExclusive " &
            "     AND ((e.employee_type='seller' AND i.seller_id=e.id) " &
            "          OR (e.employee_type='delivery' AND i.delivery_id=e.id)) " &
            "WHERE e.employee_type=@type AND e.is_active=1 " &
            "  AND (@term='' OR e.name LIKE @like OR e.code LIKE @like OR e.mobile LIKE @like) " &
            "GROUP BY e.id, e.code, e.name, e.employee_type, d.name, e.mobile, " &
            "         e.commission_percent, e.delivery_fee " &
            "ORDER BY commission_amount DESC, e.name LIMIT @limit"
        Return conn.Query(Of HrCommissionSummary)(sql,
            New With {.type = employeeType, .fromDate = fromDate.Date, .toExclusive = toExclusive.Date,
                      .term = If(term, String.Empty).Trim(), .like = likeTerm, .limit = limit}, tx).AsList()
    End Function
End Class
