Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms
Imports MySqlConnector

''' <summary>
''' Real add/edit/delete screen for the small definition tables of the
''' «البيانات الأساسية» tab. Everything is read from and written to MySQL
''' with permission checks, audit logging and sync outbox events.
''' </summary>
Public Class DefinitionForm
    Inherits SectionFormBase

    Private ReadOnly _spec As DefinitionSpec

    Public Sub New(session As AppSession, spec As DefinitionSpec)
        MyBase.New(session, spec.Title, spec.Subtitle, Color.FromArgb(37, 99, 235), New Size(1040, 640))
        _spec = spec
        BuildScreen()
        Reload()
    End Sub

    Private Sub BuildScreen()
        AddSearch("بحث بالكود أو الاسم", AddressOf Reload)

        Dim btnNew As Button = UiTheme.PrimaryButton("جديد", 110)
        AddHandler btnNew.Click, Sub(s, e) EditRecord(Nothing)
        ToolBar.Controls.Add(btnNew)

        Dim btnEdit As Button = UiTheme.MakeButton("تعديل", 90)
        AddHandler btnEdit.Click, Sub(s, e) EditSelected()
        ToolBar.Controls.Add(btnEdit)

        Dim btnDelete As Button = UiTheme.DangerButton("حذف", 90)
        AddHandler btnDelete.Click, Sub(s, e) DeleteSelected()
        ToolBar.Controls.Add(btnDelete)

        AddRefresh(AddressOf Reload)

        For Each field In _spec.GridFields
            UiTheme.AddTextColumn(Grid, field.Column, field.Label, field.Width)
        Next

        AddHandler Grid.CellDoubleClick, Sub(s, e)
                                             If e.RowIndex >= 0 Then EditSelected()
                                         End Sub
        AddHandler Grid.CellFormatting, AddressOf FormatCell
    End Sub

    Protected Overrides Sub LoadData()
        Using db = DbSession.Open(Session.ConnectionString)
            Grid.DataSource = DefinitionsRepository.Load(db.Connection, _spec, SearchText)
        End Using
        SetFooter($"{_spec.Title}: {Grid.Rows.Count} سجل — تُحفظ التغييرات في قاعدة البيانات مباشرة")
    End Sub

    Private Sub FormatCell(sender As Object, e As DataGridViewCellFormattingEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        Dim field = _spec.Fields.FirstOrDefault(Function(f) f.Column = Grid.Columns(e.ColumnIndex).Name)
        If field Is Nothing OrElse field.Kind <> DefinitionFieldKind.CheckField Then Return
        If e.Value Is Nothing OrElse e.Value Is DBNull.Value Then Return
        e.Value = If(Convert.ToInt64(e.Value) = 0, "لا", "نعم")
        e.FormattingApplied = True
    End Sub

    Private Function CurrentRow() As DataGridViewRow
        If Grid.CurrentRow Is Nothing Then Return Nothing
        Return Grid.CurrentRow
    End Function

    Private Sub EnsureCanModify()
        Dim perm As String = If(String.IsNullOrWhiteSpace(_spec.RequiredPermission), "items.manage", _spec.RequiredPermission)
        If Session.HasPermission(perm) OrElse Session.HasPermission("settings.manage") OrElse Session.HasPermission("items.manage") Then
            Return
        End If
        Session.RequirePermission(perm)
    End Sub

    Private Sub EditSelected()
        Dim row = CurrentRow()
        If row Is Nothing Then
            UiTheme.Warn("اختر سجلًا أولًا.")
            Return
        End If
        EditRecord(row)
    End Sub

    Private Sub EditRecord(row As DataGridViewRow)
        Try
            EnsureCanModify()
        Catch ex As Exception
            UiTheme.Handle(ex)
            Return
        End Try

        Dim record As DataRowView = Nothing
        If row IsNot Nothing Then record = TryCast(row.DataBoundItem, DataRowView)

        Using dialog As New DefinitionEditorDialog(_spec, Session, record)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using db = DbSession.Open(Session.ConnectionString)
                    db.BeginTransaction()
                    Dim tx = db.Transaction
                    Dim eventId As String = Uuid7.NewId()
                    Dim payloadJson As String = JsonSerializer.Serialize(dialog.Values)

                    If record Is Nothing Then
                        Dim newId As Long = DefinitionsRepository.Insert(db.Connection, _spec, dialog.Values, tx)
                        Dim idStr As String = newId.ToString(CultureInfo.InvariantCulture)

                        AuditRepository.Log(db.Connection, tx, eventId, Session.BranchId, Session.UserId,
                                            Session.Username, _spec.Table & "_create", _spec.Table,
                                            idStr, Nothing, payloadJson, Session.DeviceId)
                        SyncStore.AppendChange(db.Connection, tx, _spec.Table, idStr, "upsert", payloadJson, Session.BranchId)
                        SyncStore.Enqueue(db.Connection, tx, eventId, Session.BranchId, Session.DeviceId,
                                          _spec.Table, idStr, _spec.Table & ".upserted", 1, payloadJson, Date.UtcNow)
                    Else
                        Dim id As Long = Convert.ToInt64(record("id"))
                        Dim idStr As String = id.ToString(CultureInfo.InvariantCulture)
                        DefinitionsRepository.Update(db.Connection, _spec, id, dialog.Values, tx)

                        AuditRepository.Log(db.Connection, tx, eventId, Session.BranchId, Session.UserId,
                                            Session.Username, _spec.Table & "_update", _spec.Table,
                                            idStr, Nothing, payloadJson, Session.DeviceId)
                        SyncStore.AppendChange(db.Connection, tx, _spec.Table, idStr, "upsert", payloadJson, Session.BranchId)
                        SyncStore.Enqueue(db.Connection, tx, eventId, Session.BranchId, Session.DeviceId,
                                          _spec.Table, idStr, _spec.Table & ".upserted", 1, payloadJson, Date.UtcNow)
                    End If

                    db.Commit()
                End Using
            Catch ex As MySqlException
                If ex.Number = 1062 Then
                    UiTheme.Warn("هذه القيمة مسجّلة بالفعل، اختر كودًا أو اسمًا مختلفًا.")
                ElseIf DefinitionsRepository.IsForeignKeyViolation(ex) Then
                    UiTheme.Warn("القيمة المرتبطة غير موجودة، اختر قيمة صحيحة من القائمة.")
                Else
                    UiTheme.ShowError("تعذّر الحفظ: " & ex.Message)
                End If
                Return
            Catch ex As Exception
                UiTheme.Handle(ex)
                Return
            End Try
            Reload()
        End Using
    End Sub

    Private Sub DeleteSelected()
        Try
            EnsureCanModify()
        Catch ex As Exception
            UiTheme.Handle(ex)
            Return
        End Try

        Dim row = CurrentRow()
        If row Is Nothing Then
            UiTheme.Warn("اختر سجلًا أولًا.")
            Return
        End If
        Dim record = TryCast(row.DataBoundItem, DataRowView)
        If record Is Nothing Then Return
        Dim name As String = Convert.ToString(row.Cells(0).Value)
        If Not UiTheme.Confirm($"حذف «{name}» من {_spec.Title}؟") Then Return

        Try
            Using db = DbSession.Open(Session.ConnectionString)
                db.BeginTransaction()
                Dim tx = db.Transaction
                Dim id As Long = Convert.ToInt64(record("id"))
                Dim idStr As String = id.ToString(CultureInfo.InvariantCulture)
                Dim eventId As String = Uuid7.NewId()
                Dim payloadJson As String = JsonSerializer.Serialize(New With {.id = id, .name = name})

                DefinitionsRepository.Delete(db.Connection, _spec, id, tx)

                AuditRepository.Log(db.Connection, tx, eventId, Session.BranchId, Session.UserId,
                                    Session.Username, _spec.Table & "_delete", _spec.Table,
                                    idStr, payloadJson, Nothing, Session.DeviceId)
                SyncStore.AppendChange(db.Connection, tx, _spec.Table, idStr, "delete", payloadJson, Session.BranchId)
                SyncStore.Enqueue(db.Connection, tx, eventId, Session.BranchId, Session.DeviceId,
                                  _spec.Table, idStr, _spec.Table & ".deleted", 1, payloadJson, Date.UtcNow)

                db.Commit()
            End Using
        Catch ex As MySqlException
            If DefinitionsRepository.IsForeignKeyViolation(ex) Then
                UiTheme.Warn("لا يمكن الحذف: السجل مستخدم في بيانات أخرى (أصناف أو حركات).")
            Else
                UiTheme.ShowError("تعذّر الحذف: " & ex.Message)
            End If
            Return
        Catch ex As Exception
            UiTheme.Handle(ex)
            Return
        End Try
        Reload()
    End Sub
End Class
