Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Dynamic add/edit form for one definition row, built from its DefinitionSpec.</summary>
Public Class DefinitionEditorDialog
    Inherits Form

    Private ReadOnly _spec As DefinitionSpec
    Private ReadOnly _session As AppSession
    Private ReadOnly _inputs As New Dictionary(Of String, Control)()
    Private ReadOnly _row As DataRowView
    Private ReadOnly _result As New Dictionary(Of String, Object)()

    Public ReadOnly Property Values As Dictionary(Of String, Object)
        Get
            Return _result
        End Get
    End Property

    Public Sub New(spec As DefinitionSpec, session As AppSession, row As DataRowView)
        _spec = spec
        _session = session
        _row = row

        UiTheme.Apply(Me)
        Text = If(row Is Nothing, "إضافة - " & spec.Title, "تعديل - " & spec.Title)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterParent

        Dim fields = spec.EditableFields
        ClientSize = New Size(560, 92 + fields.Count * 44)
        MinimumSize = New Size(500, 260)

        Dim host As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .Padding = New Padding(14, 12, 14, 8),
            .BackColor = Color.White
        }
        host.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))
        host.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        For Each field In fields
            host.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))

            Dim caption As New Label With {
                .Text = field.Label & If(field.Required, " *", String.Empty),
                .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleRight,
                .Font = UiTheme.Font(9.5F, FontStyle.Bold),
                .ForeColor = UiTheme.Ink
            }
            Dim input = BuildInput(field)
            input.Dock = DockStyle.Fill
            input.Margin = New Padding(4, 7, 4, 7)

            host.Controls.Add(caption, 0, host.RowStyles.Count - 1)
            host.Controls.Add(input, 1, host.RowStyles.Count - 1)
            _inputs(field.Column) = input
        Next

        Dim bar As New FlowLayoutPanel With {
            .Dock = DockStyle.Bottom,
            .Height = 56,
            .FlowDirection = FlowDirection.LeftToRight,
            .Padding = New Padding(14, 8, 14, 8),
            .BackColor = UiTheme.Surface
        }
        Dim btnSave As Button = UiTheme.PrimaryButton("حفظ", 130)
        AddHandler btnSave.Click, AddressOf SaveClick
        Dim btnCancel As Button = UiTheme.MakeButton("إلغاء", 100)
        AddHandler btnCancel.Click, Sub(s, e) DialogResult = DialogResult.Cancel
        bar.Controls.Add(btnSave)
        bar.Controls.Add(btnCancel)

        Controls.Add(host)
        Controls.Add(bar)
        AcceptButton = btnSave
        CancelButton = btnCancel
    End Sub

    Private Function BuildInput(field As DefinitionField) As Control
        Select Case field.Kind
            Case DefinitionFieldKind.CheckField
                Dim chk As New CheckBox With {
                    .Text = String.Empty,
                    .AutoSize = False,
                    .Height = 28,
                    .TextAlign = ContentAlignment.MiddleRight
                }
                If _row IsNot Nothing Then chk.Checked = ToBool(_row(field.Column))
                Return chk

            Case DefinitionFieldKind.ComboField
                Dim combo As New ComboBox With {
                    .DropDownStyle = ComboBoxStyle.DropDownList,
                    .DisplayMember = "Name"
                }
                Using db = DbSession.Open(_session.ConnectionString)
                    Dim options = DefinitionsRepository.Lookup(db.Connection, field)
                    If Not field.Required Then options.Insert(0, New LookupRow With {.Name = "(بدون)", .Value = Nothing})
                    combo.DataSource = options
                End Using
                If _row IsNot Nothing Then
                    Dim currentValue = _row(field.Column)
                    For Each lookup As LookupRow In combo.Items
                        If lookup.Value Is Nothing AndAlso (currentValue Is Nothing OrElse currentValue Is DBNull.Value) Then
                            combo.SelectedItem = lookup
                            Exit For
                        End If
                        If lookup.Value IsNot Nothing AndAlso currentValue IsNot Nothing AndAlso Not currentValue Is DBNull.Value Then
                            If Convert.ToInt64(lookup.Value) = Convert.ToInt64(currentValue) Then
                                combo.SelectedItem = lookup
                                Exit For
                            End If
                        End If
                    Next
                End If
                Return combo

            Case DefinitionFieldKind.NumberField
                Dim box As New TextBox With {.TextAlign = HorizontalAlignment.Right}
                If _row IsNot Nothing AndAlso Not _row(field.Column) Is DBNull.Value Then
                    box.Text = Convert.ToDecimal(_row(field.Column)).ToString("0.####")
                Else
                    box.Text = "0"
                End If
                Return box

            Case Else
                Dim box As New TextBox With {
                    .TextAlign = HorizontalAlignment.Right,
                    .MaxLength = field.MaxLength
                }
                If _row IsNot Nothing AndAlso Not _row(field.Column) Is DBNull.Value Then
                    box.Text = Convert.ToString(_row(field.Column))
                End If
                Return box
        End Select
    End Function

    Private Sub SaveClick(sender As Object, e As EventArgs)
        _result.Clear()
        For Each field In _spec.EditableFields
            Dim input = _inputs(field.Column)
            Select Case field.Kind
                Case DefinitionFieldKind.CheckField
                    _result(field.Column) = If(DirectCast(input, CheckBox).Checked, 1, 0)

                Case DefinitionFieldKind.ComboField
                    Dim combo = DirectCast(input, ComboBox)
                    Dim selected = TryCast(combo.SelectedItem, LookupRow)
                    If selected Is Nothing OrElse selected.Value Is Nothing Then
                        If field.Required Then
                            UiTheme.Warn("اختر قيمة لـ«" & field.Label & "».")
                            Return
                        End If
                        _result(field.Column) = DBNull.Value
                    Else
                        _result(field.Column) = selected.Value
                    End If

                Case DefinitionFieldKind.NumberField
                    Dim value As Decimal
                    If Not Decimal.TryParse(input.Text.Trim(), value) Then
                        UiTheme.Warn("قيمة «" & field.Label & "» يجب أن تكون رقمًا.")
                        input.Focus()
                        Return
                    End If
                    _result(field.Column) = value

                Case Else
                    Dim text As String = input.Text.Trim()
                    If field.Required AndAlso text.Length = 0 Then
                        UiTheme.Warn("«" & field.Label & "» مطلوب.")
                        input.Focus()
                        Return
                    End If
                    _result(field.Column) = If(text.Length = 0, CObj(DBNull.Value), text)
            End Select
        Next
        DialogResult = DialogResult.OK
    End Sub

    Private Shared Function ToBool(value As Object) As Boolean
        If value Is Nothing OrElse value Is DBNull.Value Then Return False
        Return Convert.ToInt64(value) <> 0
    End Function
End Class
