namespace Quran.Core;

public sealed record Summary(
    string Riwaya, string Source,
    int Surahs, int Verses, int Words, int Letters,
    int DistinctWordsOriginal, int DistinctWordsNoDiacritics, int DistinctWordsNormalized,
    int Juz, int Pages);

public sealed record SurahStats(
    int Number, string NameAr, string NameEn,
    int Verses, int Words, int Letters,
    int FirstPage, int LastPage, int FirstJuz,
    double AvgWordsPerVerse, double AvgLettersPerWord);

public sealed record GroupStats(int Number, int Verses, int Words, int Letters, string From, string To);

public sealed record VerseRef(int Surah, int Verse, string SurahNameAr, int Words, int Letters, string Text);

public sealed record SurahRef(int Number, string NameAr, string NameEn, int Value);

public sealed record Extremes(
    SurahRef LongestSurahByVerses, SurahRef ShortestSurahByVerses,
    SurahRef LongestSurahByWords, SurahRef ShortestSurahByWords,
    VerseRef LongestVerseByWords, VerseRef ShortestVerseByWords,
    VerseRef LongestVerseByLetters, VerseRef ShortestVerseByLetters);

public sealed record FrequencyItem(int Rank, string Value, int Count, double Percent);

public sealed record FrequencyResult(TextForm Form, int Total, int Distinct, IReadOnlyList<FrequencyItem> Items);

public enum SearchMatch { Exact, StartsWith, EndsWith, Contains }

public sealed record Occurrence(int Surah, int Verse, int WordIndex, string Matched, string VerseText);

public sealed record SearchResult(
    string Query, string QueryInForm, TextForm Form, SearchMatch Match, int? Surah,
    int TotalOccurrences, int VersesMatched, int SurahsMatched,
    IReadOnlyList<SurahRef> BySurah, IReadOnlyList<Occurrence> Occurrences, bool Truncated);

/// <summary>Calculs statistiques sur le corpus. Sans état, sûr en accès concurrent.</summary>
public sealed class QuranStatistics(QuranCorpus corpus)
{
    public QuranCorpus Corpus { get; } = corpus;

    // ---------- Comptages de base ----------

    public Summary GetSummary()
    {
        var words = Corpus.Verses.SelectMany(v => v.Words).ToArray();
        return new Summary(
            QuranCorpus.Riwaya, QuranCorpus.Source,
            Corpus.Surahs.Count, Corpus.Verses.Count, words.Length, words.Sum(w => w.LetterCount),
            words.Select(w => w.Original).Distinct().Count(),
            words.Select(w => w.NoDiacritics).Distinct().Count(),
            words.Select(w => w.Normalized).Distinct().Count(),
            Corpus.Verses.Select(v => v.Juz).Distinct().Count(),
            Corpus.Verses.Max(v => v.PageEnd));
    }

    public IReadOnlyList<SurahStats> GetSurahStats() => Corpus.Surahs.Select(ToStats).ToArray();

    public SurahStats ToStats(Surah s) => new(
        s.Number, s.NameAr, s.NameEn, s.VerseCount, s.WordCount, s.LetterCount,
        s.Verses.Min(v => v.Page), s.Verses.Max(v => v.PageEnd), s.Verses[0].Juz,
        Math.Round((double)s.WordCount / s.VerseCount, 2),
        Math.Round((double)s.LetterCount / s.WordCount, 2));

    public IReadOnlyList<GroupStats> GetJuzStats() => Group(v => v.Juz);

    public IReadOnlyList<GroupStats> GetPageStats() => Group(v => v.Page);

    private IReadOnlyList<GroupStats> Group(Func<Verse, int> key) =>
        Corpus.Verses.GroupBy(key).OrderBy(g => g.Key).Select(g =>
        {
            var list = g.ToList();
            return new GroupStats(g.Key, list.Count, list.Sum(v => v.WordCount), list.Sum(v => v.LetterCount),
                list[0].Reference, list[^1].Reference);
        }).ToArray();

    public Extremes GetExtremes()
    {
        var s = Corpus.Surahs;
        var v = Corpus.Verses;
        SurahRef SR(Surah x, int val) => new(x.Number, x.NameAr, x.NameEn, val);

        // En cas d'égalité, on garde la première dans l'ordre du mushaf.
        var longestV = s.MaxBy(x => x.VerseCount)!;
        var shortestV = s.MinBy(x => x.VerseCount)!;
        var longestW = s.MaxBy(x => x.WordCount)!;
        var shortestW = s.MinBy(x => x.WordCount)!;

        return new Extremes(
            SR(longestV, longestV.VerseCount), SR(shortestV, shortestV.VerseCount),
            SR(longestW, longestW.WordCount), SR(shortestW, shortestW.WordCount),
            ToRef(v.MaxBy(x => x.WordCount)!), ToRef(v.MinBy(x => x.WordCount)!),
            ToRef(v.MaxBy(x => x.LetterCount)!), ToRef(v.MinBy(x => x.LetterCount)!));
    }

