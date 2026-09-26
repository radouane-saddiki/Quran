using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quran.Core;

// Usage : dotnet run --project tools/Quran.Reports -- [dossier de sortie]   (défaut : reports)
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "reports");
Directory.CreateDirectory(outDir);

var stats = new QuranStatistics(QuranCorpus.LoadEmbedded());
var inv = CultureInfo.InvariantCulture;
var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

var summary = stats.GetSummary();
var surahs = stats.GetSurahStats();
var juz = stats.GetJuzStats();
var pages = stats.GetPageStats();
var extremes = stats.GetExtremes();
var letters = stats.GetLetterFrequencies(TextForm.NoDiacritics);
var wordsNorm = stats.GetWordFrequencies(TextForm.Normalized, top: 1000);
var wordsOrig = stats.GetWordFrequencies(TextForm.Original, top: 1000);

// ---------- JSON ----------
var json = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
};
File.WriteAllText(Path.Combine(outDir, "summary.json"),
    JsonSerializer.Serialize(new { summary, extremes }, json), new UTF8Encoding(false));

// ---------- CSV ----------
static string Csv(object? v) => v switch
{
    null => "",
    string s when s.IndexOfAny([',', '"', '\n']) >= 0 => $"\"{s.Replace("\"", "\"\"")}\"",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};

void WriteCsv(string file, string header, IEnumerable<object?[]> rows)
{
    var sb = new StringBuilder().AppendLine(header);
    foreach (var r in rows) sb.AppendLine(string.Join(',', r.Select(Csv)));
    File.WriteAllText(Path.Combine(outDir, file), sb.ToString(), utf8Bom);
}

WriteCsv("surahs.csv", "number,name_ar,name_en,verses,words,letters,first_page,last_page,first_juz,avg_words_per_verse,avg_letters_per_word",
    surahs.Select(s => new object?[] { s.Number, s.NameAr, s.NameEn, s.Verses, s.Words, s.Letters, s.FirstPage, s.LastPage, s.FirstJuz, s.AvgWordsPerVerse, s.AvgLettersPerWord }));
WriteCsv("juz.csv", "juz,verses,words,letters,from,to",
    juz.Select(g => new object?[] { g.Number, g.Verses, g.Words, g.Letters, g.From, g.To }));
WriteCsv("pages.csv", "page,verses,words,letters,from,to",
    pages.Select(g => new object?[] { g.Number, g.Verses, g.Words, g.Letters, g.From, g.To }));
WriteCsv("letters.csv", "rank,letter,count,percent",
    letters.Items.Select(i => new object?[] { i.Rank, i.Value, i.Count, i.Percent }));
WriteCsv("words_normalized_top1000.csv", "rank,word,count,percent",
    wordsNorm.Items.Select(i => new object?[] { i.Rank, i.Value, i.Count, i.Percent }));
WriteCsv("words_original_top1000.csv", "rank,word,count,percent",
    wordsOrig.Items.Select(i => new object?[] { i.Rank, i.Value, i.Count, i.Percent }));

// ---------- Markdown ----------
string N(int n) => n.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ').Replace(' ', ' ');
string V(VerseRef v) => $"{v.Surah}:{v.Verse} ({v.SurahNameAr.Trim()}) — {v.Words} mots, {v.Letters} lettres";
string S(SurahRef s) => $"{s.Number}. {s.NameAr} ({s.NameEn}) — {N(s.Value)}";

var md = new StringBuilder();
md.AppendLine("# Statistiques du Coran — riwayat Warsh ʿan Nāfiʿ").AppendLine();
md.AppendLine("> Rapport généré par `tools/Quran.Reports` à partir de `data/warsh_kfgqpc_v2-1.json` (KFGQPC v2.1).");
md.AppendLine("> Définitions : un **mot** est une suite séparée par des espaces contenant au moins une lettre ; une **lettre** est une lettre arabe de base (diacritiques, signes coraniques et tatweel exclus). Dans le comptage de Nāfiʿ, la basmala n'est un verset dans aucune sourate (pas même al-Fātiḥa) : elle n'est donc pas comptée.").AppendLine();

