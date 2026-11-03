using Microsoft.AspNetCore.Mvc;

namespace RsSales.Server.Controllers;

[ApiController]
[Route("api/v1/sync")]
public class SyncController : ControllerBase
{
    private readonly IConfiguration _config;

    public SyncController(IConfiguration config)
    {
        _config = config;
    }

    private string GetConnectionString()
    {
        var cs = _config.GetConnectionString("Default")
                 ?? _config["Database:ConnectionString"]
                 ?? Environment.GetEnvironmentVariable("RSSALES_DB");
        if (string.IsNullOrWhiteSpace(cs))
        {
            var cfg = global::AppConfig.Load();
            cs = cfg.ConnectionString;
        }
        return cs;
    }

    private string? ExtractBearerToken()
    {
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(auth)) return null;
        const string prefix = "Bearer ";
        if (auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return auth.Substring(prefix.Length).Trim();
        }
        return auth.Trim();
    }

    [HttpPost("push")]
    public ActionResult<global::SyncAck> Push([FromBody] global::SyncBatch? batch)
    {
        using var db = global::DbSession.Open(GetConnectionString());
        var token = ExtractBearerToken();
        var authedBranchId = global::SyncStore.ValidateDeviceToken(db.Connection, token ?? string.Empty);
        if (!authedBranchId.HasValue)
        {
            return Unauthorized(new { error = "Invalid or missing device key." });
        }

        var ack = new global::SyncAck
        {
            Accepted = true,
            ServerTime = DateTime.UtcNow
        };

        if (batch?.Events == null || batch.Events.Count == 0)
        {
            return Ok(ack);
        }

        db.BeginTransaction();
        var tx = db.Transaction;

        foreach (var e in batch.Events)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.EventId)) continue;

            var branchId = e.BranchId > 0 ? e.BranchId : authedBranchId.Value;
            var entityType = string.IsNullOrWhiteSpace(e.EntityType) ? "unknown" : e.EntityType.Trim();
            var entityId = string.IsNullOrWhiteSpace(e.EntityId) ? "0" : e.EntityId.Trim();
            var eventType = string.IsNullOrWhiteSpace(e.EventType) ? "push" : e.EventType.Trim();
            var payload = e.Payload ?? string.Empty;

            try
            {
                var inserted = global::SyncStore.InsertInbox(
                    db.Connection, tx, e.EventId.Trim(), branchId,
                    entityType, entityId, eventType, payload, "applied", null);

                if (inserted)
                {
                    var op = global::SyncService.ApplyIncomingEvent(db.Connection, tx, e);
                    global::SyncStore.AppendChange(
                        db.Connection, tx, entityType, entityId, op, payload, branchId);

                    ack.Results.Add(new global::SyncEventAck
                    {
                        EventId = e.EventId,
                        Status = "applied"
                    });
                }
                else
                {
                    ack.Results.Add(new global::SyncEventAck
                    {
                        EventId = e.EventId,
                        Status = "duplicate"
                    });
                }
            }
            catch (Exception ex)
            {
                ack.Results.Add(new global::SyncEventAck
                {
                    EventId = e.EventId,
                    Status = "rejected",
                    Error = ex.Message
                });
            }
        }

        db.Commit();
        return Ok(ack);
    }

    [HttpGet("pull")]
    public ActionResult<global::PullResponse> Pull(
        [FromQuery] string? stream = "master_data",
        [FromQuery] long after = 0,
        [FromQuery] int limit = 200)
    {
        using var db = global::DbSession.Open(GetConnectionString());
        var token = ExtractBearerToken();
        var authedBranchId = global::SyncStore.ValidateDeviceToken(db.Connection, token ?? string.Empty);
        if (!authedBranchId.HasValue)
        {
            return Unauthorized(new { error = "Invalid or missing device key." });
        }

        var rows = global::SyncStore.QueryChanges(db.Connection, after, limit, authedBranchId.Value);
        var resp = new global::PullResponse
        {
            NextSeq = after
        };

        foreach (var row in rows)
        {
            resp.Changes.Add(new global::PullChange
            {
                Seq = row.Seq,
                EntityType = row.EntityType,
                EntityId = row.EntityId,
                Op = row.Op,
                Payload = row.Payload
            });
            if (row.Seq > resp.NextSeq)
            {
                resp.NextSeq = row.Seq;
            }
        }

        return Ok(resp);
    }
}
