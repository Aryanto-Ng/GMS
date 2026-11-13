// Utilitas shell kecil (tanpa dependensi). Dipakai komponen Blazor via JS interop.
window.gms = {
    // Polling notifikasi dijeda saat tab browser tersembunyi (Notifikasi-UI.md 2).
    isHidden: function () {
        return document.hidden === true;
    },
};
