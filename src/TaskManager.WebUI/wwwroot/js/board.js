(() => {
    const board = document.querySelector('[data-board-screen]');
    const openButton = document.querySelector('[data-board-sidebar-open]');
    const closeButton = document.querySelector('[data-board-sidebar-close]');

    if (!board) {
        return;
    }

    const setSidebarOpen = (isOpen) => {
        board.classList.toggle('is-sidebar-open', isOpen);
        openButton?.setAttribute('aria-expanded', isOpen.toString());
        document.body.style.overflow = isOpen ? 'hidden' : '';
    };

    openButton?.addEventListener('click', () => setSidebarOpen(true));
    closeButton?.addEventListener('click', () => setSidebarOpen(false));

    window.addEventListener('resize', () => {
        if (window.innerWidth > 980) {
            setSidebarOpen(false);
        }
    });

    const shareLink = document.querySelector('[data-share-link]');
    const shareCopy = document.querySelector('[data-share-copy]');
    const shareStatus = document.querySelector('[data-share-status]');
    if (shareLink instanceof HTMLInputElement) {
        // Share only the public board route, never the current task query or cookie tokens.
        shareLink.value = new URL(shareLink.dataset.sharePath, window.location.origin).href;
    }
    shareCopy?.addEventListener('click', async () => {
        if (!(shareLink instanceof HTMLInputElement) || !shareStatus) return;
        shareCopy.disabled = true;
        try {
            if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
            await navigator.clipboard.writeText(shareLink.value);
            shareStatus.textContent = shareStatus.dataset.copiedMessage;
        } catch {
            shareLink.focus();
            shareLink.select();
            shareStatus.textContent = shareStatus.dataset.failedMessage;
        } finally {
            shareCopy.disabled = false;
        }
    });

    const moveForm = document.querySelector('[data-task-move-form]');
    const moveMessage = document.querySelector('[data-task-move-error]');
    const columns = Array.from(document.querySelectorAll('[data-column-id]'));
    let draggedCard = null;
    let sourceColumn = null;
    let movePending = false;

    const showMoveMessage = (message) => {
        if (moveMessage) {
            moveMessage.textContent = message;
            moveMessage.hidden = false;
        }
    };

    const isAdjacentColumn = (column) => sourceColumn
        && Math.abs(Number(column.dataset.columnIndex)
            - Number(sourceColumn.dataset.columnIndex)) === 1;

    const clearDrag = () => {
        draggedCard?.classList.remove('is-dragging');
        columns.forEach(column => column.classList.remove('is-drop-target', 'is-drop-over'));
        draggedCard = null;
        sourceColumn = null;
    };

    if (moveForm instanceof HTMLFormElement) {
        document.querySelectorAll('[data-task-id]').forEach(card => {
            card.addEventListener('dragstart', event => {
                if (movePending || !event.dataTransfer) {
                    event.preventDefault();
                    return;
                }

                draggedCard = card;
                sourceColumn = card.closest('[data-column-id]');
                card.classList.add('is-dragging');
                if (moveMessage) moveMessage.hidden = true;
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', card.dataset.taskId);
                columns.forEach(column => {
                    column.classList.toggle('is-drop-target', Boolean(isAdjacentColumn(column)));
                });
            });
            card.addEventListener('dragend', clearDrag);
        });

        columns.forEach(column => {
            column.addEventListener('dragover', event => {
                if (!draggedCard || movePending) return;
                event.preventDefault();
                const allowed = isAdjacentColumn(column);
                if (event.dataTransfer) event.dataTransfer.dropEffect = allowed ? 'move' : 'none';
                column.classList.toggle('is-drop-over', Boolean(allowed));
            });
            column.addEventListener('dragleave', event => {
                if (!column.contains(event.relatedTarget)) {
                    column.classList.remove('is-drop-over');
                }
            });
            column.addEventListener('drop', event => {
                if (!draggedCard || movePending) return;
                event.preventDefault();
                if (column === sourceColumn) {
                    clearDrag();
                    return;
                }
                if (!isAdjacentColumn(column)) {
                    showMoveMessage(moveForm.dataset.stepError);
                    clearDrag();
                    return;
                }

                moveForm.elements.namedItem('taskId').value = draggedCard.dataset.taskId;
                moveForm.elements.namedItem('targetColumnId').value = column.dataset.columnId;
                moveForm.elements.namedItem('rowVersion').value = draggedCard.dataset.rowVersion;
                movePending = true;
                board.setAttribute('aria-busy', 'true');
                showMoveMessage(moveForm.dataset.movingMessage);
                clearDrag();
                // Submit the antiforgery-protected form; only persisted changes appear after redirect.
                moveForm.requestSubmit();
            });
        });
    }

    const showDialog = (dialog) => {
        if (dialog.id === 'share-board-dialog' && shareStatus) {
            shareStatus.textContent = '';
        }
        dialog.showModal();

        const initialFocus = dialog.querySelector('[data-dialog-initial-focus]');
        if (initialFocus instanceof HTMLInputElement) {
            initialFocus.focus();
            initialFocus.select();
        }
    };

    document.querySelectorAll('[data-dialog-open]').forEach((button) => {
        button.addEventListener('click', () => {
            const dialogId = button.getAttribute('data-dialog-open');
            const dialog = dialogId ? document.getElementById(dialogId) : null;

            if (!(dialog instanceof HTMLDialogElement)) {
                return;
            }

            showDialog(dialog);
        });
    });

    document.querySelectorAll('[data-dialog-close]').forEach((button) => {
        button.addEventListener('click', () => {
            const dialog = button.closest('dialog');
            if (dialog instanceof HTMLDialogElement) {
                dialog.close();
            }
        });
    });

    document.querySelectorAll('dialog').forEach((dialog) => {
        dialog.addEventListener('close', () => {
            if (dialog.id !== 'edit-task-dialog') {
                return;
            }

            const url = new URL(window.location.href);
            if (url.searchParams.has('taskId')) {
                url.searchParams.delete('taskId');
                // Keep other query parameters and remove only the closed task's URL state.
                window.history.replaceState(window.history.state, '', url.href);
            }
        });

        if (dialog.dataset.dialogAutoOpen === 'true') {
            showDialog(dialog);
        }
        dialog.addEventListener('click', (event) => {
            if (event.target === dialog) {
                dialog.close();
            }
        });
    });
})();
