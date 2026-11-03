Imports System.Windows.Forms

Friend Module Program

    <STAThread()>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, Sub(s, e)
            AppLog.Error("Unhandled UI exception", e.Exception)
            UiTheme.ShowError("حدث خطأ غير متوقع: " & e.Exception.Message)
        End Sub
        AddHandler AppDomain.CurrentDomain.UnhandledException, Sub(s, e)
            Dim ex = TryCast(e.ExceptionObject, Exception)
            AppLog.Error("Unhandled application exception", ex)
        End Sub

        AppLog.Directory = IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RsSales", "logs")
        Try
            IO.Directory.CreateDirectory(AppLog.Directory)
        Catch
        End Try

        Dim config As AppConfig = Nothing
        Try
            config = AppConfig.Load()
        Catch ex As Exception
            UiTheme.ShowError("تعذّر قراءة إعدادات التطبيق: " & ex.Message)
            config = New AppConfig()
        End Try

        Do
            Using login As New LoginForm(config)
                If login.ShowDialog() <> DialogResult.OK OrElse login.Session Is Nothing Then Exit Do
                Try
                    Using main As New MainForm(login.Session, config)
                        Dim result = main.ShowDialog()
                        If result <> DialogResult.Retry Then Exit Do
                    End Using
                Catch ex As Exception
                    AppLog.Error("Failed to open main window", ex)
                    UiTheme.ShowError("تعذّر فتح الواجهة الرئيسية: " & ex.Message)
                    Exit Do
                End Try
            End Using
        Loop
    End Sub

End Module
