using System.Globalization;
using Bunit;
using Gms.Shared.Security;
using Gms.Web.Components.Layout;
using Gms.Web.Components.Pages;
using Gms.Web.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>K-2: chrome dua bahasa (header, navigasi, tab, status turunan, form masuk) lewat resource ID/EN; nama status dokumen tetap Inggris.</summary>
public sealed class ChromeI18nTests : WebTestBase
{
    private static void Use(string culture) => CultureInfo.CurrentUICulture = new CultureInfo(culture);

    [Fact]
    public void Setiap_entri_katalog_navigasi_punya_label_dan_deskripsi_di_kedua_bahasa()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<ChromeResource>>();
        foreach (var culture in new[] { "id-ID", "en-US" })
        {
            Use(culture);
            foreach (var category in NavCatalog.Categories)
            {
                Assert.False(localizer[$"Nav.Cat.{category.Key}"].ResourceNotFound, $"{culture} kategori {category.Key}");
                foreach (var group in category.Groups)
                {
                    Assert.False(localizer[$"Nav.Group.{group.Title}"].ResourceNotFound, $"{culture} grup {group.Title}");
                    foreach (var entry in group.Entries)
                    {
                        Assert.False(localizer[$"Nav.{entry.Key}.Label"].ResourceNotFound, $"{culture} {entry.Key} label");
                        Assert.False(localizer[$"Nav.{entry.Key}.Desc"].ResourceNotFound, $"{culture} {entry.Key} deskripsi");
                    }
                }
            }
        }
    }

    [Fact]
    public void Navigasi_header_dan_tab_tampil_dalam_English_dan_kembali_Indonesia()
    {
        var catalog = NavCatalog.VisibleFor(_ => true);

        Use("en-US");
        var nav = Render<TopNav>(p => p.Add(x => x.Catalog, catalog));
        Assert.Equal("Main navigation", nav.Find("nav").GetAttribute("aria-label"));
        Assert.Contains("Sales", nav.FindAll("nav > button").Select(b => b.TextContent.Trim()));
        Assert.Equal("Home", nav.Find("nav > a").TextContent.Trim());
        nav.FindAll("nav > button")[0].Click();
        Assert.Contains("Create PO +", nav.FindAll("a.gms-mitem b").Select(b => b.TextContent.Trim()));

        var header = Render<AppHeader>(p => p.Add(x => x.Catalog, catalog));
        Assert.Equal("Search pages or actions", header.Find("[role=search] input").GetAttribute("aria-label"));
        header.Find("[role=search] input").Input("create po");
        Assert.Contains("Create PO", header.FindAll(".gms-sresults a b").Select(b => b.TextContent.Trim()));
        Assert.Contains("Action", header.Markup);

        var tabs = Services.GetRequiredService<Gms.Web.Services.Tabs.TabManager>();
        tabs.EnsureHome(typeof(Home));
        Assert.Contains("All tabs (1/10)", Render<TabBar>().Markup);

        Use("id-ID");
        Assert.Equal("Navigasi utama", Render<TopNav>(p => p.Add(x => x.Catalog, catalog)).Find("nav").GetAttribute("aria-label"));
        Assert.Contains("Semua tab (1/10)", Render<TabBar>().Markup);
    }

    [Fact]
    public void Status_turunan_diterjemahkan_tetapi_nama_status_dokumen_tetap_Inggris()
    {
        Use("en-US");
        Assert.Equal("Late", Render<Gms.Web.Components.Shared.StatusBadge>(p => p.Add(x => x.Value, "late")).Find(".gms-tag span").TextContent.Trim());
        Assert.Equal("Ready for production", Render<Gms.Web.Components.Shared.StatusBadge>(p => p.Add(x => x.Value, "ready")).Find(".gms-tag span").TextContent.Trim());
        Assert.Equal("In Progress", Render<Gms.Web.Components.Shared.StatusBadge>(p => p.Add(x => x.Value, Gms.Shared.Enums.PoStatus.InProgress)).Find(".gms-tag span").TextContent.Trim());

        Use("id-ID");
        Assert.Equal("Terlambat", Render<Gms.Web.Components.Shared.StatusBadge>(p => p.Add(x => x.Value, "late")).Find(".gms-tag span").TextContent.Trim());
        Assert.Equal("In Progress", Render<Gms.Web.Components.Shared.StatusBadge>(p => p.Add(x => x.Value, Gms.Shared.Enums.PoStatus.InProgress)).Find(".gms-tag span").TextContent.Trim());
    }

    [Fact]
    public void Form_masuk_dan_footer_mengikuti_bahasa_aktif()
    {
        Use("en-US");
        var login = Render<LoginPage>();
        Assert.Equal("Sign in", login.Find("h1").TextContent.Trim());
        Assert.Equal("Username", login.Find("label[for='username']").TextContent.Replace("required", "").Trim());
        Assert.Equal("Password", login.Find("label[for='password']").TextContent.Replace("required", "").Trim());
        Assert.Contains("Forgot your password?", login.Markup);
        Assert.Equal("Show", login.Find(".gms-pwtoggle").TextContent.Trim());
        _ = Permissions.CustomerPO.View;
    }
}
