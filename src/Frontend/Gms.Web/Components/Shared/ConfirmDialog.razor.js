// Dialog: jebakan fokus, Esc, dan pengembalian fokus. Esc ditangani di sini agar dapat diblok saat aksi diproses (RequestClose di C#).
let state = null;

const FOCUSABLE = 'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function attach(wrap, dotNet) {
    detach();
    if (!wrap) {
        return;
    }

    const dialog = wrap.querySelector('[role="dialog"]');
    const previous = document.activeElement;
    const onKey = e => {
        if (e.key === 'Escape') {
            e.preventDefault();
            e.stopPropagation();
            dotNet.invokeMethodAsync('RequestClose');
            return;
        }

        if (e.key !== 'Tab' || !dialog) {
            return;
        }

        const items = [...dialog.querySelectorAll(FOCUSABLE)].filter(el => el.offsetParent !== null);
        if (items.length === 0) {
            e.preventDefault();
            return;
        }

        const first = items[0];
        const last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    };

    document.addEventListener('keydown', onKey, true);
    state = { onKey, previous };

    // Fokus awal: kolom alasan bila ada, selain itu kontrol pertama di dalam dialog.
    const target = dialog && (dialog.querySelector('textarea') || dialog.querySelector(FOCUSABLE));
    (target || dialog)?.focus();
}

export function detach() {
    if (!state) {
        return;
    }

    document.removeEventListener('keydown', state.onKey, true);
    const previous = state.previous;
    state = null;
    if (previous && document.contains(previous) && typeof previous.focus === 'function') {
        previous.focus();
    }
}
