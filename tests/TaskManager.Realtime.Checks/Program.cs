using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Enums;
using TaskManager.WebUI;
using TaskManager.WebUI.Controllers;
using TaskManager.WebUI.Hubs;
using TaskManager.WebUI.Models;
using TaskManager.WebUI.Services;
using TaskManager.WebUI.BackgroundServices;

// Isolated host with fake application services: never connects to the demo DB.
var guests = new FakeGuests();
var boards = new FakeBoards(guests);
var tasks = new FakeTasks();
var liveClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSignalR();
builder.Services.AddSingleton<TimeProvider>(liveClock);
builder.Services.AddSingleton<BoardPresenceService>();
builder.Services.AddSingleton<IGuestService>(guests);
builder.Services.AddSingleton<IBoardService>(boards);
await using var app = builder.Build();
app.MapHub<BoardHub>("/hubs/board");
await app.StartAsync();
var address = app.Urls.Single();
await using var a = await HubSocket.Connect(address, "test-a");
await using var b = await HubSocket.Connect(address, "test-b");
await using var other = await HubSocket.Connect(address, "test-other");
await using var missing = await HubSocket.Connect(address, null);
await using var expired = await HubSocket.Connect(address, "expired");
var passed = 0;
void Pass(string name) { passed++; Console.WriteLine($"PASS {name}"); }
void Require(bool condition, string name) { if (!condition) throw new Exception(name); }

Require(await a.Join("board-a"), "A joins");
Require(await b.Join("board-a"), "B joins");
Require(await other.Join("board-b"), "other joins");
Pass("real WebSocket connections join board-specific groups");
Require(!await missing.Join("board-a"), "missing cookie rejected");
Require(!await expired.Join("board-a"), "expired guest rejected");
Require(!await other.Join("board-a"), "non-member rejected");
Require(!await a.Join("missing"), "missing board rejected");
Require(!await a.Join(new string('x', 33)), "invalid ID rejected");
Pass("hub rejects missing/expired guests, non-members and invalid boards");

Require(b.JoinSnapshot?.Members.Count == 2, "joining returns the current online roster");
Require((await a.Presence(2, b.JoinSnapshot!.Revision))?.Members.Count == 2, "A sees B join live");
Require(other.JoinSnapshot?.Members.Count == 1 && other.JoinSnapshot.Members[0].GuestId == guests.Other,
    "other board has its own roster");
Pass("presence broadcasts joins and isolates each board roster");
await using var duplicateTab = await HubSocket.Connect(address, "test-a");
Require(await duplicateTab.Join("board-a"), "duplicate tab joins");
Require(duplicateTab.JoinSnapshot?.Members.Count == 2, "same guest is counted once");
Require(await duplicateTab.Join("board-a"), "repeated join is idempotent");
Require(duplicateTab.JoinSnapshot?.Members.Count == 2, "repeated join has no duplicate avatar");
var duplicateRevision = duplicateTab.JoinSnapshot!.Revision;
await duplicateTab.DisposeAsync();
Require((await b.Presence(2, duplicateRevision + 1))?.Members.Any(member => member.GuestId == guests.A) == true,
    "closing one tab keeps the guest's other connection online");
Pass("multiple tabs and repeated joins show one avatar until the last tab closes");

var realHub = app.Services.GetRequiredService<IHubContext<BoardHub>>();
var localizer = new FakeText();
BoardsController Controller(bool owner = true, IHubContext<BoardHub>? hub = null, bool guest = true)
{
    var context = new DefaultHttpContext();
    context.Request.Headers.Cookie = (guest ? "taskflow_guest=test-a; " : "")
        + (owner ? "taskflow_board_owner_board-a=test-owner" : "");
    var accessor = new HttpContextAccessor { HttpContext = context };
    return new BoardsController(new GuestCookieService(accessor), new BoardOwnerCookieService(accessor),
        guests, boards, tasks, localizer, hub ?? realHub, NullLogger<BoardsController>.Instance)
    { ControllerContext = new ControllerContext { HttpContext = context } };
}
async Task Changed(Func<Task<IActionResult>> action, string name)
{
    tasks.Saved = boards.Saved = false;
    var result = await action();
    Require(result is RedirectToActionResult, $"{name}: redirect");
    Require(tasks.Saved || boards.Saved, $"{name}: persistence occurred");
    Require(await a.Notification() == "board-a", $"{name}: A notified");
    Require(await b.Notification() == "board-a", $"{name}: B notified");
    Require(await other.Notification(150) is null, $"{name}: other board isolated");
    Pass(name);
}
await Changed(() => Controller().CreateTask("board-a", new CreateTaskFormModel { Title = "New task" }, default),
    "create persists and notifies only its board");
