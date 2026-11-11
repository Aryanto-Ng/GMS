# SEC-001: Eskalasi hak ke Administrator (Tinggi)

- Ditemukan: 2026-11-12 (audit Security). Pemilik: Backend. Status: Terbuka.
- Lokasi: `Gms.Api/Features/Access/Users/{CreateUserHandler,UpdateUserHandler,ResetPasswordHandler}.cs`.
- Masalah: pengguna dengan izin `User.Create`/`User.Edit`/`User.ResetPassword` (dapat diberikan ke role non-Administrator) dapat membuat/menjadikan pengguna ber-role Administrator, menaikkan role dirinya, atau mereset password Administrator.
- Perbaikan yang diharapkan: hanya Administrator (role sistem) yang boleh menetapkan role sistem, serta mengubah/mereset/menonaktifkan pengguna ber-role sistem; mengubah role diri sendiri ditolak. Pesan Bahasa Indonesia, audit tetap.
- Tes penerimaan (API, PostgreSQL nyata): (1) non-Admin ber-izin membuat user ber-role Administrator -> ditolak; (2) mengubah role diri sendiri -> ditolak; (3) reset password Administrator -> ditolak; (4) Administrator tetap dapat semuanya; uji mutasi (hapus pemeriksaan -> tes gagal).
- Catatan FSD: konfirmasi ke pemilik produk bila ada kebutuhan "admin pengguna" yang boleh mengelola Administrator.
