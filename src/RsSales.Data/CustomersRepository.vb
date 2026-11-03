Imports System
Imports System.Collections.Generic
Imports System.Data
Imports Dapper

Public NotInheritable Class CustomersRepository
    Private Sub New()
    End Sub

    Public Shared Function Search(conn As IDbConnection, term As String, customerType As String,
                                  Optional limit As Integer = 100,
                                  Optional tx As IDbTransaction = Nothing) As List(Of CustomerSummary)
        Dim cleanTerm As String = If(term, String.Empty).Trim()
        Dim likeTerm As String = "%" & cleanTerm & "%"
        Dim sql As String =
            "SELECT c.id, c.uuid, COALESCE(c.code, CONCAT('C', LPAD(c.id, 5, '0'))) AS code, " &
            "       c.name, c.type, c.mobile, c.phone, c.tax_no, c.credit_limit, " &
            "       c.group_id, g.name AS group_name, COALESCE(g.discount_percent, 0) AS group_discount_percent, " &
            "       c.opening_balance + COALESCE((SELECT SUM(v.due) FROM invoices v " &
            "            WHERE v.customer_id=c.id AND v.status='posted'),0) AS balance " &
            "FROM customers c LEFT JOIN customer_groups g ON g.id=c.group_id " &
            "WHERE c.is_active=1 "
        If Not String.IsNullOrWhiteSpace(customerType) Then
            sql &= " AND (c.type=@type OR c.type='both') "
        End If
        sql &= " AND (@term='' " &
               "      OR CAST(c.id AS CHAR)=@term " &
               "      OR COALESCE(c.code,'') LIKE @like " &
               "      OR CONCAT('C', LPAD(c.id, 5, '0')) LIKE @like " &
               "      OR c.name LIKE @like " &
               "      OR COALESCE(c.mobile,'') LIKE @like " &
               "      OR COALESCE(c.phone,'') LIKE @like) " &
               "ORDER BY c.name LIMIT @limit"

        Return conn.Query(Of CustomerSummary)(sql,
            New With {.term = cleanTerm, .like = likeTerm, .type = customerType, .limit = limit},
            tx).AsList()
    End Function

    Public Shared Function GetById(conn As IDbConnection, id As Long, Optional tx As IDbTransaction = Nothing) As CustomerRecord
        Return conn.QueryFirstOrDefault(Of CustomerRecord)(
            "SELECT id, uuid, code, name, type, group_id, mobile, phone, email, address, tax_no, credit_limit, " &
            "opening_balance, is_active FROM customers WHERE id=@id",
            New With {.id = id}, tx)
    End Function

    Public Shared Function Insert(conn As IDbConnection, tx As IDbTransaction, uuid As String, code As String,
                                  name As String, customerType As String, mobile As String, phone As String,
                                  email As String, address As String, taxNo As String,
                                  creditLimit As Decimal, openingBalance As Decimal,
                                  Optional groupId As Long? = Nothing) As Long
        conn.Execute(
            "INSERT INTO customers(uuid, code, name, type, group_id, mobile, phone, email, address, tax_no, " &
            "credit_limit, opening_balance) VALUES(@uuid, @code, @name, @type, @groupId, @mobile, @phone, @email, " &
            "@address, @taxNo, @credit, @opening)",
            New With {
                .uuid = uuid, .code = code, .name = name, .type = customerType, .groupId = groupId,
                .mobile = mobile, .phone = phone, .email = email, .address = address, .taxNo = taxNo,
                .credit = creditLimit, .opening = openingBalance
            }, tx)
        Dim newId As Long = conn.ExecuteScalar(Of Long)("SELECT LAST_INSERT_ID()", Nothing, tx)
        If String.IsNullOrWhiteSpace(code) Then
            Dim autoCode As String = "C" & newId.ToString("00000")
            conn.Execute("UPDATE customers SET code=@c WHERE id=@id AND (code IS NULL OR code='')",
                         New With {.c = autoCode, .id = newId}, tx)
        End If
        Return newId
    End Function

    Public Shared Sub Update(conn As IDbConnection, tx As IDbTransaction, id As Long, name As String,
                             customerType As String, mobile As String, phone As String, email As String,
                             address As String, taxNo As String, creditLimit As Decimal, isActive As Boolean,
                             Optional groupId As Long? = Nothing)
        conn.Execute(
            "UPDATE customers SET name=@name, type=@type, group_id=@groupId, mobile=@mobile, phone=@phone, " &
            "email=@email, address=@address, tax_no=@taxNo, credit_limit=@credit, is_active=@active, " &
            "row_version=row_version+1 WHERE id=@id",
            New With {
                .id = id, .name = name, .type = customerType, .groupId = groupId, .mobile = mobile,
                .phone = phone, .email = email, .address = address, .taxNo = taxNo,
                .credit = creditLimit, .active = isActive
            }, tx)
    End Sub

    Public Shared Function Balance(conn As IDbConnection, customerId As Long,
                                   Optional tx As IDbTransaction = Nothing) As Decimal
        Dim value As Object = conn.ExecuteScalar(
            "SELECT c.opening_balance + COALESCE((SELECT SUM(v.due) FROM invoices v " &
            "WHERE v.customer_id=c.id AND v.status='posted'),0) FROM customers c WHERE c.id=@id",
            New With {.id = customerId}, tx)
        If value Is Nothing OrElse value Is DBNull.Value Then Return 0D
        Return Convert.ToDecimal(value)
    End Function
End Class
