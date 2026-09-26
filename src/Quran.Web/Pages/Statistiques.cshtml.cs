using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quran.Core;
using Quran.Web.Localization;
using Quran.Web.Models;

namespace Quran.Web.Pages;

public sealed class StatistiquesModel(QuranStatistics stats, Loc L) : PageModel
{
    public QuranCorpus Corpus => stats.Corpus;
    public QuranStatistics Stats => stats;

    /// <summary>Portée : null = tout le Coran, sinon numéro de sourate.</summary>
    [BindProperty(SupportsGet = true)] public int? Surah { get; set; }
    [BindProperty(SupportsGet = true)] public TextForm Form { get; set; } = TextForm.Normalized;
    [BindProperty(SupportsGet = true)] public int Top { get; set; } = 50;
    [BindProperty(SupportsGet = true)] public int MinLetters { get; set; } = 1;

    public Surah? Scope { get; private set; }
    public Summary Summary { get; private set; } = default!;
    public SurahStats? ScopeStats { get; private set; }
    public int DistinctWords { get; private set; }
    public Extremes Extremes { get; private set; } = default!;
    public IReadOnlyList<SurahStats> SurahTable { get; private set; } = [];
    public FrequencyResult Words { get; private set; } = default!;
    public List<BarChart> Charts { get; } = [];
    public BarChart Letters { get; private set; } = default!;

    public void OnGet()
    {
        Scope = Surah is null ? null : Corpus.GetSurah(Surah.Value);
        if (Scope is null) Surah = null;
        Top = Math.Clamp(Top, 10, 1000);
        MinLetters = Math.Clamp(MinLetters, 1, 10);

        Summary = stats.GetSummary();
        Extremes = stats.GetExtremes();
        SurahTable = stats.GetSurahStats();
        Words = stats.GetWordFrequencies(Form, Top, MinLetters, Surah);

        var letters = stats.GetLetterFrequencies(TextForm.NoDiacritics, Surah);
        Letters = new BarChart
        {
            Title = L["stats.chart.letters"],
            Subtitle = L.F("stats.chart.letters.sub", letters.Total),
            Horizontal = true,
            ArabicLabels = true,
            Items = letters.Items.Select(i => new BarItem(i.Value, i.Count,
                $"{i.Value} : {L.N(i.Count)} ({L.N(i.Percent, "0.00")} %)")).ToList(),
        };

        if (Scope is null)
        {
            Charts.Add(new BarChart
            {
                Title = L["stats.chart.versesBySurah"],
                Subtitle = L["stats.chart.versesBySurah.sub"],
                LabelEvery = 10,
                Items = SurahTable.Select(s => new BarItem(s.Number.ToString(), s.Verses,
                    L.F("stats.chart.versesBySurah.tip", s.Number, L.SurahName(s.Number, s.NameAr, s.NameEn), s.Verses, s.Words),
                    $"/Statistiques?surah={s.Number}")).ToList(),
            });
            Charts.Add(new BarChart
            {
                Title = L["stats.chart.wordsByJuz"],
                LabelEvery = 1,
                Items = stats.GetJuzStats().Select(g => new BarItem(g.Number.ToString(), g.Words,
                    L.F("stats.chart.wordsByJuz.tip", g.Number, g.From, g.To, g.Words, g.Verses),
                    $"/?mode=juz&n={g.Number}")).ToList(),
            });
            Charts.Add(new BarChart
            {
                Title = L["stats.chart.wordsByHizb"],
                LabelEvery = 5,
                Items = stats.GetHizbStats().Select(g => new BarItem(g.Number.ToString(), g.Words,
                    L.F("stats.chart.wordsByHizb.tip", g.Number, g.From, g.To, g.Words, g.Verses),
                    $"/?mode=hizb&n={g.Number}")).ToList(),
            });
        }
        else
        {
            ScopeStats = stats.ToStats(Scope);
            DistinctWords = Scope.Verses.SelectMany(v => v.Words).Select(w => w.Get(Form)).Distinct().Count();
            var n = Scope.VerseCount;
            Charts.Add(new BarChart
            {
                Title = L["stats.chart.wordsByVerse"],
                LabelEvery = n <= 30 ? 1 : n <= 120 ? 10 : 20,
                Items = Scope.Verses.Select(v => new BarItem(v.Number.ToString(), v.WordCount,
                    L.F("stats.chart.wordsByVerse.tip", v.Reference, v.WordCount, v.LetterCount),
                    $"/?mode=sourate&n={v.Surah}&hl={v.Reference}#v{v.Surah}-{v.Number}")).ToList(),
            });
        }
    }

    public string LinkTo(int? surah = -1, TextForm? form = null, int? top = null, int? minLetters = null)
    {
        var s = surah == -1 ? Surah : surah;
        var url = $"/Statistiques?form={form ?? Form}&top={top ?? Top}&minLetters={minLetters ?? MinLetters}";
        if (s is not null) url += $"&surah={s}";
        return url;
    }

    public static string Fmt(double v, string f = "N0") =>
        v.ToString(f, BarChart.Fr).Replace(' ', ' ').Replace(' ', ' ');
}
