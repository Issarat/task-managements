using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Application.Services;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;
using TaskManager.Infrastructure;
using TaskManager.Infrastructure.Persistence;

// Explicit opt-in. All SQL writes are restricted to a newly generated database,
// never TaskManager or a connection string supplied by the production app.
if (args.Length != 1 || args[0] != "--sql-local")
{
    Console.WriteLine("Opt in with --sql-local to create/remove an isolated test DB on .\\SQLEXPRESS.");
    return;
}
var databaseName = "TaskManager_RowVersionChecks_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder
{
    DataSource = @".\SQLEXPRESS", InitialCatalog = databaseName,
    IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 10
};
if (!databaseName.StartsWith("TaskManager_RowVersionChecks_", StringComparison.Ordinal)
    || connection.InitialCatalog != databaseName)
    throw new InvalidOperationException("Unsafe test database target.");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = connection.ConnectionString }).Build();
var hostBuilder = Host.CreateApplicationBuilder();
hostBuilder.Logging.ClearProviders();
hostBuilder.Services.AddInfrastructure(config);
using var host = hostBuilder.Build();
var provider = host.Services;
var passed = 0;
void Check(bool success, string name)
{
    if (!success) throw new Exception(name);
    passed++; Console.WriteLine($"PASS {name}");
}
async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
{
    await using var scope = provider.CreateAsyncScope();
    return await action(scope.ServiceProvider);
}
Task<BoardResponseModel> Read(string id, Guid guest) => InScope(async sp =>
    (await new BoardService(sp.GetRequiredService<IBoardRepository>()).GetAsync(id, guest))!);
TaskService Tasks(IServiceProvider sp, ITaskRepository? repository = null) => new(
    sp.GetRequiredService<IBoardRepository>(), repository ?? sp.GetRequiredService<ITaskRepository>());
UpdateTaskRequestModel Edit(BoardTaskModel task, string title) => new(task.BoardColumnId,
    title, task.DescriptionMarkdown, task.Priority, task.AssignedToParticipantId, task.StartDate, task.DueDate);
