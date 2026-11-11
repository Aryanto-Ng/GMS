// Dialog modal native: fokus terkurung, Esc, dan pengembalian fokus ke pemicu ditangani browser.
// Esc (event "cancel") dialihkan ke .NET agar dapat diblok saat aksi sedang diproses.
const state = new WeakMap();

export function show(dialog, dotNet) {
    if (!dialog) {
        return;
    }

    if (!state.has(dialog)) {
        dialog.addEventListener('cancel', e => {
            e.preventDefault();
            dotNet.invokeMethodAsync('RequestClose');
        });
        state.set(dialog, true);
    }

    if (!dialog.open) {
        dialog.showModal();
    }
}

export function close(dialog) {
    if (dialog && dialog.open) {
        dialog.close();
    }
}
