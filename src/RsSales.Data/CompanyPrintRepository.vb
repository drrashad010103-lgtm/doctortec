Imports System
Imports System.Data
Imports Dapper

''' <summary>Company identity shown at the top of printed invoices and statements.</summary>
Public Class CompanyPrintSettings
    Public Property CompanyName As String
    Public Property CompanyNameEn As String
    Public Property TaxNo As String
    Public Property Phone As String
    Public Property Address As String
    Public Property LogoPath As String
    Public Property ShowLogo As Boolean = True
    Public Property ShowTaxNo As Boolean = True
    ''' <summary>right, left, center or side.</summary>
    Public Property HeaderLayout As String = "right"
    Public Property LogoWidth As Integer = 110
    Public Property HeaderNote As String
    Public Property FooterNote As String

    Public ReadOnly Property HasName As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(CompanyName)
        End Get
    End Property
End Class

''' <summary>
''' Reads the print header configuration from the settings table. Values come
''' from «بيانات الشركة» (name, tax number, logo, phone, address) plus the
''' dedicated «ترويسة الطباعة» options.
''' </summary>
Public NotInheritable Class CompanyPrintRepository
    Private Sub New()
    End Sub

    Public Shared Function Load(conn As IDbConnection) As CompanyPrintSettings
        Dim result As New CompanyPrintSettings()
        result.CompanyName = FirstValue(conn, "company.name_ar", "company.name")
        result.CompanyNameEn = GetValue(conn, "company.name_en")
        result.TaxNo = GetValue(conn, "company.tax_no")
        result.Phone = FirstValue(conn, "company.phone", "company.mobile")
        result.Address = GetValue(conn, "company.address")
        result.LogoPath = GetValue(conn, "company.logo_path")
        result.HeaderNote = GetValue(conn, "print.header_note")
        result.FooterNote = GetValue(conn, "print.footer_note")
        result.ShowLogo = GetBool(conn, "print.show_logo", True)
        result.ShowTaxNo = GetBool(conn, "print.show_tax_no", True)
        result.HeaderLayout = NormaliseLayout(GetValue(conn, "print.header_layout"))

        Dim logoWidth As Decimal = 110D
        Decimal.TryParse(GetValue(conn, "print.logo_width"), Globalization.NumberStyles.Any,
                         Globalization.CultureInfo.InvariantCulture, logoWidth)
        If logoWidth < 40D Then logoWidth = 40D
        If logoWidth > 300D Then logoWidth = 300D
        result.LogoWidth = CInt(logoWidth)
        Return result
    End Function

    Private Shared Function NormaliseLayout(value As String) As String
        Select Case If(value, String.Empty).Trim().ToLowerInvariant()
            Case "left" : Return "left"
            Case "center" : Return "center"
            Case "side" : Return "side"
            Case Else : Return "right"
        End Select
    End Function

    Private Shared Function FirstValue(conn As IDbConnection, ParamArray keys As String()) As String
        For Each settingKey In keys
            Dim value As String = GetValue(conn, settingKey)
            If Not String.IsNullOrWhiteSpace(value) Then Return value
        Next
        Return String.Empty
    End Function

    Private Shared Function GetValue(conn As IDbConnection, settingKey As String) As String
        Dim value As Object = conn.ExecuteScalar("SELECT v FROM settings WHERE k=@k", New With {.k = settingKey})
        If value Is Nothing OrElse value Is DBNull.Value Then Return String.Empty
        Return Convert.ToString(value).Trim()
    End Function

    Private Shared Function GetBool(conn As IDbConnection, settingKey As String, fallback As Boolean) As Boolean
        Dim value As String = GetValue(conn, settingKey)
        If String.IsNullOrWhiteSpace(value) Then Return fallback
        Return value = "1" OrElse value.Equals("true", StringComparison.OrdinalIgnoreCase)
    End Function
End Class
