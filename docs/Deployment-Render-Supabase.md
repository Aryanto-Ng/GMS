# Deploy UAT ke Render + Supabase

Panduan langkah demi langkah memasang lingkungan **UAT** dengan database di **Supabase** (PostgreSQL) dan aplikasi di **Render** (dua layanan Docker). Production tetap memakai VPS (`docs/Deployment.md`).

```
Browser ──HTTPS──> Render: gms-uat-web (Blazor Server, BFF) ──HTTPS + X-Gms-Gateway-Key──> Render: gms-uat-api ──TLS──> Supabase (Session pooler)
```

## 0. Hal yang perlu Anda ketahui dulu (paket gratis)

| Hal | Dampak ke UAT | Jalan keluar |
|---|---|---|
| Layanan gratis Render **tidur setelah 15 menit tanpa lalu lintas**, bangun sekitar 1 menit | Akses pertama setelah diam terasa lambat; sirkuit Blazor terputus lalu tersambung ulang | Terima untuk UAT, atau paket berbayar (Starter) |
| Layanan gratis **tanpa disk permanen**, jadi kunci Data Protection (enkripsi cookie `gms.auth`) hilang saat layanan restart/tidur | Pengguna **harus login ulang** setelah layanan bangun | Web berbayar + disk di `/keys` dan env `DataProtection__KeysPath=/keys` |
| Layanan gratis **tidak menerima trafik jaringan privat** (Private Service tidak ada paket gratis) | API harus punya URL publik | Sudah ditangani: **kunci gerbang** `X-Gms-Gateway-Key` (lihat 3). Alternatif: API sebagai Private Service berbayar, kunci gerbang tidak perlu |
| 750 jam gratis per bulan untuk seluruh workspace | Dua layanan yang selalu aktif menghabiskan jatah sekitar pertengahan bulan; karena tidur saat idle biasanya cukup | Pantau di dashboard |
| Supabase gratis: **tanpa backup otomatis**, project **dijeda setelah 1 minggu tanpa aktivitas**, 500 MB | Data UAT bisa hilang bila project dihapus; jeda perlu "Restore" manual | Backup manual (lihat 6), atau paket Pro |