async Task<bool> Conflict(Func<Task> action)
{
    try { await action(); return false; }
    catch (DbUpdateConcurrencyException) { return true; }
}
try
{
    await InScope(async sp => await sp.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync());
    var guest = new AnonymousUser { SessionTokenHash = new string('a', 64), DisplayName = "Test owner",
        AvatarColor = "#123456", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) };
    var created = await InScope(async sp =>
    {
        await sp.GetRequiredService<IAnonymousUserRepository>().AddAsync(guest);
        return await new BoardService(sp.GetRequiredService<IBoardRepository>()).CreateAsync(
            new(guest.Id, "Concurrency test", null, guest.ExpiresAtUtc));
    });
    var id = created.Board.PublicId;
    var first = (await InScope(sp => Tasks(sp).CreateAsync(id, guest.Id,
        new("First", null, TaskPriority.Low, null, null, null))))!.Value;
    var second = (await InScope(sp => Tasks(sp).CreateAsync(id, guest.Id,
        new("Second", null, TaskPriority.Low, null, null, null))))!.Value;
    var initial = await Read(id, guest.Id);
    Check(initial.Board.RowVersion.Length == 8 && initial.Tasks.All(task => task.RowVersion.Length == 8),
        "real SQL Server generates eight-byte board and task row versions");

    // Both services load their board and task BEFORE either saves, reproducing
    // the old shared-Board RowVersion failure deterministically, not by timing.
    async Task<int> ParallelEdits(Guid taskOne, Guid taskTwo)
    {
        var gate = new TwoReaderGate();
        async Task<bool> Save(Guid taskId, string title) => await InScope(async sp =>
        {
            var task = (await Read(id, guest.Id)).Tasks.Single(item => item.TaskItemId == taskId);
            var repository = new GatedTaskRepository(sp.GetRequiredService<ITaskRepository>(), gate);
            try { return await Tasks(sp, repository).UpdateAsync(id, guest.Id, taskId, Edit(task, title), task.RowVersion); }
            catch (DbUpdateConcurrencyException) { return false; }
        });
        return (await Task.WhenAll(Save(taskOne, "Parallel A"), Save(taskTwo, "Parallel B"))).Count(saved => saved);
    }
    Check(await ParallelEdits(first, second) == 2, "different tasks with the same original board version both save");
    Check(await ParallelEdits(first, first) == 1, "same-task concurrent edits allow exactly one save");

    var beforeCreate = await Read(id, guest.Id);
    var concurrentAdds = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => InScope(sp =>
        Tasks(sp).CreateAsync(id, guest.Id, new($"Create {index}", null, TaskPriority.Low, null, null, null)))));
    var afterCreate = await Read(id, guest.Id);
    Check(concurrentAdds.All(task => task.HasValue)
        && afterCreate.Tasks.Count == beforeCreate.Tasks.Count + 4
        && afterCreate.Tasks.Select(task => task.SortOrder).Distinct().Count() == afterCreate.Tasks.Count,
        "parallel task creates all save with distinct append ordering");

    var stale = afterCreate.Tasks.Single(task => task.TaskItemId == second);
    await InScope(sp => Tasks(sp).UpdateAsync(id, guest.Id, second, Edit(stale, "Latest task"), stale.RowVersion));
    var activityBeforeFailure = (await Read(id, guest.Id)).Board.RowVersion;
    Check(await Conflict(() => InScope(sp => Tasks(sp).UpdateAsync(id, guest.Id, second, Edit(stale, "Stale overwrite"), stale.RowVersion))),
        "stale task update cannot overwrite the newest values");
    Check(await Conflict(() => InScope(sp => Tasks(sp).DeleteAsync(id, guest.Id, second, stale.RowVersion))),
        "stale task delete cannot remove the newest task");
    var target = (await Read(id, guest.Id)).Columns.Single(column => column.ColumnKey == "todo").BoardColumnId;
    Check(await Conflict(() => InScope(sp => Tasks(sp).MoveAsync(id, guest.Id, second, target, stale.RowVersion))),
        "stale task move cannot change the newest column");
    var unchanged = await Read(id, guest.Id);
    Check(unchanged.Tasks.Single(task => task.TaskItemId == second).Title == "Latest task"
        && unchanged.Board.RowVersion.SequenceEqual(activityBeforeFailure),
        "failed task writes roll back board activity and keep current task data");

    var oldBoardVersion = unchanged.Board.RowVersion;
    var renamed = await InScope(sp => new BoardService(sp.GetRequiredService<IBoardRepository>()).UpdateAsync(
        id, created.OwnerToken, new("Renamed", "Latest description", oldBoardVersion)));
    Check(renamed!.RowVersion.Length == 8 && !renamed.RowVersion.SequenceEqual(oldBoardVersion),
        "board metadata update returns its new SQL row version");
    // Even identical metadata must execute the version comparison, not become
    // a tracker no-op that would allow Reset to skip its conditional UPDATE.
    var noOpVersion = await InScope(async sp =>
    {
        var repository = sp.GetRequiredService<IBoardRepository>();
        var board = (await repository.GetByPublicIdForUpdateAsync(id))!;
        await repository.SaveAsync(board, renamed.RowVersion);
        return board.RowVersion;
    });
    Check(!noOpVersion.SequenceEqual(renamed.RowVersion),
        "identical board metadata still issues a conditional SQL update");
    Check(await Conflict(() => InScope(async sp =>
    {
        var repository = sp.GetRequiredService<IBoardRepository>();
        var board = (await repository.GetByPublicIdForUpdateAsync(id))!;
        await repository.SaveAsync(board, renamed.RowVersion);
        return true;
    })), "identical metadata with a stale version cannot skip concurrency checking");
    Check(await Conflict(() => InScope(sp => new BoardService(sp.GetRequiredService<IBoardRepository>()).UpdateAsync(
        id, created.OwnerToken, new("Old tab", "Stale description", oldBoardVersion)))),
        "stale owner form cannot overwrite renamed board metadata");
    var current = await Read(id, guest.Id);
    Check(await Conflict(() => InScope(sp => new BoardService(sp.GetRequiredService<IBoardRepository>()).ResetAsync(
        id, created.OwnerToken, oldBoardVersion))), "stale reset confirmation is rejected");
    var afterRejectedReset = await Read(id, guest.Id);
    Check(afterRejectedReset.Tasks.Count == current.Tasks.Count && afterRejectedReset.Board.Name == "Renamed"
        && afterRejectedReset.Board.RowVersion.SequenceEqual(current.Board.RowVersion),
        "rejected reset deletes zero tasks and rolls back metadata changes");

    // A task save remains valid after unrelated board metadata changes.
    var unaffected = unchanged.Tasks.Single(task => task.TaskItemId == first);
    Check(await InScope(sp => Tasks(sp).UpdateAsync(id, guest.Id, first, Edit(unaffected, "After rename"), unaffected.RowVersion)),
        "board metadata changes do not invalidate another task's row version");
    var latest = await Read(id, guest.Id);
    Check(await InScope(sp => new BoardService(sp.GetRequiredService<IBoardRepository>()).ResetAsync(
        id, created.OwnerToken, latest.Board.RowVersion)), "fresh reset confirmation succeeds");
    var empty = await Read(id, guest.Id);
    Check(empty.Tasks.Count == 0 && empty.Columns.Count == 4 && empty.Participants.Count == 1,
        "successful reset removes tasks only and never reseeds");
    Console.WriteLine($"{passed} real SQL Server concurrency checks passed.");
}
finally
{
    // Generated, validated, exact target only; the user's TaskManager DB is never used.
    await InScope(async sp => await sp.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync());
    Console.WriteLine($"Removed isolated test database {databaseName}; no demo database was changed.");
}

sealed class TwoReaderGate
{
    int arrived;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task Arrive(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }
}
sealed class GatedTaskRepository(ITaskRepository inner, TwoReaderGate gate) : ITaskRepository
{
    public async Task<TaskItem?> GetAsync(Guid boardId, Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await inner.GetAsync(boardId, taskId, cancellationToken);
        await gate.Arrive(cancellationToken);
        return task;
    }
    public Task SaveAsync(TaskItem task, byte[] rowVersion, bool delete, CancellationToken cancellationToken = default) =>
        inner.SaveAsync(task, rowVersion, delete, cancellationToken);
    public Task AddAsync(Board board, TaskItem task, CancellationToken cancellationToken = default) =>
        inner.AddAsync(board, task, cancellationToken);
}
