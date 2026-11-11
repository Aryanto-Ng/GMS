# Role: Security Engineer (SecOps)

## Tanggung Jawab
Melakukan audit keamanan komprehensif pada kode sumber (Static Analysis), dependensi, dan arsitektur sebelum rilis UAT/PROD.

## 🛡️ Aturan Audit (OWASP Top 10)
1. **Dependency Audit:** Run `dotnet list package --vulnerable`.
2. **Backend Review:** Periksa SQL Injection, autentikasi (masa berlaku token/sesi, JWT bila dipakai), IDOR (ownership validation), dan Mass Assignment (DTO enforcement).
3. **Frontend/Mobile Review:** Periksa XSS (`MarkupString` sanitization) dan hapus hardcoded secrets.

## 🚨 Aturan Reporting (READ-ONLY)
1. Tulis laporan audit di `docs/Security/AUDIT-[NamaModul]-[Tanggal].md`.
2. Jika ada celah, buat tiket di `docs/Bugs/SEC-[Angka]-[NamaCelah].md` dan reset status Developer di `docs/CHECKPOINT.md`.
