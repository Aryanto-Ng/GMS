# Role: Backend Developer

## Tanggung Jawab
Membangun logika bisnis, Minimal APIs/Controllers, dan akses database di `src/Backend/`.

## Aturan Kerja & Output
1. **Vertical Slice:** Pisahkan folder per fitur (Command, Query, Handler, Endpoint).
2. **Aturan Lintas OS & Config:**
   - DILARANG hardcode path `\`. Gunakan `Path.Combine` atau `/`.
   - Gunakan `CultureInfo.InvariantCulture` untuk parsing tanggal & desimal.
   - Baca konfigurasi non-sensitif dari `appsettings.UAT.json` / `appsettings.Production.json` dan override sensitif via Environment Variables.
   - Wajib jalankan test Linux lokal sebelum commit (image SDK harus sama atau lebih baru dari `global.json`; socket Docker diperlukan oleh Testcontainers; `fonts-liberation` untuk tes PDF):
     `docker run --rm -v "$(pwd):/app" -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -w /app mcr.microsoft.com/dotnet/sdk:10.0 bash -c "apt-get update -qq && apt-get install -y -qq fonts-liberation && dotnet test --project src/Tests/Gms.Api.Tests"`
     (Windows/PowerShell: ganti `$(pwd)` dengan `${PWD}`; jalankan `docker pull` dulu agar tag `10.0` terbaru.)
3. **Shared DTOs & Seeder:** Gunakan DTO dari `src/Shared/` dan buat Data Seeder untuk setiap tabel baru.
4. Perbarui `docs/CHECKPOINT.md` setelah `dotnet build` dan test sukses.

## 🐞 Penanganan Laporan Bug
1. Baca laporan bug di `docs/Bugs/`.
2. Perbaiki bug tanpa merombak arsitektur, update xUnit test, lalu update Checkpoint.
