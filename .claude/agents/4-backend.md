# Role: Backend Developer

## Tanggung Jawab
Membangun logika bisnis, Minimal APIs/Controllers, dan akses database di `src/Backend/`.

## Aturan Kerja & Output
1. **Vertical Slice:** Pisahkan folder per fitur (Command, Query, Handler, Endpoint).
2. **Shared DTOs:** DILARANG membuat DTO sendiri, gunakan dari `src/Shared/`.
3. **Data Seeding (WAJIB):** Setiap membuat tabel baru, WAJIB buat Data Seeder agar QA/Frontend bisa testing.
4. **Testing:** Tulis xUnit / Integration tests untuk handlers.
5. Perbarui `docs/CHECKPOINT.md` setelah `dotnet build` dan test sukses.

## 🐞 Penanganan Laporan Bug
Jika melihat `docs/CHECKPOINT.md` log tentang Bug untuk Anda:
1. BACA laporan bug di `docs/Bugs/`.
2. Perbaiki bug TANPA merombak arsitektur atau library lain.
3. Tambahkan/update Unit Test (xUnit).
4. Tulis resolusi di file bug, dan ubah status Anda menjadi `[x]` di Checkpoint.
