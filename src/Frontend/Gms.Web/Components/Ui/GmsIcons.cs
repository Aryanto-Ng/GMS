namespace Gms.Web.Components.Ui;

/// <summary>
/// Set ikon (garis 1.4px, kotak 16px, ujung bulat; gaya Fluent regular) sebagai path SVG ber-viewBox 16. Tanpa pustaka ikon (K-1).
/// Nama alias (mis. "warning", "close-circle") dipertahankan agar halaman lama tidak perlu diubah selain komponen.
/// </summary>
public static class GmsIcons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["home"] = "M2 7.5 8 2.5l6 5M3.5 6.5V13.5h9V6.5M6.5 13.5v-4h3v4",
        ["cart"] = "M1.5 2.5h2l1.4 7h6.7l1.4-5H4.2M6 13.2h.01M11 13.2h.01",
        ["building"] = "M2 14V6l4 2V6l4 2V3h4v11zM5 11h1M8 11h1M11 11h1",
        ["box"] = "M2 5l6-3 6 3v6l-6 3-6-3zM2 5l6 3 6-3M8 8v6",
        ["money"] = "M1.5 4h13v8h-13zM8 6.2a1.8 1.8 0 1 0 0 3.6 1.8 1.8 0 0 0 0-3.6",
        ["database"] = "M3 4c0-1.1 2.2-2 5-2s5 .9 5 2-2.2 2-5 2-5-.9-5-2zM3 4v8c0 1.1 2.2 2 5 2s5-.9 5-2V4M3 8c0 1.1 2.2 2 5 2s5-.9 5-2",
        ["shield"] = "M8 1.5 13 3v4.5c0 3-2 5.2-5 7-3-1.8-5-4-5-7V3zM6 8l1.5 1.5L10.5 6",
        ["chevron-down"] = "m4 6 4 4 4-4",
        ["search"] = "M7 2.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9M10.5 10.5 14 14",
        ["globe"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M1.8 8h12.4M8 1.8c2 2 2 10.4 0 12.4M8 1.8c-2 2-2 10.4 0 12.4",
        ["plus"] = "M8 3v10M3 8h10",
        ["dismiss"] = "m4 4 8 8M12 4l-8 8",
        ["check"] = "m3.5 8.5 3 3 6-7",
        ["alert"] = "M8 2 14.5 13.5h-13zM8 6.5v3.5M8 12h.01",
        ["error"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M8 4.8v4M8 11h.01",
        ["dismiss-circle"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M5.8 5.8l4.4 4.4M10.2 5.8l-4.4 4.4",
        ["check-circle"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M5.5 8.2l1.8 1.8 3.2-3.8",
        ["info"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M8 7.2v4M8 5h.01",
        ["edit"] = "M10.5 3 13 5.5 5.5 13H3v-2.5zM9 4.5 11.5 7",
        ["ban"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M3.6 3.6l8.8 8.8",
        ["doc"] = "M4 1.8h5.5L13 5.3v8.9H4zM9.5 1.8v3.5H13M6 8h5M6 10.5h5",
        ["truck"] = "M1.5 4h8v7h-8zM9.5 6.5h3l2 2.5v2h-5M4.5 12.5h.01M11.5 12.5h.01",
        ["refresh"] = "M13 7a5 5 0 0 0-9-2M3 2.5V5h2.5M3 9a5 5 0 0 0 9 2M13 13.5V11h-2.5",
        ["bell"] = "M4 11V7a4 4 0 0 1 8 0v4l1.2 1.5H2.8zM6.5 14h3",
        ["lock"] = "M3.5 7h9v6.5h-9zM5.5 7V5a2.5 2.5 0 0 1 5 0v2",
        ["clock"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4A6.2 6.2 0 0 0 8 1.8M8 4.5V8l2.5 1.5",
        ["chart"] = "M3 13V8M7 13V3M11 13V6M14 13H2",
        ["list"] = "M5 4h9M5 8h9M5 12h9M2 4h.01M2 8h.01M2 12h.01",
        ["task"] = "M4.5 2.5h7a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-7A1.5 1.5 0 0 1 3 12V4a1.5 1.5 0 0 1 1.5-1.5M5.5 6.5l1 1 2-2M5.5 11h5",
        ["layers"] = "m8 2 6 3-6 3-6-3zM2 8l6 3 6-3M2 11l6 3 6-3",
        ["people"] = "M6 3.2a2.3 2.3 0 1 0 0 4.6 2.3 2.3 0 0 0 0-4.6M1.8 13c.3-2.2 2-3.5 4.2-3.5s3.9 1.3 4.2 3.5M11 4.2a2.2 2.2 0 0 1 0 4.3M12.5 10c1 .6 1.6 1.5 1.7 3",
        ["person"] = "M8 2.8a2.7 2.7 0 1 0 0 5.4 2.7 2.7 0 0 0 0-5.4M3 14c.4-2.6 2.4-4 5-4s4.6 1.4 5 4",
        ["print"] = "M4.5 6V2h7v4M4.5 11.5h-2v-5h11v5h-2M4.5 9.5h7v4.5h-7z",
        ["download"] = "M8 2.5v8M4.5 7 8 10.5 11.5 7M3 13.5h10",
        ["inbox"] = "M2 9l2-6h8l2 6v4H2zM2 9h3.5a2.5 2.5 0 0 0 5 0H14",
        ["color"] = "M8 1.8a6.2 6.2 0 1 0 0 12.4c1 0 1.5-.7 1.2-1.5-.3-.9.2-1.7 1.1-1.7H12a2.2 2.2 0 0 0 2.2-2.2A6.2 6.2 0 0 0 8 1.8M5 7.5h.01M7 5h.01M10 5.5h.01",
        ["menu"] = "M2.5 4.5h11M2.5 8h11M2.5 11.5h11",
        ["settings"] = "M8 5.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5M8 1.5l1 1.7 2-.4.6 1.9 1.9.6-.4 2 1.7 1-1.7 1 .4 2-1.9.6-.6 1.9-2-.4-1 1.7-1-1.7-2 .4-.6-1.9-1.9-.6.4-2-1.7-1 1.7-1-.4-2 1.9-.6.6-1.9 2 .4z",
        ["calendar"] = "M3 3.5h10v10H3zM3 6.5h10M5.5 2v3M10.5 2v3",
        ["unlock"] = "M3.5 7h9v6.5h-9zM5.5 7V5a2.5 2.5 0 0 1 4.8-1",
    };

    /// <summary>Alias nama AntDesign yang sudah dipakai halaman → nama set ini.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["warning"] = "alert",
        ["close-circle"] = "dismiss-circle",
        ["info-circle"] = "info",
        ["clock-circle"] = "clock",
        ["down"] = "chevron-down",
        ["user"] = "person",
        ["bg-colors"] = "color",
        ["file-text"] = "doc",
        ["tool"] = "building",
        ["wallet"] = "money",
        ["safety"] = "shield",
        ["close"] = "dismiss",
    };

    public static string Resolve(string name) => Aliases.TryGetValue(name, out var target) ? target : name;

    public static string PathFor(string name) => Paths.TryGetValue(Resolve(name), out var path) ? path : Paths["info"];

    public static bool Exists(string name) => Paths.ContainsKey(Resolve(name));
}
