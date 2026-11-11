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

Status: **Fluent UI sudah dihapus seluruhnya (2026-11-11)**: shell, komponen bersama, dan semua halaman memakai AntDesign Blazor + token Design.md. Paket `Microsoft.FluentUI.*` dan penyedia Fluent sudah dilepas dari proyek.

**Mekanisme tema (keputusan):** AntDesign Blazor 1.6.2 hanya menjadikan palet warna sebagai variabel CSS; radius, tinggi kontrol, dan warna komponen berupa nilai tetap, dan tema gelap adalah stylesheet terpisah. Karena itu token `Design.md` diterapkan dalam tiga lapis:
1. `wwwroot/css/tokens.css`: token bagian 4 sebagai CSS variable (`--primary`, `--bg-container`, `--space-4`, ...) untuk terang dan gelap (`html[data-theme]`), plus alias `--gms-*` agar CSS halaman lama ikut palet baru.
2. `wwwroot/css/ant-theme.css`: menimpa komponen AntDesign (Button, Input, Select, Table, Modal, Tag, Alert, Tabs, ...) memakai token; menimpa variabel palet `--ant-*`. Dimuat setelah stylesheet AntDesign.
3. `wwwroot/js/theme-init.js` (sinkron di `<head>`): memilih stylesheet AntDesign terang (`variable.css`) atau gelap (`dark.css`) dan mengatur `data-theme` sebelum render pertama. Pilihan disimpan di `localStorage` (`gms.theme`: system/light/dark). `FluentDesignTheme` mengikuti pilihan itu selama komponen Fluent masih ada.

**Shell:** `Components/Layout/MainLayout.razor` (header 56px, tanpa sider), `MegaMenu.razor` (klik, satu panel, Esc/scrim, `aria-expanded`), katalog di `NavCatalog.cs` (Kategori → Grup → Halaman/Aksi, disaring menurut izin), CommandPalette `wwwroot/js/shell.js` (`Ctrl K`, penyaringan di browser dari `#gms-catalog`, Terakhir dibuka di `localStorage`). Komponen AntDesign dipakai dengan nama penuh (`<AntDesign.Icon>`) selama namespace Fluent masih global, untuk menghindari bentrok nama (`Icon`, `Button`, `Color`).

**Catatan:** AntDesign Blazor 1.x bergaya Ant Design 4 (bukan v5). Master Data memuat 13 entri katalog (batas Design.md: ±12).

### Komponen bersama (langkah 2, 2026-11-11)

| Nama Design.md | Implementasi (`Components/Shared/`) | Catatan |
|---|---|---|
| StatusTag | `StatusTag.razor` + `StatusPresentation.cs` | Ikon AntDesign + teks; nada neutral/cyan/blue/success/closed/warning/danger sesuai 4.1 |
| StatCard | `StatCard.razor` (+ `DashboardCard.razor` untuk state per kartu) | Tanpa penanda garis kiri; peringatan/bahaya = ikon + teks |
| ConfirmDialog | `ConfirmDialog.razor` (+ `.razor.js`) | **Deviasi:** memakai `<dialog>` native, bukan `<Modal>`. Alasan: fokus terkurung, Esc yang dapat diblok saat memproses, dan pengembalian fokus ditangani browser; dipakai 31 halaman dan sudah teruji. Tombol memakai `AntDesign.Button` (`Loading`, `Danger`). Menunggu keputusan pemilik produk |
| QtyGrid | `QtyGrid.razor` (+ `.razor.js`) | Input native bergaya token (`border-control`, radius-xs) |
| FormField / Input / Select | `TextField.razor`, `LookupSelect.razor` | Label di atas, penanda wajib, petunjuk, **Error** terhubung (`aria-invalid`, `aria-describedby`) |
| Alert | `ProblemBanner.razor` (Problem Details), `FormSummary.razor` | Kelas `gms-banner` bernada info/success/warning/error |
| Empty / Error state | `EmptyState.razor` (ikon `inbox`), `ErrorState.razor` | |
| DataTable | kelas `gms-table` + `PagerBar.razor` | Komponen `DataTable<T>` dibuat saat halaman dimigrasi (tiap halaman masih menulis tabelnya sendiri) |

Tombol di komponen bersama = `AntDesign.Button` (atribut `aria-label` lewat parameter `AriaLabel`); tautan bergaya tombol = `<a class="ant-btn ant-btn-*">`. Halaman yang belum dimigrasi masih memakai `FluentButton`; `app.css` menyelaraskan token Fluent (aksen, radius) dengan token GMS sebagai jembatan sementara. Pemilih tes: gunakan `button.ant-btn` bersama `fluent-button` selama masa transisi.

### Halaman (langkah 3, 2026-11-11)
Seluruh halaman (Access, Master Data, PO, WO, Produksi, Gudang, Pengiriman, Payroll, Beranda) dimigrasi: `FluentButton` → `AntDesign.Button` (jenis: Accent→Primary, Neutral→Default, aksi baris/inline→`Link`), `FluentAnchor` → `<a class="ant-btn ant-btn-*">`, `FluentIcon` → `AntDesign.Icon`, spinner → `Loading` pada tombol. Aturan penulisan:
- `AntDesign.Button` tidak menerima atribut sembarang: gunakan parameter `AriaLabel`, `HtmlType`, `Loading`, `Danger`. Tombol yang butuh `aria-describedby`/`aria-expanded` memakai `<button class="ant-btn ant-btn-*">` biasa. Penjelasan tombol nonaktif ada pada pembungkus `<span title="...">`.
- Notifikasi sukses: `IGmsToast` (`Services/GmsToast.cs`, membungkus `IMessageService` AntDesign); menggantikan `IToastService` Fluent.
- Di tes bUnit, teks tombol Ant memuat spasi/indentasi: bandingkan dengan `.TextContent.Trim()`; ikon Ant berperan `img` (gunakan `[role=img][aria-label]` untuk menemukan bar progres).

**Belum dikerjakan:** komponen `DataTable<T>` (halaman masih menulis `<table class="gms-table">` sendiri); peninjauan visual per halaman selain yang disampel (Beranda, Pengguna, Role, PO, FG, Master Data, Input Output, Payroll).

**Dokumen desain (2026-11-12):** `_Foundation-UI.md` v2.0 dan sembilan `*-UI.md` sudah selaras dengan `Design.md` (AntDesign, MegaMenu, token, StatusTag, daftar komponen/token per modul). Tes Web (433) lulus di Linux (container SDK).
