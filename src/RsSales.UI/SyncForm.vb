Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports Dapper

' <summary>
' DOCTOR TECH — شاشة المزامنة المركزية والنسخ الاحتياطي لقاعدة البيانات (.SQL Backup & Restore)
' </summary>
Public Class SyncForm
    Inherits Form

    Private ReadOnly _session As AppSession
    Private ReadOnly _config As AppConfig
    Private ReadOnly _startInBackupMode As Boolean
    Private ReadOnly lblStats As New Label()
    Private ReadOnly txtLog As New TextBox()
    Private ReadOnly btnBackup As Button = UiTheme.MakeGlassButton("💾 إنشاء نسخة احتياطية كاملة (.SQL)", 240, 38, UiTheme.ElectricBlue, UiTheme.DigitalBlue, Color.White, 12)
    Private ReadOnly btnRestore As Button = UiTheme.MakeGlassButton("📂 استعادة نسخة احتياطية (.SQL)", 210, 38, UiTheme.SoftTechBlue, UiTheme.DeepTechNavy, Color.White, 12)
    Private ReadOnly btnPush As Button = UiTheme.PrimaryButton("⬆ رفع الأحداث (Push)", 155)
    Private ReadOnly btnPull As Button = UiTheme.MakeButton("⬇ سحب التحديثات (Pull)", 160)
    Private ReadOnly btnRetry As Button = UiTheme.MakeButton("🔄 إعادة المحاولة", 130)

    Public Sub New(session As AppSession, config As AppConfig, Optional startInBackupMode As Boolean = False)
        _session = session
        _config = If(config, AppConfig.Load())
        _startInBackupMode = startInBackupMode
        InitializeUi()
        RefreshStatusData()
    End Sub

    Private Sub InitializeUi()
        UiTheme.Apply(Me)
        Text = If(_startInBackupMode, "DOCTOR TECH — النسخ الاحتياطي لقاعدة البيانات والمزامنة", "DOCTOR TECH — المزامنة المركزية والنسخ الاحتياطي")
        ClientSize = New Size(940, 600)
        StartPosition = FormStartPosition.CenterParent
        Padding = New Padding(UiTheme.ScreenMargin1Cm)

        lblStats.Font = UiTheme.Font(10.5F, FontStyle.Bold)
        lblStats.ForeColor = UiTheme.DeepTechNavy
        lblStats.BackColor = UiTheme.IceBlue
        lblStats.Dock = DockStyle.Top
        lblStats.Height = 54
        lblStats.Padding = New Padding(14, 12, 14, 0)

        Dim toolbar As FlowLayoutPanel = UiTheme.Toolbar()
        toolbar.Height = 54
        AddHandler btnBackup.Click, AddressOf CreateBackupClick
        AddHandler btnRestore.Click, AddressOf RestoreBackupClick
        AddHandler btnPush.Click, AddressOf PushClick
        AddHandler btnPull.Click, AddressOf PullClick
        AddHandler btnRetry.Click, AddressOf RetryClick

        toolbar.Controls.Add(btnBackup)
        toolbar.Controls.Add(btnRestore)
        toolbar.Controls.Add(btnPush)
        toolbar.Controls.Add(btnPull)
        toolbar.Controls.Add(btnRetry)

        Dim btnRefresh As Button = UiTheme.MakeButton("تحديث الحالة", 120)
        AddHandler btnRefresh.Click, Sub(s, e) RefreshStatusData()
        toolbar.Controls.Add(btnRefresh)

        txtLog.Dock = DockStyle.Fill
        txtLog.Multiline = True
        txtLog.ReadOnly = True
        txtLog.ScrollBars = ScrollBars.Vertical
        txtLog.Font = New Font("Consolas", 9.5F)
        txtLog.BackColor = Color.White

        Controls.Add(txtLog)
        Controls.Add(toolbar)
        Controls.Add(lblStats)
    End Sub

    Private Sub RefreshStatusData()
        Try
            Using db = DbSession.Open(_session.ConnectionString)
                Dim s = SyncStore.Stats(db.Connection)
                lblStats.Text = "📊 حالة المزامنة والنسخ الاحتياطي — معلّق: " & s.Pending.ToString() &
                                " | فشل: " & s.Failed.ToString() &
                                " | ميت: " & s.Dead.ToString() &
                                " | مؤكد اليوم: " & s.AckedToday.ToString()
            End Using
            txtLog.Text = "💎 DOCTOR TECH Backup & Sync Engine جاهز." & Environment.NewLine &
                          "• اضغط «💾 إنشاء نسخة احتياطية كاملة (.SQL)» لحفظ نسخة كاملة من جميع جداول وبيانات قاعدة البيانات في ملف SQL." & Environment.NewLine &
                          If(String.IsNullOrWhiteSpace(_config.ServerUrl),
                             "• ملاحظة المزامنة السحابية: لم يتم ضبط عنوان السيرفر المركزي (ServerUrl) بعد.",
                             "• السيرفر المركزي للمزامنة: " & _config.ServerUrl)
        Catch ex As Exception
            UiTheme.Handle(ex)
        End Try
    End Sub

    ' <summary>Generates a real, complete SQL dump file of all tables and rows in the database.</summary>
    Private Sub CreateBackupClick(sender As Object, e As EventArgs)
        Using dlg As New SaveFileDialog With {
            .Title = "حفظ النسخة الاحتياطية لقاعدة بيانات DOCTOR TECH",
            .Filter = "SQL Database Backup (*.sql)|*.sql",
            .FileName = $"doctortech_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            .AddExtension = True
        }
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Cursor = Cursors.WaitCursor
                Dim tableCount As Integer = 0
                Dim rowCount As Long = 0

                Using db = DbSession.Open(_session.ConnectionString)
                    Dim conn = db.Connection
                    Dim sb As New StringBuilder()
                    sb.AppendLine("-- ===================================================================")
                    sb.AppendLine("-- DOCTOR TECH ERP / POS — Complete Database Backup")
                    sb.AppendLine("-- Created At (UTC): " & DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
                    sb.AppendLine("-- Branch: " & _session.BranchName & " | User: " & _session.Username)
                    sb.AppendLine("-- ===================================================================")
                    sb.AppendLine("SET FOREIGN_KEY_CHECKS = 0;")
                    sb.AppendLine()

                    Dim tables = conn.Query(Of String)("SHOW FULL TABLES WHERE Table_type = 'BASE TABLE'").AsList()
                    For Each tbl In tables
                        tableCount += 1
                        sb.AppendLine($"-- Table: `{tbl}`")
                        Using cmd As IDbCommand = conn.CreateCommand()
                            cmd.CommandText = $"SELECT * FROM `{tbl}`"
                            Using reader As IDataReader = cmd.ExecuteReader()
                                Dim colNames As New List(Of String)()
                                For i As Integer = 0 To reader.FieldCount - 1
                                    colNames.Add("`" & reader.GetName(i) & "`")
                                Next
                                Dim colsJoined As String = String.Join(", ", colNames)
                                While reader.Read()
                                    rowCount += 1
                                    Dim vals As New List(Of String)()
                                    For i As Integer = 0 To reader.FieldCount - 1
                                        vals.Add(FormatSqlValue(reader.GetValue(i)))
                                    Next
                                    sb.AppendLine($"INSERT IGNORE INTO `{tbl}` ({colsJoined}) VALUES ({String.Join(", ", vals)});")
                                End While
                            End Using
                        End Using
                        sb.AppendLine()
                    Next

                    sb.AppendLine("SET FOREIGN_KEY_CHECKS = 1;")
                    File.WriteAllText(dlg.FileName, sb.ToString(), New UTF8Encoding(True))

                    db.BeginTransaction()
                    AuditRepository.Log(conn, db.Transaction, Nothing, _session.BranchId, _session.UserId,
                                        _session.Username, "db_backup_create", "database", Path.GetFileName(dlg.FileName),
                                        Nothing, $"tables={tableCount};rows={rowCount}", _session.DeviceId)
                    db.Commit()
                End Using

                Dim info As New FileInfo(dlg.FileName)
                txtLog.AppendText(Environment.NewLine & Environment.NewLine &
                                  $"✅ [{DateTime.Now:HH:mm:ss}] تم إنشاء النسخة الاحتياطية بنجاح:" & Environment.NewLine &
                                  $"   المسار: {dlg.FileName}" & Environment.NewLine &
                                  $"   عدد الجداول: {tableCount} جدول | إجمالي السجلات المؤمنة: {rowCount:N0} سجل | الحجم: {(info.Length / 1024.0):N1} KB")
                UiTheme.Info($"تم إنشاء النسخة الاحتياطية بنجاح ({tableCount} جدول / {rowCount:N0} سجل) في الملف:" & Environment.NewLine & dlg.FileName)
            Catch ex As Exception
                UiTheme.Handle(ex, "فشل إنشاء النسخة الاحتياطية")
            Finally
                Cursor = Cursors.Default
            End Try
        End Using
    End Sub

    Private Sub RestoreBackupClick(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog With {
            .Title = "اختيار ملف النسخة الاحتياطية (.sql)",
            .Filter = "SQL Database Backup (*.sql)|*.sql"
        }
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If Not UiTheme.Confirm("هل أنت متأكد من استعادة النسخة الاحتياطية المحددة؟ سيتم دمج السجلات في قاعدة البيانات.") Then Return
            Try
                Cursor = Cursors.WaitCursor
                Dim sqlText As String = File.ReadAllText(dlg.FileName, Encoding.UTF8)
                Using db = DbSession.Open(_session.ConnectionString)
                    db.BeginTransaction()
                    db.Connection.Execute(sqlText, Nothing, db.Transaction, commandTimeout:=300)
                    AuditRepository.Log(db.Connection, db.Transaction, Nothing, _session.BranchId, _session.UserId,
                                        _session.Username, "db_backup_restore", "database", Path.GetFileName(dlg.FileName),
                                        Nothing, Nothing, _session.DeviceId)
                    db.Commit()
                End Using
                txtLog.AppendText(Environment.NewLine & $"✅ [{DateTime.Now:HH:mm:ss}] تم استعادة النسخة الاحتياطية من: {dlg.FileName}")
                UiTheme.Info("تم استعادة النسخة الاحتياطية بنجاح.")
            Catch ex As Exception
                UiTheme.Handle(ex, "فشل استعادة النسخة الاحتياطية")
            Finally
                Cursor = Cursors.Default
            End Try
        End Using
    End Sub

    Private Shared Function FormatSqlValue(val As Object) As String
        If val Is Nothing OrElse val Is DBNull.Value Then Return "NULL"
        If TypeOf val Is Boolean Then Return If(DirectCast(val, Boolean), "1", "0")
        If TypeOf val Is DateTime Then Return "'" & DirectCast(val, DateTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) & "'"
        If TypeOf val Is Byte() Then
            Dim bytes = DirectCast(val, Byte())
            If bytes.Length = 0 Then Return "''"
            Return "0x" & BitConverter.ToString(bytes).Replace("-", "")
        End If
        If TypeOf val Is Decimal OrElse TypeOf val Is Double OrElse TypeOf val Is Single OrElse
           TypeOf val Is Long OrElse TypeOf val Is Integer OrElse TypeOf val Is Short OrElse TypeOf val Is Byte Then
            Return Convert.ToString(val, CultureInfo.InvariantCulture)
        End If
        Dim s As String = Convert.ToString(val)
        s = s.Replace("\", "\\").Replace("'", "\'").Replace(vbCr, "\r").Replace(vbLf, "\n")
        Return "'" & s & "'"
    End Function

    Private Function CreateService() As SyncService
        If String.IsNullOrWhiteSpace(_config.ServerUrl) Then Throw New BusinessException("يرجى ضبط عنوان السيرفر المركزي أولاً في شاشة الإعدادات.")
        Dim transport As New HttpSyncTransport(_config.ServerUrl, _config.DeviceKey)
        Return New SyncService(_session.ConnectionString, _session.BranchId, _session.DeviceId, transport, _config.SyncMaxAttempts)
    End Function

    Private Sub PushClick(sender As Object, e As EventArgs)
        Try
            Cursor = Cursors.WaitCursor
            Dim svc = CreateService()
            Dim r = svc.PushOnce(_config.SyncBatchSize)
            txtLog.AppendText(Environment.NewLine & Environment.NewLine & DateTime.Now.ToString("HH:mm:ss") & " PUSH" &
                              Environment.NewLine & "مطالب بها: " & r.Claimed.ToString() & " | مقبولة: " & r.Acked.ToString() &
                              " | مرفوضة: " & r.Failed.ToString() & " | ميتة: " & r.Dead.ToString() & Environment.NewLine & r.Message)
            RefreshStatusData()
        Catch ex As Exception
            UiTheme.Handle(ex, "فشل الرفع")
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub PullClick(sender As Object, e As EventArgs)
        Try
            Cursor = Cursors.WaitCursor
            Dim svc = CreateService()
            Dim n = svc.PullOnce(200)
            txtLog.AppendText(Environment.NewLine & Environment.NewLine & DateTime.Now.ToString("HH:mm:ss") & " PULL" &
                              Environment.NewLine & "تم تطبيق: " & n.ToString() & " تحديث")
            RefreshStatusData()
        Catch ex As Exception
            UiTheme.Handle(ex, "فشل السحب")
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub

    Private Sub RetryClick(sender As Object, e As EventArgs)
        Try
            Cursor = Cursors.WaitCursor
            Using db = DbSession.Open(_session.ConnectionString)
                db.BeginTransaction()
                SyncStore.RequeueStuck(db.Connection, db.Transaction, _config.SyncMaxAttempts)
                db.Commit()
            End Using
            UiTheme.Info("تم نقل الأحداث الفاشلة إلى قائمة الانتظار لإعادة المحاولة.")
            RefreshStatusData()
        Catch ex As Exception
            UiTheme.Handle(ex)
        Finally
            Cursor = Cursors.Default
        End Try
    End Sub
End Class
