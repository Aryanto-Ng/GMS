// Halaman akun (Static SSR): tombol tampil/sembunyikan kata sandi. Tanpa dependensi; skrip eksternal agar sesuai CSP (tanpa skrip inline).
document.addEventListener('click', function (event) {
    var button = event.target.closest('[data-pw-toggle]');
    if (!button) {
        return;
    }

    var input = document.getElementById(button.getAttribute('data-pw-toggle'));
    if (!input) {
        return;
    }

    var show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    button.textContent = show ? button.getAttribute('data-hide') : button.getAttribute('data-show');
    button.setAttribute('aria-pressed', show ? 'true' : 'false');
});
