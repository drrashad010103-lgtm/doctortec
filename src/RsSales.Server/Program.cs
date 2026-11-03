using System.Security.Cryptography;
using System.Text;
using RsSales.Server.Sync;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ---------------------------------------------------------------------------
// Sync configuration is validated before the app starts. Enabling sync without
// a token or a durable store stops startup instead of exposing a half-working
// endpoint. When disabled, every /api/v1/sync request is answered with 404.
// ---------------------------------------------------------------------------
var syncEnabled = builder.Configuration.GetValue<bool>("Sync:Enabled");
var syncToken = builder.Configuration["Sync:ApiToken"] ?? string.Empty;
var syncConnectionString = builder.Configuration["Sync:ConnectionString"] ?? string.Empty;

if (syncEnabled)
{
    if (string.IsNullOrWhiteSpace(syncToken))
    {
        throw new InvalidOperationException(
            "Sync:ApiToken must be set when Sync:Enabled is true.");
    }

    if (string.IsNullOrWhiteSpace(syncConnectionString))
    {
        throw new InvalidOperationException(
            "Sync:ConnectionString must be set when Sync:Enabled is true.");
    }

    builder.Services.AddSingleton(new SyncServerStore(syncConnectionString));
}

var app = builder.Build();

// ---------------------------------------------------------------------------
// Sync endpoint protection.
// The sync API is disabled unless BOTH Sync:Enabled=true and a non-empty
// Sync:ApiToken are configured. This fails closed: an operator cannot expose
// an unauthenticated endpoint by accident.
// Clients send: Authorization: Bearer <token>
// ---------------------------------------------------------------------------

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/v1/sync"))
    {
        await next();
        return;
    }

    if (!syncEnabled || string.IsNullOrWhiteSpace(syncToken))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "Sync is disabled on this server." });
        return;
    }

    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing bearer token." });
        return;
    }

    var provided = header["Bearer ".Length..].Trim();
    if (!TokenEquals(provided, syncToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid sync token." });
        return;
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

static bool TokenEquals(string provided, string expected)
{
    var a = Encoding.UTF8.GetBytes(provided);
    var b = Encoding.UTF8.GetBytes(expected);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}
