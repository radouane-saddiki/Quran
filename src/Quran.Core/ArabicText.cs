using System.Text;

namespace Quran.Core;

/// <summary>Forme du texte utilisée pour les comptages, fréquences et recherches.</summary>
public enum TextForm
{
    /// <summary>Texte tel qu'imprimé (diacritiques, signes de lecture et de pause compris).</summary>
    Original,
    /// <summary>Lettres de base uniquement : diacritiques et signes coraniques retirés, « ے » ramené à « ي ».</summary>
    NoDiacritics,
    /// <summary>Comme NoDiacritics, avec en plus les alifs unifiés (أ إ آ ٱ → ا) et « ى » → « ي ».</summary>
    Normalized,
}

/// <summary>Outils de traitement du texte arabe du mushaf KFGQPC (riwayat Warsh).</summary>
public static class ArabicText
{
    /// <summary>
    /// Vrai pour une lettre arabe de base (hamza à yā', plus le yā' maghrébin « ے »).
    /// La tatweel (U+0640) n'est pas une lettre.
    /// </summary>
    public static bool IsBaseLetter(char c) =>
        c is >= 'ء' and <= 'غ'
          or >= 'ف' and <= 'ي'
          or 'ے';

    /// <summary>
    /// Dans les données KFGQPC, le numéro de verset est encodé par un glyphe de la zone
    /// U+FC00–U+FDFF (U+FC00 = verset 1, U+FC01 = verset 2…).
    /// </summary>
    public static bool IsVerseNumberGlyph(char c) => c is >= 'ﰀ' and <= '﷿';

    /// <summary>Retire le numéro de verset final et les espaces superflus.</summary>
    public static string StripVerseNumber(string ayaText)
    {
        var sb = new StringBuilder(ayaText.Length);
        foreach (var c in ayaText)
        {
            if (!IsVerseNumberGlyph(c)) sb.Append(c);
        }
        return sb.ToString().Replace(' ', ' ').Replace("‏", string.Empty).Trim();
    }

    /// <summary>
    /// Découpe un verset en mots. Un mot est une suite de caractères séparée par des espaces
    /// et contenant au moins une lettre de base. Les signes isolés (۞, numéros de verset) sont ignorés.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string ayaText)
    {
        var text = StripVerseNumber(ayaText);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Where(t => t.Any(IsBaseLetter)).ToArray();
    }

    /// <summary>Conserve uniquement les lettres de base ; « ے » devient « ي ».</summary>
    public static string RemoveDiacritics(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (!IsBaseLetter(c)) continue;
            sb.Append(c == 'ے' ? 'ي' : c);
        }
        return sb.ToString();
    }

    /// <summary>RemoveDiacritics + unification des alifs et du alif maqsūra.</summary>
    public static string Normalize(string word)
    {
        var bare = RemoveDiacritics(word);
        var sb = new StringBuilder(bare.Length);
        foreach (var c in bare)
        {
            sb.Append(c switch
            {
                'آ' or 'أ' or 'إ' or 'ٱ' => 'ا', // آ أ إ ٱ → ا
                'ى' => 'ي',                                    // ى → ي
                _ => c,
            });
        }
        return sb.ToString();
    }

    public static string ToForm(string word, TextForm form) => form switch
    {
        TextForm.Original => word.Trim(),
        TextForm.NoDiacritics => RemoveDiacritics(word),
        TextForm.Normalized => Normalize(word),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    /// <summary>Nombre de lettres de base d'un mot.</summary>
    public static int CountLetters(string word) => word.Count(IsBaseLetter);
}
