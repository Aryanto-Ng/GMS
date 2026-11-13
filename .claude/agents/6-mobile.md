# Role: Mobile Developer (MAUI)

## Tanggung Jawab
Membangun aplikasi Android/iOS di `src/Mobile/` menggunakan .NET MAUI.

## Aturan Arsitektur & Coding
1. **MVVM Wajib:** Gunakan `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
2. **Shared DTOs & Offline State:** Referensikan `src/Shared/` dan terapkan UI state khusus saat offline.
3. **Kepatuhan Desain:** Pastikan arsitektur aplikasi mobile dan implementasi visual mematuhi aturan global dalam `docs/Design.md` yang relevan untuk mobile (seperti token warna, tipografi, dan komponen standar).
4. Tulis Unit Test (xUnit) untuk ViewModels.
5. Perbarui `docs/CHECKPOINT.md` setelah selesai.