await Changed(() => Controller().UpdateTask("board-a", new EditTaskFormModel {
    TaskItemId = boards.TaskId, BoardColumnId = boards.ColumnId, Title = "Edited",
    RowVersion = Convert.ToBase64String(new byte[8]) }, default), "update broadcasts after save");
await Changed(() => Controller().DeleteTask("board-a", boards.TaskId,
    Convert.ToBase64String(new byte[8]), default), "delete broadcasts after save");
await Changed(() => Controller().MoveTask("board-a", boards.TaskId, boards.ColumnId,
    Convert.ToBase64String(new byte[8]), default), "move broadcasts after save");
await Changed(() => Controller().Update("board-a", "Renamed", null, default), "rename broadcasts after save");
await Changed(() => Controller().Reset("board-a", default), "reset broadcasts after save");
var denied = await Controller(owner: false).Reset("board-a", default);
Require(denied is StatusCodeResult { StatusCode: 403 }, "owner-only reset");
Require(await b.Notification(150) is null, "no notification on denied reset");
Pass("participant reset is forbidden and sends no event");

var invalid = await Controller().CreateTask("board-a", new CreateTaskFormModel { Title = "" }, default);
Require(invalid is ViewResult, "validation returns view");
Require(await b.Notification(150) is null, "no event on validation failure");
Pass("invalid task sends no change event");
tasks.ReturnMissing = true;
var notFound = await Controller().DeleteTask("board-a", boards.TaskId,
    Convert.ToBase64String(new byte[8]), default);
Require(notFound is NotFoundResult, "missing task");
Require(await b.Notification(150) is null, "no event on unsuccessful save");
tasks.ReturnMissing = false;
Pass("unsuccessful save sends no event");

var failedDelivery = await Controller(hub: new BrokenHub()).DeleteTask("board-a", boards.TaskId,
    Convert.ToBase64String(new byte[8]), default);
Require(failedDelivery is RedirectToActionResult && tasks.Saved, "delivery failure must not undo save");
Pass("notification failure does not turn a committed save into an error");
Require(await Controller(guest: false).Snapshot("board-a", default) is UnauthorizedResult,
    "snapshot requires guest");
Require(await Controller().Snapshot("board-b", default) is NotFoundResult, "snapshot checks membership");
Require(await Controller().Snapshot("board-a", default) is ViewResult, "snapshot renders valid board");
Pass("snapshot rejects missing guest/non-member and renders valid board");

await a.DisposeAsync();
Require((await b.Presence(1, duplicateRevision + 1))?.Members.Single().GuestId == guests.B,
    "last connection leaving removes only that guest");
Pass("disconnecting the last tab removes the avatar live");
await using var reconnect = await HubSocket.Connect(address, "test-a");
Require(await reconnect.Join("board-a"), "rejoin after reconnect");
await realHub.Clients.Group("board:board-a").SendAsync("BoardChanged", "board-a");
Require(await reconnect.Notification() == "board-a", "reconnected client receives event");
Pass("new connection rejoins and receives notifications");
Require(reconnect.JoinSnapshot?.Members.Count == 2, "reconnect restores the avatar");
Pass("reconnect restores presence alongside existing users");

var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
var state = new BoardPresenceService(clock);
var profile = new OnlineBoardMemberModel(guests.A, "A", "A", "#123456");
var lifetime = clock.GetUtcNow().UtcDateTime;
state.Join("one", "board-a", profile, lifetime.AddSeconds(10));
state.Join("two", "board-a", profile, lifetime.AddSeconds(20));
state.Join("other", "board-b", profile, lifetime.AddSeconds(30));
clock.Advance(TimeSpan.FromSeconds(10));
var expiration = state.RemoveExpired();
Require(expiration.Connections.Count == 1 && expiration.Connections[0].ConnectionId == "one", "inclusive expiry");
Require(expiration.Snapshots.Single().Members.Count == 1, "another unexpired tab remains online");
Require(expiration.Snapshots.Single().PublicId == "board-a", "expiry only broadcasts affected boards");
clock.Advance(TimeSpan.FromSeconds(10));
Require(state.RemoveExpired().Snapshots.Single().Members.Count == 0, "expired final tab is offline");
Require(state.RemoveExpired().Connections.Count == 0, "expiry is idempotent");
Require(state.Leave("missing") is null, "unknown disconnect is harmless");
Require(state.Leave("other")?.Members.Count == 0, "empty roster on final leave");
Pass("expiry is inclusive, deduplicated, isolated, bounded to live connections and idempotent");
try { state.Join("expired", "board-a", profile, lifetime); throw new Exception("Expired join accepted"); }
catch (InvalidOperationException) { }
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { state.Join("cancelled", "board-a", profile, lifetime.AddHours(1), cancelled.Token); throw new Exception("Cancelled join accepted"); }
    catch (OperationCanceledException) { }
}
Pass("expired or cancelled joins never add online users");

