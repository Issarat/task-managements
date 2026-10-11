// Run: node tests/board-realtime.checks.cjs (no external test dependencies).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname,
    '../src/TaskManager.WebUI/wwwroot/js/board-realtime.js'), 'utf8');
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(setImmediate); };

function environment() {
    const timers = new Map();
    const calls = { fetch: 0, replaced: [], events: [], joins: 0, starts: 0, stops: 0 };
    const regions = ['.kanban-grid', '.board-heading', '.board-sidebar__workspace'];
    let sequence = 0;
    const handlers = {};
    const dialog = { open: true, title: 'Unsaved draft', rowVersion: 'original' };
    const online = { dataset: { onlineHint: 'Online' }, children: [],
        replaceChildren(...children) { this.children = children; } };
    const board = {
        dataset: { boardPublicId: 'board-a', boardRefreshUrl: '/boards/board-a/snapshot' },
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
    const page = { title: 'Updated board', querySelector: () => updated };
    const connection = {
        state: 'Disconnected', startFailure: false, joinFailure: false,
        async start() {
            calls.starts++;
            if (this.startFailure) throw new Error('offline');
            this.state = 'Connected';
        },
        async invoke(name, id) {
            assert.equal(name, 'JoinBoard'); assert.equal(id, 'board-a'); calls.joins++;
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
        createElement: () => ({ style: { setProperty(name, value) { this[name] = value; } },
            setAttribute(name, value) { this[name] = value; } }),
        dispatchEvent: event => calls.events.push(event.type) };
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
    return { calls, board, dialog, online, updated, connection, handlers, document, timers, env,
        run: async () => { vm.runInNewContext(script, context); await settle(); },
        tick: async () => {
            const [id, timer] = timers.entries().next().value ?? [];
            assert.ok(timer, 'a timer must be scheduled');
            timers.delete(id); timer.callback(); await settle();
        } };
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
    console.log(`${passed} checks passed.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
