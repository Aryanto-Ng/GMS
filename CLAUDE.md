# [ProjectName] - Global Project Context & Constraints

## Tech Stack Utama
- **Backend:** .NET Core C# (Versi Terbaru)
- **Frontend:** Blazor WebApp (AntDesign v5 Enterprise)
- **Mobile:** .NET MAUI (MVVM)
- **Database:** PostgreSQL
- **Arsitektur:** Vertical Slice Design + Clean Architecture (Backend), MVVM (Mobile)

## 🚨 ATURAN KETAT LIBRARY & DEPENDENCY 🚨
1. **DILARANG KERAS** menggunakan library berbayar/komersial (Syncfusion, Telerik, dll). Gunakan murni open-source.
2. **WAJIB KONFIRMASI:** Selalu minta izin user sebelum menambahkan NuGet package baru.
3. **GLOBAL LIBRARY FILE:** Semua manajemen versi WAJIB terpusat di `src/Directory.Packages.props` (Central Package Management). DILARANG menulis versi di `.csproj`.

## 🚨 ATURAN GIT & DEFINITION OF DONE (DoD) 🚨
1. **Git Branching Strategy:** Seluruh pekerjaan pengembangan dilakukan di branch `uat`. Merge ke `main` (Production) HANYA dilakukan setelah UAT lulus dan disetujui.
2. **Git Commit Otonom:** Setiap kali menyelesaikan SATU checklist di `docs/CHECKPOINT.md`, Anda WAJIB melakukan `git add .` dan `git commit -m "feat/fix/chore: deskripsi"` (Conventional Commits).
3. **DoD:** Sebelum berpindah peran, Anda WAJIB memastikan kode bisa di-build tanpa error (`dotnet build`).

## 🚨 ATURAN KETAT KOMPATIBILITAS LINTAS OS & ENVIRONMENT PARITY 🚨
1. **Case-Sensitivity:** OS Linux (GitHub Actions CI/Server UAT/PROD) bersifat *case-sensitive*. Semua nama file, folder, namespace, rute API, dan aset static WAJIB cocok kapitalisasinya secara presisi.
2. **Path Separators:** DILARANG KERAS menggunakan `\` (backslash). Selalu gunakan `/` (forward-slash) atau `Path.Combine(...)`.
3. **Culture Invariance:** Parsing tanggal, waktu, atau desimal pada logika, API, serialisasi, dan penyimpanan wajib menggunakan `CultureInfo.InvariantCulture`. Khusus tampilan dan input pengguna di UI, gunakan budaya `id-ID` secara **eksplisit** (bukan budaya bawaan server) sesuai format di `docs/Design.md`; nilai dikonversi ke Invariant sebelum dikirim ke API.
4. **Git Local Config:** Pastikan `git config core.ignorecase false` aktif di repositori lokal.

## 🚨 ATURAN MULTI-ENVIRONMENT CONFIGURATION (3-TIER) 🚨
1. **Tier 1 (Base):** `appsettings.json` menyimpan pengaturan umum default.
2. **Tier 2 (Environment Specific):** `appsettings.UAT.json` dan `appsettings.Production.json` menyimpan variabel non-sensitif (URL API, CORS, Log Level).
3. **Tier 3 (Secrets / Overrides):** Password DB, API Keys, dan Private Keys DILARANG di-commit. Gunakan Environment Variables (`.env`) atau GitHub Secrets.

## Agent Routing Workflow
Identifikasi fase yang diminta user, lalu BACA file berikut sebelum bekerja:
1. Analisa Requirement -> `.claude/agents/1-system-analyst.md`
2. Desain Visual & UX -> `.claude/agents/2-ui-ux.md`
3. Arsitektur & Database -> `.claude/agents/3-architect.md`
4. Coding Backend API -> `.claude/agents/4-backend.md`
5. Coding Frontend Blazor -> `.claude/agents/5-frontend.md`
6. Coding Mobile MAUI -> `.claude/agents/6-mobile.md`
7. Testing & QA -> `.claude/agents/7-qa.md`
8. Security Audit -> `.claude/agents/9-security.md`
9. Infra & Deployment -> `.claude/agents/8-devops.md`

## 📍 WAJIB BACA: Sistem Checkpoint
Setiap menerima instruksi baru, Anda WAJIB:
1. Membaca `docs/CHECKPOINT.md` untuk mengetahui status proyek.
2. Setelah tugas selesai, edit `docs/CHECKPOINT.md` (ubah `[ ]` jadi `[x]`), tinggalkan pesan handover, dan lakukan Git Commit.
