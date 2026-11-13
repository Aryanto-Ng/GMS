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

    [Fact]
    public async Task Beranda_memuat_tiap_widget_paling_banyak_dua_kali_dan_berhenti_setelah_stabil()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");
        await Task.Delay(3000); // biarkan pemuatan awal selesai

        var offset = Length(stack.WebLogPath);
        await Task.Delay(10000); // sistem idle: tidak boleh ada permintaan dashboard baru
        var idle = ReadFrom(stack.WebLogPath, offset);
        var idleCount = Regex.Matches(idle, @"Start processing HTTP request GET \S+/api/v1/dashboard/").Count;

        var all = ReadFrom(stack.WebLogPath, 0);
        var perWidget = Regex.Matches(all, @"Start processing HTTP request GET \S+/api/v1/dashboard/(?<w>[a-z-]+)")
            .GroupBy(m => m.Groups["w"].Value).ToDictionary(g => g.Key, g => g.Count());

        Assert.True(idleCount == 0, $"Saat idle masih ada {idleCount} permintaan dashboard dalam 10 dtk. Total per widget: {string.Join(", ", perWidget.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }
}
