// Shell GMS: jembatan tema untuk Blazor + CommandPalette (Design.md 7.3). Penyaringan katalog di browser, tanpa SignalR per ketikan.
(function () {
    'use strict';

    window.gmsShell = {
        getTheme: function () { return window.gmsTheme ? window.gmsTheme.get() : 'system'; },
        setTheme: function (mode) { if (window.gmsTheme) { window.gmsTheme.set(mode); } }
    };

    var RECENT_KEY = 'gms.recent';
    var root = null, input = null, list = null, chipsEl = null, trigger = null;
    var entries = [], results = [], active = 0, filter = 'all';

    function readCatalog() {
        var el = document.getElementById('gms-catalog');
        if (!el) { return null; }
        try { return JSON.parse(el.getAttribute('data-json') || '[]'); } catch (e) { return []; }
    }
    function recent() { try { return JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); } catch (e) { return []; } }
    function remember(key) {
        try {
            var r = recent().filter(function (k) { return k !== key; });
            r.unshift(key);
            localStorage.setItem(RECENT_KEY, JSON.stringify(r.slice(0, 5)));
        } catch (e) { }
    }

    var SVG_NS = 'http://www.w3.org/2000/svg';
    function icon(kind) {
        var svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('viewBox', '0 0 16 16');
        svg.setAttribute('class', 'ico');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('fill', 'none');
        svg.setAttribute('stroke', 'currentColor');
        svg.setAttribute('stroke-width', '1.4');
        var p = document.createElementNS(SVG_NS, 'path');
        p.setAttribute('d', kind === 'action' ? 'M8 3v10M3 8h10' : 'M4 2h6l3 3v9H4zM10 2v3h3');
        svg.appendChild(p);
        return svg;
    }

    function build() {
        root = document.createElement('div');
        root.className = 'gms-palette-root';
        root.hidden = true;
        root.addEventListener('mousedown', function (e) { if (e.target === root) { close(); } });

        var box = document.createElement('div');
        box.className = 'gms-palette';
        box.setAttribute('role', 'dialog');
        box.setAttribute('aria-modal', 'true');
        box.setAttribute('aria-label', 'Cari menu');

        input = document.createElement('input');
        input.type = 'text';
        input.className = 'gms-palette-input';
        input.placeholder = 'Cari halaman atau aksi...';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-expanded', 'true');
        input.setAttribute('aria-controls', 'gms-palette-list');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-label', 'Cari menu');
        input.autocomplete = 'off';
        input.addEventListener('input', function () { active = 0; render(); });

        chipsEl = document.createElement('div');
        chipsEl.className = 'gms-palette-chips';
        [['all', 'Semua'], ['page', 'Halaman'], ['action', 'Aksi']].forEach(function (c) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'gms-chip';
            b.textContent = c[1];
            b.setAttribute('data-filter', c[0]);
            b.addEventListener('click', function () { filter = c[0]; active = 0; render(); input.focus(); });
            chipsEl.appendChild(b);
        });

        list = document.createElement('ul');
        list.id = 'gms-palette-list';
        list.className = 'gms-palette-list';
        list.setAttribute('role', 'listbox');

        var foot = document.createElement('div');
        foot.className = 'gms-palette-foot';
        foot.textContent = '↑ ↓ pilih · Enter buka · Esc tutup';

        box.appendChild(input);
        box.appendChild(chipsEl);
        box.appendChild(list);
        box.appendChild(foot);
        root.appendChild(box);
        document.body.appendChild(root);

        root.addEventListener('keydown', onKey);
    }

    function tokens(q) { return q.toLowerCase().split(/\s+/).filter(Boolean); }
    function haystack(e) { return (e.l + ' ' + e.d + ' ' + e.c + ' ' + e.g + ' ' + (e.w || '')).toLowerCase(); }

    function highlight(text, toks) {
        var frag = document.createDocumentFragment();
        if (!toks.length) { frag.appendChild(document.createTextNode(text)); return frag; }
        var lower = text.toLowerCase();
        var i = 0;
        while (i < text.length) {
            var best = -1, len = 0;
            for (var n = 0; n < toks.length; n++) {
                var at = lower.indexOf(toks[n], i);
                if (at !== -1 && (best === -1 || at < best)) { best = at; len = toks[n].length; }
            }
            if (best === -1) { frag.appendChild(document.createTextNode(text.slice(i))); break; }
            if (best > i) { frag.appendChild(document.createTextNode(text.slice(i, best))); }
            var m = document.createElement('mark');
            m.textContent = text.slice(best, best + len);
            frag.appendChild(m);
            i = best + len;
        }
        return frag;
    }

    function heading(text) {
        var li = document.createElement('li');
        li.className = 'gms-palette-heading';
        li.setAttribute('role', 'presentation');
        li.textContent = text;
        list.appendChild(li);
    }

    function option(e, idx, toks) {
        var li = document.createElement('li');
        li.id = 'gms-opt-' + idx;
        li.className = 'gms-palette-opt';
        li.setAttribute('role', 'option');
        li.setAttribute('aria-selected', idx === active ? 'true' : 'false');
        li.appendChild(icon(e.t));

        var txt = document.createElement('span');
        txt.className = 'txt';
        var label = document.createElement('span');
        label.appendChild(highlight(e.l, toks));
        var desc = document.createElement('span');
        desc.className = 'desc';
        desc.textContent = e.d;
        txt.appendChild(label);
        txt.appendChild(desc);
        li.appendChild(txt);

        if (e.c) {
            var path = document.createElement('span');
            path.className = 'path';
            path.textContent = e.c + ' › ' + e.g;
            li.appendChild(path);
        }
        if (e.t === 'action') {
            var tag = document.createElement('span');
            tag.className = 'gms-palette-tag';
            tag.textContent = 'Aksi';
            li.appendChild(tag);
        }
        li.addEventListener('mousemove', function () { if (active !== idx) { active = idx; markActive(); } });
        li.addEventListener('click', function () { go(e); });
        list.appendChild(li);
    }

    function addResult(e, toks) { results.push(e); option(e, results.length - 1, toks); }

    function render() {
        list.textContent = '';
        for (var c = 0; c < chipsEl.children.length; c++) {
            var chip = chipsEl.children[c];
            chip.setAttribute('aria-pressed', chip.getAttribute('data-filter') === filter ? 'true' : 'false');
        }
        var q = input.value.trim();
        var toks = tokens(q);
        var pool = entries.filter(function (e) { return filter === 'all' || e.t === filter; });
        results = [];

        if (!toks.length) {
            var rec = recent()
                .map(function (k) { return pool.filter(function (e) { return e.k === k; })[0]; })
                .filter(Boolean);
            var quick = pool.filter(function (e) { return e.t === 'action' && rec.indexOf(e) === -1; }).slice(0, 6);
            if (rec.length) { heading('Terakhir dibuka'); rec.forEach(function (e) { addResult(e, toks); }); }
            if (quick.length) { heading('Aksi cepat'); quick.forEach(function (e) { addResult(e, toks); }); }
            if (!results.length) { pool.slice(0, 8).forEach(function (e) { addResult(e, toks); }); }
        } else {
            var seen = {};
            pool.filter(function (e) {
                var h = haystack(e);
                return toks.every(function (t) { return h.indexOf(t) !== -1; });
            }).forEach(function (e) {
                var cat = e.c || 'Beranda';
                if (!seen[cat]) { seen[cat] = true; heading(cat); }
                addResult(e, toks);
            });
        }

        if (!results.length) {
            var empty = document.createElement('li');
            empty.className = 'gms-palette-empty';
            empty.setAttribute('role', 'presentation');
            empty.textContent = 'Tidak ada menu yang cocok dengan "' + q + '". Coba kata lain atau lihat MegaMenu.';
            list.appendChild(empty);
        }
        if (active >= results.length) { active = Math.max(0, results.length - 1); }
        markActive();
    }

    function markActive() {
        var opts = list.querySelectorAll('[role=option]');
        for (var i = 0; i < opts.length; i++) { opts[i].setAttribute('aria-selected', i === active ? 'true' : 'false'); }
        if (opts[active]) {
            input.setAttribute('aria-activedescendant', opts[active].id);
            if (opts[active].scrollIntoView) { opts[active].scrollIntoView({ block: 'nearest' }); }
        } else {
            input.removeAttribute('aria-activedescendant');
        }
    }

    function onKey(e) {
        if (e.key === 'Escape') { e.preventDefault(); close(); }
        else if (e.key === 'ArrowDown') { e.preventDefault(); if (results.length) { active = (active + 1) % results.length; markActive(); } }
        else if (e.key === 'ArrowUp') { e.preventDefault(); if (results.length) { active = (active - 1 + results.length) % results.length; markActive(); } }
        else if (e.key === 'Enter') { e.preventDefault(); if (results[active]) { go(results[active]); } }
        else if (e.key === 'Tab') { e.preventDefault(); input.focus(); }
    }

    function go(e) {
        remember(e.k);
        close(false);
        if (window.Blazor && typeof window.Blazor.navigateTo === 'function') { window.Blazor.navigateTo(e.h); }
        else { window.location.href = e.h; }
    }

    function open() {
        var cat = readCatalog();
        if (cat === null) { return; }
        entries = cat;
        if (!root) { build(); }
        trigger = document.activeElement;
        filter = 'all';
        active = 0;
        input.value = '';
        root.hidden = false;
        render();
        input.focus();
    }

    function close(restoreFocus) {
        if (!root || root.hidden) { return; }
        root.hidden = true;
        if (restoreFocus !== false && trigger && trigger.focus) { trigger.focus(); }
    }

    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && (e.key === 'k' || e.key === 'K')) {
            e.preventDefault();
            if (root && !root.hidden) { close(); } else { open(); }
        }
    });

    document.addEventListener('click', function (e) {
        var t = e.target;
        while (t && t !== document) {
            if (t.hasAttribute && t.hasAttribute('data-palette-open')) { e.preventDefault(); open(); return; }
            t = t.parentNode;
        }
    });
})();
