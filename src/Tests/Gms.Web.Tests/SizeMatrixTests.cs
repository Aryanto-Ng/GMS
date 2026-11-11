using Bunit;
using Gms.Web.Components.Shared;

namespace Gms.Web.Tests;

public sealed class SizeMatrixLogicTests
{
    private static readonly IReadOnlyList<MatrixRow> Rows = [new("navy", "Navy"), new("red", "Merah")];
    private static readonly IReadOnlyList<MatrixColumn> Cols = [new("s", "S"), new("m", "M"), new("l", "L")];

    private static MatrixCellInfo Open(string r, string c) => new();

    [Fact]
    public void Total_baris_dan_kolom_hanya_menghitung_sel_berlaku_dan_sel_kosong_dianggap_nol()
    {
        var v = MatrixValues.From([("navy", "s", 10), ("navy", "m", 5), ("red", "m", 7), ("red", "l", 100)]);
        MatrixCellInfo Info(string r, string c) => new(Enabled: !(r == "red" && c == "l"));

        Assert.Equal(15, SizeMatrixLogic.RowTotal(v, "navy", Cols, Info));
        Assert.Equal(7, SizeMatrixLogic.RowTotal(v, "red", Cols, Info)); // sel tidak berlaku (100) diabaikan
        Assert.Equal(12, SizeMatrixLogic.ColumnTotal(v, "m", Rows, Info));
        Assert.Equal(0, SizeMatrixLogic.ColumnTotal(v, "l", Rows, Info));
    }

    [Fact]
    public void Set_null_menghapus_nilai_dan_nol_tetap_tersimpan_sebagai_nol()
    {
        var v = new MatrixValues();
        v.Set("navy", "s", 0);
        Assert.Equal(0, v.Get("navy", "s"));
        v.Set("navy", "s", null);
        Assert.Null(v.Get("navy", "s"));
        Assert.Empty(v.All);
    }

    [Fact]
    public void Evaluate_batas_keras_adalah_galat_batas_lunak_peringatan_dan_tepat_di_batas_aman()
    {
        var v = MatrixValues.From([("navy", "s", 11), ("navy", "m", 10), ("navy", "l", 51), ("red", "s", 30)]);
        MatrixCellInfo Info(string r, string c) => (r, c) switch
        {
            ("navy", "s") => new(Max: 10),      // 11 > 10 → galat
            ("navy", "m") => new(Max: 10),      // tepat 10 → aman
            ("navy", "l") => new(SoftMax: 50),  // 51 > 50 → peringatan
            ("red", "s") => new(Enabled: false, Max: 1), // tidak berlaku → diabaikan
            _ => new(),
        };

        var issues = SizeMatrixLogic.Evaluate(Rows, Cols, v, Info);

        Assert.Equal(2, issues.Count);
        var error = Assert.Single(issues, i => i.IsError);
        Assert.Equal(("navy", "s"), (error.RowKey, error.ColumnKey));
        Assert.Equal("Navy, Size S: melebihi batas 10.", error.Message);
        var warning = Assert.Single(issues, i => !i.IsError);
        Assert.Equal(("navy", "l"), (warning.RowKey, warning.ColumnKey));
        Assert.Contains("melebihi Qty WO (50)", warning.Message);
    }

    [Fact]
    public void Evaluate_batas_keras_didahulukan_atas_batas_lunak_pada_sel_yang_sama()
    {
        var v = MatrixValues.From([("navy", "s", 99)]);

        var issues = SizeMatrixLogic.Evaluate(Rows, Cols, v, (_, _) => new MatrixCellInfo(Max: 10, SoftMax: 5));

        var only = Assert.Single(issues);
        Assert.True(only.IsError);
    }

    [Fact]
    public void Paste_mengisi_blok_mulai_dari_sel_awal_dan_mengabaikan_newline_akhir_Excel()
    {
        var v = new MatrixValues();

        var (applied, skipped) = SizeMatrixLogic.Paste(Rows, Cols, v, Open, 0, 1, "1\t2\r\n3\t4\r\n");

        Assert.Equal((4, 0), (applied, skipped));
        Assert.Equal(1, v.Get("navy", "m"));
        Assert.Equal(2, v.Get("navy", "l"));
        Assert.Equal(3, v.Get("red", "m"));
        Assert.Equal(4, v.Get("red", "l"));
        Assert.Null(v.Get("navy", "s")); // di kiri sel awal tidak tersentuh
    }

    [Fact]
    public void Paste_melewati_teks_non_angka_negatif_pecahan_dan_sel_di_luar_grid_atau_tidak_berlaku()
    {
        var v = new MatrixValues();
        MatrixCellInfo Info(string r, string c) => new(Enabled: !(r == "navy" && c == "m"));

        // baris 1: s ok, m tidak berlaku, l "abc" ; baris 2: -5 (negatif), 1,5 (ambigu), 8 ok ; baris 3: di luar grid
        var (applied, skipped) = SizeMatrixLogic.Paste(Rows, Cols, v, Info, 0, 0, "1\t2\tabc\n-5\t1,5\t8\n9\t9\t9");

        Assert.Equal(2, applied); // navy/s=1 dan red/l=8
        Assert.Equal(7, skipped); // tdk berlaku, abc, -5, 1,5, dan 3 sel baris di luar grid
        Assert.Equal(1, v.Get("navy", "s"));
        Assert.Equal(8, v.Get("red", "l"));
        Assert.Null(v.Get("red", "s"));
    }

