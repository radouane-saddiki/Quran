using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Quran.Core;
using Quran.Web.Audio;

namespace Quran.Web.Pages;

public sealed class IndexModel(QuranCorpus corpus, IOptions<AudioOptions> audio) : PageModel
{
    public QuranCorpus Corpus => corpus;
    public IReadOnlyList<Reciter> Reciters => audio.Value.Reciters;

    /// <summary>sourate | juz | hizb | page</summary>
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "sourate";
    [BindProperty(SupportsGet = true)] public int N { get; set; } = 1;
    /// <summary>Verset à mettre en évidence, ex. « 2:255 ».</summary>
    [BindProperty(SupportsGet = true)] public string? Hl { get; set; }
    /// <summary>mushaf (texte continu) | liste (un verset par ligne)</summary>
    [BindProperty(SupportsGet = true)] public string Vue { get; set; } = "mushaf";
    /// <summary>Saisie « sourate:verset » pour aller directement à un verset.</summary>
    [BindProperty(SupportsGet = true)] public string? Aller { get; set; }

    public string Heading { get; private set; } = "";
    public string SubHeading { get; private set; } = "";
    public IReadOnlyList<(Surah Surah, IReadOnlyList<Verse> Verses)> Segments { get; private set; } = [];
    public int Max { get; private set; }
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
        switch (Mode)
        {
            case "sourate":
                var s = corpus.GetSurah(N)!;
                Heading = $"Sourate {s.Number} — {s.NameEn}";
                SubHeading = $"{s.VerseCount} versets · pages {s.Verses[0].Page}–{s.Verses[^1].PageEnd}";
                break;
            default:
                var label = Mode switch { "juz" => "Juz", "hizb" => "Hizb", _ => "Page" };
                Heading = $"{label} {N}";
                SubHeading = $"{list.Count} versets · de {first.Reference} à {last.Reference}";
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
                Error = $"Le verset {input} n'existe pas (la sourate {s} compte {corpus.GetSurah(s)!.VerseCount} versets en Warsh).";
        }
        else
        {
            Error = $"Référence « {input} » invalide. Exemple : 2:253.";
        }
    }

    public string LinkTo(string mode, int n, string? hl = null) =>
        $"/?mode={mode}&n={n}" + (Vue == "liste" ? "&vue=liste" : "") + (hl is null ? "" : $"&hl={hl}#v{hl.Replace(':', '-')}");

    public string ToggleViewUrl() =>
        $"/?mode={Mode}&n={N}" + (Vue == "liste" ? "" : "&vue=liste") + (Hl is null ? "" : $"&hl={Hl}");

    public static string Anchor(Verse v) => $"v{v.Surah}-{v.Number}";
}
