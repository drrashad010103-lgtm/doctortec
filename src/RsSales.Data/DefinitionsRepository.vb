Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Text
Imports Dapper
Imports MySqlConnector

''' <summary>Input type used by the generic definition editor.</summary>
Public Enum DefinitionFieldKind
    TextField
    NumberField
    CheckField
    ComboField
End Enum

Public Class DefinitionField
    Public Property Column As String
    Public Property Label As String
    Public Property Kind As DefinitionFieldKind = DefinitionFieldKind.TextField
    Public Property Width As Integer = 170
    Public Property Required As Boolean
    Public Property MaxLength As Integer = 128
    Public Property InGrid As Boolean = True
    ''' <summary>Optional table for combo fields (id + display text).</summary>
    Public Property LookupTable As String
    Public Property LookupTextColumn As String = "name"
    Public Property LookupFilter As String
    ''' <summary>Fixed choices for a combo field, e.g. debit/credit.</summary>
    Public Property Options As New List(Of KeyValuePair(Of String, String))()
End Class

''' <summary>Everything the generic screen needs to list and edit one definition table.</summary>
Public Class DefinitionSpec
    Public Property Table As String
    Public Property Title As String
    Public Property Subtitle As String
    Public Property Fields As New List(Of DefinitionField)()
    ''' <summary>Extra WHERE clause applied to every read (and to the list).</summary>
    Public Property Filter As String
    ''' <summary>Values forced on insert, e.g. nature='expense'.</summary>
    Public Property Defaults As New Dictionary(Of String, Object)()
    Public Property OrderBy As String = "id"
    Public Property SearchColumns As String() = New String() {}
    ''' <summary>Permission required to add, update or delete records in this table.</summary>
    Public Property RequiredPermission As String = "items.manage"

    Public ReadOnly Property GridFields As List(Of DefinitionField)
        Get
            Return Fields.Where(Function(f) f.InGrid).ToList()
        End Get
    End Property

    Public ReadOnly Property EditableFields As List(Of DefinitionField)
        Get
            Return Fields.Where(Function(f) f.Kind <> DefinitionFieldKind.ComboField OrElse
                                        f.LookupTable IsNot Nothing OrElse f.Options.Count > 0).ToList()
        End Get
    End Property
End Class

Public Class LookupRow
    Public Property Id As Long
    Public Property Name As String
    ''' <summary>Value stored in the row: the id for table lookups, the literal for fixed choices.</summary>
    Public Property Value As Object

    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

''' <summary>
''' Generic list/insert/update/delete for the small definition tables of the
''' basic-data tab. Table and column names always come from <see cref="DefinitionSpec"/>
''' constants in code, never from user input; values are always parameterised.
''' </summary>
Public NotInheritable Class DefinitionsRepository
    Private Sub New()
    End Sub

    Public Shared Function Load(conn As IDbConnection, spec As DefinitionSpec, Optional search As String = "") As DataTable
        Dim columns As New List(Of String) From {"id"}
        columns.AddRange(spec.Fields.Select(Function(f) f.Column))
        Dim sql As New StringBuilder("SELECT " & String.Join(", ", columns) & " FROM " & spec.Table)

        Dim conditions As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(spec.Filter) Then conditions.Add("(" & spec.Filter & ")")
        If Not String.IsNullOrWhiteSpace(search) AndAlso spec.SearchColumns.Length > 0 Then
            Dim likes As String() = spec.SearchColumns.Select(Function(c) c & " LIKE @search").ToArray()
            conditions.Add("(" & String.Join(" OR ", likes) & ")")
        End If
        If conditions.Count > 0 Then sql.Append(" WHERE " & String.Join(" AND ", conditions))
        sql.Append(" ORDER BY " & spec.OrderBy)

        Dim table As New DataTable()
        Using cmd As New MySqlCommand(sql.ToString(), DirectCast(conn, MySqlConnection))
            If Not String.IsNullOrWhiteSpace(search) Then cmd.Parameters.AddWithValue("@search", "%" & search & "%")
            Using reader = cmd.ExecuteReader()
                table.Load(reader)
            End Using
        End Using
        Return table
    End Function

    Public Shared Function Lookup(conn As IDbConnection, field As DefinitionField) As List(Of LookupRow)
        If String.IsNullOrWhiteSpace(field.LookupTable) Then
            Return field.Options.Select(Function(o) New LookupRow With {.Name = o.Value, .Value = o.Key}).ToList()
        End If
        Dim sql As String = "SELECT id, " & field.LookupTextColumn & " AS name FROM " & field.LookupTable
        If Not String.IsNullOrWhiteSpace(field.LookupFilter) Then sql &= " WHERE " & field.LookupFilter
        sql &= " ORDER BY " & field.LookupTextColumn
        Dim rows = conn.Query(Of LookupRow)(sql).AsList()
        For Each row In rows
            row.Value = row.Id
        Next
        Return rows
    End Function

    Public Shared Function Insert(conn As IDbConnection, spec As DefinitionSpec,
                                  values As Dictionary(Of String, Object),
                                  Optional tx As IDbTransaction = Nothing) As Long
        Dim merged As New Dictionary(Of String, Object)(values)
        For Each pair In spec.Defaults
            merged(pair.Key) = pair.Value
        Next
        Dim columns = merged.Keys.ToList()
        Dim sql As String = "INSERT INTO " & spec.Table & " (" & String.Join(", ", columns) & ") VALUES (" &
                            String.Join(", ", columns.Select(Function(c) "@" & c)) & ")"
        Using cmd As New MySqlCommand(sql, DirectCast(conn, MySqlConnection), TryCast(tx, MySqlTransaction))
            For Each column In columns
                cmd.Parameters.AddWithValue("@" & column, If(merged(column), CObj(DBNull.Value)))
            Next
            cmd.ExecuteNonQuery()
        End Using
        Return Convert.ToInt64(conn.ExecuteScalar("SELECT LAST_INSERT_ID()", Nothing, tx))
    End Function

    Public Shared Sub Update(conn As IDbConnection, spec As DefinitionSpec, id As Long,
                             values As Dictionary(Of String, Object),
                             Optional tx As IDbTransaction = Nothing)
        Dim columns = values.Keys.ToList()
        If columns.Count = 0 Then Return
        Dim sql As String = "UPDATE " & spec.Table & " SET " &
                            String.Join(", ", columns.Select(Function(c) c & "=@" & c)) & " WHERE id=@id"
        Using cmd As New MySqlCommand(sql, DirectCast(conn, MySqlConnection), TryCast(tx, MySqlTransaction))
            For Each column In columns
                cmd.Parameters.AddWithValue("@" & column, If(values(column), CObj(DBNull.Value)))
            Next
            cmd.Parameters.AddWithValue("@id", id)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Public Shared Sub Delete(conn As IDbConnection, spec As DefinitionSpec, id As Long,
                             Optional tx As IDbTransaction = Nothing)
        Using cmd As New MySqlCommand("DELETE FROM " & spec.Table & " WHERE id=@id",
                                      DirectCast(conn, MySqlConnection),
                                      TryCast(tx, MySqlTransaction))
            cmd.Parameters.AddWithValue("@id", id)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>True when another row still references this record (FK RESTRICT).</summary>
    Public Shared Function IsForeignKeyViolation(ex As MySqlException) As Boolean
        Return ex IsNot Nothing AndAlso (ex.Number = 1451 OrElse ex.Number = 1452)
    End Function

End Class
