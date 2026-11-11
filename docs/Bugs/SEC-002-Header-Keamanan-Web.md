# SEC-002: Header keamanan Web belum ada (Sedang)

- Ditemukan: 2026-11-12. Pemilik: Frontend (+DevOps bila lewat Traefik). Status: Terbuka.
- Masalah: tidak ada CSP, `frame-ancestors`/`X-Frame-Options`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`.
- Perbaikan: middleware di `Gms.Web/Program.cs` (atau label Traefik). CSP kompatibel Blazor Server + AntDesign (gaya inline, `wss:`), `frame-ancestors 'none'`, `nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`.
- Penerimaan: tes integrasi memeriksa header pada `/login`; seluruh E2E (41) tetap lulus; tidak ada galat CSP di console.
