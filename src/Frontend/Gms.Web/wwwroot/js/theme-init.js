// Tema GMS (Design.md 12): atribut data-theme pada <html> + pilihan stylesheet AntDesign terang/gelap.
// Dimuat sinkron di <head> agar tema benar sebelum render pertama (tanpa kilatan).
(function () {
    var KEY = 'gms.theme';
    function stored() { try { return localStorage.getItem(KEY); } catch (e) { return null; } }
    function resolve(mode) {
        if (mode === 'light' || mode === 'dark') { return mode; }
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    function apply(mode) {
        var resolved = resolve(mode);
        document.documentElement.setAttribute('data-theme', resolved);
        var light = document.getElementById('ant-light');
        var dark = document.getElementById('ant-dark');
        if (light && dark) {
            light.media = resolved === 'dark' ? 'not all' : 'all';
            dark.media = resolved === 'dark' ? 'all' : 'not all';
        }
    }
    window.gmsTheme = {
        get: function () { var s = stored(); return s === 'light' || s === 'dark' ? s : 'system'; },
        set: function (mode) { try { localStorage.setItem(KEY, mode); } catch (e) { } apply(mode); },
        apply: apply
    };
    apply(window.gmsTheme.get());
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            if (window.gmsTheme.get() === 'system') { apply('system'); }
        });
    }
})();
