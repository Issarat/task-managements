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

    const stopForUnavailableBoard = async () => {
        connectionErrorRetry = false;
        refreshNeeded = false;
        clearTimeout(refreshTimer);
        refreshTimer = null;
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

            for (const selector of ['.kanban-grid', '.board-heading', '.board-members', '.board-sidebar__workspace']) {
                const current = board.querySelector(selector);
                const updated = updatedBoard.querySelector(selector);
                if (current && updated) current.replaceChildren(...updated.childNodes);
            }
            document.title = page.title;
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

    const joinBoard = async () => {
        try {
            await connection.invoke('JoinBoard', publicId);
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
        // withAutomaticReconnect stops after its default attempts are exhausted.
        // Retry network disconnects, but not a failed JoinBoard authorization.
        if (!disposed && connectionErrorRetry) startTimer = setTimeout(start, 5000);
    });
    window.addEventListener('pagehide', () => {
        disposed = true;
        clearTimeout(startTimer);
        clearTimeout(refreshTimer);
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