md.AppendLine("## Vue d'ensemble").AppendLine();
md.AppendLine("| Indicateur | Valeur |").AppendLine("|---|---:|");
md.AppendLine($"| Sourates | {N(summary.Surahs)} |");
md.AppendLine($"| Versets | {N(summary.Verses)} |");
md.AppendLine($"| Mots | {N(summary.Words)} |");
md.AppendLine($"| Lettres | {N(summary.Letters)} |");
md.AppendLine($"| Mots distincts (texte original) | {N(summary.DistinctWordsOriginal)} |");
md.AppendLine($"| Mots distincts (sans diacritiques) | {N(summary.DistinctWordsNoDiacritics)} |");
md.AppendLine($"| Mots distincts (normalisés) | {N(summary.DistinctWordsNormalized)} |");
md.AppendLine($"| Juz | {summary.Juz} |");
md.AppendLine($"| Pages | {summary.Pages} |").AppendLine();

md.AppendLine("## Extrêmes").AppendLine();
md.AppendLine($"- Sourate la plus longue (versets) : {S(extremes.LongestSurahByVerses)}");
md.AppendLine($"- Sourate la plus courte (versets) : {S(extremes.ShortestSurahByVerses)}");
md.AppendLine($"- Sourate la plus longue (mots) : {S(extremes.LongestSurahByWords)}");
md.AppendLine($"- Sourate la plus courte (mots) : {S(extremes.ShortestSurahByWords)}");
md.AppendLine($"- Verset le plus long (mots) : {V(extremes.LongestVerseByWords)}");
md.AppendLine($"- Verset le plus court (mots) : {V(extremes.ShortestVerseByWords)}");
md.AppendLine($"- Verset le plus long (lettres) : {V(extremes.LongestVerseByLetters)}");
md.AppendLine($"- Verset le plus court (lettres) : {V(extremes.ShortestVerseByLetters)}").AppendLine();

md.AppendLine("## Fréquence des lettres").AppendLine();
md.AppendLine("| Rang | Lettre | Occurrences | % |").AppendLine("|---:|:---:|---:|---:|");
foreach (var i in letters.Items)
    md.AppendLine($"| {i.Rank} | {i.Value} | {N(i.Count)} | {i.Percent.ToString("0.00", inv)} |");
md.AppendLine();

md.AppendLine("## 30 mots les plus fréquents (forme normalisée)").AppendLine();
md.AppendLine("| Rang | Mot | Occurrences | % |").AppendLine("|---:|:---:|---:|---:|");
foreach (var i in wordsNorm.Items.Take(30))
    md.AppendLine($"| {i.Rank} | {i.Value} | {N(i.Count)} | {i.Percent.ToString("0.00", inv)} |");
md.AppendLine();

md.AppendLine("## Par juz").AppendLine();
md.AppendLine("| Juz | Versets | Mots | Lettres | Début | Fin |").AppendLine("|---:|---:|---:|---:|---|---|");
foreach (var g in juz)
    md.AppendLine($"| {g.Number} | {N(g.Verses)} | {N(g.Words)} | {N(g.Letters)} | {g.From} | {g.To} |");
md.AppendLine();

md.AppendLine("## Par sourate").AppendLine();
md.AppendLine("| N° | Sourate | Versets | Mots | Lettres | Pages |").AppendLine("|---:|---|---:|---:|---:|---|");
foreach (var s in surahs)
    md.AppendLine($"| {s.Number} | {s.NameAr} — {s.NameEn} | {N(s.Verses)} | {N(s.Words)} | {N(s.Letters)} | {s.FirstPage}–{s.LastPage} |");
md.AppendLine();

md.AppendLine("## Fichiers détaillés").AppendLine();
md.AppendLine("- [`summary.json`](summary.json) — vue d'ensemble et extrêmes");
md.AppendLine("- [`surahs.csv`](surahs.csv), [`juz.csv`](juz.csv), [`pages.csv`](pages.csv) — comptages");
md.AppendLine("- [`letters.csv`](letters.csv) — fréquence des lettres");
md.AppendLine("- [`words_normalized_top1000.csv`](words_normalized_top1000.csv), [`words_original_top1000.csv`](words_original_top1000.csv) — fréquence des mots");

File.WriteAllText(Path.Combine(outDir, "README.md"), md.ToString(), new UTF8Encoding(false));

Console.WriteLine($"Rapports écrits dans {outDir}");
Console.WriteLine($"{summary.Surahs} sourates, {summary.Verses} versets, {summary.Words} mots, {summary.Letters} lettres.");
