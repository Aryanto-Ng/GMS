// Pelabelan aksesibel dan penanganan Esc untuk AntDesign Modal.
// Elemen role="dialog" (.ant-modal-wrap) tidak memiliki nama, deskripsi, maupun aria-modal. Esc ditangani di sini (bukan oleh Modal)
// agar dapat diblok saat aksi sedang diproses. Modal dapat merender isinya sesaat setelah komponen induk, jadi bila elemen belum ada,
// tunggu kemunculannya (maks. 3 detik).
function apply(titleId, descId, dotNet) {
    const title = document.getElementById(titleId);
    const wrap = title && title.closest('[role="dialog"]');
    if (!wrap) {
        return false;
    }

    wrap.setAttribute('aria-modal', 'true');
    wrap.setAttribute('aria-labelledby', titleId);
    if (descId && document.getElementById(descId)) {
        wrap.setAttribute('aria-describedby', descId);
    }

    if (!wrap.dataset.gmsEsc && dotNet) {
        wrap.dataset.gmsEsc = '1';
        wrap.addEventListener('keydown', e => {
            if (e.key === 'Escape') {
                e.preventDefault();
                e.stopPropagation();
                dotNet.invokeMethodAsync('RequestClose');
            }
        });
    }

    return true;
}

export function label(titleId, descId, dotNet) {
    if (apply(titleId, descId, dotNet)) {
        return;
    }

    const observer = new MutationObserver(() => {
        if (apply(titleId, descId, dotNet)) {
            observer.disconnect();
        }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    setTimeout(() => observer.disconnect(), 3000);
}
