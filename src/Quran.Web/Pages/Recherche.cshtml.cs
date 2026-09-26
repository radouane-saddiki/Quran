using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quran.Core;
using Quran.Web.Localization;
using Quran.Web.Models;

namespace Quran.Web.Pages;

public sealed class RechercheModel(QuranStatistics stats, Loc L) : PageModel
{
    public const int PageSize = 50;

    public QuranCorpus Corpus => stats.Corpus;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public SearchMatch Match { get; set; } = SearchMatch.Exact;
    [BindProperty(SupportsGet = true)] public TextForm Form { get; set; } = TextForm.Normalized;
    [BindProperty(SupportsGet = true)] public int? Surah { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public SearchResult? Result { get; private set; }
    public string? Error { get; private set; }
    public int TermCount { get; private set; }
    public int PageCount => Result is null ? 0 : Math.Max(1, (int)Math.Ceiling(Result.TotalOccurrences / (double)PageSize));

    /// <summary>Occurrences de la page, regroupées par verset (un verset peut contenir plusieurs occurrences).</summary>
    public IReadOnlyList<(Verse Verse, HashSet<int> Highlighted)> Hits { get; private set; } = [];

    public BarChart? Distribution { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Q)) return;
        if (Surah is not null && Corpus.GetSurah(Surah.Value) is null) Surah = null;
        PageNumber = Math.Max(1, PageNumber);

        try
        {
            Result = stats.Search(Q, Match, Form, Surah, limit: PageSize, skip: (PageNumber - 1) * PageSize);
        }
        catch (ArgumentException)
        {
            Error = L["search.noLetters"];
            return;
        }

        TermCount = Result.QueryInForm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        Hits = Result.Occurrences
            .GroupBy(o => (o.Surah, o.Verse))
            .Select(g =>
            {
                var set = new HashSet<int>();
                foreach (var o in g)
                    for (var k = 0; k < TermCount; k++) set.Add(o.WordIndex + k);
                return (Corpus.GetVerse(g.Key.Surah, g.Key.Verse)!, set);
            })
            .ToList();

        if (Surah is null && Result.TotalOccurrences > 0)
        {
            var counts = Result.BySurah.ToDictionary(b => b.Number, b => b.Value);
            Distribution = new BarChart
            {
                Title = L["search.chart.title"],
                Subtitle = L.F("search.chart.sub", Result.SurahsMatched),
                LabelEvery = 10,
                Items = Corpus.Surahs.Select(s => new BarItem(
                    s.Number.ToString(),
                    counts.GetValueOrDefault(s.Number),
                    L.F("search.chart.tip", s.Number, L.SurahName(s), counts.GetValueOrDefault(s.Number)),
                    counts.ContainsKey(s.Number) ? QueryUrl(surah: s.Number, page: 1) : null)).ToList(),
            };
        }
    }

    public string QueryUrl(int? surah = -1, int? page = null)
    {
        var s = surah == -1 ? Surah : surah;
        var url = $"/Recherche?q={Uri.EscapeDataString(Q ?? "")}&match={Match}&form={Form}";
        if (s is not null) url += $"&surah={s}";
        if ((page ?? PageNumber) > 1) url += $"&p={page ?? PageNumber}";
        return url;
    }

    public static readonly SearchMatch[] Matches = [SearchMatch.Exact, SearchMatch.StartsWith, SearchMatch.EndsWith, SearchMatch.Contains];

    public static readonly TextForm[] Forms = [TextForm.Normalized, TextForm.NoDiacritics, TextForm.Original];
}
