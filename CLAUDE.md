# [ProjectName] - Global Project Context & Constraints

## Tech Stack Utama
- **Backend:** .NET Core C# (Versi Terbaru)
- **Frontend:** Blazor WebApp (Microsoft Fluent Design 2)
- **Mobile:** .NET MAUI
- **Arsitektur:** Vertical Slice Design + Clean Architecture (Backend), MVVM (Mobile)

## 🚨 ATURAN KETAT LIBRARY & DEPENDENCY 🚨
1. **DILARANG KERAS** menggunakan library berbayar/komersial (Syncfusion, Telerik, dll). Gunakan murni open-source.
2. **WAJIB KONFIRMASI:** Selalu minta izin user sebelum menambahkan NuGet package baru.
3. **GLOBAL LIBRARY FILE:** Semua manajemen versi WAJIB terpusat di `src/Directory.Packages.props` (Central Package Management). DILARANG menulis versi di `.csproj`.

## 🚨 ATURAN GIT & DEFINITION OF DONE (DoD) 🚨
1. **Git Commit Otonom:** Setiap kali Anda menyelesaikan SATU checklist di `docs/CHECKPOINT.md`, Anda WAJIB melakukan `git add .` dan `git commit -m "feat/fix/chore: deskripsi"` dengan format Conventional Commits.
2. **DoD:** Sebelum berpindah peran, Anda WAJIB memastikan kode bisa di-build tanpa error (`dotnet build`).

## Agent Routing Workflow
Identifikasi fase yang diminta user, lalu BACA file berikut sebelum bekerja:
1. Analisa Requirement -> `.claude/agents/1-system-analyst.md`
2. Desain Visual & UX -> `.claude/agents/2-ui-ux.md`
3. Arsitektur & Database -> `.claude/agents/3-architect.md`
4. Coding Backend API -> `.claude/agents/4-backend.md`
5. Coding Frontend Blazor -> `.claude/agents/5-frontend.md`
6. Coding Mobile MAUI -> `.claude/agents/6-mobile.md`
7. Testing & QA -> `.claude/agents/7-qa.md`
8. Infra & Deployment -> `.claude/agents/8-devops.md`

## 📍 WAJIB BACA: Sistem Checkpoint
Setiap menerima instruksi baru, Anda WAJIB:
1. Membaca `docs/CHECKPOINT.md` untuk mengetahui status proyek.
2. Setelah tugas selesai, edit `docs/CHECKPOINT.md` (ubah `[ ]` jadi `[x]`), tinggalkan pesan handover, dan lakukan Git Commit.
