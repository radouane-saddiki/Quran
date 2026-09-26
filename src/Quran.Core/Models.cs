namespace Quran.Core;

/// <summary>Un mot du texte, sous ses trois formes.</summary>
public sealed record Word(string Original, string NoDiacritics, string Normalized)
{
    public int LetterCount => NoDiacritics.Length;

    public string Get(TextForm form) => form switch
    {
        TextForm.Original => Original,
        TextForm.NoDiacritics => NoDiacritics,
        TextForm.Normalized => Normalized,
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };
}

/// <summary>Un verset (āya).</summary>
public sealed class Verse
{
    public required int Id { get; init; }
    public required int Surah { get; init; }
    public required int Number { get; init; }
    public required int Juz { get; init; }
    /// <summary>Hizb (1–60) selon la division maghrébine du mushaf Warsh (voir data/warsh_hizb.json).</summary>
    public int Hizb { get; internal set; }
    /// <summary>Page où commence le verset (mushaf Warsh KFGQPC, 604 pages).</summary>
    public required int Page { get; init; }
    /// <summary>Page où finit le verset (différente de Page pour quelques versets à cheval).</summary>
    public required int PageEnd { get; init; }
    /// <summary>Texte sans le glyphe de numéro de verset.</summary>
    public required string Text { get; init; }
    public required IReadOnlyList<Word> Words { get; init; }

    public int WordCount => Words.Count;
    public int LetterCount => Words.Sum(w => w.LetterCount);
    public string Reference => $"{Surah}:{Number}";
    /// <summary>Verset de prosternation (signe ۩ présent dans le texte).</summary>
    public bool IsSajda => Text.Contains('\u06E9');
    /// <summary>Glyphe du numéro de verset dans la police KFGQPC (U+FC00 = 1).</summary>
    public string NumberGlyph => ((char)(0xFC00 + Number - 1)).ToString();
}

/// <summary>Une sourate.</summary>
public sealed class Surah
{
    public required int Number { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public required IReadOnlyList<Verse> Verses { get; init; }

    public int VerseCount => Verses.Count;
    public int WordCount => Verses.Sum(v => v.WordCount);
    public int LetterCount => Verses.Sum(v => v.LetterCount);
}
