Imports System
Imports System.Collections.Generic

''' <summary>HR departments.</summary>
Public NotInheritable Class HrDepartmentService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional includeInactive As Boolean = True) As List(Of HrDepartmentSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrDepartmentsRepository.Search(db.Connection, term, includeInactive)
        End Using
    End Function

    Public Shared Function Save(session As AppSession, id As Long?, name As String, notes As String,
                                isActive As Boolean) As Long
        session.RequirePermission("hr.manage")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم الإدارة مطلوب.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim duplicate = HrDepartmentsRepository.FindByName(db.Connection, name.Trim(), db.Transaction)
            If duplicate IsNot Nothing AndAlso (Not id.HasValue OrElse duplicate.Id <> id.Value) Then
                Throw New BusinessException("اسم الإدارة مستخدم بالفعل.")
            End If

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                If HrDepartmentsRepository.GetById(db.Connection, id.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("الإدارة غير موجودة.")
                End If
                HrDepartmentsRepository.Update(db.Connection, db.Transaction, id.Value, name.Trim(), notes, isActive)
                newId = id.Value
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_department_update", "hr_department", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            Else
                newId = HrDepartmentsRepository.Insert(db.Connection, db.Transaction, name.Trim(), notes)
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_department_create", "hr_department", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            End If
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("hr.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            HrDepartmentsRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_department_toggle", "hr_department", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>HR sections inside departments.</summary>
Public NotInheritable Class HrSectionService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String,
                                  Optional includeInactive As Boolean = True) As List(Of HrSectionSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrSectionsRepository.Search(db.Connection, term, includeInactive)
        End Using
    End Function

    Public Shared Function ListForDepartment(session As AppSession, departmentId As Long,
                                             Optional includeInactive As Boolean = True) As List(Of HrSectionSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrSectionsRepository.ListForDepartment(db.Connection, departmentId, includeInactive)
        End Using
    End Function

    Public Shared Function Save(session As AppSession, id As Long?, departmentId As Long, name As String,
                                notes As String, isActive As Boolean) As Long
        session.RequirePermission("hr.manage")
        If departmentId <= 0 Then Throw New BusinessException("اختر الإدارة أولاً.")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم القسم مطلوب.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            If HrDepartmentsRepository.GetById(db.Connection, departmentId, db.Transaction) Is Nothing Then
                Throw New BusinessException("الإدارة المختارة غير موجودة.")
            End If

            Dim duplicate = HrSectionsRepository.FindByName(db.Connection, departmentId, name.Trim(), db.Transaction)
            If duplicate IsNot Nothing AndAlso (Not id.HasValue OrElse duplicate.Id <> id.Value) Then
                Throw New BusinessException("اسم القسم مستخدم بالفعل في هذه الإدارة.")
            End If

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                If HrSectionsRepository.GetById(db.Connection, id.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("القسم غير موجود.")
                End If
                HrSectionsRepository.Update(db.Connection, db.Transaction, id.Value, departmentId, name.Trim(),
                                            notes, isActive)
                newId = id.Value
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_section_update", "hr_section", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            Else
                newId = HrSectionsRepository.Insert(db.Connection, db.Transaction, departmentId, name.Trim(), notes)
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_section_create", "hr_section", newId.ToString(),
                                    Nothing, name.Trim(), session.DeviceId)
            End If
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("hr.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            HrSectionsRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_section_toggle", "hr_section", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>Employees, sellers and delivery agents.</summary>
Public NotInheritable Class HrEmployeeService
    Private Sub New()
    End Sub

    Public Shared Function Search(session As AppSession, term As String, employeeType As String,
                                  Optional includeInactive As Boolean = True) As List(Of HrEmployeeSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrEmployeesRepository.Search(db.Connection, term, employeeType, includeInactive)
        End Using
    End Function

    Public Shared Function GetById(session As AppSession, id As Long) As HrEmployeeSummary
        Using db = DbSession.Open(session.ConnectionString)
            Return HrEmployeesRepository.GetById(db.Connection, id)
        End Using
    End Function

    Public Shared Function Save(session As AppSession, id As Long?, code As String, name As String,
                                employeeType As String, departmentId As Long?, sectionId As Long?,
                                mobile As String, phone As String, email As String, address As String,
                                hireDate As Date?, baseSalary As Decimal, commissionPercent As Decimal,
                                deliveryFee As Decimal, notes As String, isActive As Boolean) As Long
        session.RequirePermission("hr.manage")
        If String.IsNullOrWhiteSpace(name) Then Throw New BusinessException("اسم الموظف مطلوب.")
        If employeeType <> "employee" AndAlso employeeType <> "seller" AndAlso employeeType <> "delivery" Then
            Throw New BusinessException("نوع الموظف غير صالح.")
        End If
        If baseSalary < 0D Then Throw New BusinessException("الراتب الأساسي لا يمكن أن يكون سالبًا.")
        If commissionPercent < 0D OrElse commissionPercent > 100D Then
            Throw New BusinessException("نسبة العمولة يجب أن تكون بين 0 و 100.")
        End If
        If deliveryFee < 0D Then Throw New BusinessException("قيمة التوصيل لا يمكن أن تكون سالبة.")
        If employeeType <> "seller" Then commissionPercent = 0D
        If employeeType <> "delivery" Then deliveryFee = 0D

        Dim deptId As Long? = If(departmentId.HasValue AndAlso departmentId.Value > 0, departmentId, Nothing)
        Dim sectId As Long? = If(sectionId.HasValue AndAlso sectionId.Value > 0, sectionId, Nothing)

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            If sectId.HasValue Then
                Dim section = HrSectionsRepository.GetById(db.Connection, sectId.Value, db.Transaction)
                If section Is Nothing Then Throw New BusinessException("القسم غير موجود.")
                If deptId.HasValue AndAlso section.DepartmentId <> deptId.Value Then
                    Throw New BusinessException("القسم لا يتبع الإدارة المختارة.")
                End If
                deptId = section.DepartmentId
            End If
            If deptId.HasValue Then
                If HrDepartmentsRepository.GetById(db.Connection, deptId.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("الإدارة المختارة غير موجودة.")
                End If
            End If

            Dim finalCode As String = If(code, String.Empty).Trim()
            If finalCode.Length = 0 Then
                Dim prefix As String = If(employeeType = "seller", "SAL",
                                          If(employeeType = "delivery", "DLV", "EMP"))
                Dim attempt As Integer = 0
                Do
                    finalCode = HrEmployeesRepository.NextCode(db.Connection, db.Transaction, prefix, attempt)
                    attempt += 1
                Loop While HrEmployeesRepository.FindByCode(db.Connection, finalCode, db.Transaction) IsNot Nothing
            Else
                Dim duplicate = HrEmployeesRepository.FindByCode(db.Connection, finalCode, db.Transaction)
                If duplicate IsNot Nothing AndAlso (Not id.HasValue OrElse duplicate.Id <> id.Value) Then
                    Throw New BusinessException("كود الموظف مستخدم بالفعل.")
                End If
            End If

            Dim newId As Long
            If id.HasValue AndAlso id.Value > 0 Then
                If HrEmployeesRepository.GetById(db.Connection, id.Value, db.Transaction) Is Nothing Then
                    Throw New BusinessException("الموظف غير موجود.")
                End If
                HrEmployeesRepository.Update(db.Connection, db.Transaction, id.Value, finalCode, name.Trim(),
                                             employeeType, deptId, sectId, mobile, phone, email, address,
                                             hireDate, baseSalary, commissionPercent, deliveryFee, notes, isActive)
                newId = id.Value
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_employee_update", "hr_employee", newId.ToString(),
                                    Nothing, finalCode & " | " & name.Trim(), session.DeviceId)
            Else
                newId = HrEmployeesRepository.Insert(db.Connection, db.Transaction, Uuid7.NewId(), finalCode,
                                                     name.Trim(), employeeType, deptId, sectId, mobile, phone,
                                                     email, address, hireDate, baseSalary, commissionPercent,
                                                     deliveryFee, notes)
                AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                    session.Username, "hr_employee_create", "hr_employee", newId.ToString(),
                                    Nothing, finalCode & " | " & name.Trim(), session.DeviceId)
            End If
            db.Commit()
            Return newId
        End Using
    End Function

    Public Shared Sub SetActive(session As AppSession, id As Long, isActive As Boolean)
        session.RequirePermission("hr.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            HrEmployeesRepository.SetActive(db.Connection, db.Transaction, id, isActive)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_employee_toggle", "hr_employee", id.ToString(),
                                Nothing, If(isActive, "active", "inactive"), session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>Monthly payroll generation, line adjustments and posting.</summary>
Public NotInheritable Class HrPayrollService
    Private Sub New()
    End Sub

    Public Shared Function Runs(session As AppSession, term As String) As List(Of HrPayrollRunSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrPayrollRepository.Runs(db.Connection, term)
        End Using
    End Function

    Public Shared Function GetRun(session As AppSession, id As Long) As HrPayrollRunSummary
        Using db = DbSession.Open(session.ConnectionString)
            Return HrPayrollRepository.GetRunById(db.Connection, id)
        End Using
    End Function

    Public Shared Function Lines(session As AppSession, runId As Long) As List(Of HrPayrollLineSummary)
        Using db = DbSession.Open(session.ConnectionString)
            Return HrPayrollRepository.Lines(db.Connection, runId)
        End Using
    End Function

    Public Shared Function Generate(session As AppSession, periodYear As Integer, periodMonth As Integer,
                                    notes As String) As Long
        session.RequirePermission("hr.manage")
        If periodYear < 2000 OrElse periodYear > 2100 Then Throw New BusinessException("سنة المسير غير صالحة.")
        If periodMonth < 1 OrElse periodMonth > 12 Then Throw New BusinessException("شهر المسير غير صالح.")

        Dim fromDate As New Date(periodYear, periodMonth, 1)
        Dim toExclusive As Date = fromDate.AddMonths(1)

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            If HrPayrollRepository.FindRunByPeriod(db.Connection, periodYear, periodMonth, db.Transaction) IsNot Nothing Then
                Throw New BusinessException("يوجد مسير رواتب لهذا الشهر بالفعل.")
            End If

            Dim employees = HrEmployeesRepository.Search(db.Connection, String.Empty, String.Empty, False, 10000,
                                                          db.Transaction)
            If employees.Count = 0 Then Throw New BusinessException("لا يوجد موظفون مفعّلون لاحتساب رواتبهم.")

            Dim commissionById As New Dictionary(Of Long, Decimal)()
            For Each row In HrCommissionsRepository.ForRange(db.Connection, "seller", fromDate, toExclusive,
                                                             String.Empty, 10000, db.Transaction)
                commissionById(row.EmployeeId) = row.CommissionAmount
            Next
            For Each row In HrCommissionsRepository.ForRange(db.Connection, "delivery", fromDate, toExclusive,
                                                             String.Empty, 10000, db.Transaction)
                commissionById(row.EmployeeId) = row.CommissionAmount
            Next

            Dim runId As Long = HrPayrollRepository.InsertRun(db.Connection, db.Transaction, Uuid7.NewId(),
                                                               periodYear, periodMonth, notes, session.UserId)
            For Each employee In employees
                Dim commission As Decimal = 0D
                If commissionById.ContainsKey(employee.Id) Then commission = commissionById(employee.Id)
                Dim net As Decimal = employee.BaseSalary + commission
                HrPayrollRepository.InsertLine(db.Connection, db.Transaction, runId, employee.Id, employee.Code,
                                               employee.Name, employee.EmployeeType, employee.DepartmentName,
                                               employee.BaseSalary, commission, 0D, 0D, net, Nothing)
            Next
            HrPayrollRepository.UpdateRunTotals(db.Connection, db.Transaction, runId)

            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_payroll_generate", "hr_payroll_run", runId.ToString(),
                                Nothing, periodYear.ToString() & "-" & periodMonth.ToString("00"), session.DeviceId)
            db.Commit()
            Return runId
        End Using
    End Function

    Public Shared Function UpdateLine(session As AppSession, lineId As Long, deductions As Decimal,
                                      bonus As Decimal, notes As String) As Decimal
        session.RequirePermission("hr.manage")
        If deductions < 0D Then Throw New BusinessException("الخصومات لا يمكن أن تكون سالبة.")
        If bonus < 0D Then Throw New BusinessException("المكافأة لا يمكن أن تكون سالبة.")

        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim line = HrPayrollRepository.GetLineById(db.Connection, lineId, db.Transaction)
            If line Is Nothing Then Throw New BusinessException("بند الراتب غير موجود.")
            Dim run = HrPayrollRepository.GetRunById(db.Connection, line.RunId, db.Transaction)
            If run Is Nothing Then Throw New BusinessException("المسير غير موجود.")
            If run.Status <> "draft" Then Throw New BusinessException("لا يمكن تعديل مسير رواتب تم صرفه.")

            Dim net As Decimal = line.BaseSalary + line.CommissionAmount + bonus - deductions
            If net < 0D Then Throw New BusinessException("الخصومات أكبر من مستحقات الموظف.")
            HrPayrollRepository.UpdateLine(db.Connection, db.Transaction, lineId, deductions, bonus, net, notes)
            HrPayrollRepository.UpdateRunTotals(db.Connection, db.Transaction, line.RunId)

            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_payroll_update_line", "hr_payroll_line", lineId.ToString(),
                                line.NetAmount.ToString("0.00"), net.ToString("0.00"), session.DeviceId)
            db.Commit()
            Return net
        End Using
    End Function

    Public Shared Sub Post(session As AppSession, runId As Long)
        session.RequirePermission("hr.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim tx = db.Transaction
            Dim run = HrPayrollRepository.GetRunById(db.Connection, runId, tx)
            If run Is Nothing Then Throw New BusinessException("المسير غير موجود.")
            If run.Status <> "draft" Then Throw New BusinessException("تم صرف هذا المسير بالفعل.")
            If HrPayrollRepository.CountLines(db.Connection, runId, tx) = 0 Then
                Throw New BusinessException("المسير لا يحتوي على بنود.")
            End If

            HrPayrollRepository.UpdateRunTotals(db.Connection, tx, runId)
            run = HrPayrollRepository.GetRunById(db.Connection, runId, tx)

            Dim nowUtc As Date = Date.UtcNow
            Dim periodText As String = run.PeriodYear.ToString() & "-" & run.PeriodMonth.ToString("00")
            Dim totalNet As Decimal = Money.Round2(run.TotalNet)
            Dim eventId As String = Uuid7.NewId()

            If totalNet > 0D Then
                Dim salaryAccId As Long = AccountingRepository.GetOrCreateAccountId(
                    db.Connection, AccountingRepository.AccSalaries, "الرواتب والأجور", "expense", tx)
                Dim cashAccId As Long = AccountingRepository.GetAccountId(
                    db.Connection, AccountingRepository.AccCash, tx)

                Dim journalLines As New List(Of JournalLineInput) From {
                    New JournalLineInput With {
                        .AccountId = salaryAccId,
                        .Debit = totalNet,
                        .Description = "استحقاق وصرف مسير رواتب " & periodText
                    },
                    New JournalLineInput With {
                        .AccountId = cashAccId,
                        .Credit = totalNet,
                        .Description = "صرف نقدي مسير رواتب " & periodText
                    }
                }

                AccountingRepository.InsertEntry(
                    db.Connection, tx, Uuid7.NewId(), session.BranchId, nowUtc.Date,
                    "صرف مسير رواتب شهر " & periodText, "hr_payroll_run", runId, session.UserId, journalLines)

                Dim cashboxId As Long? = ShiftsRepository.GetDefaultCashbox(db.Connection, session.BranchId, tx)
                If cashboxId.HasValue Then
                    Dim openShift = ShiftsRepository.GetOpen(db.Connection, session.BranchId, session.UserId, tx)
                    Dim shiftId As Long? = If(openShift IsNot Nothing, CType(openShift.Id, Long?), Nothing)
                    ShiftsRepository.AddCashMove(
                        db.Connection, tx, Uuid7.NewId(), cashboxId.Value, shiftId,
                        "out", totalNet, "hr_payroll_run", runId, session.UserId)
                End If
            End If

            HrPayrollRepository.SetStatus(db.Connection, tx, runId, "paid", nowUtc, session.UserId)

            Dim payload As String = PostingHelper.Serialize(New With {
                .run_id = runId,
                .period = periodText,
                .total_net = totalNet,
                .paid_at = nowUtc
            })

            AuditRepository.Log(db.Connection, tx, eventId, session.BranchId, session.UserId,
                                session.Username, "hr_payroll_post", "hr_payroll_run", runId.ToString(),
                                "draft", payload, session.DeviceId)

            SyncStore.Enqueue(db.Connection, tx, eventId, session.BranchId, session.DeviceId,
                              "hr_payroll_run", runId.ToString(), "hr.payroll_posted", 1, payload, nowUtc)

            db.Commit()
        End Using
    End Sub

    Public Shared Sub DeleteRun(session As AppSession, runId As Long)
        session.RequirePermission("hr.manage")
        Using db = DbSession.Open(session.ConnectionString)
            db.BeginTransaction()
            Dim run = HrPayrollRepository.GetRunById(db.Connection, runId, db.Transaction)
            If run Is Nothing Then Throw New BusinessException("المسير غير موجود.")
            If run.Status <> "draft" Then Throw New BusinessException("لا يمكن حذف مسير رواتب تم صرفه.")

            HrPayrollRepository.DeleteRun(db.Connection, db.Transaction, runId)
            AuditRepository.Log(db.Connection, db.Transaction, Nothing, session.BranchId, session.UserId,
                                session.Username, "hr_payroll_delete", "hr_payroll_run", runId.ToString(),
                                run.Status, Nothing, session.DeviceId)
            db.Commit()
        End Using
    End Sub
End Class

''' <summary>Seller and delivery commissions from posted sale invoices.</summary>
Public NotInheritable Class HrCommissionService
    Private Sub New()
    End Sub

    Public Shared Function ForRange(session As AppSession, employeeType As String, fromDate As Date,
                                    toDate As Date, term As String) As List(Of HrCommissionSummary)
        If employeeType <> "seller" AndAlso employeeType <> "delivery" Then
            Throw New BusinessException("نوع العمولة غير صالح.")
        End If
        If toDate.Date < fromDate.Date Then Throw New BusinessException("تاريخ النهاية يجب ألا يسبق تاريخ البداية.")
        Using db = DbSession.Open(session.ConnectionString)
            Return HrCommissionsRepository.ForRange(db.Connection, employeeType, fromDate.Date,
                                                    toDate.Date.AddDays(1), term)
        End Using
    End Function
End Class
