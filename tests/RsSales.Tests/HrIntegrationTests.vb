Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Xunit

<Collection("Database")>
Public Class HrIntegrationTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function UtcAt(day As Date, hour As Integer) As Date
        Return New Date(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc)
    End Function

    Private Shared Function BuildSale(itemId As Long, qty As Decimal, price As Decimal,
                                      businessDate As Date, sellerId As Long?, deliveryId As Long?) As InvoiceRequest
        Return New InvoiceRequest With {
            .OccurredAt = UtcAt(businessDate, 9),
            .BusinessDate = businessDate,
            .PaymentMethodId = 1,
            .Paid = qty * price,
            .SellerId = sellerId,
            .DeliveryId = deliveryId,
            .Lines = New List(Of InvoiceLineRequest) From {
                New InvoiceLineRequest With {.ItemId = itemId, .Qty = qty, .UnitPrice = price}
            }
        }
    End Function

    <Fact>
    Public Sub DepartmentsSectionsAndEmployeesCrud()
        Dim session = _db.Session()
        Dim suffix As String = Guid.NewGuid().ToString("N").Substring(0, 8)

        Dim departmentName As String = "إدارة اختبار " & suffix
        Dim departmentId = HrDepartmentService.Save(session, Nothing, departmentName, "ملاحظات", True)
        Assert.True(departmentId > 0)

        Dim department = HrDepartmentService.Search(session, suffix, True).First(Function(d) d.Id = departmentId)
        Assert.Equal(departmentName, department.Name)
        Assert.True(department.IsActive)
        Assert.Equal("مفعّل", department.StatusName)

        Assert.Throws(Of BusinessException)(
            Sub() HrDepartmentService.Save(session, Nothing, departmentName, Nothing, True))

        Dim sectionName As String = "قسم اختبار " & suffix
        Dim sectionId = HrSectionService.Save(session, Nothing, departmentId, sectionName, Nothing, True)
        Assert.True(sectionId > 0)
        Dim section = HrSectionService.Search(session, suffix, True).First(Function(s) s.Id = sectionId)
        Assert.Equal(departmentId, section.DepartmentId)
        Assert.Equal(departmentName, section.DepartmentName)
        Assert.Equal(sectionName, section.Name)

        Dim employeeId = HrEmployeeService.Save(session, Nothing, String.Empty, "موظف اختبار " & suffix,
                                               "employee", departmentId, sectionId, "01000000001", Nothing,
                                               Nothing, Nothing, New Date(2024, 1, 15), 5000D, 0D, 0D,
                                               "ملاحظات", True)
        Dim employee = HrEmployeeService.GetById(session, employeeId)
        Assert.StartsWith("EMP-", employee.Code)
        Assert.Equal("موظف", employee.TypeName)
        Assert.Equal(departmentName, employee.DepartmentName)
        Assert.Equal(sectionName, employee.SectionName)
        Assert.Equal(5000D, employee.BaseSalary)

        Dim sellerId = HrEmployeeService.Save(session, Nothing, String.Empty, "بائع اختبار " & suffix,
                                              "seller", departmentId, sectionId, "01000000002", Nothing,
                                              Nothing, Nothing, Nothing, 3000D, 7.5D, 0D, Nothing, True)
        Dim seller = HrEmployeeService.GetById(session, sellerId)
        Assert.StartsWith("SAL-", seller.Code)
        Assert.Equal("بائع", seller.TypeName)
        Assert.Equal(7.5D, seller.CommissionPercent)

        Dim deliveryId = HrEmployeeService.Save(session, Nothing, String.Empty, "دليفري اختبار " & suffix,
                                                "delivery", departmentId, Nothing, "01000000003", Nothing,
                                                Nothing, Nothing, Nothing, 2000D, 0D, 12.5D, Nothing, True)
        Dim deliveryAgent = HrEmployeeService.GetById(session, deliveryId)
        Assert.StartsWith("DLV-", deliveryAgent.Code)
        Assert.Equal("دليفري", deliveryAgent.TypeName)
        Assert.Equal(12.5D, deliveryAgent.DeliveryFee)

        Assert.Throws(Of BusinessException)(
            Sub() HrEmployeeService.Save(session, Nothing, String.Empty, "بائع خاطئ", "seller",
                                         departmentId, sectionId, Nothing, Nothing, Nothing, Nothing,
                                         Nothing, 3000D, 101D, 0D, Nothing, True))
        Assert.Throws(Of BusinessException)(
            Sub() HrEmployeeService.Save(session, Nothing, String.Empty, "موظف خاطئ", "employee",
                                         departmentId, sectionId, Nothing, Nothing, Nothing, Nothing,
                                         Nothing, -1D, 0D, 0D, Nothing, True))

        HrEmployeeService.Save(session, sellerId, seller.Code, "بائع محدث " & suffix, "seller",
                               departmentId, sectionId, "01000000009", Nothing, Nothing, Nothing,
                               Nothing, 3500D, 8.25D, 0D, "محدث", True)
        seller = HrEmployeeService.GetById(session, sellerId)
        Assert.Equal("بائع محدث " & suffix, seller.Name)
        Assert.Equal(3500D, seller.BaseSalary)
        Assert.Equal(8.25D, seller.CommissionPercent)

        HrEmployeeService.SetActive(session, deliveryId, False)
        deliveryAgent = HrEmployeeService.GetById(session, deliveryId)
        Assert.False(deliveryAgent.IsActive)
        Assert.Equal("موقوف", deliveryAgent.StatusName)

        Dim restricted = _db.Session()
        restricted.Permissions.Remove("hr.manage")
        Assert.Throws(Of BusinessException)(
            Sub() HrDepartmentService.Save(restricted, Nothing, "إدارة بدون صلاحية " & suffix, Nothing, True))
    End Sub

    <Fact>
    Public Sub CommissionsAndPayrollUsePostedSaleInvoices()
        Dim session = _db.Session()
        Dim suffix As String = Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim departmentId = HrDepartmentService.Save(session, Nothing, "إدارة عمولات " & suffix, Nothing, True)

        Dim sellerId = HrEmployeeService.Save(session, Nothing, String.Empty, "بائع عمولة " & suffix,
                                              "seller", departmentId, Nothing, Nothing, Nothing, Nothing,
                                              Nothing, Nothing, 4000D, 10D, 0D, Nothing, True)
        Dim deliveryId = HrEmployeeService.Save(session, Nothing, String.Empty, "دليفري عمولة " & suffix,
                                                "delivery", departmentId, Nothing, Nothing, Nothing, Nothing,
                                                Nothing, Nothing, 2500D, 0D, 15D, Nothing, True)

        Dim day = _db.NextTestDate()
        Dim code As String = "HR-" & suffix
        Dim itemId = _db.CreateItemWithStock(code, 114D, 50D, 100D, occurredAt:=UtcAt(day, 8))

        Assert.Throws(Of BusinessException)(
            Sub() SalesService.PostSale(session, BuildSale(itemId, 1D, 114D, day, deliveryId, Nothing)))
        Assert.Throws(Of BusinessException)(
            Sub() SalesService.PostSale(session, BuildSale(itemId, 1D, 114D, day, 999999999L, Nothing)))

        Dim sale = SalesService.PostSale(session, BuildSale(itemId, 2D, 114D, day, sellerId, deliveryId))
        Assert.True(sale.InvoiceId > 0)

        Dim sellers = HrCommissionService.ForRange(session, "seller", day, day, suffix)
        Dim sellerRow = sellers.First(Function(r) r.EmployeeId = sellerId)
        Assert.Equal(1, sellerRow.InvoiceCount)
        Assert.Equal(228D, sellerRow.SalesTotal)
        Assert.Equal(22.8D, sellerRow.CommissionAmount)
        Assert.Equal("10%", sellerRow.RateText)

        Dim deliveries = HrCommissionService.ForRange(session, "delivery", day, day, suffix)
        Dim deliveryRow = deliveries.First(Function(r) r.EmployeeId = deliveryId)
        Assert.Equal(1, deliveryRow.InvoiceCount)
        Assert.Equal(15D, deliveryRow.CommissionAmount)
        Assert.Contains("15", deliveryRow.RateText)

        Dim runId = HrPayrollService.Generate(session, day.Year, day.Month, "مسير اختبار " & suffix)
        Assert.True(runId > 0)
        Assert.Throws(Of BusinessException)(
            Sub() HrPayrollService.Generate(session, day.Year, day.Month, "مكرر"))

        Dim lines = HrPayrollService.Lines(session, runId)
        Dim sellerLine = lines.First(Function(l) l.EmployeeId = sellerId)
        Dim deliveryLine = lines.First(Function(l) l.EmployeeId = deliveryId)
        Assert.Equal(22.8D, sellerLine.CommissionAmount)
        Assert.Equal(4022.8D, sellerLine.NetAmount)
        Assert.Equal(15D, deliveryLine.CommissionAmount)
        Assert.Equal(2515D, deliveryLine.NetAmount)

        Dim netAfterEdit = HrPayrollService.UpdateLine(session, sellerLine.Id, 10D, 50D, "تعديل اختبار")
        Assert.Equal(4062.8D, netAfterEdit)
        sellerLine = HrPayrollService.Lines(session, runId).First(Function(l) l.EmployeeId = sellerId)
        Assert.Equal(10D, sellerLine.Deductions)
        Assert.Equal(50D, sellerLine.Bonus)
        Assert.Equal(4062.8D, sellerLine.NetAmount)

        Dim run = HrPayrollService.GetRun(session, runId)
        Dim expectedNet As Decimal = HrPayrollService.Lines(session, runId).Sum(Function(l) l.NetAmount)
        Assert.Equal(expectedNet, run.TotalNet)
        Assert.Equal("مسودة", run.StatusName)

        HrPayrollService.Post(session, runId)
        run = HrPayrollService.GetRun(session, runId)
        Assert.Equal("paid", run.Status)
        Assert.Equal("مصروف", run.StatusName)
        Assert.True(run.PaidAt.HasValue)

        Assert.Throws(Of BusinessException)(
            Sub() HrPayrollService.UpdateLine(session, sellerLine.Id, 1D, 2D, Nothing))
        Assert.Throws(Of BusinessException)(Sub() HrPayrollService.Post(session, runId))
        Assert.Throws(Of BusinessException)(Sub() HrPayrollService.DeleteRun(session, runId))

        Dim restricted = _db.Session()
        restricted.Permissions.Remove("hr.manage")
        Assert.Throws(Of BusinessException)(
            Sub() HrPayrollService.Generate(restricted, day.Year, day.Month, Nothing))
    End Sub
End Class
