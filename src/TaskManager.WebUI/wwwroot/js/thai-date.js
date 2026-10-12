(() => {
    const cache = new Map();
    document.querySelectorAll('[data-thai-date]').forEach(control => {
        const source = control.querySelector('[data-date-source]');
        const display = control.querySelector('[data-date-display]');
        const open = control.querySelector('[data-date-open]');
        const clear = control.querySelector('[data-date-clear]');
        const status = control.parentElement.querySelector('[data-date-status]');
        const label = document.getElementById(`${source.id}_label`);
        // Progressive enhancement: unsupported browsers keep a usable native input.
        if (typeof source.showPicker !== 'function') return;
        let revision = 0;
        let pending;
        if (source.value && display.value) cache.set(source.value, display.value);

        const sync = async () => {
            const current = ++revision;
            pending?.abort();
            const value = source.value;
            clear.hidden = !value;
            status.hidden = true;
            display.removeAttribute('aria-busy');
            if (!value) { display.value = ''; return; }
            if (cache.has(value)) { display.value = cache.get(value); return; }
            // Safe fallback while loading/offline: show the actual ISO value,
            // never keep text for a previously selected date.
            display.value = value;
            display.setAttribute('aria-busy', 'true');
            pending = new AbortController();
            try {
                const url = new URL(control.dataset.formatUrl, window.location.origin);
                url.searchParams.set('value', value);
                const response = await fetch(url, { signal: pending.signal, cache: 'no-store', credentials: 'same-origin' });
                if (!response.ok) throw new Error('Date formatting failed');
                const formatted = await response.json();
                if (current !== revision || source.value !== value) return;
                if (formatted.value !== value || typeof formatted.full !== 'string') throw new Error('Invalid formatted date');
                if (cache.size >= 128) cache.clear();
                cache.set(value, formatted.full);
                display.value = formatted.full;
            } catch {
                if (current !== revision) return;
                status.textContent = control.dataset.formatError;
                status.hidden = false;
            } finally {
                if (current === revision) display.removeAttribute('aria-busy');
            }
        };
        const openPicker = () => {
            try { source.showPicker(); if (!status.hidden) sync(); }
            catch {
                control.classList.remove('is-enhanced');
                display.hidden = open.hidden = clear.hidden = true;
                source.removeAttribute('aria-hidden'); source.tabIndex = 0;
                if (label) label.htmlFor = source.id;
                source.focus();
            }
        };
        control.classList.add('is-enhanced');
        display.hidden = open.hidden = false;
        clear.hidden = !source.value;
        source.tabIndex = -1; source.setAttribute('aria-hidden', 'true');
        if (label) label.htmlFor = display.id;
        open.addEventListener('click', openPicker);
        display.addEventListener('click', openPicker);
        display.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); openPicker(); }
        });
        source.addEventListener('change', sync);
        clear.addEventListener('click', () => {
            source.value = ''; source.dispatchEvent(new Event('change', { bubbles: true })); display.focus();
        });
        source.form?.addEventListener('reset', () => setTimeout(sync, 0));
        window.addEventListener('pageshow', sync);
    });
})();
