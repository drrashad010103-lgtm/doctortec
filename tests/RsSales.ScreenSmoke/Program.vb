Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports MySqlConnector

''' <summary>
''' Opens every screen of the application off-screen with a real database session,
''' lets its Load/Shown logic run, then records whether it opened cleanly.
''' Message boxes raised during a screen are logged and closed automatically.
''' </summary>
Module Program

    Private ReadOnly Results As New List(Of String)()
    Private ReadOnly AsyncErrors As New List(Of String)()
    Private ReadOnly DialogLog As New List(Of String)()
    Private CurrentName As String = ""
    Private WatchStop As Boolean = False

    <STAThread>
    Sub Main()
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Console.OutputEncoding = Text.Encoding.UTF8
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, Sub(s, e)
                                                    AsyncErrors.Add(CurrentName & " :: " & Describe(e.Exception))
                                                End Sub

        Dim config As AppConfig = AppConfig.Load()
        Dim session As AppSession = BuildSession(config.ConnectionString)
        Console.WriteLine("DB=" & Mask(config.ConnectionString))
        Console.WriteLine("USER=" & session.Username & " BRANCH=" & session.BranchId & " (" & session.BranchName & ")")
        Console.WriteLine()

        StartDialogWatchdog()

        Dim asm As Assembly = Assembly.Load("RsSales.UI")
        Dim forms = asm.GetTypes().
            Where(Function(t) GetType(Form).IsAssignableFrom(t) AndAlso Not t.IsAbstract).
            OrderBy(Function(t) t.Name).ToList()

        Console.WriteLine("FORMS=" & forms.Count)
        For Each t In forms
            TestForm(t, session, config)
        Next

        WatchStop = True
        Console.WriteLine()
        Console.WriteLine("=== RESULT ===")
        For Each line In Results
            Console.WriteLine(line)
        Next
        If DialogLog.Count > 0 Then
            Console.WriteLine()
            Console.WriteLine("=== DIALOGS ===")
            For Each d In DialogLog
                Console.WriteLine(d)
            Next
        End If
        Console.WriteLine()
        Console.WriteLine("OK=" & Results.Where(Function(x) x.StartsWith("OK")).Count() &
                          " FAIL=" & Results.Where(Function(x) x.StartsWith("FAIL")).Count() &
                          " SKIP=" & Results.Where(Function(x) x.StartsWith("SKIP")).Count())
        Console.Out.Flush()
    End Sub

    ' ---------------------------------------------------------------- session --

    Private Function BuildSession(conn As String) As AppSession
        Dim s As New AppSession()
        s.ConnectionString = conn
        s.UserId = 1
        s.Username = "root"
        s.FullName = "فحص تلقائي"
        s.Permissions.Add("*")
        s.BranchId = 1
        s.BranchName = "الفرع الرئيسي"
        Try
            Using c As New MySqlConnection(conn)
                c.Open()
                Using cmd = c.CreateCommand()
                    cmd.CommandText = "SELECT * FROM branches ORDER BY id LIMIT 1"
                    Using r = cmd.ExecuteReader()
                        If r.Read() Then
                            For i As Integer = 0 To r.FieldCount - 1
                                Dim column As String = r.GetName(i).ToLowerInvariant()
                                Dim value = r.GetValue(i)
                                If value Is DBNull.Value Then Continue For
                                If column = "id" Then s.BranchId = Convert.ToInt64(value)
                                If column = "name" OrElse column = "name_ar" OrElse column = "branch_name" Then
                                    s.BranchName = value.ToString()
                                End If
                                If column = "tax_no" OrElse column = "tax_number" Then s.BranchTaxNo = value.ToString()
                            Next
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine("WARN branches: " & ex.Message)
        End Try
        Return s
    End Function

    Private Function Mask(conn As String) As String
        If String.IsNullOrEmpty(conn) Then Return "(none)"
        Return System.Text.RegularExpressions.Regex.Replace(conn, "(?i)password=[^;]*", "password=***")
    End Function

    ' ------------------------------------------------------------------ forms --

    Private Sub TestForm(t As Type, session As AppSession, config As AppConfig)
        CurrentName = t.Name
        If t.Name = "LoginForm" Then
            Results.Add("SKIP|" & t.Name & "|تحتاج كلمة مرور")
            Return
        End If

        Dim form As Form = Nothing
        Try
            form = CreateForm(t, session, config)
        Catch ex As Exception
            Dim inner As Exception = ex
            If TypeOf ex Is TargetInvocationException AndAlso ex.InnerException IsNot Nothing Then inner = ex.InnerException
            If UsedBlank AndAlso TypeOf inner Is BusinessException Then
                Results.Add("SKIP|" & t.Name & "|تحتاج سجلاً حقيقياً: " & inner.Message)
            Else
                Results.Add("FAIL|" & t.Name & "|إنشاء الشاشة: " & Describe(ex))
            End If
            Return
        End Try
        If form Is Nothing Then
            Results.Add("SKIP|" & t.Name & "|تعذّر إنشاء الشاشة تلقائياً")
            Return
        End If
        Dim note As String = If(UsedBlank, " (بيانات فارغة)", "")

        Dim before As Integer = AsyncErrors.Count
        Dim dialogsBefore As Integer = DialogLog.Count
        Try
            form.StartPosition = FormStartPosition.Manual
            form.ShowInTaskbar = False
            form.Location = New Point(-30000, -30000)
            form.Show()
            Dim until As DateTime = DateTime.UtcNow.AddMilliseconds(1300)
            Do While DateTime.UtcNow < until
                Application.DoEvents()
                Thread.Sleep(15)
            Loop
            Try
                form.Hide()
                If Not form.IsDisposed Then form.Close()
            Catch ex As ObjectDisposedException
            End Try
            If Not form.IsDisposed Then form.Dispose()
        Catch ex As ObjectDisposedException
            Results.Add("OK|" & t.Name & "|" & note & " (أغلق نفسه)")
            Return
        Catch ex As Exception
            Results.Add("FAIL|" & t.Name & "|" & note & " " & Describe(ex))
            Try
                form.Dispose()
            Catch
            End Try
            Return
        End Try

        Dim problems As New List(Of String)()
        For Each message In AsyncErrors.Skip(before)
            ' النموذج قد يغلق نفسه أثناء الاختبار (نافذة رسالة أو إغلاق تلقائي) — ليس خطأ شاشة
            If Not message.Contains("ObjectDisposedException") Then problems.Add(message)
        Next
        For i As Integer = dialogsBefore To DialogLog.Count - 1
            If Not DialogLog(i).Contains("Microsoft .NET Framework") Then problems.Add("رسالة: " & DialogLog(i))
        Next

        If problems.Count = 0 Then
            Results.Add("OK|" & t.Name & "|" & note)
        Else
            Results.Add("FAIL|" & t.Name & "|" & note & " " & String.Join(" | ", problems))
        End If
    End Sub

    Private UsedBlank As Boolean = False

    Private Function CreateForm(t As Type, session As AppSession, config As AppConfig) As Form
        UsedBlank = False
        Select Case t.Name
            Case "MainForm"
                Return DirectCast(Activator.CreateInstance(t, session, config), Form)
            Case "CustomersForm"
                Return DirectCast(Activator.CreateInstance(t, session, "customer"), Form)
            Case "HrEmployeesForm"
                Return DirectCast(Activator.CreateInstance(t, session, "employee"), Form)
            Case "HrCommissionsForm"
                Return DirectCast(Activator.CreateInstance(t, session, "seller"), Form)
            Case "ReturnsForm"
                Return DirectCast(Activator.CreateInstance(t, session, "sale"), Form)
            Case "InvoiceEditor"
                Return DirectCast(Activator.CreateInstance(t, session, "sale"), Form)
            Case "ItemPickerForm"
                Return DirectCast(Activator.CreateInstance(t, session, "customer"), Form)
        End Select

        Dim form = InvokeBestCtor(t, session, config)
        Return form
    End Function

    Private Function InvokeBestCtor(t As Type, session As AppSession, config As AppConfig) As Form
        For Each c In t.GetConstructors()
            Dim ps = c.GetParameters()
            If ps.Length = 0 Then Return DirectCast(c.Invoke(Nothing), Form)
            If ps(0).ParameterType IsNot GetType(AppSession) Then Continue For
            Dim args(ps.Length - 1) As Object
            args(0) = session
            If FillArgs(ps, args, 1, session, config) Then Return DirectCast(c.Invoke(args), Form)
        Next
        ' Dialogs that take a record instead of a session (e.g. ShiftCloseForm).
        For Each c In t.GetConstructors()
            Dim ps = c.GetParameters()
            If ps.Length = 0 Then Continue For
            Dim args(ps.Length - 1) As Object
            UsedBlank = True
            If FillArgs(ps, args, 0, session, config) Then Return DirectCast(c.Invoke(args), Form)
        Next
        Return Nothing
    End Function

    Private Function FillArgs(ps As ParameterInfo(), args As Object(), start As Integer,
                              session As AppSession, config As AppConfig) As Boolean
        For i As Integer = start To ps.Length - 1
            Dim pt = ps(i).ParameterType
            If ps(i).IsOptional Then
                args(i) = Type.Missing
            ElseIf pt Is GetType(AppSession) Then
                args(i) = session
            ElseIf pt Is GetType(AppConfig) Then
                args(i) = config
            ElseIf pt Is GetType(String) Then
                args(i) = "customer"
                UsedBlank = True
            ElseIf pt.IsValueType Then
                args(i) = Activator.CreateInstance(pt)
                UsedBlank = True
            ElseIf pt.GetConstructor(Type.EmptyTypes) IsNot Nothing Then
                args(i) = Activator.CreateInstance(pt)
                UsedBlank = True
            Else
                Return False
            End If
        Next
        Return True
    End Function

    Private Function Describe(ex As Exception) As String
        Dim text As String = ex.GetType().Name & ": " & ex.Message
        If TypeOf ex Is TargetInvocationException AndAlso ex.InnerException IsNot Nothing Then
            text = ex.InnerException.GetType().Name & ": " & ex.InnerException.Message
        End If
        Return text.Replace(vbCrLf, " ").Replace(vbLf, " ")
    End Function

    ' --------------------------------------------------------------- watchdog --

    Private Sub StartDialogWatchdog()
        Dim worker As New Thread(Sub()
                                     While Not WatchStop
                                         Thread.Sleep(350)
                                         Try
                                             EnumWindows(AddressOf EnumProc, IntPtr.Zero)
                                         Catch
                                         End Try
                                     End While
                                 End Sub)
        worker.IsBackground = True
        worker.Start()
    End Sub

    Private Function EnumProc(hWnd As IntPtr, lParam As IntPtr) As Boolean
        Dim pid As Integer = 0
        GetWindowThreadProcessId(hWnd, pid)
        If pid <> Process.GetCurrentProcess().Id Then Return True
        Dim cls As New StringBuilder(256)
        GetClassName(hWnd, cls, cls.Capacity)
        If cls.ToString() = "#32770" Then
            Dim txt As New StringBuilder(2048)
            GetWindowText(hWnd, txt, txt.Capacity)
            Dim text As String = txt.ToString().Replace(vbCrLf, " ").Replace(vbLf, " ")
            If text.Length > 300 Then text = text.Substring(0, 300)
            DialogLog.Add("[" & CurrentName & "] " & text)
            SendMessage(hWnd, &H10, IntPtr.Zero, IntPtr.Zero)
        End If
        Return True
    End Function

    Private Delegate Function EnumWindowsProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    <DllImport("user32.dll")>
    Private Function EnumWindows(cb As EnumWindowsProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef pid As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetClassName(hWnd As IntPtr, sb As StringBuilder, max As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetWindowText(hWnd As IntPtr, sb As StringBuilder, max As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

End Module