// Exercise the production expiry worker over real sockets without sleeping for
// an hour and without touching either the demo DB or real guest identities.
await b.Notification(); // drain the preceding manual BoardChanged broadcast
liveClock.Advance(TimeSpan.FromHours(2));
using (var expiryWorker = new BoardPresenceBackgroundService(
    app.Services.GetRequiredService<BoardPresenceService>(), realHub, liveClock,
    NullLogger<BoardPresenceBackgroundService>.Instance))
{
    await expiryWorker.StartAsync(default);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await Task.WhenAll(b.SessionExpired, other.SessionExpired, reconnect.SessionExpired).WaitAsync(deadline.Token);
    await realHub.Clients.Group("board:board-a").SendAsync("BoardChanged", "board-a");
    Require(await b.Notification(150) is null, "expired connections leave the SignalR group");
    await expiryWorker.StopAsync(default);
}
Require(app.Services.GetRequiredService<BoardPresenceService>().RemoveExpired().Connections.Count == 0,
    "worker removes expired connections");
Pass("expiry worker notifies expired browsers and removes real group memberships");
Console.WriteLine($"{passed} server checks passed; no real database used.");
// Close clients before the host so the test's receive loops can stop cleanly.
foreach (var client in new[] { a, b, other, missing, expired, reconnect })
    await client.DisposeAsync();
await app.StopAsync();

sealed class HubSocket : IAsyncDisposable
{
    readonly ClientWebSocket socket = new();
    readonly Channel<JsonElement> messages = Channel.CreateUnbounded<JsonElement>();
    readonly Channel<BoardPresenceSnapshot> presence = Channel.CreateUnbounded<BoardPresenceSnapshot>();
    readonly TaskCompletionSource sessionExpired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task SessionExpired => sessionExpired.Task;
    public BoardPresenceSnapshot? JoinSnapshot { get; private set; }
    readonly CancellationTokenSource shutdown = new();
    Task? reader;
    int invocation;
    bool disposed;
    public static async Task<HubSocket> Connect(string address, string? token)
    {
        var client = new HubSocket();
        if (token is not null) client.socket.Options.SetRequestHeader("Cookie", $"taskflow_guest={token}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.socket.ConnectAsync(new Uri(address.Replace("http:", "ws:") + "/hubs/board"), timeout.Token);
        client.reader = client.ReadLoop();
        await client.Send("{\"protocol\":\"json\",\"version\":1}");
        await client.messages.Reader.ReadAsync(timeout.Token); // handshake
        return client;
    }
    async Task Send(string value) => await socket.SendAsync(Encoding.UTF8.GetBytes(value + '\u001e'),
        WebSocketMessageType.Text, true, shutdown.Token);
    async Task ReadLoop()
    {
        var buffer = new byte[8192];
        var pending = "";
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, shutdown.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                pending += Encoding.UTF8.GetString(buffer, 0, result.Count);
                int separator;
                while ((separator = pending.IndexOf('\u001e')) >= 0)
                {
                    using var json = JsonDocument.Parse(pending[..separator]);
                    var message = json.RootElement;
                    if (message.TryGetProperty("target", out var target)
                        && target.GetString() == "BoardPresenceChanged")
                        await presence.Writer.WriteAsync(message.GetProperty("arguments")[0]
                            .Deserialize<BoardPresenceSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
                    else if (message.TryGetProperty("target", out target) && target.GetString() == "BoardSessionExpired")
                        sessionExpired.TrySetResult();
                    else
                        await messages.Writer.WriteAsync(message.Clone());
                    pending = pending[(separator + 1)..];
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) when (shutdown.IsCancellationRequested) { }
        finally { messages.Writer.TryComplete(); presence.Writer.TryComplete(); }
    }
    public async Task<bool> Join(string id)
    {
        var callId = (++invocation).ToString();
        await Send(JsonSerializer.Serialize(new { type = 1, invocationId = callId, target = "JoinBoard", arguments = new[] { id } }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await messages.Reader.ReadAsync(timeout.Token);
            if (message.TryGetProperty("invocationId", out var value) && value.GetString() == callId)
            {
                if (message.TryGetProperty("result", out var result))
                    JoinSnapshot = result.Deserialize<BoardPresenceSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return !message.TryGetProperty("error", out _);
            }
        }
    }
    public async Task<string?> Notification(int milliseconds = 3000)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        try
        {
            while (true)
            {
                var message = await messages.Reader.ReadAsync(timeout.Token);
                if (message.TryGetProperty("target", out var target) && target.GetString() == "BoardChanged")
                    return message.GetProperty("arguments")[0].GetString();
            }
        }
        catch (OperationCanceledException) { return null; }
    }
    public async Task<BoardPresenceSnapshot?> Presence(int expectedCount, long minimumRevision)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                var snapshot = await presence.Reader.ReadAsync(timeout.Token);
                if (snapshot.Revision >= minimumRevision && snapshot.Members.Count == expectedCount)
                    return snapshot;
            }
        }
        catch (OperationCanceledException) { return null; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; shutdown.Cancel(); socket.Abort();
        if (reader is not null) await reader;
        socket.Dispose(); shutdown.Dispose();
    }
}

