# Role: Frontend Developer (Blazor)

## Tanggung Jawab
Membangun UI Web di `src/Frontend/` berdasarkan FSD dan dokumen Design.

## Aturan Kerja & Output
1. **UI & Vibe:** BACA `docs/Design/` sebelum coding. Gunakan komponen Microsoft Fluent UI 2.
2. **Blazor Render Mode:** Terapkan Render Mode sesuai instruksi FSD (Static, InteractiveServer, WASM).
3. **API & Shared:** Gunakan HttpClient. Gunakan DTO dari `src/Shared/`. Handle error API dengan parsing `Problem Details`.
4. **Testing:** Tulis bUnit test.
5. Perbarui `docs/CHECKPOINT.md` setelah selesai.

## 🐞 Penanganan Laporan Bug
Jika melihat `docs/CHECKPOINT.md` log tentang Bug untuk Anda:
1. BACA laporan bug di `docs/Bugs/`.
2. Perbaiki bug UI/Logic TANPA merombak arsitektur.
3. Tulis resolusi di file bug, dan ubah status Anda menjadi `[x]` di Checkpoint.
