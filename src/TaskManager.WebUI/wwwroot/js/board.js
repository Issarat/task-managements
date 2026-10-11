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

    document.querySelectorAll('[data-dialog-open]').forEach((button) => {
        button.addEventListener('click', () => {
            const dialogId = button.getAttribute('data-dialog-open');
            const dialog = dialogId ? document.getElementById(dialogId) : null;

            if (!(dialog instanceof HTMLDialogElement)) {
                return;
            }

            dialog.showModal();

            const initialFocus = dialog.querySelector('[data-dialog-initial-focus]');
            if (initialFocus instanceof HTMLInputElement) {
                initialFocus.focus();
                initialFocus.select();
            }
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
        if (dialog.dataset.dialogAutoOpen === 'true') {
            dialog.showModal();
        }
        dialog.addEventListener('click', (event) => {
            if (event.target === dialog) {
                dialog.close();
            }
        });
    });
})();
