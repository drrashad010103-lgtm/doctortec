Imports System
Imports Dapper
Imports RsSales.Server.Sync
Imports Xunit

''' <summary>
''' The central sync store must be durable and idempotent: a repeated event_id
''' is acknowledged as duplicate without writing a second event or change row.
''' </summary>
<Collection("Database")>
Public Class SyncServerStoreTests
    Private ReadOnly _db As DbFixture

    Public Sub New(db As DbFixture)
        _db = db
    End Sub

    Private Shared Function ItemEvent(branchId As Long, entityId As String) As SyncEvent
        Return New SyncEvent With {
            .EventId = Guid.NewGuid().ToString("N"),
            .BranchId = branchId,
            .EntityType = "item",
            .EntityId = entityId,
            .EventType = "item.upserted",
            .Payload = "{""id"":" & entityId & ",""code"":""SRV-1"",""name_ar"":""صنف من الخادم""}"
        }
    End Function

    <Fact>
    Public Sub PushIsIdempotentAndPublishesMasterData()
        Dim branch As Long = 920001L
        Dim otherBranch As Long = 920002L
        Dim baseSeq As Long = _db.ScalarLong("SELECT COALESCE(MAX(seq),0) FROM server_change_log")
        Dim change = ItemEvent(branch, "50001")
        Dim store As New SyncServerStore(_db.ConnectionString)

        Assert.Equal("acked", store.Record(change, Nothing).Status)
        Assert.Equal("duplicate", store.Record(change, Nothing).Status)

        Assert.Equal(1L, _db.ScalarLong(
            "SELECT COUNT(*) FROM server_sync_events WHERE branch_id=@b", New With {.b = branch}))
        Assert.Equal(1L, _db.ScalarLong(
            "SELECT COUNT(*) FROM server_change_log WHERE origin_branch_id=@b", New With {.b = branch}))

        ' The originating branch must not receive its own change back.
        Dim own = store.Pull(baseSeq, 200, branch)
        Assert.Equal(0, own.Changes.Count)
        Assert.Equal(baseSeq, own.NextSeq)

        Dim remote = store.Pull(baseSeq, 200, otherBranch)
        Assert.Equal(1, remote.Changes.Count)
        Assert.Equal(change.EntityId, remote.Changes(0).EntityId)
        Assert.Equal("upsert", remote.Changes(0).Op)
        Assert.True(remote.NextSeq > 0)
    End Sub

    <Fact>
    Public Sub PushRejectsInvalidEventWithoutWriting()
        Dim store As New SyncServerStore(_db.ConnectionString)
        Dim bad = ItemEvent(920010L, "50010")
        bad.BranchId = 0

        Dim ack = store.Record(bad, Nothing)
        Assert.Equal("rejected", ack.Status)
        Assert.False(String.IsNullOrWhiteSpace(ack.Error))
        Assert.Equal(0L, _db.ScalarLong(
            "SELECT COUNT(*) FROM server_sync_events WHERE event_id=@id", New With {.id = bad.EventId}))
    End Sub

    <Fact>
    Public Sub DeleteEventIsPublishedAsDelete()
        Dim branch As Long = 920020L
        Dim e = ItemEvent(branch, "50020")
        e.EventType = "item.deleted"
        Dim store As New SyncServerStore(_db.ConnectionString)

        Assert.Equal("acked", store.Record(e, Nothing).Status)

        Dim remote = store.Pull(0, 200, 920021L)
        Dim change = remote.Changes.Find(Function(c) c.EntityId = e.EntityId)
        Assert.NotNull(change)
        Assert.Equal("delete", change.Op)
    End Sub
End Class