    public VerseRef ToRef(Verse v) =>
        new(v.Surah, v.Number, Corpus.Surahs[v.Surah - 1].NameAr, v.WordCount, v.LetterCount, v.Text);

    // ---------- Fréquences ----------

    /// <summary>Fréquence des lettres de base (forme NoDiacritics ou Normalized).</summary>
    public FrequencyResult GetLetterFrequencies(TextForm form = TextForm.NoDiacritics, int? surah = null)
    {
        if (form == TextForm.Original) form = TextForm.NoDiacritics; // les signes ne sont pas des lettres
        var counts = new Dictionary<string, int>();
        foreach (var w in WordsOf(surah))
            foreach (var c in w.Get(form))
            {
                var key = c.ToString();
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        return ToFrequency(form, counts, top: null);
    }

    /// <summary>Fréquence des mots.</summary>
    public FrequencyResult GetWordFrequencies(TextForm form = TextForm.Normalized, int top = 100, int minLetters = 1, int? surah = null)
    {
        var counts = new Dictionary<string, int>();
        foreach (var w in WordsOf(surah))
        {
            if (w.LetterCount < minLetters) continue;
            var key = w.Get(form);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return ToFrequency(form, counts, top);
    }

    private IEnumerable<Word> WordsOf(int? surah) =>
        (surah is null ? Corpus.Verses : Corpus.GetSurah(surah.Value)?.Verses ?? [])
        .SelectMany(v => v.Words);

    private static FrequencyResult ToFrequency(TextForm form, Dictionary<string, int> counts, int? top)
    {
        var total = counts.Values.Sum();
        var ordered = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select((kv, i) => new FrequencyItem(i + 1, kv.Key, kv.Value,
                total == 0 ? 0 : Math.Round(100.0 * kv.Value / total, 3)));
        if (top is > 0) ordered = ordered.Take(top.Value);
        return new FrequencyResult(form, total, counts.Count, ordered.ToArray());
    }

    // ---------- Recherche ----------

    /// <summary>
    /// Recherche un mot ou une expression (plusieurs mots consécutifs). La requête est convertie
    /// dans la même forme que le texte : chercher « الله » en forme Normalized trouve « اَ۬للَّهُ ».
    /// Pour une expression, le mode de correspondance s'applique à chaque mot.
    /// </summary>
    public SearchResult Search(string query, SearchMatch match = SearchMatch.Exact,
        TextForm form = TextForm.Normalized, int? surah = null, int limit = 200)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => ArabicText.ToForm(t, form))
            .Where(t => t.Length > 0)
            .ToArray();
        if (terms.Length == 0)
            throw new ArgumentException("La requête ne contient aucune lettre arabe.", nameof(query));

        var occurrences = new List<Occurrence>();
        var total = 0;
        var verses = new HashSet<int>();
        var perSurah = new SortedDictionary<int, int>();

        var source = surah is null ? Corpus.Verses : Corpus.GetSurah(surah.Value)?.Verses ?? [];
        foreach (var verse in source)
        {
            var words = verse.Words;
            for (var i = 0; i + terms.Length <= words.Count; i++)
            {
                var ok = true;
                for (var j = 0; j < terms.Length && ok; j++)
                    ok = Matches(words[i + j].Get(form), terms[j], match);
                if (!ok) continue;

                total++;
                verses.Add(verse.Id);
                perSurah[verse.Surah] = perSurah.GetValueOrDefault(verse.Surah) + 1;
                if (occurrences.Count < limit)
                {
                    var matched = string.Join(' ', words.Skip(i).Take(terms.Length).Select(w => w.Original));
                    occurrences.Add(new Occurrence(verse.Surah, verse.Number, i + 1, matched, verse.Text));
                }
            }
        }

        var bySurah = perSurah
            .Select(kv => new SurahRef(kv.Key, Corpus.Surahs[kv.Key - 1].NameAr, Corpus.Surahs[kv.Key - 1].NameEn, kv.Value))
            .ToArray();

        return new SearchResult(query, string.Join(' ', terms), form, match, surah,
            total, verses.Count, perSurah.Count, bySurah, occurrences, total > occurrences.Count);
    }

    private static bool Matches(string word, string term, SearchMatch match) => match switch
    {
        SearchMatch.Exact => string.Equals(word, term, StringComparison.Ordinal),
        SearchMatch.StartsWith => word.StartsWith(term, StringComparison.Ordinal),
        SearchMatch.EndsWith => word.EndsWith(term, StringComparison.Ordinal),
        SearchMatch.Contains => word.Contains(term, StringComparison.Ordinal),
        _ => false,
    };
}