Angka dan batas dapat berubah; cek [Render free tier](https://render.com/docs/free) dan [Supabase pricing](https://supabase.com/pricing) saat memulai.

## 1. Supabase: buat database

1. <https://supabase.com> → **New project**. Pilih region **Southeast Asia (Singapore)**. Buat **Database password** yang kuat, **hanya huruf dan angka** (karakter `;` `=` `@` memecah connection string), dan simpan.
2. Setelah project aktif, buka **Connect** (tombol di atas dashboard) → bagian **Session pooler** (bukan Direct, bukan Transaction):
   - Direct connection di paket gratis hanya **IPv6**; Render keluar lewat IPv4, jadi gunakan **Session pooler** (IPv4, port **5432**, mendukung prepared statements, cocok untuk aplikasi yang berjalan terus).
   - Jangan pakai **Transaction pooler** (port 6543): tidak mendukung prepared statements dan state sesi, yang dipakai Npgsql/EF Core.
3. Salin host dan user, lalu susun connection string **format Npgsql** (bukan URL `postgresql://`):

```
Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.PROJECTREF;Password=PASSWORDANDA;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10
```
   - `Host`: **salin persis dari dialog Connect** (`aws-<n>-<region>.pooler.supabase.com`; angka `<n>` bisa berbeda).
   - `Username`: `postgres.<PROJECTREF>` (user pooler), bukan `postgres`.
   - `SSL Mode=Require` wajib. `Trust Server Certificate=true` menerima sertifikat tanpa verifikasi rantai; untuk verifikasi penuh gunakan root certificate Supabase (opsional, lanjutan).
4. **Kunci Data API (wajib, sekali).** Supabase membuka skema `public` ke internet lewat Data API (PostgREST) untuk role `anon`/`authenticated`. Aplikasi kita tidak memakainya, jadi cabut aksesnya **sebelum deploy pertama**. Buka **SQL Editor** lalu jalankan:

```sql
REVOKE ALL ON SCHEMA public FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM anon, authenticated;
ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM anon, authenticated;
```
   Migrasi aplikasi berjalan sebagai role `postgres`, jadi tabel baru otomatis tidak terbuka. Tambahan: matikan Data API di pengaturan project bila opsinya tersedia.

## 2. GitHub dan Render: Blueprint

1. Pastikan repo di GitHub berisi branch `uat` dengan berkas [render.yaml](../render.yaml) di root. Di `render.yaml`, ganti `SUFFIX` pada kedua `name` dengan akhiran unik Anda (mis. `gms-uat-api-aryan`, `gms-uat-web-aryan`). Nama menjadi alamat `https://<name>.onrender.com`. Commit dan push ke `uat`.
2. <https://dashboard.render.com> → **New** → **Blueprint** → hubungkan akun GitHub dan pilih repo → branch `uat`. Render membaca `render.yaml` dan menampilkan dua layanan.
3. Render meminta nilai untuk variabel `sync: false`. Isi:

| Layanan | Variabel | Nilai |
|---|---|---|
| API | `ConnectionStrings__Default` | connection string dari langkah 1.3 |
| API | `Seed__AdminPassword` | password awal admin (min 8 karakter, ada huruf dan angka) |
| API | `Gateway__Key` | kunci acak: `openssl rand -base64 32` (atau pembangkit password 32+ karakter) |
| Web | `Api__BaseUrl` | `https://gms-uat-api-SUFFIX.onrender.com` (tanpa garis miring akhir) |
| Web | `Api__GatewayKey` | **sama persis** dengan `Gateway__Key` |
| Web | `AllowedHosts` | `gms-uat-web-SUFFIX.onrender.com` (tanpa `https://`) |

4. **Apply**. Render membangun image dari `deploy/Dockerfile.api` dan `deploy/Dockerfile.web` (build pertama beberapa menit). Saat API pertama kali berjalan ia menjalankan migrasi dan mengisi data contoh (`Database__MigrateOnStartup`, `Seed__DemoData`).

Penerapan berikutnya: Render memakai `autoDeployTrigger: checksPass`, artinya **deploy otomatis setiap push ke `uat` hanya bila cek GitHub Actions (`ci.yml`) pada commit itu lulus**. Tes merah = tidak ada deploy. Workflow SSH lama (`deploy-uat.yml`) sudah dihapus karena tidak diperlukan.

> Bila nama layanan ternyata sudah dipakai orang lain, Render menambahkan akhiran acak ke URL. Lihat URL sebenarnya di dashboard lalu perbarui `Api__BaseUrl` dan `AllowedHosts`.

## 3. Mengapa ada "kunci gerbang"

API gratis di Render tidak bisa privat, sehingga siapa pun yang tahu URL-nya dapat memanggilnya. Aplikasi sudah menolak permintaan tanpa sesi, tetapi kami menambah satu lapisan: bila `Gateway__Key` diisi, API menolak **semua** permintaan (kecuali `/api/v1/health`) yang tidak membawa header `X-Gms-Gateway-Key` yang benar, dengan 403, sebelum autentikasi. Web mengirim header itu otomatis dari `Api__GatewayKey`. Kunci hanya ada di environment Render, tidak pernah di repository atau browser. Di VPS/compose (API internal) kunci dibiarkan kosong.

## 4. Verifikasi setelah deploy

Ganti `API` dan `WEB` dengan URL Anda.

```bash
curl -s https://API/api/v1/health                       # {"status":"ok"} (boleh lambat saat bangun)
curl -s -o /dev/null -w "%{http_code}\n" https://API/api/v1/auth/me     # 403 (tanpa kunci gerbang)
curl -s https://WEB/healthz                              # {"status":"ok"}
curl -sI https://WEB/login | grep -i -E "content-security|x-frame|strict-transport"
```

Lalu di browser:
1. Buka `https://WEB`, login `admin` + `Seed__AdminPassword`; Anda diminta mengganti password.
2. Beranda terbuka, MegaMenu dan `Ctrl K` berfungsi, data contoh tampil (Master Data, PO).
3. Buat atau buka satu Surat Jalan dan **cetak PDF** (uji font Linux di image API).
4. Diamkan 15+ menit, buka lagi: halaman lambat sekali lalu meminta login ulang (perilaku paket gratis, lihat 0).

Untuk verifikasi keamanan (SEC-002/003/004): header CSP ada, 5x login salah ke akun yang sama mengunci akun, lebih dari 20 percobaan login per menit dari satu IP dialihkan dengan pesan "Terlalu banyak percobaan".

## 5. Operasi harian

- **Log:** Render dashboard → layanan → **Logs**.
- **Deploy ulang manual:** layanan → **Manual Deploy**. **Rollback:** tab Events/Deploys → deploy sebelumnya.
- **Ubah env:** layanan → **Environment**; Render me-restart otomatis.
- **Data demo:** setelah UAT berjalan dan Anda ingin data bersih, set `Seed__DemoData=false` dan kosongkan tabel lewat SQL Editor sebelum UAT sungguhan.
- **Migrasi baru:** otomatis saat API dimulai (`Database__MigrateOnStartup=true`). Setelah rilis yang menambah tabel, jalankan ulang empat perintah `REVOKE`/`ALTER DEFAULT PRIVILEGES` bila project dibuat ulang.

## 6. Backup (paket gratis Supabase tidak punya)

Dump manual dari komputer Anda (PostgreSQL client terpasang), memakai Session pooler:

```bash
pg_dump "postgresql://postgres.PROJECTREF:PASSWORD@aws-1-ap-southeast-1.pooler.supabase.com:5432/postgres?sslmode=require" \
  --no-owner --format=custom --file=gms-uat-$(date +%Y%m%d).dump
```
Pulihkan dengan `pg_restore --no-owner -d "<connection string tujuan>" gms-uat-YYYYMMDD.dump`. Lakukan sebelum rilis besar dan sebelum project berpotensi dijeda. Production tetap memakai backup harian off-site (`deploy/backup/`).

## 7. Masalah umum

| Gejala | Penyebab / solusi |
|---|---|
| API gagal start: `password authentication failed` | Username harus `postgres.<PROJECTREF>`; password salah/berisi karakter khusus |
| `Tenant or user not found` | Host pooler salah; salin ulang dari dialog Connect |
| `Network is unreachable` / timeout ke `db.<ref>.supabase.co` | Anda memakai Direct (IPv6). Ganti ke Session pooler |
| Web: halaman putih/berulang menyambung | Layanan sedang bangun; tunggu 1 menit. WebSocket didukung Render |
| Web mengalihkan ke login terus / `403` dari API | `Api__GatewayKey` ≠ `Gateway__Key`, atau `Api__BaseUrl` salah |
| `Bad Request - Invalid Hostname` di Web | `AllowedHosts` tidak sama dengan host Web |
| Login sukses tapi langsung keluar | Cookie tidak valid setelah restart (tanpa disk, lihat 0) atau `X-Forwarded-Proto` tidak diterima; periksa log |
| Render: "no open ports detected" | Variabel `PORT=8080` hilang |
| PDF gagal | Lihat log API; font ada di image (`fonts-liberation`) |

## 8. Naik kelas (bila UAT dipakai serius)

1. Web **Starter** + **Persistent Disk** di `/keys` dan `DataProtection__KeysPath=/keys` (login bertahan), dan API Starter (tidak tidur).
2. API sebagai **Private Service** (berbayar), lalu kosongkan `Gateway__Key`/`Api__GatewayKey` dan pakai alamat internal Render untuk `Api__BaseUrl`.
3. Supabase Pro (backup harian, tanpa jeda).
