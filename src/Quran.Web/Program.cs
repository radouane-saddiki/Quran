using System.Globalization;
using System.Text;
using Quran.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton(_ => QuranCorpus.LoadEmbedded());
builder.Services.AddSingleton<QuranStatistics>();

var app = builder.Build();

// Chargement du texte au démarrage.
app.Services.GetRequiredService<QuranStatistics>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// ---------- Exports CSV (UTF-8 avec BOM pour Excel) ----------
app.MapGet("/export/{name}.csv", (string name, QuranStatistics s) =>
{
    var sb = new StringBuilder();
    void Row(params object?[] cells) => sb.AppendLine(string.Join(',', cells.Select(Csv)));

    switch (name)
    {
        case "sourates":
            Row("numero", "nom_ar", "nom_en", "versets", "mots", "lettres", "page_debut", "page_fin", "juz_debut", "mots_par_verset", "lettres_par_mot");
            foreach (var x in s.GetSurahStats())
                Row(x.Number, x.NameAr, x.NameEn, x.Verses, x.Words, x.Letters, x.FirstPage, x.LastPage, x.FirstJuz, x.AvgWordsPerVerse, x.AvgLettersPerWord);
            break;
        case "juz":
        case "hizb":
        case "pages":
            var groups = name switch { "juz" => s.GetJuzStats(), "hizb" => s.GetHizbStats(), _ => s.GetPageStats() };
            Row(name, "versets", "mots", "lettres", "debut", "fin");
            foreach (var g in groups) Row(g.Number, g.Verses, g.Words, g.Letters, g.From, g.To);
            break;
        case "lettres":
            Row("rang", "lettre", "occurrences", "pourcentage");
            foreach (var i in s.GetLetterFrequencies().Items) Row(i.Rank, i.Value, i.Count, i.Percent);
            break;
        case "mots":
            Row("rang", "mot", "occurrences", "pourcentage");
            foreach (var i in s.GetWordFrequencies(TextForm.Normalized, top: 5000).Items) Row(i.Rank, i.Value, i.Count, i.Percent);
            break;
        default:
            return Results.NotFound();
    }

    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    return Results.File(bytes, "text/csv; charset=utf-8", $"quran-warsh-{name}.csv");
});

app.Run();

static string Csv(object? v) => v switch
{
    null => "",
    string s when s.IndexOfAny([',', '"', '\n']) >= 0 => $"\"{s.Replace("\"", "\"\"")}\"",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};
