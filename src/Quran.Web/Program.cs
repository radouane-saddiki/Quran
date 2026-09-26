using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Quran.Core;
using Quran.Web.Audio;
using Quran.Web.Localization;
using Quran.Web.Recitation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
// Arabe écrit tel quel dans le HTML (et non en entités &#x...;) : pages bien plus légères.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddScoped<Loc>();
builder.Services.AddSingleton(_ => QuranCorpus.LoadEmbedded());
builder.Services.AddSingleton<QuranStatistics>();
builder.Services.AddSingleton(sp => MushafLayout.LoadEmbedded(sp.GetRequiredService<QuranCorpus>()));
builder.Services.Configure<AudioOptions>(builder.Configuration.GetSection("Audio"));
builder.Services.AddHttpClient<TimingService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("QuranWarshWeb/1.0");
});
builder.Services.AddHttpClient("audio-download", c =>
{
    c.Timeout = TimeSpan.FromMinutes(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("QuranWarshWeb/1.0");
});
builder.Services.AddSingleton<LocalAudio>();
builder.Services.Configure<RecitationOptions>(builder.Configuration.GetSection("Recitation"));
builder.Services.AddHttpClient<WhisperClient>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddTransient<AudioDownloader>();

var app = builder.Build();

// Chargement du texte au démarrage.
app.Services.GetRequiredService<QuranStatistics>();
app.Services.GetRequiredService<MushafLayout>();

// Option : « dotnet run --project src/Quran.Web -- --telecharger-minutages »
// télécharge tous les minutages (114 sourates × récitateurs) pour un usage hors ligne, puis quitte.
if (args.Contains("--telecharger-minutages"))
{
    await PrefetchTimingsAsync(app.Services);
    return;
}

// Option : « dotnet run --project src/Quran.Web -- --telecharger-audio [qazabri] [koshi] »
// télécharge les MP3 dans wwwroot/audio/ (reprise possible), puis les minutages. Ensuite le site lit les fichiers locaux.
if (args.Contains("--telecharger-audio"))
{
    var only = args.SkipWhile(a => a != "--telecharger-audio").Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
    var code = await app.Services.GetRequiredService<AudioDownloader>().RunAsync(only);
    Console.WriteLine("\nMinutages :");
    await PrefetchTimingsAsync(app.Services);
    Environment.ExitCode = code;
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// ---------- Langue de l'interface (cookie, arabe par défaut) ----------
app.MapGet("/langue/{code}", (string code, string? retour, HttpContext ctx) =>
{
    if (Loc.Supported.Contains(code))
        ctx.Response.Cookies.Append(Loc.CookieName, code, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            HttpOnly = true,
        });
    var back = !string.IsNullOrEmpty(retour) && retour.StartsWith('/') && !retour.StartsWith("//") && !retour.StartsWith("/\\") ? retour : "/";
    return Results.LocalRedirect(back);
});

// ---------- Audio ----------
app.MapGet("/audio/reciters", (TimingService t, LocalAudio local) =>
    t.Options.Reciters.Select(r => new
    {
        r.Id, r.Name, r.NameAr, audioUrl = r.AudioUrl,
        localSurahs = Enumerable.Range(1, 114).Count(s => local.Exists(r, s)),
    }));

app.MapGet("/audio/timings/{reciter}/{surah:int}", async (string reciter, int surah, TimingService t, LocalAudio local, CancellationToken ct) =>
{
    var r = t.Find(reciter);
    if (r is null) return Results.NotFound(new { error = $"Récitateur « {reciter} » inconnu." });
    if (surah is < 1 or > 114) return Results.NotFound(new { error = "Sourate inexistante (1–114)." });
    try
    {
        var timings = await t.GetAsync(r, surah, ct);
        return Results.Ok(new { reciter = r.Id, surah, audio = local.UrlFor(r, surah), local = local.Exists(r, surah), timings });
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Minutages indisponibles : connexion à mp3quran.net impossible.", detail = ex.Message },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

// ---------- Récitation : relais vers le serveur Whisper local (optionnel) ----------
app.MapGet("/recitation/whisper", async (WhisperClient w, CancellationToken ct) =>
{
    var health = await w.HealthAsync(ct);
    if (health is null) return Results.Ok(new { available = false });
    using var doc = System.Text.Json.JsonDocument.Parse(health);
    string? Prop(string name) => doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
    return Results.Ok(new { available = true, model = Prop("model"), device = Prop("device") });
});

app.MapPost("/recitation/transcribe", async (HttpRequest req, WhisperClient w, CancellationToken ct) =>
{
    if (req.ContentLength is null or 0 || req.ContentLength > WhisperClient.MaxAudioBytes)
        return Results.BadRequest(new { error = "Audio absent ou trop long." });
    try
    {
        var (status, json) = await w.TranscribeAsync(req.Body, ct);
        return Results.Content(json, "application/json", Encoding.UTF8, status);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new { error = "Serveur Whisper injoignable.", detail = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
}).DisableAntiforgery();

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

static async Task PrefetchTimingsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var t = scope.ServiceProvider.GetRequiredService<TimingService>();
    var corpus = scope.ServiceProvider.GetRequiredService<QuranCorpus>();
    foreach (var r in t.Options.Reciters)
    {
        var mismatches = 0;
        for (var s = 1; s <= 114; s++)
        {
            IReadOnlyList<AyahTiming> timings;
            try
            {
                timings = await t.GetAsync(r, s);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                Console.WriteLine($"  ! {r.Id} sourate {s} : minutages indisponibles ({ex.Message})");
                mismatches++;
                continue;
            }
            var expected = corpus.GetSurah(s)!.VerseCount;
            if (timings.Count != expected)
            {
                mismatches++;
                Console.WriteLine($"  ! {r.Id} sourate {s} : {timings.Count} minutages pour {expected} versets");
            }
        }
        Console.WriteLine($"{r.Name} : 114 sourates, {mismatches} écart(s) de nombre de versets.");
    }
}

static string Csv(object? v) => v switch
{
    null => "",
    string s when s.IndexOfAny([',', '"', '\n']) >= 0 => $"\"{s.Replace("\"", "\"\"")}\"",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};
