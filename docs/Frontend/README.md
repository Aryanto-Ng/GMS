# Frontend (Blazor Web App + Fluent UI 2)

Proyek: `src/Frontend/Gms.Web`; tes: `src/Tests/Gms.Web.Tests` (bUnit + xunit.v3, `dotnet test --project …`).

## Autentikasi (BFF, ADR-002)
- Token sesi opaque dari API disimpan di cookie terenkripsi `gms.auth` (HttpOnly, SameSite=Lax, geser 30 menit). Browser tidak pernah memegang token.
- Klaim: `gms:session`, `gms:perm` (satu per izin), `gms:full-name`, `gms:role`, `gms:is-admin`, `gms:must-change-password`.
- Halaman akun (Login, Ganti Password, 403, Error, 404) = Static SSR (`[ExcludeFromInteractiveRouting]`), form `post` ke `/account/login|logout|change-password` (antiforgery). `returnUrl` disaring `AccountEndpoints.SafeReturnUrl` (anti open-redirect). `MustChangePasswordMiddleware` memaksa ganti password.
- Otorisasi: kebijakan fallback = harus login; kebijakan dinamis `perm:<Izin>`. Menu hanya menampilkan item yang diizinkan.

## Klien API
- `GmsApiClient` (HttpClient bernama `gms-api`, Bearer dari `ISessionTokenProvider`): semua galat → `ApiException(GmsProblemDetails)`; gangguan jaringan/timeout → 503; badan non-JSON tidak dibocorkan.
- Saklar `Api:UseMock` (ADR-014): saat ini hanya `IAuthApi` (Http/Mock). Modul lain menyusul saat halaman dibuat.
- `ProblemPresenter` memetakan Problem Details ke jenis UI (`ProblemKind`) sesuai `docs/Design/_Foundation-UI.md` §7; `ApiErrorHandler` mengalihkan 401 ke `/account/session-ended`.
- Format tanpa ketergantungan ICU: `IdFormat` (tanggal, qty `12.500`, `Rp 1.250.000`, tarif ≤5 desimal; zona WIB), `IdNumber` (parser ketat; "1.5" ditolak karena ambigu).

## Komponen bersama (`Components/Shared`)
PageHeader, MetricCard, EmptyState, ErrorState, SkeletonRows, PagerBar, StatusChip, ProblemBanner, FormSummary, AsyncContent, UnsavedChangesGuard, ImpactPreviewTable, ReasonDialog (`ShowReason="false"` = konfirmasi biasa), SizeMatrixGrid (+ `SizeMatrixLogic`), MasterList (Daftar Master Generik), TextField, LookupSelect, ImportWizard. Status selalu ikon + teks; galat menampilkan `traceId`.

## Render mode
Interactive Server untuk halaman kerja; Static SSR untuk halaman akun (sesuai FSD).

## Belum / berikutnya
- Selesai: seluruh modul (5.0–5.9). Berikutnya: QA (smoke terhadap API nyata, Playwright) dan DevOps.
- Komponen tersisa: Matriks Progres (bersama modul WO).
- Perilaku JS (keyboard/tempel grid, dialog native) diuji via Playwright (QA).

## Migrasi ke AntDesign Blazor (acuan: `docs/Design.md`)

Status: **fondasi dan shell selesai (2026-11-11)**; halaman dan komponen bersama masih Fluent dan dimigrasi bertahap (Fluent dihapus setelah selesai).

**Mekanisme tema (keputusan):** AntDesign Blazor 1.6.2 hanya menjadikan palet warna sebagai variabel CSS; radius, tinggi kontrol, dan warna komponen berupa nilai tetap, dan tema gelap adalah stylesheet terpisah. Karena itu token `Design.md` diterapkan dalam tiga lapis:
1. `wwwroot/css/tokens.css`: token bagian 4 sebagai CSS variable (`--primary`, `--bg-container`, `--space-4`, ...) untuk terang dan gelap (`html[data-theme]`), plus alias `--gms-*` agar CSS halaman lama ikut palet baru.
2. `wwwroot/css/ant-theme.css`: menimpa komponen AntDesign (Button, Input, Select, Table, Modal, Tag, Alert, Tabs, ...) memakai token; menimpa variabel palet `--ant-*`. Dimuat setelah stylesheet AntDesign.
3. `wwwroot/js/theme-init.js` (sinkron di `<head>`): memilih stylesheet AntDesign terang (`variable.css`) atau gelap (`dark.css`) dan mengatur `data-theme` sebelum render pertama. Pilihan disimpan di `localStorage` (`gms.theme`: system/light/dark). `FluentDesignTheme` mengikuti pilihan itu selama komponen Fluent masih ada.

**Shell:** `Components/Layout/MainLayout.razor` (header 56px, tanpa sider), `MegaMenu.razor` (klik, satu panel, Esc/scrim, `aria-expanded`), katalog di `NavCatalog.cs` (Kategori → Grup → Halaman/Aksi, disaring menurut izin), CommandPalette `wwwroot/js/shell.js` (`Ctrl K`, penyaringan di browser dari `#gms-catalog`, Terakhir dibuka di `localStorage`). Komponen AntDesign dipakai dengan nama penuh (`<AntDesign.Icon>`) selama namespace Fluent masih global, untuk menghindari bentrok nama (`Icon`, `Button`, `Color`).

**Catatan:** AntDesign Blazor 1.x bergaya Ant Design 4 (bukan v5). Master Data memuat 13 entri katalog (batas Design.md: ±12).

