Imports System.Collections.Generic
Imports System.Text.Json.Serialization

''' <summary>One outbox event as sent to the central API.</summary>
Public Class SyncEvent
    <JsonPropertyName("event_id")>
    Public Property EventId As String
    <JsonPropertyName("branch_id")>
    Public Property BranchId As Long
    <JsonPropertyName("entity_type")>
    Public Property EntityType As String
    <JsonPropertyName("entity_id")>
    Public Property EntityId As String
    <JsonPropertyName("event_type")>
    Public Property EventType As String
    <JsonPropertyName("attempts")>
    Public Property Attempts As Integer
    <JsonPropertyName("payload")>
    Public Property Payload As String
End Class

Public Class SyncBatch
    <JsonPropertyName("device_id")>
    Public Property DeviceId As Long?
    <JsonPropertyName("events")>
    Public Property Events As New List(Of SyncEvent)()
End Class

Public Class SyncEventAck
    <JsonPropertyName("event_id")>
    Public Property EventId As String
    <JsonPropertyName("status")>
    Public Property Status As String
    <JsonPropertyName("error")>
    Public Property [Error] As String
End Class

Public Class SyncAck
    <JsonPropertyName("accepted")>
    Public Property Accepted As Boolean
    <JsonPropertyName("results")>
    Public Property Results As New List(Of SyncEventAck)()
    <JsonPropertyName("server_time")>
    Public Property ServerTime As Date?
End Class

Public Class PullChange
    <JsonPropertyName("seq")>
    Public Property Seq As Long
    <JsonPropertyName("entity_type")>
    Public Property EntityType As String
    <JsonPropertyName("entity_id")>
    Public Property EntityId As String
    <JsonPropertyName("op")>
    Public Property Op As String
    <JsonPropertyName("payload")>
    Public Property Payload As String
End Class

Public Class PullResponse
    <JsonPropertyName("changes")>
    Public Property Changes As New List(Of PullChange)()
    <JsonPropertyName("next_seq")>
    Public Property NextSeq As Long
End Class

''' <summary>Transport abstraction so tests can run without a server.</summary>
Public Interface ISyncTransport
    Function Push(batch As SyncBatch) As SyncAck
    Function Pull(stream As String, afterSeq As Long, limit As Integer) As PullResponse
End Interface

Public Class SyncRunResult
    Public Property Claimed As Integer
    Public Property Acked As Integer
    Public Property Failed As Integer
    Public Property Dead As Integer
    Public Property Message As String
End Class
