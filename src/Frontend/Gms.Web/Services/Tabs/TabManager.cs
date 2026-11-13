using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Gms.Web.Services.Tabs;

/// <summary>
/// Menandai properti halaman yang diisi dari query string TAB-nya sendiri (menggantikan <c>[SupplyParameterFromQuery]</c>, yang membaca URL aktif
/// dan akan memengaruhi halaman di tab tersembunyi). Properti wajib juga <c>[Parameter]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TabQueryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public enum TabAlertKind { None, Warning, Error }

/// <summary>Satu tab dokumen (Design.md 6): ID = layar + nomor dokumen; keadaan halaman dipertahankan selama tab terbuka.</summary>
public sealed class Tab
{
    internal Tab(string id, Type pageType, IReadOnlyDictionary<string, object?> parameters, string url, bool pinned)
    {
        Id = id;
        PageType = pageType;
        Parameters = parameters;
        Url = url;
        Pinned = pinned;
        Title = DefaultTitle(pageType);
    }

    public string Id { get; }

    public Type PageType { get; internal set; }

    /// <summary>Nilai route + query yang diteruskan sebagai parameter halaman.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; internal set; }

    public string Url { get; internal set; }

    public bool Pinned { get; }

    public string Title { get; internal set; }

    public bool Dirty { get; internal set; }

    public TabAlertKind Alert { get; internal set; }

    public string? AlertText { get; internal set; }

    /// <summary>Naik setiap "Muat ulang tab": membuat ulang komponen halaman.</summary>
    public int Generation { get; internal set; }

    /// <summary>Konteks yang dicascade ke halaman agar PageHeader/UnsavedChangesGuard dapat menetapkan judul, status belum disimpan, dan alert.</summary>
    public TabContext Context { get; internal set; } = null!;

    private static string DefaultTitle(Type type) => type.Name.EndsWith("Page", StringComparison.Ordinal) ? type.Name[..^4] : type.Name;
}

/// <summary>Tautan halaman ↔ tab-nya. Semua setter aman dipanggil dari komponen halaman.</summary>
public sealed class TabContext(TabManager manager, string tabId)
{
    public string TabId { get; } = tabId;

    public void SetTitle(string title) => manager.SetTitle(TabId, title);

    public void SetDirty(bool dirty) => manager.SetDirty(TabId, dirty);

    public void SetAlert(TabAlertKind kind, string? text = null) => manager.SetAlert(TabId, kind, text);
}

/// <summary>
/// Internal Tab Manager (Design.md 6), per sirkuit. Router tetap menentukan halaman untuk URL aktif; manager memetakan URL itu ke sebuah tab
/// (membuka atau memfokuskan, maks. 10) lalu <c>TabHost</c> merender SEMUA tab terbuka (yang tidak aktif disembunyikan) sehingga isi form,
/// filter, dan sub-tab tidak hilang saat berpindah tab.
/// </summary>
public sealed class TabManager
{
    public const int MaxTabs = 10;
    public const string HomeId = "home";

    private readonly List<Tab> _tabs = [];
    private readonly NavigationManager _nav;

    public TabManager(NavigationManager nav)
    {
        _nav = nav;
    }

    public event Action? Changed;

    public IReadOnlyList<Tab> Tabs => _tabs;

    public string? ActiveId { get; private set; }

    public Tab? Active => _tabs.FirstOrDefault(t => t.Id == ActiveId);

    /// <summary>Terisi saat pembukaan tab ditolak karena batas 10 tab; dibersihkan oleh <see cref="DismissLimit"/>.</summary>
    public bool LimitReached { get; private set; }

    /// <summary>Menetapkan halaman Beranda (tab terpaku, tidak dapat ditutup). Dipanggil sekali oleh layout.</summary>
    public void EnsureHome(Type homePage)
    {
        if (_tabs.Any(t => t.Id == HomeId))
        {
            return;
        }

        var tab = new Tab(HomeId, homePage, new Dictionary<string, object?>(), "/", pinned: true) { Title = "Beranda" };
        tab.Context = new TabContext(this, tab.Id);
        _tabs.Insert(0, tab);
    }

