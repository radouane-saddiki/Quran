using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quran.Core;

/// <summary>Le texte complet chargé en mémoire (riwayat Warsh ʿan Nāfiʿ, KFGQPC v2.1).</summary>
public sealed class QuranCorpus
{
    public const string Riwaya = "Warsh ʿan Nāfiʿ";
    public const string Source = "KFGQPC Warsh Uthmanic v2.1 (via quran-center/quran-meta)";
    /// <summary>La basmala, telle qu'écrite en Warsh (reprise de 27:30), affichée en tête des sourates sauf at-Tawba.</summary>
    public const string Basmala = "بِسْمِ اِ۬للَّهِ اِ۬لرَّحْمَٰنِ اِ۬لرَّحِيمِ";

    public IReadOnlyList<Surah> Surahs { get; }
    public IReadOnlyList<Verse> Verses { get; }

    private QuranCorpus(IReadOnlyList<Surah> surahs)
    {
        Surahs = surahs;
        Verses = surahs.SelectMany(s => s.Verses).ToArray();
        AssignHizb();
    }

    public IEnumerable<Verse> VersesOfJuz(int juz) => Verses.Where(v => v.Juz == juz);
    public IEnumerable<Verse> VersesOfHizb(int hizb) => Verses.Where(v => v.Hizb == hizb);
    public IEnumerable<Verse> VersesOfPage(int page) => Verses.Where(v => v.Page <= page && v.PageEnd >= page);

    /// <summary>Début de chaque hizb (id de verset), lu depuis la ressource warsh_hizb.json.</summary>
    private void AssignHizb()
    {
        using var stream = typeof(QuranCorpus).Assembly.GetManifestResourceStream("Quran.Core.warsh_hizb.json");
        if (stream is null) return;
        var starts = JsonSerializer.Deserialize<List<HizbStart>>(stream)!.OrderBy(h => h.Id).ToArray();
        var k = 0;
        foreach (var v in Verses)
        {
            while (k + 1 < starts.Length && v.Id >= starts[k + 1].Id) k++;
            v.Hizb = starts[k].Hizb;
        }
    }

    private sealed class HizbStart
    {
        [JsonPropertyName("hizb")] public int Hizb { get; set; }
        [JsonPropertyName("id")] public int Id { get; set; }
    }

    public Surah? GetSurah(int number) =>
        number is >= 1 and <= 114 ? Surahs[number - 1] : null;

    public Verse? GetVerse(int surah, int verse)
    {
        var s = GetSurah(surah);
        return s is not null && verse >= 1 && verse <= s.VerseCount ? s.Verses[verse - 1] : null;
    }

    /// <summary>Charge le texte embarqué dans l'assembly Quran.Core.</summary>
    public static QuranCorpus LoadEmbedded()
    {
        using var stream = typeof(QuranCorpus).Assembly.GetManifestResourceStream("Quran.Core.warsh.json")
            ?? throw new InvalidOperationException("Ressource embarquée Quran.Core.warsh.json introuvable.");
        return Load(stream);
    }

    /// <summary>Charge un fichier au format KFGQPC (tableau JSON de versets).</summary>
    public static QuranCorpus LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static QuranCorpus Load(Stream stream)
    {
        var rows = JsonSerializer.Deserialize<List<KfgqpcRow>>(stream)
                   ?? throw new InvalidDataException("Fichier de données vide.");

        var surahs = rows
            .OrderBy(r => r.Id)
            .GroupBy(r => r.SuraNo)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var first = g.First();
                var verses = g.OrderBy(r => r.AyaNo).Select(r => new Verse
                {
                    Id = r.Id,
                    Surah = r.SuraNo,
                    Number = r.AyaNo,
                    Juz = r.Jozz,
                    Page = ParsePages(r.Page).Start,
                    PageEnd = ParsePages(r.Page).End,
                    Text = ArabicText.StripVerseNumber(r.AyaText),
                    Words = ArabicText.Tokenize(r.AyaText)
                        .Select(w => new Word(w, ArabicText.RemoveDiacritics(w), ArabicText.Normalize(w)))
                        .ToArray(),
                }).ToArray();

                return new Surah
                {
                    Number = g.Key,
                    NameAr = first.SuraNameAr.Trim(),
                    NameEn = first.SuraNameEn.Trim(),
                    Verses = verses,
                };
            })
            .ToArray();

        if (surahs.Length != 114)
            throw new InvalidDataException($"114 sourates attendues, {surahs.Length} trouvées.");

        return new QuranCorpus(surahs);
    }

    // « 85 » ou « 85-86 » pour un verset à cheval sur deux pages.
    private static (int Start, int End) ParsePages(string page)
    {
        var parts = page.Split('-', StringSplitOptions.TrimEntries);
        var start = int.Parse(parts[0]);
        return (start, parts.Length > 1 ? int.Parse(parts[1]) : start);
    }

    private sealed class KfgqpcRow
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("jozz")] public int Jozz { get; set; }
        [JsonPropertyName("page")] public string Page { get; set; } = "0";
        [JsonPropertyName("sura_no")] public int SuraNo { get; set; }
        [JsonPropertyName("sura_name_en")] public string SuraNameEn { get; set; } = "";
        [JsonPropertyName("sura_name_ar")] public string SuraNameAr { get; set; } = "";
        [JsonPropertyName("aya_no")] public int AyaNo { get; set; }
        [JsonPropertyName("aya_text")] public string AyaText { get; set; } = "";
    }
}
