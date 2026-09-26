using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Quran.Core;

namespace Quran.Web.Pages;

/// <summary>Mode تسميع : l'utilisateur récite de mémoire, le texte masqué se dévoile mot à mot.</summary>
public sealed class RecitationModel(QuranCorpus corpus) : PageModel
{
    public const int MaxVerses = 60;

    public QuranCorpus Corpus => corpus;

    [BindProperty(SupportsGet = true)] public int Surah { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int From { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int? To { get; set; }

    public Surah Current { get; private set; } = default!;
    public IReadOnlyList<Verse> Verses { get; private set; } = [];

    public void OnGet()
    {
        Current = corpus.GetSurah(Math.Clamp(Surah, 1, 114))!;
        Surah = Current.Number;
        From = Math.Clamp(From, 1, Current.VerseCount);
        // Par défaut : la sourate entière si elle est courte, sinon 10 versets.
        var to = To ?? (Current.VerseCount - From < 20 ? Current.VerseCount : From + 9);
        To = Math.Clamp(to, From, Math.Min(Current.VerseCount, From + MaxVerses - 1));
        Verses = Current.Verses.Skip(From - 1).Take(To.Value - From + 1).ToList();
    }

    /// <summary>Mots attendus (forme normalisée) et leur verset, pour l'alignement côté navigateur.</summary>
    public string WordsJson() => JsonSerializer.Serialize(
        Verses.SelectMany(v => v.Words.Select(w => new { s = v.Surah, v = v.Number, n = w.Normalized, o = w.Original })));
}
