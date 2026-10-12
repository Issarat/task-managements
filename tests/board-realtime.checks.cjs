// Run: node tests/board-realtime.checks.cjs (no external test dependencies).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname,
    '../src/TaskManager.WebUI/wwwroot/js/board-realtime.js'), 'utf8');
const interactionScript = fs.readFileSync(path.join(__dirname,
    '../src/TaskManager.WebUI/wwwroot/js/board.js'), 'utf8');
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(setImmediate); };

function environment() {
    const timers = new Map();
    const calls = { fetch: 0, replaced: [], events: [], joins: 0, starts: 0, stops: 0, editing: [] };
    const regions = ['.kanban-grid', '.board-heading', '.board-sidebar__workspace'];
    let sequence = 0;
    const handlers = {};
    const dialog = { open: true, title: 'Unsaved draft', rowVersion: 'original' };
    const editDialog = { open: false, dataset: { editingTaskId: 'task-1' } };
    const listeners = {};
    const link = { setAttribute(name, value) { this[name] = value; },
        removeAttribute(name) { delete this[name]; } };
    const card = { dataset: { taskId: 'task-1' }, classes: new Set(),
        classList: { toggle(name, on) { if (on) card.classes.add(name); else card.classes.delete(name); } },
        style: { setProperty(name, value) { this[name] = value; }, removeProperty(name) { delete this[name]; } },
        querySelector: () => link, removeAttribute(name) { delete this[name]; } };
    const online = { dataset: { onlineHint: 'Online' }, children: [],
        replaceChildren(...children) { this.children = children; } };
    const board = {
        dataset: { boardPublicId: 'board-a', boardRefreshUrl: '/boards/board-a/snapshot', taskEditingHint: 'Editing by' },
        querySelectorAll: () => [card],
        dragging: false, busy: false,
        getAttribute() { return this.busy ? 'true' : null; },
        querySelector(selector) {
            if (selector === '[data-board-online-members]') return online;
            if (selector === '.is-dragging') return this.dragging ? {} : null;
            if (regions.includes(selector)) return {
                replaceChildren: (...nodes) => calls.replaced.push({ selector, nodes })
            };
            return dialog;
        }
    };
    const updated = { dataset: { boardPublicId: 'board-a' },
        querySelector: selector => ({ childNodes: [`new ${selector}`] }) };
    const page = { title: 'Updated board', querySelector: () => updated, getElementById: () => null };
    const connection = {
        state: 'Disconnected', startFailure: false, joinFailure: false,
        async start() {
            calls.starts++;
            if (this.startFailure) throw new Error('offline');
            this.state = 'Connected';
        },
        async invoke(name, id, taskId) {
            assert.equal(id, 'board-a');
            if (name !== 'JoinBoard') {
                assert.ok(['StartEditingTask', 'StopEditingTask'].includes(name));
                calls.editing.push({ name, taskId });
                await env.editingHook?.(name);
                if (env.editingFailure) throw new Error('Editing rejected');
                return { publicId: 'board-a', revision: 100 + calls.editing.length, members: [], editingTasks: [] };
            }
            calls.joins++;
            if (this.joinFailure) throw new Error('expired');
            return { publicId: 'board-a', revision: 1, members: [] };
        },
        async stop() { this.state = 'Disconnected'; calls.stops++; handlers.close?.(); },
        on: (name, callback) => { handlers[name] = callback; },
        onreconnected: callback => { handlers.reconnected = callback; },
        onreconnecting: callback => { handlers.reconnecting = callback; },
        onclose: callback => { handlers.close = callback; }
    };
    const document = { title: 'Original board', querySelector: () => board,
        getElementById: id => id === 'edit-task-dialog' ? editDialog : null,
        addEventListener(name, callback) { (listeners[name] ??= []).push(callback); },
        createElement: () => ({ style: { setProperty(name, value) { this[name] = value; } },
            setAttribute(name, value) { this[name] = value; } }),
        dispatchEvent(event) { calls.events.push(event.type); listeners[event.type]?.forEach(callback => callback(event)); } };
    const signalR = { HubConnectionState: { Connected: 'Connected' },
        HubConnectionBuilder: class {
            withUrl(url) { assert.equal(url, '/hubs/board'); return this; }
            withAutomaticReconnect() { return this; }
            build() { return connection; }
        } };
    const env = { status: 200, fetchFailure: false, fetchHook: null };
    const context = {
        window: { signalR, addEventListener: (name, callback) => { handlers[name] = callback; } },
        signalR, document, Event: class { constructor(type) { this.type = type; } },
        console: { error() {} },
        setTimeout: (callback, delay) => { const id = ++sequence; timers.set(id, { callback, delay }); return id; },
        clearTimeout: id => timers.delete(id),
        DOMParser: class { parseFromString() { return page; } },
        fetch: async (url, options) => {
            calls.fetch++;
            assert.equal(url, '/boards/board-a/snapshot');
            assert.equal(options.cache, 'no-store'); assert.equal(options.credentials, 'same-origin');
            await env.fetchHook?.();
            if (env.fetchFailure) throw new Error('network');
            return { status: env.status, ok: env.status === 200, text: async () => '<html />' };
        }
    };
    return { calls, board, dialog, editDialog, card, link, online, page, updated, connection, handlers, document, timers, env, context,
        run: async () => { vm.runInNewContext(script, context); await settle(); },
        tick: async () => {
            const [id, timer] = timers.entries().next().value ?? [];
            assert.ok(timer, 'a timer must be scheduled');
            timers.delete(id); timer.callback(); await settle();
        } };
}

