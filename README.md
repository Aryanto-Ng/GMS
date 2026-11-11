# [ProjectName] - Master Project Template (Boilerplate)

## 🚀 Cara Memulai Proyek Baru
1. **Gunakan Template / Clone:** Clone repositori ini atau buat dari GitHub Template Repository.
2. **Setup Git Branching:**
   ```bash
   git init
   git config core.ignorecase false
   git add .
   git commit -m "chore: initial project template"
   git checkout -b uat
   ```
3. **Ubah Placeholder:** Lakukan *Find & Replace* untuk teks `[ProjectName]` dengan nama proyek Anda di seluruh file.
4. **Isi Secrets Checklist:** Buka `docs/Secrets-Checklist.md` dan daftarkan rahasia/kredensial ke **GitHub Secrets** (Environment UAT & Production).
5. **Jalankan Claude Code:**
   ```bash
   claude
   ```
   Berikan instruksi awal: *"Halo Claude, tolong bertindak sebagai System Analyst, mari kita bahas requirement untuk modul pertama."*

## 📂 Struktur Folder Proyek
```text
Claude_Code_Master_Project/
├── README.md                           # Panduan inisialisasi & workflow
├── CLAUDE.md                           # Context global, router 9 agen & rules
├── .claude/
│   └── agents/                         # Prompt & guardrails 9 agen AI
├── deploy/
│   ├── uat/                            # Docker Compose & config UAT (PaaS/VPS)
│   └── prod/                           # Docker Compose & config Production (VPS)
├── docs/
│   ├── CHECKPOINT.md                   # State tracker 9 agen (Active Sprint)
│   ├── ARCHIVE_CHECKPOINT.md           # Arsip sprint/modul yang lulus QA & DevOps
│   ├── Secrets-Checklist.md            # Checklist variabel & Secrets untuk GitHub
│   ├── FSD/                            # Functional Specification Documents
│   ├── Design/                         # Spesifikasi UI/UX & Vibe Rules
│   ├── Bugs/                           # Tiket Laporan Bug (QA/Security/CI)
│   └── Security/                       # Laporan Audit Keamanan
└── src/
    ├── Directory.Packages.props        # Central Package Management (CPM) .NET
    ├── Backend/                        # Vertical Slice Clean Architecture API
    │   ├── appsettings.json
    │   ├── appsettings.Development.json
    │   ├── appsettings.UAT.json
    │   └── appsettings.Production.json
    ├── Frontend/                       # Blazor WebApp (AntDesign Blazor)
    │   └── wwwroot/
    │       ├── appsettings.json
    │       ├── appsettings.UAT.json
    │       └── appsettings.Production.json
    ├── Mobile/                         # .NET MAUI (MVVM)
    └── Shared/                         # API Contracts, DTOs, Enums
```
