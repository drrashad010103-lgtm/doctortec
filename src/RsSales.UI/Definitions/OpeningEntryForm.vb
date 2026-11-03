Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Globalization
Imports System.Text.Json
Imports System.Windows.Forms

Public Class OpeningEntryForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly txtEntryNo As New TextBox()
    Private ReadOnly dtpDate As New DateTimePicker()
    Private ReadOnly txtDescription As New TextBox()
    Private ReadOnly cboStatus As New ComboBox()
    Private ReadOnly grid As New DataGridView()
    Private ReadOnly lblTotalDebit As New Label()
    Private ReadOnly lblTotalCredit As New Label()
    Private _suppressRecalc As Boolean = False

    Public Sub New(session As AppSession)
        _session = session
        InitializeUi()
        LoadAccountsFromDatabase()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = "القيود الافتتاحية — صيدلية الشفاء"
        FormBorderStyle = FormBorderStyle.None
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(1050, 720)
        BackColor = Color.FromArgb(245, 246, 250)
        DoubleBuffered = True
        RightToLeft = RightToLeft.Yes
        RightToLeftLayout = True

        ' شريط العنوان العلوي
        Dim titleBar As New Panel With {.Dock = DockStyle.Top, .Height = 44, .BackColor = Color.FromArgb(18, 65, 24), .Padding = New Padding(14, 6, 10, 6)}
        Dim lblWinTitle As New Label With {.Text = "📒 القيود الافتتاحية", .ForeColor = Color.White, .Font = New Font("Cairo", 11.0F, FontStyle.Bold), .Dock = DockStyle.Left, .AutoSize = True, .TextAlign = ContentAlignment.MiddleLeft}
        Dim btnClose As New Button With {.Text = "✖ إغلاق", .Size = New Size(68, 28), .Location = New Point(10, 8), .BackColor = Color.FromArgb(220, 53, 69), .ForeColor = Color.White, .FlatStyle = FlatStyle.Flat, .Font = New Font("Cairo", 8.5F, FontStyle.Bold), .Cursor = Cursors.Hand}
        btnClose.FlatAppearance.BorderSize = 0
        AddHandler btnClose.Click, Sub(s, e) Close()
        titleBar.Controls.AddRange({lblWinTitle, btnClose})

        ' لوحة معلومات القيد العلوية
        Dim topPanel As New Panel With {.Dock = DockStyle.Top, .Height = 110, .BackColor = Color.White, .Padding = New Padding(15)}

        AddLabel(topPanel, "رقم القيد", New Point(900, 15))
        txtEntryNo.Location = New Point(790, 12) : txtEntryNo.Width = 100 : txtEntryNo.Text = "تلقائي" : txtEntryNo.ReadOnly = True : txtEntryNo.Font = New Font("Cairo", 9.5F, FontStyle.Bold) : txtEntryNo.TextAlign = HorizontalAlignment.Center

        AddLabel(topPanel, "تاريخ القيد", New Point(690, 15))
        dtpDate.Location = New Point(550, 12) : dtpDate.Width = 130 : dtpDate.Format = DateTimePickerFormat.Short : dtpDate.Font = New Font("Cairo", 9.0F, FontStyle.Regular)

        AddLabel(topPanel, "حالة القيد", New Point(450, 15))
        cboStatus.Location = New Point(320, 12) : cboStatus.Width = 120 : cboStatus.DropDownStyle = ComboBoxStyle.DropDownList : cboStatus.Font = New Font("Cairo", 9.0F, FontStyle.Regular)
        cboStatus.Items.AddRange({"حالة القيد معتمد"})
        cboStatus.SelectedIndex = 0

        AddLabel(topPanel, "بيان القيد", New Point(900, 60))
        txtDescription.Location = New Point(320, 57) : txtDescription.Width = 570 : txtDescription.Text = "قيد افتتاحي للميزانية والأرصدة الأولية" : txtDescription.Font = New Font("Cairo", 9.0F, FontStyle.Regular)

        Dim btnAddRow As New Button With {
            .Text = "🔄 تحديث الحسابات", .Size = New Size(125, 34), .Location = New Point(180, 55),
            .BackColor = Color.FromArgb(46, 125, 50), .ForeColor = Color.White, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        btnAddRow.FlatAppearance.BorderSize = 0
        AddHandler btnAddRow.Click, Sub(s, e) LoadAccountsFromDatabase()
        topPanel.Controls.AddRange({txtEntryNo, dtpDate, cboStatus, txtDescription, btnAddRow})

        ' جدول القيود الافتتاحية المربوط بدليل الحسابات الفعلي
        grid.Dock = DockStyle.Fill
        grid.BackgroundColor = Color.White
        grid.Font = New Font("Cairo", 9.0F, FontStyle.Regular)
        grid.RowTemplate.Height = 32
        grid.ColumnHeadersHeight = 38
        grid.AllowUserToAddRows = False
        grid.EnableHeadersVisualStyles = False
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 232, 240)
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 40, 50)
        grid.ColumnHeadersDefaultCellStyle.Font = New Font("Cairo", 9.5F, FontStyle.Bold)

        Dim colAccountId As New DataGridViewTextBoxColumn With {.Name = "ColAccountId", .HeaderText = "ID", .Visible = False}
        grid.Columns.Add(colAccountId)
        AddGridColumn(grid, "ColNo", "م", 50, True)
        AddGridColumn(grid, "ColCode", "كود الحساب", 130, True)
        AddGridColumn(grid, "ColName", "اسم الحساب", 220, True)
        AddGridColumn(grid, "ColDebit", "مدين", 110, False)
        AddGridColumn(grid, "ColCredit", "دائن", 110, False)
        AddGridColumn(grid, "ColDesc", "البيان", 220, False)
        AddGridColumn(grid, "ColCost", "مركز التكلفة", 130, False)

        AddHandler grid.CellEndEdit, Sub(s, e) RecalculateTotals()
        AddHandler grid.CellValueChanged, Sub(s, e)
                                              If Not _suppressRecalc Then RecalculateTotals()
                                          End Sub

        ' الشريط السفلي للإجماليات والإجراءات
        Dim bottomBar As New Panel With {.Dock = DockStyle.Bottom, .Height = 65, .BackColor = Color.FromArgb(240, 242, 248), .Padding = New Padding(15)}

        lblTotalDebit.Text = "إجمالي مدين: 0.00" : lblTotalDebit.Font = New Font("Cairo", 10.0F, FontStyle.Bold) : lblTotalDebit.Location = New Point(800, 18) : lblTotalDebit.AutoSize = True
        lblTotalCredit.Text = "إجمالي دائن: 0.00" : lblTotalCredit.Font = New Font("Cairo", 10.0F, FontStyle.Bold) : lblTotalCredit.Location = New Point(600, 18) : lblTotalCredit.AutoSize = True

        Dim btnSave As New Button With {
            .Text = "💾 حفظ القيد", .Size = New Size(130, 38), .Location = New Point(15, 12),
            .BackColor = Color.FromArgb(24, 165, 88), .ForeColor = Color.White, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Cairo", 10.0F, FontStyle.Bold), .Cursor = Cursors.Hand
        }
        btnSave.FlatAppearance.BorderSize = 0
        AddHandler btnSave.Click, Sub(s, e) SaveOpeningEntry()

        bottomBar.Controls.AddRange({lblTotalDebit, lblTotalCredit, btnSave})

        Controls.Add(grid)
        Controls.Add(topPanel)
        Controls.Add(bottomBar)
        Controls.Add(titleBar)
    End Sub

    Private Sub LoadAccountsFromDatabase()
        Try
            _suppressRecalc = True
            grid.Rows.Clear()
            Using db = DbSession.Open(_session.ConnectionString)
                Dim accounts = AccountingRepository.ListPostableAccounts(db.Connection)
                Dim idx As Integer = 1
                For Each acc In accounts
                    grid.Rows.Add(acc.AccountId.ToString(CultureInfo.InvariantCulture),
                                  idx.ToString(CultureInfo.InvariantCulture),
                                  acc.Code,
                                  acc.Name,
                                  "0.00",
                                  "0.00",
                                  "قيد افتتاحي",
                                  String.Empty)
                    idx += 1
                Next
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر تحميل دليل الحسابات")
        Finally
            _suppressRecalc = False
            RecalculateTotals()
        End Try
    End Sub

    Private Sub RecalculateTotals()
        Dim totalDebit As Decimal = 0D
        Dim totalCredit As Decimal = 0D

        For Each row As DataGridViewRow In grid.Rows
            If row.IsNewRow Then Continue For
            totalDebit += ParseAmount(row.Cells("ColDebit").Value)
            totalCredit += ParseAmount(row.Cells("ColCredit").Value)
        Next

        totalDebit = Money.Round2(totalDebit)
        totalCredit = Money.Round2(totalCredit)

        lblTotalDebit.Text = "إجمالي مدين: " & totalDebit.ToString("0.00")
        lblTotalCredit.Text = "إجمالي دائن: " & totalCredit.ToString("0.00")

        Dim balanced As Boolean = (totalDebit = totalCredit AndAlso totalDebit > 0D)
        Dim color As Color = If(balanced, Color.FromArgb(24, 135, 70),
                                If(totalDebit = 0D AndAlso totalCredit = 0D, Color.FromArgb(60, 60, 70), Color.FromArgb(200, 40, 50)))
        lblTotalDebit.ForeColor = color
        lblTotalCredit.ForeColor = color
    End Sub

    Private Sub SaveOpeningEntry()
        Try
            If Not _session.HasPermission("settings.manage") AndAlso Not _session.HasPermission("stock.opening") Then
                _session.RequirePermission("settings.manage")
            End If

            grid.EndEdit()

            Dim lines As New List(Of JournalLineInput)()
            Dim totalDebit As Decimal = 0D
            Dim totalCredit As Decimal = 0D

            Using db = DbSession.Open(_session.ConnectionString)
                db.BeginTransaction()
                Dim tx = db.Transaction

                For Each row As DataGridViewRow In grid.Rows
                    If row.IsNewRow Then Continue For
                    Dim debit As Decimal = Money.Round2(ParseAmount(row.Cells("ColDebit").Value))
                    Dim credit As Decimal = Money.Round2(ParseAmount(row.Cells("ColCredit").Value))
                    If debit = 0D AndAlso credit = 0D Then Continue For

                    Dim code As String = Convert.ToString(row.Cells("ColCode").Value).Trim()
                    Dim name As String = Convert.ToString(row.Cells("ColName").Value).Trim()

                    If debit < 0D OrElse credit < 0D Then
                        Throw New BusinessException("القيم المدينة والدائنة لا يمكن أن تكون سالبة في الحساب «" & name & "».")
                    End If
                    If debit > 0D AndAlso credit > 0D Then
                        Throw New BusinessException("لا يمكن إدخال قيمة مدينة ودائنة في نفس السطر للحساب «" & name & "».")
                    End If

                    Dim accountId As Long = 0
                    Dim rawId As String = Convert.ToString(row.Cells("ColAccountId").Value)
                    If Not Long.TryParse(rawId, accountId) OrElse accountId <= 0 Then
                        accountId = AccountingRepository.GetAccountId(db.Connection, code, tx)
                    End If

                    Dim lineDesc As String = Convert.ToString(row.Cells("ColDesc").Value).Trim()
                    If String.IsNullOrWhiteSpace(lineDesc) Then lineDesc = txtDescription.Text.Trim()

                    lines.Add(New JournalLineInput With {
                        .AccountId = accountId,
                        .Debit = debit,
                        .Credit = credit,
                        .Description = lineDesc
                    })
                    totalDebit += debit
                    totalCredit += credit
                Next

                If lines.Count = 0 Then
                    Throw New BusinessException("أدخل قيمة مدينة أو دائنة لسطرين على الأقل قبل حفظ القيد الافتتاحي.")
                End If

                totalDebit = Money.Round2(totalDebit)
                totalCredit = Money.Round2(totalCredit)
                If totalDebit <> totalCredit Then
                    Dim diff As Decimal = Math.Abs(totalDebit - totalCredit)
                    Throw New BusinessException(
                        "القيد غير متوازن: إجمالي المدين (" & totalDebit.ToString("0.00") &
                        ") لا يساوي إجمالي الدائن (" & totalCredit.ToString("0.00") &
                        ") — الفرق: " & diff.ToString("0.00") & ".")
                End If

                Dim entryDesc As String = If(String.IsNullOrWhiteSpace(txtDescription.Text),
                                             "قيد افتتاحي للميزانية والأرصدة الأولية",
                                             txtDescription.Text.Trim())
                Dim eventId As String = Uuid7.NewId()
                Dim entryDate As Date = dtpDate.Value.Date

                Dim entryId As Long = AccountingRepository.InsertEntry(
                    db.Connection, tx, eventId, _session.BranchId, entryDate,
                    entryDesc, "opening_entry", Nothing, _session.UserId, lines)

                Dim payload As String = JsonSerializer.Serialize(New With {
                    .entry_id = entryId,
                    .entry_date = entryDate.ToString("yyyy-MM-dd"),
                    .description = entryDesc,
                    .total_debit = totalDebit,
                    .total_credit = totalCredit,
                    .lines_count = lines.Count
                })

                AuditRepository.Log(db.Connection, tx, eventId, _session.BranchId, _session.UserId,
                                    _session.Username, "opening_entry_post", "journal_entry",
                                    entryId.ToString(CultureInfo.InvariantCulture), Nothing, payload, _session.DeviceId)

                SyncStore.Enqueue(db.Connection, tx, eventId, _session.BranchId, _session.DeviceId,
                                  "journal_entry", entryId.ToString(CultureInfo.InvariantCulture),
                                  "journal.opening_posted", 1, payload, Date.UtcNow)

                db.Commit()

                txtEntryNo.Text = entryId.ToString(CultureInfo.InvariantCulture)
                MessageBox.Show(Me,
                                "تم حفظ واعتماد قيد الافتتاح المحاسبي رقم #" & entryId.ToString(CultureInfo.InvariantCulture) &
                                " بمبلغ متوازن قدره " & totalDebit.ToString("0.00") & " بنجاح.",
                                "حفظ القيد الافتتاحي",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)
            End Using
        Catch ex As Exception
            UiTheme.Handle(ex, "تعذّر حفظ القيد الافتتاحي")
        End Try
    End Sub

    Private Shared Function ParseAmount(raw As Object) As Decimal
        If raw Is Nothing OrElse raw Is DBNull.Value Then Return 0D
        Dim text As String = Convert.ToString(raw).Trim()
        If String.IsNullOrWhiteSpace(text) Then Return 0D
        Dim value As Decimal
        If Decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, value) Then Return value
        If Decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, value) Then Return value
        Return 0D
    End Function

    Private Sub AddLabel(parent As Panel, text As String, pt As Point)
        Dim lbl As New Label With {.Text = text, .Location = pt, .AutoSize = True, .Font = New Font("Cairo", 9.0F, FontStyle.Bold), .ForeColor = Color.FromArgb(70, 70, 80)}
        parent.Controls.Add(lbl)
    End Sub

    Private Sub AddGridColumn(dgv As DataGridView, name As String, headerText As String, width As Integer, [readOnly] As Boolean)
        Dim col As New DataGridViewTextBoxColumn With {
            .Name = name,
            .HeaderText = headerText,
            .Width = width,
            .ReadOnly = [readOnly]
        }
        dgv.Columns.Add(col)
    End Sub
End Class