sealed class FakeGuests : IGuestService
{
    public Guid A { get; } = Guid.NewGuid();
    public Guid B { get; } = Guid.NewGuid();
    public Guid Other { get; } = Guid.NewGuid();
    public Task<CreatedGuestModel> GenerateGuestAsync(CancellationToken cancellationToken) => throw new Exception("Must not create a guest");
    public Task<GuestResponseModel?> GetGuestByTokenAsync(string token, CancellationToken cancellationToken)
    {
        var id = token switch { "test-a" => A, "test-b" => B, "test-other" => Other, _ => Guid.Empty };
        return Task.FromResult(id == Guid.Empty ? null : new GuestResponseModel(id, "Test guest", "TG", "#123456",
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
    }
}
sealed class FakeBoards(FakeGuests guests) : IBoardService
{
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid ColumnId { get; } = Guid.NewGuid();
    public bool Saved { get; set; }
    public Task<BoardModel?> GetPreviewAsync(string publicId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BoardModel?>(publicId is "board-a" or "board-b" ? Metadata(publicId) : null);
    public Task<CreatedBoardModel> CreateAsync(CreateBoardRequestModel request, CancellationToken cancellationToken = default) => throw new Exception("Must not seed a board");
    public Task<IReadOnlyList<BoardNavigationModel>> GetListBySessionIdAsync(Guid guestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BoardNavigationModel>>([new(Guid.NewGuid(), guestId == guests.Other ? "board-b" : "board-a", "Test")]);
    public Task<BoardResponseModel?> GetAsync(string publicId, Guid guestId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BoardResponseModel?>(new(Metadata(publicId), [], [new(ColumnId, "Backlog", "backlog", 1)],
            [new(TaskId, ColumnId, "Task", null, TaskPriority.Low, 1, null, Guid.NewGuid(), null, null, new byte[8])]));
    public Task<bool> ResetAsync(string publicId, string ownerToken, CancellationToken cancellationToken = default) { Saved = true; return Task.FromResult(true); }
    public Task<BoardModel?> UpdateAsync(string publicId, string ownerToken, UpdateBoardRequestModel request, CancellationToken cancellationToken = default)
    { Saved = true; return Task.FromResult<BoardModel?>(Metadata(publicId)); }
    static BoardModel Metadata(string id) => new(Guid.NewGuid(), id, "Test", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
}
sealed class FakeTasks : ITaskService
{
    public bool Saved { get; set; }
    public bool ReturnMissing { get; set; }
    bool Save() { Saved = !ReturnMissing; return Saved; }
    public Task<Guid?> CreateAsync(string publicId, Guid guestId, CreateTaskRequestModel request, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(Save() ? Guid.NewGuid() : null);
    public Task<bool> UpdateAsync(string publicId, Guid guestId, Guid taskId, UpdateTaskRequestModel request, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(Save());
    public Task<bool> DeleteAsync(string publicId, Guid guestId, Guid taskId, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(Save());
    public Task<bool> MoveAsync(string publicId, Guid guestId, Guid taskId, Guid targetColumnId, byte[] rowVersion, CancellationToken cancellationToken = default) => Task.FromResult(Save());
}
sealed class FakeText : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] args] => new(name, name);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
sealed class BrokenHub : IHubContext<BoardHub>
{
    public IHubClients Clients => throw new InvalidOperationException("Synthetic notification failure");
    public IGroupManager Groups => throw new NotSupportedException();
}
sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset now = initial;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan delta) => now += delta;
}
