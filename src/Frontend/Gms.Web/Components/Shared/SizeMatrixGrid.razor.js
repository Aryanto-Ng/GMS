// Grid Matriks Ukuran: navigasi keyboard, penyaringan angka, dan tempel blok. Logika bisnis (batas, total, parsing tempel) ada di C#;
// modul ini hanya memindahkan fokus dan meneruskan teks tempel ke .NET.
const controllers = new WeakMap();

export function init(root, dotNet) {
    if (!root || controllers.has(root)) {
        return;
    }

    const controller = new AbortController();
    controllers.set(root, controller);
    const opt = { signal: controller.signal };

    const find = (r, c) => root.querySelector(`input[data-r="${r}"][data-c="${c}"]`);
    const bounds = () => {
        const inputs = [...root.querySelectorAll('input[data-r]')];
        return {
            rows: inputs.reduce((m, i) => Math.max(m, +i.dataset.r), -1),
            cols: inputs.reduce((m, i) => Math.max(m, +i.dataset.c), -1),
        };
    };

    // Pindah ke sel berlaku berikutnya pada arah (dr, dc); sel tidak berlaku ("—") dilewati.
    const move = (r, c, dr, dc) => {
        const { rows, cols } = bounds();
        let nr = r + dr;
        let nc = c + dc;
        while (nr >= 0 && nr <= rows && nc >= 0 && nc <= cols) {
            const target = find(nr, nc);
            if (target) {
                target.focus();
                target.select();
                return true;
            }

            nr += dr;
            nc += dc;
        }

        return false;
    };

    // Capture: menyaring non-angka SEBELUM Blazor membaca nilai input.
    root.addEventListener('input', e => {
        const el = e.target;
        if (el instanceof HTMLInputElement && el.dataset.r !== undefined) {
            const clean = el.value.replace(/\D/g, '');
            if (clean !== el.value) {
                el.value = clean;
            }
        }
    }, { ...opt, capture: true });

    root.addEventListener('focusin', e => {
        if (e.target instanceof HTMLInputElement && e.target.dataset.r !== undefined) {
            e.target.select();
        }
    }, opt);

    root.addEventListener('keydown', e => {
        const el = e.target;
        if (!(el instanceof HTMLInputElement) || el.dataset.r === undefined) {
            return;
        }

        const r = +el.dataset.r;
        const c = +el.dataset.c;
        let handled = false;
        switch (e.key) {
            case 'Enter': handled = move(r, c, e.shiftKey ? -1 : 1, 0); break;
            case 'ArrowDown': handled = move(r, c, 1, 0); break;
            case 'ArrowUp': handled = move(r, c, -1, 0); break;
            case 'ArrowRight':
                if (el.selectionStart === el.value.length || el.selectionStart === 0 && el.selectionEnd === el.value.length) {
                    handled = move(r, c, 0, 1);
                }
                break;
            case 'ArrowLeft':
                if (el.selectionStart === 0) {
                    handled = move(r, c, 0, -1);
                }
                break;
            case 'Home': handled = e.ctrlKey ? false : move(r, -1, 0, 1); break;
            case 'End': {
                const { cols } = bounds();
                handled = e.ctrlKey ? false : move(r, cols + 1, 0, -1);
                break;
            }
            default: break;
        }

        if (handled || e.key === 'Enter') {
            e.preventDefault(); // Enter tidak boleh mengirim form
        }
    }, opt);

    root.addEventListener('paste', e => {
        const el = e.target;
        if (!(el instanceof HTMLInputElement) || el.dataset.r === undefined) {
            return;
        }

        const text = e.clipboardData?.getData('text') ?? '';
        if (text.includes('\t') || /\r?\n./.test(text)) {
            e.preventDefault();
            dotNet.invokeMethodAsync('PasteAsync', +el.dataset.r, +el.dataset.c, text);
        }
    }, opt);
}

export function dispose(root) {
    controllers.get(root)?.abort();
    controllers.delete(root);
}
