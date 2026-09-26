using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Quran.Core;
using Quran.Web.Audio;
using Quran.Web.Localization;

namespace Quran.Web.Pages;

public sealed class IndexModel(QuranCorpus corpus, MushafLayout mushaf, IOptions<AudioOptions> audio, Loc L) : PageModel
{
    public QuranCorpus Corpus => corpus;
    public IReadOnlyList<Reciter> Reciters => audio.Value.Reciters;

    /// <summary>sourate | juz | hizb | page</summary>
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "sourate";
    [BindProperty(SupportsGet = true)] public int N { get; set; } = 1;
    /// <summary>Verset à mettre en évidence, ex. « 2:255 ».</summary>
    [BindProperty(SupportsGet = true)] public string? Hl { get; set; }
    /// <summary>mushaf (pages du mushaf, ligne par ligne) | liste (un verset par ligne)</summary>
    [BindProperty(SupportsGet = true)] public string Vue { get; set; } = "mushaf";
    /// <summary>Saisie « sourate:verset » pour aller directement à un verset.</summary>
    [BindProperty(SupportsGet = true)] public string? Aller { get; set; }

    public string Heading { get; private set; } = "";
    public string SubHeading { get; private set; } = "";
    public IReadOnlyList<(Surah Surah, IReadOnlyList<Verse> Verses)> Segments { get; private set; } = [];
    public int Max { get; private set; }
    /// <summary>Pages du mushaf à afficher (vue mushaf) : celles qui contiennent la sélection.</summary>
    public IReadOnlyList<MushafPage> Pages { get; private set; } = [];
    /// <summary>Versets sélectionnés ; les autres versets des pages affichées sont estompés.</summary>
    public HashSet<int> Selected { get; private set; } = [];
    public MushafLayout Mushaf => mushaf;
    public string? Error { get; private set; }

    public void OnGet()
    {
        if (!string.IsNullOrWhiteSpace(Aller)) JumpTo(Aller);

        Mode = Mode?.ToLowerInvariant() switch
        {
            "juz" => "juz",
            "hizb" => "hizb",
            "page" => "page",
            _ => "sourate",
        };
        Max = Mode switch { "juz" => 30, "hizb" => 60, "page" => 604, _ => 114 };
        N = Math.Clamp(N, 1, Max);
        Vue = Vue == "liste" ? "liste" : "mushaf";

        IEnumerable<Verse> verses = Mode switch
        {
            "juz" => corpus.VersesOfJuz(N),
            "hizb" => corpus.VersesOfHizb(N),
            "page" => corpus.VersesOfPage(N),
            _ => corpus.GetSurah(N)!.Verses,
        };
        var list = verses.ToList();

        Segments = list
            .GroupBy(v => v.Surah)
            .Select(g => (corpus.GetSurah(g.Key)!, (IReadOnlyList<Verse>)g.ToList()))
            .ToList();

        var first = list[0];
        var last = list[^1];
        Selected = list.Select(v => v.Id).ToHashSet();
        if (Vue == "mushaf")
            Pages = Enumerable.Range(first.Page, last.PageEnd - first.Page + 1).Select(mushaf.GetPage).ToList();
        switch (Mode)
        {
            case "sourate":
                var s = corpus.GetSurah(N)!;
                Heading = L.F("read.heading.surah", L.SurahName(s), s.Number);
                SubHeading = L.F("read.sub.surah", s.VerseCount, s.Verses[0].Page, s.Verses[^1].PageEnd);
                break;
            default:
                var label = L[Mode switch { "juz" => "juz", "hizb" => "hizb", _ => "page" }];
                Heading = $"{label} {N}";
                SubHeading = L.F("read.sub.range", list.Count, first.Reference, last.Reference);
                break;
        }
    }

    private void JumpTo(string input)
    {
        var parts = input.Replace(' ', ':').Replace('.', ':').Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && int.TryParse(parts[0], out var s) && corpus.GetSurah(s) is not null)
        {
            Mode = "sourate";
            N = s;
            if (parts.Length >= 2 && int.TryParse(parts[1], out var v) && corpus.GetVerse(s, v) is not null)
                Hl = $"{s}:{v}";
            else if (parts.Length >= 2)
                Error = L.F("read.err.verse", input, s, corpus.GetSurah(s)!.VerseCount);
        }
        else
        {
            Error = L.F("read.err.ref", input);
        }
    }

    public string LinkTo(string mode, int n, string? hl = null) =>
        $"/?mode={mode}&n={n}" + (Vue == "liste" ? "&vue=liste" : "") + (hl is null ? "" : $"&hl={hl}#v{hl.Replace(':', '-')}");

    public string ToggleViewUrl() =>
        $"/?mode={Mode}&n={N}" + (Vue == "liste" ? "" : "&vue=liste") + (Hl is null ? "" : $"&hl={Hl}");

    public static string Anchor(Verse v) => $"v{v.Surah}-{v.Number}";
}
