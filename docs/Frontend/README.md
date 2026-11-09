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
PageHeader, MetricCard, EmptyState, ErrorState, SkeletonRows, PagerBar, StatusChip, ProblemBanner, FormSummary, AsyncContent, UnsavedChangesGuard, ImpactPreviewTable, ReasonDialog, SizeMatrixGrid (+ `SizeMatrixLogic`). Status selalu ikon + teks; galat menampilkan `traceId`.

## Render mode
Interactive Server untuk halaman kerja; Static SSR untuk halaman akun (sesuai FSD).

## Belum / berikutnya
- Halaman per modul (urutan di `CHECKPOINT.md` 5.1–5.9) + `HttpXxxApi`/`MockXxxApi`.
- Komponen: Daftar Master Generik, Wizard Import CSV, Matriks Progres.
- Perilaku JS (keyboard/tempel grid, dialog native) diuji via Playwright (QA).