    [Fact]
    public void Paste_membiarkan_sel_kosong_dan_tidak_menimpa_nilai_lama_dengan_kosong()
    {
        var v = MatrixValues.From([("navy", "m", 5)]);

        var (applied, skipped) = SizeMatrixLogic.Paste(Rows, Cols, v, Open, 0, 0, "7\t\t9");

        Assert.Equal((2, 0), (applied, skipped));
        Assert.Equal(5, v.Get("navy", "m"));
        Assert.Equal(9, v.Get("navy", "l"));
    }

    [Fact]
    public void Paste_menolak_nilai_di_atas_batas_atas_teknis_dan_menerima_ribuan_Indonesia()
    {
        var v = new MatrixValues();

        var (applied, skipped) = SizeMatrixLogic.Paste(Rows, Cols, v, Open, 0, 0, "10000000\t1.250", maxValue: 9_999_999);

        Assert.Equal((1, 1), (applied, skipped));
        Assert.Equal(1250, v.Get("navy", "m"));
    }

    [Fact]
    public void Paste_teks_kosong_tidak_melakukan_apa_apa()
    {
        var v = new MatrixValues();

        Assert.Equal((0, 0), SizeMatrixLogic.Paste(Rows, Cols, v, Open, 0, 0, ""));
        Assert.Equal((0, 0), SizeMatrixLogic.Paste(Rows, Cols, v, Open, 0, 0, null!));
    }
}

public sealed class QtyGridTests : WebTestBase
{
    private static readonly IReadOnlyList<MatrixRow> Rows = [new("navy", "Navy", "Kain A"), new("red", "Merah")];
    private static readonly IReadOnlyList<MatrixColumn> Cols = [new("s", "S"), new("m", "M")];

    private IRenderedComponent<QtyGrid> Grid(
        MatrixValues? values = null, Func<string, string, MatrixCellInfo>? info = null, bool readOnly = false, bool showMax = false, Action? changed = null) =>
        Render<QtyGrid>(p =>
        {
            p.Add(x => x.Caption, "Matriks PO").Add(x => x.Rows, Rows).Add(x => x.Columns, Cols).Add(x => x.Values, values ?? new MatrixValues())
             .Add(x => x.ReadOnly, readOnly).Add(x => x.ShowMax, showMax);
            if (info is not null)
            {
                p.Add(x => x.CellInfo, info);
            }

            if (changed is not null)
            {
                p.Add(x => x.OnChanged, changed);
            }
        });

    [Fact]
    public void Struktur_tabel_aksesibel_dengan_caption_scope_dan_label_per_sel()
    {
        var cut = Grid();

        Assert.Equal("Matriks PO", cut.Find("caption").TextContent);
        Assert.All(cut.FindAll("thead th"), th => Assert.Equal("col", th.GetAttribute("scope")));
        Assert.Equal(["row", "row", "row"], cut.FindAll("th[scope='row']").Select(t => t.GetAttribute("scope")!).ToArray());
        Assert.Contains("Kain A", cut.Markup);
        var labels = cut.FindAll("input").Select(i => i.GetAttribute("aria-label")).ToArray();
        Assert.Equal(["Navy, Size S", "Navy, Size M", "Merah, Size S", "Merah, Size M"], labels);
        Assert.All(cut.FindAll("input"), i => Assert.Equal("numeric", i.GetAttribute("inputmode")));
    }

