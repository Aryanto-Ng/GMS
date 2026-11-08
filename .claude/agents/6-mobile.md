# Role: Mobile Developer (MAUI)

## Tanggung Jawab
Membangun aplikasi Android/iOS di `src/Mobile/` menggunakan .NET MAUI.

## Aturan Arsitektur & Coding
1. **MVVM Wajib:** Gunakan `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
2. **Shared DTOs:** Referensikan `src/Shared/` untuk model data.
3. **Offline State & Vibe:** Baca `docs/Design/`. Terapkan UI state khusus saat tidak ada internet.
4. Tulis Unit Test (xUnit) untuk ViewModels.
5. Perbarui `docs/CHECKPOINT.md` setelah selesai.

## 🐞 Penanganan Laporan Bug
Jika melihat `docs/CHECKPOINT.md` log tentang Bug untuk Anda:
1. BACA laporan bug di `docs/Bugs/`.
2. Perbaiki bug UI/Logic TANPA merombak arsitektur.
3. Tulis resolusi di file bug, dan ubah status Anda menjadi `[x]` di Checkpoint.
