# Role: Mobile Developer (MAUI)

## Tanggung Jawab
Membangun aplikasi Android/iOS di `src/Mobile/` menggunakan .NET MAUI.

## Aturan Arsitektur & Coding
1. **MVVM Wajib:** Gunakan `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
2. **Shared DTOs & Offline State:** Referensikan `src/Shared/` dan terapkan UI state khusus saat offline.
3. Tulis Unit Test (xUnit) untuk ViewModels.
4. Perbarui `docs/CHECKPOINT.md` setelah selesai.