    [Fact]
    public void Total_baris_kolom_dan_keseluruhan_dihitung_dan_diperbarui_saat_mengetik()
    {
        var values = MatrixValues.From([("navy", "s", 10), ("red", "m", 5)]);
        var changes = 0;
        var cut = Grid(values, changed: () => changes++);

        Assert.Equal(["10", "5"], cut.FindAll("tbody td.gms-matrix-total strong").Select(e => e.TextContent).ToArray());
        Assert.Equal(["10", "5"], cut.FindAll("tfoot td.num:not(.gms-matrix-total) strong").Select(e => e.TextContent).ToArray());
        Assert.Equal("15", cut.Find("tfoot td.gms-matrix-total strong").TextContent);

        cut.Find("input[aria-label='Navy, Size M']").Input("2.5ab0"); // hanya digit dipertahankan → 250

        Assert.Equal(250, values.Get("navy", "m"));
        Assert.Equal("265", cut.Find("tfoot td.gms-matrix-total strong").TextContent);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Mengosongkan_sel_menghapus_nilai_dan_input_dibatasi_tujuh_digit()
    {
        var values = MatrixValues.From([("navy", "s", 10)]);
        var cut = Grid(values);

        cut.Find("input[aria-label='Navy, Size S']").Input("");
        Assert.Null(values.Get("navy", "s"));

        cut.Find("input[aria-label='Navy, Size S']").Input("123456789");
        Assert.Equal(1234567, values.Get("navy", "s"));
    }

    [Fact]
    public void Sel_tidak_berlaku_tampil_sebagai_strip_berlabel_tanpa_input()
    {
        var cut = Grid(info: (r, c) => new MatrixCellInfo(Enabled: !(r == "red" && c == "s")));

        Assert.Equal(3, cut.FindAll("input").Count);
        var na = cut.Find(".gms-na");
        Assert.Equal("—", na.TextContent);
        Assert.Equal("Merah, Size S: tidak berlaku", na.GetAttribute("aria-label"));
        Assert.Contains("disabled", na.ParentElement!.ClassName);
    }

    [Fact]
    public void Batas_keras_menandai_sel_dengan_aria_invalid_pesan_terhubung_dan_ikon_bukan_hanya_warna()
    {
        var values = MatrixValues.From([("navy", "s", 20)]);
        var cut = Grid(values, info: (_, _) => new MatrixCellInfo(Max: 10));

        var input = cut.Find("input[aria-label='Navy, Size S']");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        var message = cut.Find(".gms-cellmsg.error");
        Assert.Equal(message.Id, input.GetAttribute("aria-describedby"));
        Assert.Contains("melebihi batas", message.TextContent);
        Assert.NotEmpty(message.QuerySelectorAll("svg"));
        Assert.True(cut.Instance.HasErrors);
        Assert.Single(cut.FindAll("td.gms-cell.error"));
    }

    [Fact]
    public void Batas_lunak_hanya_peringatan_tidak_dihitung_sebagai_galat()
    {
        var values = MatrixValues.From([("navy", "s", 20)]);
        var cut = Grid(values, info: (_, _) => new MatrixCellInfo(SoftMax: 10));

        Assert.Contains("melebihi Qty WO", cut.Find(".gms-cellmsg.warning").TextContent);
        Assert.False(cut.Instance.HasErrors);
        Assert.Single(cut.Instance.Issues);
        Assert.Null(cut.Find("input[aria-label='Navy, Size S']").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Mengetik_melewati_batas_memunculkan_galat_dan_memperbaikinya_menghapusnya()
    {
        var cut = Grid(info: (_, _) => new MatrixCellInfo(Max: 10));

        cut.Find("input[aria-label='Navy, Size S']").Input("11");
        Assert.True(cut.Instance.HasErrors);

        cut.Find("input[aria-label='Navy, Size S']").Input("10");
        Assert.False(cut.Instance.HasErrors);
        Assert.Empty(cut.FindAll(".gms-cellmsg"));
    }

    [Fact]
    public void ShowMax_menampilkan_batas_per_sel_atau_tanpa_batas()
    {
        var cut = Grid(showMax: true, info: (r, _) => r == "navy" ? new MatrixCellInfo(Max: 1500) : new MatrixCellInfo());

        Assert.Equal(2, cut.FindAll(".gms-cellmax").Count(e => e.TextContent == "maks 1.500"));
        Assert.Equal(2, cut.FindAll(".gms-cellmax").Count(e => e.TextContent == "tanpa batas"));
    }

    [Fact]
    public void ReadOnly_menampilkan_angka_biasa_tanpa_input_atau_modul_JS_dan_negatif_bertanda()
    {
        var values = MatrixValues.From([("navy", "s", 1500), ("red", "m", -20)]);
        var cut = Grid(values, readOnly: true);

        Assert.Empty(cut.FindAll("input"));
        Assert.Contains("1.500", cut.Markup);
        Assert.Contains("−20", cut.Find(".gms-negative").TextContent);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "init");
    }

    [Fact]
    public void Modul_JS_diinisialisasi_untuk_mode_edit()
    {
        var cut = Grid();

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "import"));
    }

    [Fact]
    public async Task Tempel_dari_JS_mengisi_sel_mengumumkan_hasil_dan_memanggil_OnChanged()
    {
        var values = new MatrixValues();
        var changes = 0;
        var cut = Grid(values, info: (r, c) => new MatrixCellInfo(Enabled: !(r == "red" && c == "m")), changed: () => changes++);

        await cut.InvokeAsync(() => cut.Instance.PasteAsync(0, 0, "1\t2\n3\t4"));

        Assert.Equal(1, values.Get("navy", "s"));
        Assert.Equal(2, values.Get("navy", "m"));
        Assert.Equal(3, values.Get("red", "s"));
        Assert.Null(values.Get("red", "m"));
        Assert.Contains("3 sel ditempel, 1 dilewati", cut.Find("[role='status']").TextContent);
        Assert.Equal("1", cut.Find("input[aria-label='Navy, Size S']").GetAttribute("value"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Tempel_tanpa_sel_dilewati_mengumumkan_jumlah_saja()
    {
        var cut = Grid();

        await cut.InvokeAsync(() => cut.Instance.PasteAsync(0, 0, "5"));

        Assert.Equal("1 sel ditempel.", cut.Find("[role='status']").TextContent);
    }
}
