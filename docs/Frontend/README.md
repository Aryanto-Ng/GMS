# Frontend (Blazor Web App, Fluent 2 — komponen sendiri)

Proyek: `src/Frontend/Gms.Web`; tes: `src/Tests/Gms.Web.Tests` (bUnit + xunit.v3) dan `src/Tests/Gms.E2E.Tests` (Playwright terhadap tumpukan nyata).
Acuan visual TUNGGAL: `docs/Design.md` v2.0 (Microsoft Fluent 2) dan `docs/Design/*-UI.md`. **Tanpa pustaka UI pihak ketiga** (keputusan K-1, 2026-11-13): komponen dibangun sendiri dengan CSS token. AntDesign dan Fluent UI Blazor sudah dihapus dari proyek dan `Directory.Packages.props`.

## Autentikasi (BFF, ADR-002)
- Token sesi opaque dari API disimpan di cookie terenkripsi `gms.auth` (HttpOnly, SameSite=Lax, geser 30 menit). Browser tidak pernah memegang token.
- Klaim: `gms:session`, `gms:perm` (satu per izin), `gms:full-name`, `gms:role`, `gms:is-admin`, `gms:must-change-password`.
- Halaman akun (Login, Ganti Password, 403, Error, 404) = Static SSR (`[ExcludeFromInteractiveRouting]`) dengan `AccountLayout` (latar SVG bergerak + kartu 420px, Design.md 10); form `post` ke `/account/login|logout|change-password` (antiforgery). `GET /account/language?culture=id-ID|en-US` menulis cookie bahasa lalu kembali (alamat lokal aman). `returnUrl` disaring `AccountEndpoints.SafeReturnUrl`.
- Otorisasi: kebijakan fallback = harus login; kebijakan dinamis `perm:<Izin>`. Menu dan hasil pencarian hanya memuat entri yang diizinkan.

## Klien API
- `GmsApiClient` (HttpClient bernama `gms-api`, Bearer dari `ISessionTokenProvider`): semua galat → `ApiException(GmsProblemDetails)`; gangguan jaringan/timeout → 503.
- Saklar `Api:UseMock` (ADR-014): tiap antarmuka punya `HttpXxxApi` dan `MockXxxApi`.
- `ProblemPresenter` memetakan Problem Details ke jenis UI; `ApiErrorHandler` mengalihkan 401 ke `/account/session-ended`.
- Format tanpa ICU: `IdFormat` (tanggal, qty, `Rp`, tarif sesuai FSD; zona WIB), `IdNumber` (parser ketat).

## Shell dan token (Design.md 3 sampai 9)
- `wwwroot/css/tokens.css`: satu-satunya berkas dengan nilai mentah (token Design.md bagian 8 dan 9; **tema terang saja**). `wwwroot/app.css`: semua gaya memakai `var(--token)`.
- `MainLayout` = 4 zona: `AppHeader` (logo dari `AppIdentityOptions`, pencarian halaman/aksi maks. 7 hasil, menu bahasa, `NotificationBell`, menu pengguna) → `TopNav` (kategori, panel mega, grup dan item berikon; sumber `NavCatalog`) → `TabBar` → workspace berisi `TabHost` + footer. `ToastHost` di dasar layout.
- **Tab Manager** (`Services/Tabs`): Router tetap menentukan halaman untuk URL aktif; `MainLayout` menyelaraskannya ke `TabManager` (buka/fokus, maks. 10, tab Beranda terpaku). `TabHost` merender SEMUA tab terbuka (yang tidak aktif `hidden`) lewat `AuthorizeRouteView` per tab sehingga isi form, filter, dan sub-tab hidup per tab. ID tab = path (`/` → `home`, `/x/{id}` dan `/x/{id}/edit` satu tab, `/x/new` satu tab per layar). `PageHeader` menetapkan judul tab; `UnsavedChangesGuard` menandai tab belum disimpan (menutup tab dirty meminta konfirmasi di `TabBar`; navigasi internal tidak ditanya karena halaman tab tetap hidup); `TabContext.SetAlert` untuk ikon alert.
- **Query halaman:** `[SupplyParameterFromQuery]` DILARANG pada halaman interaktif (membaca URL aktif dan memengaruhi tab tersembunyi). Pakai `[Parameter, TabQuery("nama")]`; `TabManager` mengisinya dari query string tab itu sendiri (bool, Guid, int, string).
- Header/Router: `Routes.razor` mencascade `RouteData` ke layout; `@Body` layout tidak dirender (isi halaman dari `TabHost`).

