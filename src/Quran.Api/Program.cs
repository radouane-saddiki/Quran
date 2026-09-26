using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Quran.Core;

var builder = WebApplication.CreateBuilder(args);

// Données : fichier configuré (QuranData:Path) ou texte Warsh embarqué par défaut.
builder.Services.AddSingleton(sp =>
{
    var path = sp.GetRequiredService<IConfiguration>()["QuranData:Path"];
    return string.IsNullOrWhiteSpace(path) ? QuranCorpus.LoadEmbedded() : QuranCorpus.LoadFile(path);
});
builder.Services.AddSingleton<QuranStatistics>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // arabe lisible dans le JSON
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// Chargement au démarrage plutôt qu'à la première requête.
app.Services.GetRequiredService<QuranStatistics>();

app.MapGet("/", () => Results.Ok(new
{
    name = "Quran Warsh Statistics API",
    riwaya = QuranCorpus.Riwaya,
    forms = Enum.GetNames<TextForm>(),
    matches = Enum.GetNames<SearchMatch>(),
    endpoints = new[]
    {
        "GET /api/summary",
        "GET /api/surahs",
        "GET /api/surahs/{number}?includeVerses=true",
        "GET /api/surahs/{surah}/verses/{verse}",
        "GET /api/juz",
        "GET /api/hizb",
        "GET /api/pages",
        "GET /api/extremes",
        "GET /api/frequencies/letters?form=NoDiacritics&surah=",
        "GET /api/frequencies/words?form=Normalized&top=100&minLetters=1&surah=",
        "GET /api/search?q=...&match=Exact|StartsWith|EndsWith|Contains&form=Normalized&surah=&limit=200",
    },
}));

var api = app.MapGroup("/api");

// ---------- Comptages de base ----------

api.MapGet("/summary", (QuranStatistics s) => s.GetSummary());

api.MapGet("/surahs", (QuranStatistics s) => s.GetSurahStats());

api.MapGet("/surahs/{number:int}", (int number, bool? includeVerses, QuranStatistics s) =>
{
    var surah = s.Corpus.GetSurah(number);
    if (surah is null) return Results.NotFound(Error($"Sourate {number} inexistante (1–114)."));
    return Results.Ok(new
    {
        stats = s.ToStats(surah),
        verses = includeVerses == false ? null : surah.Verses.Select(s.ToRef),
    });
});

api.MapGet("/surahs/{surah:int}/verses/{verse:int}", (int surah, int verse, QuranStatistics s) =>
{
    var v = s.Corpus.GetVerse(surah, verse);
    if (v is null) return Results.NotFound(Error($"Verset {surah}:{verse} inexistant."));
    return Results.Ok(new
    {
        verse = s.ToRef(v),
        v.Juz,
        v.Hizb,
        v.Page,
        v.PageEnd,
        words = v.Words.Select((w, i) => new { index = i + 1, w.Original, w.NoDiacritics, w.Normalized, letters = w.LetterCount }),
    });
});

api.MapGet("/juz", (QuranStatistics s) => s.GetJuzStats());

api.MapGet("/hizb", (QuranStatistics s) => s.GetHizbStats());

api.MapGet("/pages", (QuranStatistics s) => s.GetPageStats());

api.MapGet("/extremes", (QuranStatistics s) => s.GetExtremes());

// ---------- Fréquences ----------

api.MapGet("/frequencies/letters", (string? form, int? surah, QuranStatistics s) =>
{
    if (!TryParse(form, TextForm.NoDiacritics, out TextForm f, out var err)) return err!;
    if (surah is not null && s.Corpus.GetSurah(surah.Value) is null) return Results.NotFound(Error($"Sourate {surah} inexistante."));
    return Results.Ok(s.GetLetterFrequencies(f, surah));
});

api.MapGet("/frequencies/words", (string? form, int? top, int? minLetters, int? surah, QuranStatistics s) =>
{
    if (!TryParse(form, TextForm.Normalized, out TextForm f, out var err)) return err!;
    if (surah is not null && s.Corpus.GetSurah(surah.Value) is null) return Results.NotFound(Error($"Sourate {surah} inexistante."));
    var t = Math.Clamp(top ?? 100, 1, 10_000);
    return Results.Ok(s.GetWordFrequencies(f, t, Math.Max(1, minLetters ?? 1), surah));
});

// ---------- Recherche ----------

api.MapGet("/search", (string? q, string? match, string? form, int? surah, int? limit, QuranStatistics s) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(Error("Paramètre q obligatoire."));
    if (!TryParse(match, SearchMatch.Exact, out SearchMatch m, out var err)) return err!;
    if (!TryParse(form, TextForm.Normalized, out TextForm f, out err)) return err!;
    if (surah is not null && s.Corpus.GetSurah(surah.Value) is null) return Results.NotFound(Error($"Sourate {surah} inexistante."));
    try
    {
        return Results.Ok(s.Search(q, m, f, surah, Math.Clamp(limit ?? 200, 0, 5_000)));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(Error(ex.Message));
    }
});

app.Run();

static object Error(string message) => new { error = message };

static bool TryParse<T>(string? value, T fallback, out T result, out IResult? error) where T : struct, Enum
{
    error = null;
    if (string.IsNullOrWhiteSpace(value)) { result = fallback; return true; }
    if (Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result)) return true;
    error = Results.BadRequest(Error($"Valeur « {value} » invalide. Valeurs possibles : {string.Join(", ", Enum.GetNames<T>())}."));
    return false;
}

public partial class Program;
