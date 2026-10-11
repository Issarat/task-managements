using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using TaskManager.Application.Interfaces;
using TaskManager.Application.Models;
using TaskManager.Domain.Enums;
using TaskManager.Domain.Extensions;
using TaskManager.WebUI.Models;
using TaskManager.WebUI.Services;

namespace TaskManager.WebUI.Controllers;

public sealed class BoardsController(
    GuestCookieService guestCookieService,
    BoardOwnerCookieService boardOwnerCookieService,
    IGuestService guestService,
    IBoardService boardService,
    ITaskService taskService,
    IStringLocalizer<SharedResource> text) : Controller
{
    [HttpGet("/boards")]
    [HttpGet("/boards/{publicId}")]
    public async Task<IActionResult> Index(
        string? publicId,
        CancellationToken cancellationToken)
    {
        var guest = await GetCurrentGuestAsync(cancellationToken);
        if (guest is null)
        {
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        if (string.IsNullOrWhiteSpace(publicId))
        {
            var existingBoards = await boardService.GetListBySessionIdAsync(
                guest.Id,
                cancellationToken);

            return existingBoards.Count == 0
                ? RedirectToAction(nameof(HomeController.Index), "Home")
                : RedirectToAction(
                    nameof(Index),
                    new { publicId = existingBoards[0].PublicId });
        }

        var board = await boardService.GetAsync(
            publicId,
            guest.Id,
            cancellationToken);

        if (board is null)
        {
            return NotFound();
        }

        var navigationBoards = await boardService.GetListBySessionIdAsync(
            guest.Id,
            cancellationToken);

        var canManage = !string.IsNullOrWhiteSpace(
            boardOwnerCookieService.GetToken(board.Board.PublicId));

        return View(MapPage(
            board,
            navigationBoards,
            guest,
            canManage));
    }

    [HttpPost("/boards/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string? name,
        string? description,
        CancellationToken cancellationToken)
    {
        var guest = await GetCurrentGuestAsync(cancellationToken);
        if (guest is null)
        {
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        var created = await boardService.CreateAsync(
            new CreateBoardRequestModel(
                guest.Id,
                string.IsNullOrWhiteSpace(name) ? text["BoardDefaultName"].Value : name,
                description,
                guest.ExpiresAtUtc),
            cancellationToken);

        boardOwnerCookieService.SetToken(
            created.Board.PublicId,
            created.OwnerToken,
            created.Board.ExpiresAtUtc);

        return RedirectToAction(
            nameof(Index),
            new { publicId = created.Board.PublicId });
    }

    [HttpPost("/boards/{publicId}/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        string publicId,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        var ownerToken = boardOwnerCookieService.GetToken(publicId);
        if (string.IsNullOrWhiteSpace(ownerToken))
        {
            return Forbid();
        }

        try
        {
            var updated = await boardService.UpdateAsync(
                publicId,
                ownerToken,
                new UpdateBoardRequestModel(name, description),
                cancellationToken);

            return updated is null
                ? NotFound()
                : RedirectToAction(nameof(Index), new { publicId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("/boards/{publicId}/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(
        string publicId,
        CancellationToken cancellationToken)
    {
        var guest = await GetCurrentGuestAsync(cancellationToken);
        if (guest is null)
        {
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        var ownerToken = boardOwnerCookieService.GetToken(publicId);
        if (string.IsNullOrWhiteSpace(ownerToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var reset = await boardService.ResetAsync(
                publicId, ownerToken, cancellationToken);

            return reset
                ? RedirectToAction(nameof(Index), new { publicId })
                : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }


    [HttpPost("/boards/{publicId}/tasks/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTask(
        string publicId,
        [Bind(Prefix = "CreateTask")] CreateTaskFormModel form,
        CancellationToken cancellationToken)
    {
        var guest = await GetCurrentGuestAsync(cancellationToken);
        if (guest is null)
        {
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        var board = await boardService.GetAsync(publicId, guest.Id, cancellationToken);
        if (board is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(form.Title) || form.Title.Trim().Length > 60)
            ModelState.AddModelError("CreateTask.Title", text["TaskTitleValidation"]);
        if (form.DescriptionMarkdown?.Length > 20_000)
            ModelState.AddModelError("CreateTask.DescriptionMarkdown", text["TaskDescriptionValidation"]);
        if (!Enum.IsDefined(form.Priority))
            ModelState.AddModelError("CreateTask.Priority", text["TaskPriorityValidation"]);
        if (!board.Columns.Any(column => column.ColumnKey == "backlog"))
            ModelState.AddModelError(string.Empty, text["TaskBacklogMissing"]);
        if (form.AssignedToParticipantId.HasValue
            && !board.Participants.Any(participant =>
                participant.BoardParticipantId == form.AssignedToParticipantId.Value))
            ModelState.AddModelError("CreateTask.AssignedToParticipantId", text["TaskAssigneeValidation"]);
        if (form.StartDate.HasValue && form.DueDate.HasValue
            && form.StartDate.Value > form.DueDate.Value)
            ModelState.AddModelError("CreateTask.DueDate", text["TaskDateValidation"]);

        if (ModelState.IsValid)
        {
            try
            {
                var taskId = await taskService.CreateAsync(
                    publicId, guest.Id,
                    new CreateTaskRequestModel(form.Title,
                        form.DescriptionMarkdown, form.Priority,
                        form.AssignedToParticipantId, form.StartDate, form.DueDate),
                    cancellationToken);

                return taskId.HasValue
                    ? RedirectToAction(nameof(Index), new { publicId })
                    : NotFound();
            }
            catch (ArgumentException)
            {
                ModelState.AddModelError(string.Empty, text["TaskInvalidDetails"]);
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, text["TaskConcurrentChange"]);
            }
        }

        var navigationBoards = await boardService.GetListBySessionIdAsync(
            guest.Id, cancellationToken);
        var page = MapPage(board, navigationBoards, guest,
            !string.IsNullOrWhiteSpace(boardOwnerCookieService.GetToken(publicId)))
            with { CreateTask = form, ShowCreateTaskDialog = true };

        return View("Index", page);
    }

    private async Task<GuestResponseModel?> GetCurrentGuestAsync(
        CancellationToken cancellationToken)
    {
        var guestToken = guestCookieService.GetOrCreateToken();
        if (string.IsNullOrWhiteSpace(guestToken))
        {
            return null;
        }

        var guest = await guestService.GetGuestByTokenAsync(
            guestToken,
            cancellationToken);

        if (guest is null)
        {
            guestCookieService.DeleteToken();
        }

        return guest;
    }

    private BoardPageViewModel MapPage(
        BoardResponseModel response,
        IReadOnlyList<BoardNavigationModel> navigationBoards,
        GuestResponseModel guest,
        bool canManage)
    {
        var participantById = response.Participants.ToDictionary(
            participant => participant.BoardParticipantId);

        var members = response.Participants
            .Select(participant => new BoardMemberViewModel(
                participant.BoardParticipantId,
                AvatarExtension.CreateShortName(participant.DisplayName),
                participant.DisplayName,
                participant.AvatarColor))
            .ToArray();

        var columns = response.Columns
            .OrderBy(column => column.SortOrder)
            .Select(column => new BoardColumnViewModel(
                column.BoardColumnId,
                column.Name,
                column.ColumnKey,
                GetColumnTone(column.ColumnKey),
                response.Tasks
                    .Where(task => task.BoardColumnId == column.BoardColumnId)
                    .OrderBy(task => task.SortOrder)
                    .Select(task => MapTask(task, participantById))
                    .ToArray()))
            .ToArray();

        return new BoardPageViewModel(
            response.Board.PublicId,
            response.Board.Name,
            response.Board.Description ?? string.Empty,
            canManage,
            guest,
            navigationBoards
                .Select(board => new BoardNavigationViewModel(
                    board.PublicId,
                    board.Name,
                    board.PublicId == response.Board.PublicId))
                .ToArray(),
            members,
            columns);
    }

    private BoardTaskCardViewModel MapTask(
        BoardTaskModel task,
        IReadOnlyDictionary<Guid, BoardParticipantModel> participantById)
    {
        participantById.TryGetValue(
            task.AssignedToParticipantId ?? Guid.Empty,
            out var assignee);

        var (priorityText, priorityTone) = task.Priority switch
        {
            TaskPriority.Low => (text["BoardPriorityLow"].Value, "low"),
            TaskPriority.Medium => (text["BoardPriorityMedium"].Value, "medium"),
            TaskPriority.High => (text["BoardPriorityHigh"].Value, "high"),
            _ => (text["BoardEmptyValue"].Value, "medium")
        };

        return new BoardTaskCardViewModel(
            task.Title,
            CreateCardDescription(task.DescriptionMarkdown),
            priorityText,
            priorityTone,
            assignee is null
                ? text["BoardEmptyValue"].Value
                : AvatarExtension.CreateShortName(assignee.DisplayName),
            assignee?.DisplayName ?? text["BoardUnassigned"].Value,
            assignee?.AvatarColor ?? "#94a3b8",
            task.StartDate,
            task.DueDate);
    }

    private static string CreateCardDescription(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var firstLine = markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? string.Empty;

        return firstLine.Length <= 90
            ? firstLine
            : $"{firstLine[..87]}...";
    }

    private static string GetColumnTone(string columnKey) =>
        columnKey switch
        {
            "todo" => "purple",
            "in-progress" => "blue",
            "done" => "green",
            _ => "slate"
        };
}
