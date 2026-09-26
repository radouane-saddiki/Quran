using System.Text.Json;

namespace Quran.Core;

public enum MushafLineKind { Text, SurahTitle, Basmala }

/// <summary>Morceau d'un verset sur une ligne : mots FirstWord..LastWord de Verse.DisplayWords.</summary>
public sealed record MushafPiece(Verse Verse, int FirstWord, int LastWord)
{
    public bool IsVerseEnd => LastWord == Verse.DisplayWords.Count - 1;
    public IEnumerable<string> Words => Verse.DisplayWords.Skip(FirstWord).Take(LastWord - FirstWord + 1);
}

public sealed class MushafLine
{
    public required int Number { get; init; }
    public required MushafLineKind Kind { get; init; }
    /// <summary>Sourate annoncée (lignes de titre et de basmala).</summary>
    public Surah? Surah { get; init; }
    public IReadOnlyList<MushafPiece> Pieces { get; init; } = [];
    /// <summary>Largeur du texte rapportée à celle d'une ligne pleine (1 = pleine).</summary>
    public double Fill { get; init; } = 1;
}

public sealed class MushafPage
{
    public required int Number { get; init; }
    public required IReadOnlyList<MushafLine> Lines { get; init; }
    public IEnumerable<Verse> Verses => Lines.SelectMany(l => l.Pieces).Select(p => p.Verse).Distinct();
    public Verse FirstVerse => Verses.First();
}

/// <summary>
/// Le mushaf Warsh ligne par ligne (604 pages de 15 lignes, 8 pour les deux premières).
/// Coupures calculées par tools/mushaf-layout/layout.py (data/warsh_lines.json).
/// </summary>
public sealed class MushafLayout
{
    public const int PageCount = 604;
    public IReadOnlyList<MushafPage> Pages { get; }
    /// <summary>Largeur d'une ligne pleine, en em de la police du mushaf.</summary>
    public double Width { get; }

    public MushafPage GetPage(int number) => Pages[Math.Clamp(number, 1, PageCount) - 1];

    private MushafLayout(IReadOnlyList<MushafPage> pages, double width) => (Pages, Width) = (pages, width);

    public static MushafLayout LoadEmbedded(QuranCorpus corpus)
    {
        using var stream = typeof(MushafLayout).Assembly.GetManifestResourceStream("Quran.Core.warsh_lines.json")
            ?? throw new InvalidOperationException("Ressource embarquée Quran.Core.warsh_lines.json introuvable.");
        using var doc = JsonDocument.Parse(stream);
        var target = doc.RootElement.GetProperty("width").GetDouble();
        var byId = corpus.Verses.ToDictionary(v => v.Id);

        // Lignes de texte, regroupées par page.
        var text = new Dictionary<int, SortedDictionary<int, MushafLine>>();
        foreach (var l in doc.RootElement.GetProperty("lines").EnumerateArray())
        {
            int page = l[0].GetInt32(), line = l[1].GetInt32();
            var width = l[2].GetDouble();
            var pieces = l[3].EnumerateArray().Select(p =>
            {
                var v = byId[p[0].GetInt32()];
                int a = p[1].GetInt32(), b = p[2].GetInt32();
                if (b >= v.DisplayWords.Count)
                    throw new InvalidDataException($"warsh_lines.json ne correspond pas au texte ({v.Reference}).");
                return new MushafPiece(v, a, b);
            }).ToList();
            // Pages 1 et 2 : lignes courtes et centrées, comme dans le mushaf imprimé.
            var fill = page <= 2 ? 0 : width / target;
            if (!text.TryGetValue(page, out var lines)) text[page] = lines = [];
            lines[line] = new MushafLine { Number = line, Kind = MushafLineKind.Text, Pieces = pieces, Fill = fill };
        }

        // Lignes de titre de sourate et de basmala : juste avant le premier verset de chaque sourate.
        foreach (var s in corpus.Surahs)
        {
            var first = s.Verses[0];
            var lines = text[first.Page];
            var start = lines.First(kv => kv.Value.Pieces.Any(p => p.Verse == first)).Key;
            var titleLine = s.Number == 9 ? start - 1 : start - 2;
            lines[titleLine] = new MushafLine { Number = titleLine, Kind = MushafLineKind.SurahTitle, Surah = s };
            if (s.Number != 9)
                lines[start - 1] = new MushafLine { Number = start - 1, Kind = MushafLineKind.Basmala, Surah = s };
        }

        var pages = Enumerable.Range(1, PageCount)
            .Select(p => new MushafPage { Number = p, Lines = text[p].Values.ToList() })
            .ToList();
        return new MushafLayout(pages, target);
    }
}
