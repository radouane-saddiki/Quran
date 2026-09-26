using System.Globalization;

namespace Quran.Web.Models;

public sealed record BarItem(string Label, double Value, string? Tooltip = null, string? Href = null);

/// <summary>Graphique en barres (une seule série) rendu en SVG côté serveur.</summary>
public sealed class BarChart
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required IReadOnlyList<BarItem> Items { get; init; }
    /// <summary>Vrai : barres horizontales (une ligne par élément). Faux : colonnes.</summary>
    public bool Horizontal { get; init; }
    /// <summary>Afficher un libellé d'axe tous les N éléments (colonnes).</summary>
    public int LabelEvery { get; init; } = 1;
    /// <summary>Libellés de catégories en arabe (police arabe, sens RTL).</summary>
    public bool ArabicLabels { get; init; }
    public string ValueFormat { get; init; } = "N0";

    public double Max => Items.Count == 0 ? 0 : Items.Max(i => i.Value);

    public string Format(double v) =>
        v.ToString(ValueFormat, Fr).Replace(' ', ' ').Replace(' ', ' ');

    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Graduations « rondes » de l'axe des valeurs (0 inclus).</summary>
    public IReadOnlyList<double> Ticks(int target = 4)
    {
        var max = Max;
        if (max <= 0) return [0];
        var raw = max / target;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = Math.Max(1, new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * mag).First(s => s >= raw)); // comptages entiers
        var ticks = new List<double>();
        for (double t = 0; ; t += step)
        {
            ticks.Add(t);
            if (t >= max) break;
        }
        return ticks;
    }

    public double AxisMax
    {
        get
        {
            var t = Ticks();
            return Math.Max(t[^1], Max);
        }
    }
}
