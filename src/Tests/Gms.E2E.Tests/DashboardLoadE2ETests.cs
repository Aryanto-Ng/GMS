using System.Text.RegularExpressions;

namespace Gms.E2E.Tests;

/// <summary>BUG-003: Beranda tidak boleh memuat widget dashboard berulang-ulang (siklus 12 permintaan tanpa henti menghabiskan koneksi DB).</summary>
[Collection(StackCollection.Name)]
public sealed class DashboardLoadE2ETests(StackFixture stack)
{
    private static string ReadFrom(string path, long offset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static long Length(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Length;
    }

    private static readonly string[] ChipWidgets = ["po-late", "po-no-customer-number", "wo-late"];

    private static Dictionary<string, int> CountWidgets(string log) =>
        Regex.Matches(log, @"Start processing HTTP request GET \S+/api/v1/dashboard/(?<w>[a-z-]+)")
            .GroupBy(m => m.Groups["w"].Value).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public async Task Beranda_memuat_tiap_widget_sekali_per_kunjungan_dan_tidak_ada_permintaan_saat_idle()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/work-orders"); // pastikan sirkuit hidup, lalu mulai dari halaman lain
        await Task.Delay(2000);

        var visitStart = Length(stack.WebLogPath);
        await StackFixture.GotoInteractiveAsync(page, "/");
        await Task.Delay(4000);
        var visit = CountWidgets(ReadFrom(stack.WebLogPath, visitStart));

        // Tanpa prerender: satu muatan per widget; widget yang juga dipakai chip ringkasan boleh dua kali.
        Assert.NotEmpty(visit);
        foreach (var (widget, count) in visit)
        {
            var allowed = ChipWidgets.Contains(widget) ? 2 : 1;
            Assert.True(count <= allowed, $"Widget {widget} dimuat {count}x (maks {allowed}). Semua: {string.Join(", ", visit.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        var idleStart = Length(stack.WebLogPath);
        await Task.Delay(10000); // sistem idle: tidak boleh ada permintaan dashboard baru
        var idle = CountWidgets(ReadFrom(stack.WebLogPath, idleStart));
        Assert.True(idle.Count == 0, $"Saat idle masih ada permintaan dashboard: {string.Join(", ", idle.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}
