Imports System.Collections.Generic

''' <summary>Read-only invoice queries shared by the sales and purchase return screens.</summary>
Public NotInheritable Class InvoiceQueryService
    Private Sub New()
    End Sub

    Public Shared Function GetReturnableLines(session As AppSession, invoiceId As Long,
                                              expectedType As String) As List(Of ReturnableLine)
        Using db = DbSession.Open(session.ConnectionString)
            Dim header = InvoicesRepository.GetById(db.Connection, invoiceId)
            If header Is Nothing Then Throw New BusinessException("الفاتورة غير موجودة.")
            If header.BranchId <> session.BranchId Then Throw New BusinessException("الفاتورة تخص فرعًا آخر.")
            If header.Status <> "posted" Then Throw New BusinessException("الفاتورة ملغاة أو غير مرحّلة.")
            If Not String.IsNullOrWhiteSpace(expectedType) AndAlso header.InvoiceType <> expectedType Then
                Throw New BusinessException("نوع الفاتورة لا يطابق شاشة المرتجعات المطلوبة.")
            End If
            Return InvoicesRepository.GetReturnableLines(db.Connection, invoiceId)
        End Using
    End Function
End Class