    /// <summary>Menyelaraskan tab dengan URL aktif hasil Router: fokus ke tab yang ada, atau buka tab baru (ditolak bila sudah 10).</summary>
    public void Sync(Type pageType, IReadOnlyDictionary<string, object?> routeValues, string absoluteUri)
    {
        var relative = _nav.ToBaseRelativePath(absoluteUri);
        var url = "/" + relative;
        var queryIndex = relative.IndexOf('?');
        var path = "/" + (queryIndex >= 0 ? relative[..queryIndex] : relative);
        var query = queryIndex >= 0 ? QueryHelpers.ParseQuery(relative[queryIndex..]) : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        var parameters = BuildParameters(pageType, routeValues, query);
        var id = TabIdentity.For(pageType, routeValues, path);

        var existing = _tabs.FirstOrDefault(t => t.Id == id);
        if (existing is not null)
        {
            var changed = existing.PageType != pageType || existing.Url != url || ActiveId != id;
            existing.PageType = pageType;
            existing.Parameters = parameters;
            existing.Url = url;
            ActiveId = id;
            LimitReached = false;
            if (changed)
            {
                Raise();
            }

            return;
        }

        if (_tabs.Count >= MaxTabs)
        {
            // Tidak menutup tab lama diam-diam: tolak dan kembali ke tab aktif (Design.md 6).
            LimitReached = true;
            var back = Active?.Url ?? "/";
            Raise();
            _nav.NavigateTo(back, replace: true);
            return;
        }

        var tab = new Tab(id, pageType, parameters, url, pinned: false);
        tab.Context = new TabContext(this, id);
        _tabs.Add(tab);
        ActiveId = id;
        LimitReached = false;
        Raise();
    }

    public void Activate(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null)
        {
            return;
        }

        _nav.NavigateTo(tab.Url);
    }

    /// <summary>Menutup tab (tab Beranda tidak dapat ditutup). Pemanggil bertanggung jawab atas konfirmasi bila <see cref="Tab.Dirty"/>.</summary>
    public void Close(string id)
    {
        var index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0 || _tabs[index].Pinned)
        {
            return;
        }

        var wasActive = ActiveId == id;
        _tabs.RemoveAt(index);
        if (wasActive)
        {
            var next = _tabs[Math.Clamp(index - 1, 0, _tabs.Count - 1)];
            ActiveId = next.Id;
            Raise();
            _nav.NavigateTo(next.Url);
            return;
        }

        Raise();
    }

    public void CloseOthers(string id)
    {
        _tabs.RemoveAll(t => t.Id != id && !t.Pinned);
        if (!_tabs.Any(t => t.Id == ActiveId) || ActiveId != id)
        {
            ActiveId = _tabs.Any(t => t.Id == id) ? id : HomeId;
        }

        Raise();
        _nav.NavigateTo(_tabs.First(t => t.Id == ActiveId).Url);
    }

    public void CloseAll()
    {
        _tabs.RemoveAll(t => !t.Pinned);
        ActiveId = HomeId;
        Raise();
        _nav.NavigateTo("/");
    }

    /// <summary>Muat ulang: komponen halaman dibuat ulang (state tab dibuang).</summary>
    public void Reload(string id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is null)
        {
            return;
        }

        tab.Generation++;
        tab.Dirty = false;
        Raise();
    }

    public bool HasDirty => _tabs.Any(t => t.Dirty);

    public void DismissLimit()
    {
        LimitReached = false;
        Raise();
    }

    internal void SetTitle(string id, string title)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is { Pinned: false } && tab.Title != title) // tab Beranda tetap berjudul "Beranda" (Design.md 6)
        {
            tab.Title = title;
            Raise();
        }
    }

    internal void SetDirty(string id, bool dirty)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is not null && tab.Dirty != dirty)
        {
            tab.Dirty = dirty;
            Raise();
        }
    }

    internal void SetAlert(string id, TabAlertKind kind, string? text)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab is not null && (tab.Alert != kind || tab.AlertText != text))
        {
            tab.Alert = kind;
            tab.AlertText = text;
            Raise();
        }
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // sirkuit tertutup
        }
    }

    /// <summary>Nilai route + query (hanya properti ber-<see cref="TabQueryAttribute"/>) sebagai parameter halaman.</summary>
    internal static Dictionary<string, object?> BuildParameters(
        Type pageType, IReadOnlyDictionary<string, object?> routeValues, IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        var result = new Dictionary<string, object?>(routeValues, StringComparer.OrdinalIgnoreCase);
        foreach (var property in pageType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attribute = property.GetCustomAttribute<TabQueryAttribute>();
            if (attribute is null)
            {
                continue;
            }

            if (query.TryGetValue(attribute.Name, out var raw) && Convert(raw.ToString(), property.PropertyType) is { } value)
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    private static object? Convert(string text, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return text;
        }

        if (target == typeof(bool))
        {
            return text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        if (target == typeof(Guid))
        {
            return Guid.TryParse(text, out var g) ? g : null;
        }

        if (target == typeof(int))
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        }

        return null;
    }
}