## Komponen (Design.md 13; pemetaan dari nama lama di `_Foundation-UI.md` bagian 8)
`GmsButton` (Default/Primary/Link, `Danger`, `Loading`, `Icon`, `AriaLabel`), `GmsIcon` (set SVG sendiri `GmsIcons`; nama alias Ant dipertahankan), `StatusBadge` + `StatusPresentation` (peta 6 warna `t-proc/t-prog/t-ok/t-obs/t-err/t-warn`; Cancelled/Void abu), `StatTile`, `PageHeader`, `ProblemBanner`/`FormSummary` (MessageBar), `ConfirmDialog` (Dialog 480px; **lebar 720px** otomatis bila ada pratinjau dampak; fokus terkurung + Esc lewat `ConfirmDialog.razor.js`; `CancelText`), `ToastHost` + `IGmsToast`/`GmsToast`, `EmptyState`/`ErrorState`/`SkeletonRows`, `PagerBar`, `QtyGrid`, `ProgressMatrix`, `StackedProgress`, `ImportWizard`, `MasterList<T>`, `TextField`/`LookupSelect`, `AsyncContent`. Tabel memakai kelas `gms-table`.

## Tes
- bUnit: 452 lulus; E2E: 48 lulus (`dotnet test --project src/Tests/Gms.Web.Tests`). Pemilih: tombol `button.gms-btn`; dialog `.gms-dialog-wrap`, `.gms-dialog-body`, `.gms-dialog-footer`; badge `.gms-tag.t-*`.
- E2E: Playwright terhadap Postgres + API + Web nyata (`ShellE2ETests`: panel navigasi, pencarian header, tab menjaga isi form dan konfirmasi tutup, tema terang saja). `ScreenshotE2ETests` menangkap layar bila `GMS_SHOTS_DIR` diisi (peninjauan visual).
- Bila `Gms.Api`/`Gms.Web` sedang berjalan dan mengunci `bin/`, bangun dan uji ke jalur lain: `-p:UseArtifactsOutput=true -p:ArtifactsPath=<folder>` (E2E mencari keluaran dengan tata letak itu).

## Notifikasi (FSD Notifikasi v1.0, `docs/Design/Notifikasi-UI.md`)
- `Services/Api/NotificationApis.cs` (Http + Mock), `Services/Notifications/NotificationText.cs` (kunci resource + parameter → kalimat dalam bahasa aktif; `NotificationLinks` memetakan tujuan klik ke rute), `Layout/NotificationBell.razor`, `Pages/Notifications/{NotificationsPage,NotificationSettingsPage}.razor`.
- i18n: `Resources/NotificationResource.resx` (Indonesia, bawaan) dan `.en.resx`; menambah jenis notifikasi = tambah 3 kunci (`{TypeCode}.Title/Body/Badge`) di kedua berkas. Culture dipilih lewat cookie (`/account/language`).

## Penyempurnaan halaman (2026-11-14)
`Stepper` dipakai di detail PO/WO/Periode Gaji; `ChartStack`/`ChartBars` di Beranda (tanpa donut); header halaman berikon dengan satu primary per status; `TabContext.SetAlert` dipakai detail PO/WO/Periode (variabel konteks di halaman bernama `TabCtx` karena `Tab` adalah enum bagian halaman).

## Penyempurnaan tahap 2 (2026-11-14)
- `MessageBar` (info/peringatan/sukses dapat ditutup; error tidak; pesan penahan aksi `Closable=false`); 14 banner satu-baris dimigrasi.
- `StatusChips` (chip berjumlah dari server) pada Surat Jalan, Serah Terima FG, dan `MasterList` (semua master); jumlah = query 1 baris per status.
- Alert tab: PO/WO terlambat, Periode Open lewat tanggal, FG Belum Siap, Input Output (merah = sel melebihi batas, oranye = peringatan).
- **i18n chrome (K-2):** `Resources/ChromeResource(.en).resx` (142 kunci: navigasi, header, tab, footer, form masuk, status turunan). Bahasa dipilih HANYA lewat cookie (`/account/language`); header `Accept-Language` diabaikan (bawaan Indonesia). `LocalizerExtensions.Or` memberi fallback bila kunci belum ada. Nama status dokumen (Draft, In Progress, ...) tetap Inggris. Pesan galat per halaman modul masih Indonesia (kunci belum diekstrak). bUnit memaksa `id-ID` di `WebTestBase`.
- **DEVIASI-4:** Koreksi Output/Pengiriman dan Ubah Qty WO tetap dialog (lebar 720px), bukan halaman; **disetujui Ary 2026-11-14** (lihat `_Foundation-UI.md` bagian 12).

## Status migrasi Fluent 2
- **Selesai (2026-11-14):** token + CSS, ikon, tombol, toast, dialog, status/badge, shell 4 zona, Tab Manager, form masuk, penghapusan AntDesign, tes bUnit dan E2E.
- **Berikutnya (penyempurnaan per halaman, `docs/Design/*-UI.md` bagian "Revisi Fluent 2"):** header halaman berikon dengan satu primary per status, `Stepper` (PO/WO/Periode), `MessageBar` yang dapat ditutup, filter chip berjumlah + pencarian pada semua `DataList`, Beranda (hero satu baris, `ChartStack`/`ChartBars`, kartu interaktif), alert tab per layar, dialog Koreksi/Ubah Qty menjadi halaman, kunci resource i18n ID/EN (K-2), (lonceng notifikasi: selesai 2026-11-14).
