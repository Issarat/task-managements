const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../src/TaskManager.WebUI/wwwroot/js/thai-date.js'), 'utf8');
const settle = async () => { for (let i = 0; i < 5; i++) await new Promise(setImmediate); };
function element(extra = {}) {
    return { hidden: true, value: '', listeners: {}, attrs: {},
        addEventListener(name, handler) { this.listeners[name] = handler; },
        dispatchEvent(event) { return this.listeners[event.type]?.(event); },
        setAttribute(name, value) { this.attrs[name] = value; },
        removeAttribute(name) { delete this.attrs[name]; },
        focus() { this.focused = true; }, ...extra };
}
function environment() {
    const source = element({ id: 'EditTask_StartDate', value: '2026-10-12',
        pickerCalls: 0, showPicker() { this.pickerCalls++; } });
    const display = element({ id: 'EditTask_StartDate_text', value: '12 ตุลาคม 2569' });
    const open = element(), clear = element(), status = element(), label = { htmlFor: source.id };
    const controls = { '[data-date-source]': source, '[data-date-display]': display,
        '[data-date-open]': open, '[data-date-clear]': clear };
    const classes = new Set();
    const control = { dataset: { formatUrl: '/dates/format', formatError: 'Try again' },
        querySelector: selector => controls[selector], parentElement: { querySelector: () => status },
        classList: { add: name => classes.add(name), remove: name => classes.delete(name) } };
    const events = {}, calls = [];
    const env = { hook: null };
    const context = { document: { querySelectorAll: () => [control], getElementById: () => label },
        window: { location: { origin: 'https://example.test' }, addEventListener: (name, handler) => events[name] = handler },
        AbortController, URL, Event, setTimeout,
        fetch: async (url, options) => {
            calls.push({ value: url.searchParams.get('value'), url: url.href, options });
            return env.hook ? env.hook(url, options) : { ok: true, json: async () => ({
                value: url.searchParams.get('value'), full: '13 ตุลาคม 2569' }) };
        } };
    return { source, display, open, clear, status, label, classes, events, calls, env,
        run() { vm.runInNewContext(script, context); },
        async change(value) { source.value = value; await source.dispatchEvent(new Event('change')); await settle(); } };
}
(async () => {
    let count = 0;
    const check = async (name, action) => { await action(); count++; console.log('PASS ' + name); };
    await check('initial server Thai date is shown without changing the ISO submitted value', async () => {
        const e = environment(); e.run();
        assert.equal(e.display.value, '12 ตุลาคม 2569'); assert.equal(e.source.value, '2026-10-12');
        assert.equal(e.display.hidden, false); assert.equal(e.label.htmlFor, e.display.id);
        assert.equal(e.calls.length, 0); assert.ok(e.classes.has('is-enhanced'));
    });
    await check('calendar button and Enter/Space trigger the native picker synchronously', async () => {
        const e = environment(); e.run(); e.open.listeners.click();
        e.display.listeners.keydown({ key: 'Enter', preventDefault() {} });
        e.display.listeners.keydown({ key: ' ', preventDefault() {} });
        assert.equal(e.source.pickerCalls, 3);
    });
    await check('date change uses server formatting and keeps original ISO value', async () => {
        const e = environment(); e.run(); await e.change('2026-10-13');
        assert.equal(e.calls[0].value, '2026-10-13');
        assert.equal(e.display.value, '13 ตุลาคม 2569'); assert.equal(e.source.value, '2026-10-13');
        assert.equal(e.display.attrs['aria-busy'], undefined);
    });
    await check('formatting cache avoids reformatting a server-rendered date', async () => {
        const e = environment(); e.run(); await e.change('2026-10-13'); await e.change('2026-10-12');
        assert.equal(e.calls.length, 1); assert.equal(e.display.value, '12 ตุลาคม 2569');
    });
    await check('slow older response cannot overwrite a newer date', async () => {
        const e = environment(); const responses = [];
        e.env.hook = () => new Promise(resolve => responses.push(resolve)); e.run();
        const first = e.change('2026-10-13'); await settle();
        const second = e.change('2026-10-14'); await settle();
        responses[1]({ ok: true, json: async () => ({ value: '2026-10-14', full: '14 ตุลาคม 2569' }) }); await second;
        responses[0]({ ok: true, json: async () => ({ value: '2026-10-13', full: '13 ตุลาคม 2569' }) }); await first;
        assert.equal(e.display.value, '14 ตุลาคม 2569'); assert.equal(e.source.value, '2026-10-14');
        assert.equal(e.calls[0].options.signal.aborted, true);
    });
    await check('clear cancels pending text and submits an empty nullable date', async () => {
        const e = environment(); let respond; e.env.hook = () => new Promise(resolve => respond = resolve); e.run();
        const pending = e.change('2026-10-13'); await settle(); e.clear.listeners.click(); await settle();
        respond({ ok: true, json: async () => ({ value: '2026-10-13', full: '13 ตุลาคม 2569' }) }); await pending;
        assert.equal(e.source.value, ''); assert.equal(e.display.value, ''); assert.equal(e.clear.hidden, true);
    });
    await check('format failure shows actual ISO date and a retry message, not stale Thai text', async () => {
        const e = environment(); e.env.hook = async () => { throw Error('offline'); }; e.run(); await e.change('2026-10-13');
        assert.equal(e.display.value, '2026-10-13'); assert.equal(e.source.value, '2026-10-13');
        assert.equal(e.status.hidden, false); assert.equal(e.status.textContent, 'Try again');
        e.env.hook = null; e.open.listeners.click(); await settle();
        assert.equal(e.display.value, '13 ตุลาคม 2569'); assert.equal(e.status.hidden, true);
    });
    await check('unsupported or blocked picker falls back to a usable native date control', async () => {
        const missing = environment(); missing.source.showPicker = undefined; missing.run();
        assert.equal(missing.display.hidden, true); assert.equal(missing.classes.size, 0);
        const denied = environment(); denied.source.showPicker = () => { throw Error('blocked'); }; denied.run();
        denied.open.listeners.click(); assert.equal(denied.classes.size, 0);
        assert.equal(denied.display.hidden, true); assert.equal(denied.source.tabIndex, 0);
        assert.equal(denied.label.htmlFor, denied.source.id); assert.equal(denied.source.focused, true);
    });
    await check('mismatched formatter payload cannot replace the selected date', async () => {
        const e = environment(); e.env.hook = async () => ({ ok: true, json: async () => ({ value: '2026-01-01', full: 'Wrong date' }) });
        e.run(); await e.change('2026-10-13'); assert.equal(e.display.value, '2026-10-13'); assert.equal(e.status.hidden, false);
    });
    console.log(`${count} Thai date checks passed.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