function assigneeSelect(items, value = '') {
    return { options: items.map(([value, textContent]) => ({ value, textContent,
        selected: value === '', cloneNode() { return { ...this }; } })), value, focused: true,
        replaceChildren(...options) {
            this.options = options;
            this.value = (options.find(option => option.selected) ?? options[0])?.value ?? '';
        } };
}

function assignees(e, selected = 'participant-a') {
    const ids = ['CreateTask_AssignedToParticipantId', 'EditTask_AssignedToParticipantId'];
    const inputs = Object.fromEntries(ids.map(id => [id,
        assigneeSelect([['', 'Unassigned'], ['participant-a', 'Guest A']], selected)]));
    const updated = Object.fromEntries(ids.map(id => [id,
        assigneeSelect([['', 'Unassigned'], ['participant-a', 'Guest A'],
            ['participant-b', 'Guest B'], ['participant-offline', 'Offline guest']])]));
    const previousLookup = e.document.getElementById;
    e.document.getElementById = id => inputs[id] ?? previousLookup(id);
    e.page.getElementById = id => updated[id] ?? null;
    return { inputs, updated };
}

(async () => {
    let passed = 0;
    const check = async (name, action) => { await action(); passed++; console.log(`PASS ${name}`); };
    await check('join refreshes only display regions and preserves drafts', async () => {
        const e = environment(); await e.run(); await e.tick();
        assert.equal(e.calls.joins, 1); assert.equal(e.calls.replaced.length, 3);
        assert.deepEqual(e.dialog, { open: true, title: 'Unsaved draft', rowVersion: 'original' });
        assert.deepEqual(e.calls.events, ['board:refreshed']); assert.equal(e.document.title, 'Updated board');
    });
    await check('ignores other boards and coalesces event bursts', async () => {
        const e = environment(); await e.run(); await e.tick();
        e.handlers.BoardChanged('other'); assert.equal(e.timers.size, 0);
        for (let i = 0; i < 10; i++) e.handlers.BoardChanged('board-a');
        assert.equal(e.timers.size, 1); await e.tick(); assert.equal(e.calls.fetch, 2);
    });
    await check('waits until dragging ends', async () => {
        const e = environment(); await e.run(); e.board.dragging = true;
        await e.tick(); assert.equal(e.calls.fetch, 0);
        e.board.dragging = false; await e.tick(); assert.equal(e.calls.fetch, 1);
    });
    await check('defers replacement if a drag starts during fetch', async () => {
        const e = environment(); await e.run(); e.env.fetchHook = () => { e.board.dragging = true; };
        await e.tick(); assert.equal(e.calls.replaced.length, 0);
        e.env.fetchHook = null; e.board.dragging = false; await e.tick(); assert.equal(e.calls.replaced.length, 3);
    });
    await check('never interrupts a pending move', async () => {
        const e = environment(); await e.run(); e.board.busy = true; await e.tick(); assert.equal(e.calls.fetch, 0);
    });
    await check('an event during fetch triggers another refresh', async () => {
        const e = environment(); await e.run();
        e.env.fetchHook = async () => { e.handlers.BoardChanged('board-a'); await e.tick(); };
        await e.tick(); e.env.fetchHook = null;
        assert.equal(e.timers.size, 1); await e.tick(); assert.equal(e.calls.fetch, 2);
    });
    await check('reconnect rejoins and fetches missed changes', async () => {
        const e = environment(); await e.run(); await e.tick();
        await e.handlers.reconnected(); await e.tick(); assert.equal(e.calls.joins, 2);
    });
    for (const status of [401, 403, 404]) await check(`HTTP ${status} stops without retries`, async () => {
        const e = environment(); await e.run(); e.env.status = status; await e.tick();
        assert.equal(e.calls.stops, 1); assert.equal(e.timers.size, 0);
    });
    await check('failed authorization stops without reconnecting', async () => {
        const e = environment(); e.connection.joinFailure = true; await e.run();
        assert.equal(e.calls.stops, 1); assert.equal(e.timers.size, 0);
    });
    await check('initial connection failure retries', async () => {
        const e = environment(); e.connection.startFailure = true; await e.run();
        e.connection.startFailure = false; await e.tick(); assert.equal(e.calls.starts, 2);
    });
    await check('failed refresh retries without losing the event', async () => {
        const e = environment(); await e.run(); e.env.fetchFailure = true; await e.tick();
        e.env.fetchFailure = false; await e.tick(); assert.equal(e.calls.replaced.length, 3);
    });
    await check('navigation cleans up timers and connection', async () => {
        const e = environment(); await e.run(); e.handlers.pagehide(); await settle();
        assert.equal(e.calls.stops, 1); assert.equal(e.timers.size, 0);
    });
    await check('Back navigation from page cache reconnects', async () => {
        const e = environment(); await e.run(); e.handlers.pagehide(); await settle();
        e.handlers.pageshow({ persisted: true }); await settle();
        assert.equal(e.calls.starts, 2); await e.tick(); assert.equal(e.calls.fetch, 1);
    });
    const member = (id) => ({ guestId: `guest-${id}`, displayName: `Person ${id}`,
        shortName: `P${id}`, avatarColor: '#abcdef' });
    const snapshot = (revision, members, publicId = 'board-a') => ({ publicId, revision, members });
    await check('presence shows trusted profile text and avatar colors', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(2, [member(1), member(2)]));
        assert.equal(e.online.children.length, 2);
        assert.equal(e.online.children[0].textContent, 'P1');
        assert.equal(e.online.children[0].title, 'Person 1 — Online');
        assert.equal(e.online.children[0].style['--avatar-color'], '#abcdef');
    });
    await check('presence ignores older revisions and other boards', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(5, [member(1)]));
        e.handlers.BoardPresenceChanged(snapshot(4, [member(2)]));
        e.handlers.BoardPresenceChanged(snapshot(6, [], 'board-b'));
        assert.equal(e.online.children[0].textContent, 'P1');
    });
    await check('presence deduplicates users and shows overflow with names', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(2, [member(1), member(1), ...[2, 3, 4, 5, 6].map(member)]));
        assert.equal(e.online.children.length, 5);
        assert.equal(e.online.children[4].textContent, '+2');
        assert.equal(e.online.children[4].title, 'Person 5\nPerson 6');
    });
    await check('DB refresh does not overwrite online avatars or drafts', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(2, [member(1)]));
        await e.tick(); assert.equal(e.online.children[0].textContent, 'P1');
        assert.equal(e.dialog.title, 'Unsaved draft');
    });
    await check('disconnect clears online users and resets server revision', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(100, [member(1)]));
        e.handlers.reconnecting(); assert.equal(e.online.children.length, 0);
        await e.handlers.reconnected();
        e.handlers.BoardPresenceChanged(snapshot(2, [member(2)]));
        assert.equal(e.online.children[0].textContent, 'P2');
    });
    await check('expiration clears avatars and stops without reconnecting', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(snapshot(2, [member(1)]));
        await e.handlers.BoardSessionExpired();
        assert.equal(e.online.children.length, 0); assert.equal(e.calls.stops, 1);
        assert.equal(e.timers.size, 0);
    });
    await check('unsafe profile strings are text, not HTML or CSS', async () => {
        const e = environment(); await e.run();
        const unsafe = { ...member(1), shortName: '<img src=x onerror=alert(1)>',
            displayName: '<script>alert(1)</script>', avatarColor: 'red; background:url(x)' };
        e.handlers.BoardPresenceChanged(snapshot(2, [unsafe]));
        assert.equal(e.online.children[0].textContent, unsafe.shortName);
        assert.equal(e.online.children[0].style['--avatar-color'], '#94a3b8');
        assert.equal(e.online.children[0].innerHTML, undefined);
    });
    const editingSnapshot = (revision, editors) => ({ ...snapshot(revision, editors),
        editingTasks: editors.length ? [{ taskItemId: 'task-1', editors }] : [] });
    const editChanged = async e => {
        e.document.dispatchEvent({ type: 'board:task-editing-changed' }); await settle();
    };
    await check('editing border and tooltip show all editors without locking the card', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(2, [member(1), member(2)]));
        assert.ok(e.card.classes.has('is-being-edited'));
        assert.equal(e.card.style['--task-editor-color'], '#abcdef');
        assert.equal(e.link.title, 'Editing by: Person 1, Person 2');
        assert.equal(e.link['aria-description'], e.link.title);
        assert.equal(e.link.disabled, undefined);
    });
    await check('ending editing removes the border, tooltip and accessible description', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(2, [member(1)]));
        e.handlers.BoardPresenceChanged(editingSnapshot(3, []));
        assert.equal(e.card.classes.size, 0); assert.equal(e.card.title, undefined);
        assert.equal(e.link.title, undefined); assert.equal(e.link['aria-description'], undefined);
        assert.equal(e.card.style['--task-editor-color'], undefined);
    });
    await check('old or cross-board snapshots cannot restore stale editing', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(3, []));
        e.handlers.BoardPresenceChanged(editingSnapshot(2, [member(1)]));
        e.handlers.BoardPresenceChanged({ ...editingSnapshot(4, [member(1)]), publicId: 'board-b' });
        assert.equal(e.card.classes.size, 0);
    });
    await check('task refresh reapplies editing to replacement cards', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(2, [member(1)]));
        e.card.classes.clear(); delete e.link.title;
        await e.tick(); assert.ok(e.card.classes.has('is-being-edited'));
        assert.equal(e.link.title, 'Editing by: Person 1');
    });
    await check('auto-opened edit dialog announces editing after join', async () => {
        const e = environment(); e.editDialog.open = true; await e.run();
        assert.deepEqual(e.calls.editing, [{ name: 'StartEditingTask', taskId: 'task-1' }]);
        e.editDialog.open = false; await editChanged(e);
        assert.equal(e.calls.editing[1].name, 'StopEditingTask');
    });
    await check('closing during a pending start sends stop afterwards', async () => {
        const e = environment(); await e.run();
        let release;
        e.env.editingHook = () => new Promise(resolve => { release = resolve; });
        e.editDialog.open = true; await editChanged(e);
        e.editDialog.open = false; await editChanged(e);
        assert.equal(e.calls.editing.length, 1);
        e.env.editingHook = null; release(); await settle();
        assert.deepEqual(e.calls.editing.map(call => call.name), ['StartEditingTask', 'StopEditingTask']);
    });
    await check('reconnect clears unknown editing and reannounces the open dialog', async () => {
        const e = environment(); e.editDialog.open = true; await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(200, [member(1)]));
        e.handlers.reconnecting(); assert.equal(e.card.classes.size, 0);
        await e.handlers.reconnected(); await settle();
        assert.equal(e.calls.editing.length, 2);
        assert.ok(e.calls.editing.every(call => call.name === 'StartEditingTask'));
    });
    await check('a late old-connection response cannot restore its editing border', async () => {
        const e = environment(); await e.run(); let release;
        e.env.editingHook = () => new Promise(resolve => { release = resolve; });
        e.editDialog.open = true; await editChanged(e);
        e.connection.state = 'Disconnected'; e.handlers.reconnecting();
        e.env.editingHook = null; release(); await settle();
        assert.equal(e.card.classes.size, 0); assert.equal(e.calls.editing.length, 1);
        e.editDialog.open = false; e.connection.state = 'Connected';
        await e.handlers.reconnected(); await settle();
        assert.equal(e.calls.editing.length, 1);
    });
    await check('failed editing announcement does not lock tasks or loop retries', async () => {
        const e = environment(); e.env.editingFailure = true; e.editDialog.open = true; await e.run();
        assert.equal(e.calls.editing.length, 1); assert.equal(e.calls.stops, 0);
        e.editDialog.open = false; await editChanged(e);
        assert.equal(e.calls.editing.length, 1);
    });
    await check('untrusted editing names stay text and colors are constrained', async () => {
        const e = environment(); await e.run();
        e.handlers.BoardPresenceChanged(editingSnapshot(2, [{ ...member(1),
            displayName: '<script>alert(1)</script>', avatarColor: 'red; background:url(x)' }]));
        assert.equal(e.link.title, 'Editing by: <script>alert(1)</script>');
        assert.equal(e.card.style['--task-editor-color'], '#8b5cf6');
        assert.equal(e.link.innerHTML, undefined);
    });
    await check('dialog interactions announce editing, keep title focus and clear URL state on every close route', async () => {
        const e = environment();
        class Input { focus() { this.focused = true; } select() { this.selected = true; } }
        class Dialog {
            addEventListener(name, handler) { this.listeners[name] = handler; }
            showModal() { this.open = true; }
            close() { this.open = false; this.listeners.close(); }
            querySelector() { return input; }
        }
        const input = new Input();
        Object.setPrototypeOf(e.editDialog, Dialog.prototype);
        Object.assign(e.editDialog, { id: 'edit-task-dialog', listeners: {} });
        e.editDialog.dataset.dialogAutoOpen = 'true';
        const open = { addEventListener(name, handler) { this.click = handler; },
            getAttribute() { return 'edit-task-dialog'; } };
        const close = { addEventListener(name, handler) { this.click = handler; },
            closest: () => e.editDialog };
        e.document.querySelector = selector => selector === '[data-board-screen]' ? e.board : null;
        e.document.querySelectorAll = selector => selector === 'dialog' ? [e.editDialog]
            : selector === '[data-dialog-open]' ? [open]
            : selector === '[data-dialog-close]' ? [close] : [];
        Object.assign(e.context, { HTMLInputElement: Input, HTMLDialogElement: Dialog,
            HTMLFormElement: class {}, URL });
        e.context.window.location = { href: 'https://example.test/boards/board-a?taskId=task-1&keep=yes' };
        e.context.window.history = { state: null,
            replaceState(state, unused, href) { e.context.window.location.href = href; } };
        vm.runInNewContext(interactionScript, e.context);
        await e.run();
        assert.ok(input.focused && input.selected);
        assert.equal(e.calls.editing[0].name, 'StartEditingTask');
        close.click(); await settle();
        assert.equal(e.calls.editing[1].name, 'StopEditingTask');
        assert.equal(new URL(e.context.window.location.href).searchParams.has('taskId'), false);
        assert.equal(new URL(e.context.window.location.href).searchParams.get('keep'), 'yes');
        open.click(); await settle();
        e.editDialog.listeners.click({ target: e.editDialog }); await settle();
        assert.equal(e.calls.editing[3].name, 'StopEditingTask');
        open.click(); await settle();
        // Escape closes a native dialog and raises the same close event.
        e.editDialog.close(); await settle();
        assert.equal(e.calls.editing[5].name, 'StopEditingTask');
    });
    for (const isOpen of [false, true]) await check(`board forms ${isOpen ? 'preserve open drafts and confirmation versions' : 'refresh closed data and versions together'}`, async () => {
        const e = environment();
        const fields = { 'EditBoard.Name': { value: 'Draft' }, 'EditBoard.Description': { value: 'Draft description' },
            'EditBoard.RowVersion': { value: 'old-version' }, rowVersion: { value: 'old-confirmation' } };
        const dialog = { open: isOpen, querySelector(selector) {
            return fields[selector.match(/name="([^"]+)"/)?.[1]] ?? null;
        } };
        const previousLookup = e.document.getElementById;
        e.document.getElementById = id => ['update-board-dialog', 'reset-board-dialog'].includes(id) ? dialog : previousLookup(id);
        e.page.getElementById = () => ({ querySelector(selector) {
            return selector.startsWith('[name=') ? { value: 'latest-data-and-version' } : null;
        } });
        await e.run(); await e.tick();
        assert.equal(fields['EditBoard.Name'].value, isOpen ? 'Draft' : 'latest-data-and-version');
        assert.equal(fields['EditBoard.Description'].value, isOpen ? 'Draft description' : 'latest-data-and-version');
        assert.equal(fields['EditBoard.RowVersion'].value, isOpen ? 'old-version' : 'latest-data-and-version');
        assert.equal(fields.rowVersion.value, isOpen ? 'old-confirmation' : 'latest-data-and-version');
    });
    await check('new online guest refreshes DB assignees without a task change event', async () => {
        const e = environment(); const { inputs } = assignees(e);
        await e.run(); await e.tick();
        // Reset choices to the initial page so the presence event proves refresh.
        for (const input of Object.values(inputs))
            input.options = assigneeSelect([['', 'Unassigned'], ['participant-a', 'Guest A']]).options;
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 2,
            members: [{ guestId: 'guest-b', displayName: 'Guest B', shortName: 'GB', avatarColor: '#123456' }] });
        await e.tick();
        assert.equal(e.calls.fetch, 2);
        for (const input of Object.values(inputs)) {
            assert.ok(input.options.some(option => option.value === 'participant-b'));
            assert.equal(input.options.some(option => option.value === 'guest-b'), false);
            assert.equal(input.value, 'participant-a');
        }
    });
    for (const isOpen of [false, true]) await check(`assignee choices refresh with ${isOpen ? 'open' : 'closed'} dialogs without changing drafts or focus`, async () => {
        const e = environment(); const { inputs } = assignees(e);
        e.editDialog.open = isOpen;
        e.editDialog.dataset.editingTaskId = '';
        e.editDialog.title = 'Unsaved title'; e.editDialog.rowVersion = 'original-task-version';
        e.editDialog.description = 'Unsaved description'; e.editDialog.priority = '3';
        e.editDialog.startDate = '2026-10-13';
        await e.run(); await e.tick();
        for (const input of Object.values(inputs)) {
            assert.equal(input.options.length, 4);
            assert.equal(input.value, 'participant-a'); assert.equal(input.focused, true);
        }
        assert.equal(e.editDialog.open, isOpen);
        assert.equal(e.editDialog.title, 'Unsaved title');
        assert.equal(e.editDialog.rowVersion, 'original-task-version');
        assert.equal(e.editDialog.description, 'Unsaved description');
        assert.equal(e.editDialog.priority, '3'); assert.equal(e.editDialog.startDate, '2026-10-13');
    });
    await check('assignee selection made while snapshot is in flight is preserved', async () => {
        const e = environment(); const { inputs } = assignees(e, '');
        e.env.fetchHook = () => { Object.values(inputs).forEach(input => { input.value = 'participant-a'; }); };
        await e.run(); await e.tick();
        assert.ok(Object.values(inputs).every(input => input.value === 'participant-a'));
    });
    await check('offline DB members stay assignable and editing/leave events do not fetch repeatedly', async () => {
        const e = environment(); const { inputs } = assignees(e);
        await e.run(); await e.tick();
        const member = { guestId: 'guest-a', displayName: 'Guest A', shortName: 'GA', avatarColor: '#123456' };
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 2, members: [member] });
        await e.tick();
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 3, members: [member],
            editingTasks: [{ taskItemId: 'task-1', editors: [member] }] });
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 4, members: [] });
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 5, members: [member] });
        assert.equal(e.timers.size, 0);
        for (const input of Object.values(inputs))
            assert.ok(input.options.some(option => option.value === 'participant-offline'));
    });
    await check('missing selected assignee is kept rather than silently unassigning a draft', async () => {
        const e = environment(); const { inputs, updated } = assignees(e);
        for (const select of Object.values(updated)) select.options = assigneeSelect([['', 'Unassigned']]).options;
        await e.run(); await e.tick();
        for (const input of Object.values(inputs)) {
            assert.equal(input.value, 'participant-a');
            assert.ok(input.options.some(option => option.value === 'participant-a' && option.textContent === 'Guest A'));
        }
    });
    await check('unassigned choices stay unassigned and stale/other-board presence does not refresh options', async () => {
        const e = environment(); const { inputs } = assignees(e, '');
        await e.run(); await e.tick();
        assert.ok(Object.values(inputs).every(input => input.value === ''));
        e.handlers.BoardPresenceChanged({ publicId: 'other-board', revision: 2, members: [{ guestId: 'other' }] });
        e.handlers.BoardPresenceChanged({ publicId: 'board-a', revision: 1, members: [{ guestId: 'stale' }] });
        assert.equal(e.timers.size, 0);
    });
    await check('invalid board creation auto-opens its dialog and focuses the preserved name draft', async () => {
        const e = environment();
        class Input {
            value = 'x'.repeat(121);
            focus() { this.focused = true; }
            select() { this.selected = true; }
        }
        const input = new Input();
        class Dialog {
            id = 'create-board-dialog';
            dataset = { dialogAutoOpen: 'true' };
            open = false;
            showModal() { this.open = true; }
            querySelector() { return input; }
            addEventListener() {}
        }
        const dialog = new Dialog();
        e.document.querySelector = selector => selector === '[data-board-screen]' ? e.board : null;
        e.document.querySelectorAll = selector => selector === 'dialog' ? [dialog] : [];
        Object.assign(e.context, { HTMLInputElement: Input, HTMLDialogElement: Dialog,
            HTMLFormElement: class {}, URL });
        vm.runInNewContext(interactionScript, e.context);
        assert.equal(dialog.open, true);
        assert.equal(input.focused, true); assert.equal(input.selected, true);
        assert.equal(input.value, 'x'.repeat(121));
        assert.equal(e.calls.events.includes('board:task-editing-changed'), false);
    });
    console.log(`${passed} checks passed.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
