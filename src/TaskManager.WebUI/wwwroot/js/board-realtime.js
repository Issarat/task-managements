(() => {
    const board = document.querySelector('[data-board-screen]');
    if (!board || !window.signalR) return;

    const publicId = board.dataset.boardPublicId;
    const refreshUrl = board.dataset.boardRefreshUrl;
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/board')
        .withAutomaticReconnect()
        .build();

    let disposed = false;
    let refreshNeeded = false;
    let refreshing = false;
    let refreshTimer;
    let startTimer;
    let connectionErrorRetry = true;
    const onlineMembers = board.querySelector('[data-board-online-members]');
    let presenceRevision = -1;
    let editingTasks = [];
    const editDialog = document.getElementById('edit-task-dialog');
    let joined = false;
    let presenceEpoch = 0;
    let editingPump = null;
    let editingDirty = false;
    let sentEditingTask = null;

    const renderEditingTasks = () => {
        const active = new Map(editingTasks.map(task => [task.taskItemId, task.editors]));
        for (const card of board.querySelectorAll('.kanban-card[data-task-id]')) {
            const editors = active.get(card.dataset.taskId) ?? [];
            const link = card.querySelector('.kanban-card__open');
            card.classList.toggle('is-being-edited', editors.length > 0);
            if (editors.length) {
                const hint = `${board.dataset.taskEditingHint}: ${editors.map(editor => editor.displayName).join(', ')}`;
                card.style.setProperty('--task-editor-color', /^#[0-9a-f]{6}$/i.test(editors[0].avatarColor)
                    ? editors[0].avatarColor : '#8b5cf6');
                card.title = hint;
                if (link) {
                    link.title = hint;
                    link.setAttribute('aria-description', hint);
                }
            } else {
                card.style.removeProperty('--task-editor-color');
                card.removeAttribute('title');
                link?.removeAttribute('title');
                link?.removeAttribute('aria-description');
            }
        }
    };

    const clearOnlineMembers = () => {
        joined = false;
        presenceEpoch++;
        editingPump = null;
        editingDirty = false;
        sentEditingTask = null;
        editingTasks = [];
        presenceRevision = -1;
        onlineMembers?.replaceChildren();
        renderEditingTasks();
    };

    const renderOnlineMembers = (snapshot) => {
        if (disposed || !onlineMembers || snapshot?.publicId !== publicId
            || !Number.isSafeInteger(snapshot.revision) || snapshot.revision <= presenceRevision
            || !Array.isArray(snapshot.members)) return;

        presenceRevision = snapshot.revision;
        editingTasks = Array.isArray(snapshot.editingTasks) ? snapshot.editingTasks : [];
        renderEditingTasks();
        const members = [...new Map(snapshot.members.map(member => [member.guestId, member])).values()];
        const avatars = members.slice(0, 4).map(member => {
            const avatar = document.createElement('span');
            avatar.className = 'board-member-avatar';
            avatar.textContent = member.shortName;
            avatar.title = `${member.displayName} — ${onlineMembers.dataset.onlineHint}`;
            avatar.setAttribute('aria-label', avatar.title);
            avatar.style.setProperty('--avatar-color', /^#[0-9a-f]{6}$/i.test(member.avatarColor)
                ? member.avatarColor : '#94a3b8');
            return avatar;
        });
        if (members.length > 4) {
            const more = document.createElement('span');
            more.className = 'board-member-more';
            more.textContent = `+${members.length - 4}`;
            more.title = members.slice(4).map(member => member.displayName).join('\n');
            more.setAttribute('aria-label', more.title);
            avatars.push(more);
        }
        onlineMembers.replaceChildren(...avatars);
    };

    // Serialize start/stop: a close while StartEditingTask is pending must send
    // StopEditingTask afterwards, rather than leave a late start visible forever.
    const syncEditing = () => {
        editingDirty = true;
        if (disposed || !joined || editingPump
            || connection.state !== signalR.HubConnectionState.Connected) return;
        const epoch = presenceEpoch;
        editingPump = (async () => {
            while (!disposed && joined && epoch === presenceEpoch) {
                editingDirty = false;
                const taskId = editDialog?.open ? editDialog.dataset.editingTaskId : null;
                if (taskId === sentEditingTask) return;
                const snapshot = taskId
                    ? await connection.invoke('StartEditingTask', publicId, taskId)
                    : await connection.invoke('StopEditingTask', publicId);
                if (epoch !== presenceEpoch || disposed) return;
                sentEditingTask = taskId;
                renderOnlineMembers(snapshot);
            }
        })().catch(error => console.error('Could not update task editing presence', error))
            .finally(() => {
                if (epoch !== presenceEpoch) return;
                editingPump = null;
                if (editingDirty) syncEditing();
            });
    };
    document.addEventListener('board:task-editing-changed', syncEditing);
    document.addEventListener('board:refreshed', renderEditingTasks);

    const stopForUnavailableBoard = async () => {
        connectionErrorRetry = false;
        refreshNeeded = false;
        clearTimeout(refreshTimer);
        refreshTimer = null;
        clearOnlineMembers();
        await connection.stop();
    };

    const scheduleRefresh = (delay = 100) => {
        if (disposed) return;
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(() => {
            refreshTimer = null;
            refreshBoard();
        }, delay);
    };

    const refreshClosedBoardDialogs = (page) => {
        for (const [id, fields] of [
            ['update-board-dialog', ['EditBoard.Name', 'EditBoard.Description', 'EditBoard.RowVersion']],
            ['reset-board-dialog', ['rowVersion']]
        ]) {
            const current = document.getElementById(id);
            const updated = page.getElementById(id);
            // A version may only be refreshed alongside its data, never under
            // an open draft or a reset confirmation the owner is reading.
            if (!current || !updated || current.open) continue;
            for (const name of fields) {
                const input = current.querySelector(`[name="${name}"]`);
                const replacement = updated.querySelector(`[name="${name}"]`);
                if (input && replacement) input.value = replacement.value;
            }
            if (id === 'reset-board-dialog') {
                const description = current.querySelector('#reset-board-description');
                const replacement = updated.querySelector('#reset-board-description');
                if (description && replacement) description.textContent = replacement.textContent;
            }
        }
    };

    const refreshBoard = async () => {
        if (disposed || !refreshNeeded || refreshing) return;
        // Never replace a card during a drag or while a move is submitting.
        if (board.querySelector('.is-dragging') || board.getAttribute('aria-busy') === 'true') {
            scheduleRefresh(250);
            return;
        }

        refreshing = true;
        refreshNeeded = false;
        try {
            // Reuse server-rendered markup and existing cookie checks. Fetch no taskId,
            // and only replace display regions; scripts and dialogs are left untouched.
            const response = await fetch(refreshUrl, {
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if ([401, 403, 404].includes(response.status)) {
                await stopForUnavailableBoard();
                return;
            }
            if (!response.ok) throw new Error(`Board refresh failed: ${response.status}`);
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const updatedBoard = page.querySelector('[data-board-screen]');
            if (!updatedBoard || updatedBoard.dataset.boardPublicId !== publicId) {
                await stopForUnavailableBoard();
                throw new Error('Board or guest session is no longer available.');
            }
            if (disposed) return;
            // A drag could have started while the request was in flight.
            if (board.querySelector('.is-dragging') || board.getAttribute('aria-busy') === 'true') {
                refreshNeeded = true;
                return;
            }

            // Online avatars belong to the live connection, not DB membership markup.
            for (const selector of ['.kanban-grid', '.board-heading', '.board-sidebar__workspace']) {
                const current = board.querySelector(selector);
                const updated = updatedBoard.querySelector(selector);
                if (current && updated) current.replaceChildren(...updated.childNodes);
            }
            document.title = page.title;
            refreshClosedBoardDialogs(page);
            document.dispatchEvent(new Event('board:refreshed'));
        } catch (error) {
            console.error('Board refresh failed', error);
            // Network failures must not permanently lose the invalidation event.
            if (connection.state === signalR.HubConnectionState.Connected) {
                refreshNeeded = true;
                scheduleRefresh(5000);
            }
        } finally {
            refreshing = false;
            if (refreshNeeded && !refreshTimer) scheduleRefresh();
        }
    };

    const invalidateBoard = () => {
        refreshNeeded = true;
        scheduleRefresh();
    };

    // Register before start so no change notification is missed after joining.
    connection.on('BoardChanged', (changedPublicId) => {
        if (changedPublicId === publicId) invalidateBoard();
    });
    connection.on('BoardPresenceChanged', renderOnlineMembers);
    connection.on('BoardSessionExpired', stopForUnavailableBoard);
    connection.onreconnecting(clearOnlineMembers);

    const joinBoard = async () => {
        try {
            const snapshot = await connection.invoke('JoinBoard', publicId);
            renderOnlineMembers(snapshot);
            joined = true;
            // Includes a server-auto-opened dialog and restores state on reconnect.
            syncEditing();
            // Also catch changes made while this connection was offline.
            invalidateBoard();
        } catch (error) {
            console.error('Could not join the board', error);
            await stopForUnavailableBoard();
        }
    };
    connection.onreconnected(joinBoard);

    const start = async () => {
        if (disposed) return;
        try {
            await connection.start();
        } catch (error) {
            console.error('Board connection failed', error);
            if (!disposed) startTimer = setTimeout(start, 5000);
            return;
        }
        await joinBoard();
    };

    connection.onclose(() => {
        clearOnlineMembers();
        // withAutomaticReconnect stops after its default attempts are exhausted.
        // Retry network disconnects, but not a failed JoinBoard authorization.
        if (!disposed && connectionErrorRetry) startTimer = setTimeout(start, 5000);
    });
    window.addEventListener('pagehide', () => {
        disposed = true;
        clearTimeout(startTimer);
        clearTimeout(refreshTimer);
        clearOnlineMembers();
        connection.stop();
    });
    window.addEventListener('pageshow', (event) => {
        // A browser Back navigation may restore this page from its page cache.
        if (event.persisted) {
            disposed = false;
            connectionErrorRetry = true;
            start();
        }
    });
    start();
})();
