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
    const regions = ['.kanban-grid', '.board-heading', '.board-members', '.board-sidebar__workspace'];
    let sequence = 0;
    const handlers = {};
    const dialog = { open: true, title: 'Unsaved draft', rowVersion: 'original' };
    const board = {
        dataset: { boardPublicId: 'board-a', boardRefreshUrl: '/boards/board-a/snapshot' },
        dragging: false, busy: false,
        getAttribute() { return this.busy ? 'true' : null; },
        querySelector(selector) {
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
        },
        async stop() { this.state = 'Disconnected'; calls.stops++; handlers.close?.(); },
        on: (name, callback) => { handlers[name] = callback; },
        onreconnected: callback => { handlers.reconnected = callback; },
        onclose: callback => { handlers.close = callback; }
    };
    const document = { title: 'Original board', querySelector: () => board,
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
    return { calls, board, dialog, updated, connection, handlers, document, timers, env,
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
        assert.equal(e.calls.joins, 1); assert.equal(e.calls.replaced.length, 4);
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
        e.env.fetchHook = null; e.board.dragging = false; await e.tick(); assert.equal(e.calls.replaced.length, 4);
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
        e.env.fetchFailure = false; await e.tick(); assert.equal(e.calls.replaced.length, 4);
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
    console.log(`${passed} checks passed.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
