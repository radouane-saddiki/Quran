using System.Globalization;
using System.Text.Json;
using Quran.Core;

namespace Quran.Web.Localization;

/// <summary>
/// Langue de l'interface : arabe (par défaut, de droite à gauche) ou français.
/// Le choix est conservé dans le cookie « lang ».
/// </summary>
public sealed class Loc(IHttpContextAccessor accessor)
{
    public const string CookieName = "lang";
    public const string Default = "ar";
    public static readonly string[] Supported = ["ar", "fr"];

    public string Lang
    {
        get
        {
            var c = accessor.HttpContext?.Request.Cookies[CookieName];
            return c is not null && Supported.Contains(c) ? c : Default;
        }
    }

    public bool IsAr => Lang == "ar";
    public string Dir => IsAr ? "rtl" : "ltr";
    public string Other => IsAr ? "fr" : "ar";
    public string OtherLabel => IsAr ? "Français" : "العربية";

    public string this[string key] =>
        Strings.TryGetValue(key, out var t) ? (IsAr ? t.Ar : t.Fr) : key;

    public string F(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, this[key], args.Select(Arg).ToArray());

    private object? Arg(object? a) => a is int or long or double ? N(Convert.ToDouble(a)) : a;

    /// <summary>
    /// Nombre avec séparateur de milliers (chiffres occidentaux, usuels au Maroc).
    /// En arabe : virgule (« 6,214 »), car un espace couperait le nombre en deux dans un texte de droite à gauche.
    /// </summary>
    public string N(double v, string format = "N0") =>
        IsAr ? v.ToString(format, CultureInfo.InvariantCulture) : Num(v, format);

    public static string Num(double v, string format = "N0") =>
        v.ToString(format, Fr).Replace(' ', ' ').Replace(' ', ' ');

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Nom de sourate principal dans la langue courante.</summary>
    public string SurahName(Surah s) => IsAr ? s.NameAr : s.NameEn;
    public string SurahName(int number, string nameAr, string nameEn) => IsAr ? nameAr : nameEn;

    /// <summary>Libellé d'option de liste : « 2. البقرة » ou « 2. Al-Baqarah — البَقَرَة ».</summary>
    public string SurahOption(Surah s) => IsAr ? $"{s.Number}. {s.NameAr}" : $"{s.Number}. {s.NameEn} — {s.NameAr}";

    public string FormLabel(TextForm f) => this[f switch
    {
        TextForm.Normalized => "form.normalized",
        TextForm.NoDiacritics => "form.nodiacritics",
        _ => "form.original",
    }];

    public string FormShort(TextForm f) => this[f switch
    {
        TextForm.Normalized => "form.normalized.short",
        TextForm.NoDiacritics => "form.nodiacritics.short",
        _ => "form.original.short",
    }];

    public string MatchLabel(SearchMatch m) => this[m switch
    {
        SearchMatch.StartsWith => "match.startswith",
        SearchMatch.EndsWith => "match.endswith",
        SearchMatch.Contains => "match.contains",
        _ => "match.exact",
    }];

    /// <summary>Textes du lecteur audio pour le JavaScript (attribut data-i18n).</summary>
    public string PlayerJson() => ScriptJson("pl.");

    /// <summary>Textes d'un script : clés commençant par le préfixe, sans le préfixe.</summary>
    public string ScriptJson(string prefix) => JsonSerializer.Serialize(
        Strings.Where(kv => kv.Key.StartsWith(prefix)).ToDictionary(kv => kv.Key[prefix.Length..], kv => IsAr ? kv.Value.Ar : kv.Value.Fr));

    private static readonly Dictionary<string, (string Ar, string Fr)> Strings = new()
    {
        // ---------- Général ----------
        ["site.title"] = ("القرآن الكريم · ورش", "Coran Warsh"),
        ["site.brand"] = ("القرآن الكريم", "القرآن الكريم"),
        ["site.riwaya"] = ("رواية ورش عن نافع", "Riwayat Warsh ʿan Nāfiʿ"),
        ["nav.read"] = ("القراءة", "Lecture"),
        ["nav.search"] = ("البحث", "Recherche"),
        ["nav.stats"] = ("الإحصاءات", "Statistiques"),
        ["footer.text"] = ("النص: مجمع الملك فهد، رواية ورش عن نافع (الإصدار 2.1) · خط مجمع الملك فهد", "Texte : KFGQPC, riwayat Warsh ʿan Nāfiʿ (v2.1) · Police KFGQPC Warsh"),
        ["footer.source"] = ("الشيفرة المصدرية", "Code source"),
        ["lang.switch"] = ("تغيير اللغة", "Changer de langue"),
        ["prev"] = ("› السابق", "‹ Précédent"),
        ["next"] = ("التالي ‹", "Suivant ›"),
        ["surah"] = ("السورة", "Sourate"),
        ["juz"] = ("الجزء", "Juz"),
        ["hizb"] = ("الحزب", "Hizb"),
        ["page"] = ("الصفحة", "Page"),
        ["verse"] = ("الآية", "Verset"),
        ["whole"] = ("القرآن كاملًا", "Tout le Coran"),
        ["scope"] = ("النطاق", "Portée"),
        ["error.title"] = ("حدث خطأ", "Une erreur est survenue"),
        ["error.text"] = ("تعذّر عرض الصفحة المطلوبة.", "La page demandée n'a pas pu être affichée."),
        ["error.back"] = ("العودة إلى القراءة", "Retour à la lecture"),

        // ---------- Lecture ----------
        ["read.choose"] = ("اختيار المقطع", "Choisir le passage"),
        ["read.ok"] = ("عرض", "OK"),
        ["read.go"] = ("انتقال", "Aller"),
        ["read.goto.placeholder"] = ("مثال: 2:253", "ex. 2:253"),
        ["read.listen"] = ("▶ استماع", "▶ Écouter"),
        ["read.listen.title"] = ("الاستماع ابتداءً من أول آية معروضة", "Écouter depuis le premier verset affiché"),
        ["read.view.mushaf"] = ("عرض المصحف", "Vue mushaf"),
        ["read.view.list"] = ("عرض القائمة", "Vue liste"),
        ["read.size"] = ("حجم الخط", "Taille du texte"),
        ["read.smaller"] = ("تصغير الخط", "Réduire le texte"),
        ["read.bigger"] = ("تكبير الخط", "Agrandir le texte"),
        ["read.heading.surah"] = ("سورة {0}", "Sourate {1} — {0}"),
        ["read.sub.surah"] = ("عدد الآيات: {0} · الصفحات {1}–{2}", "{0} versets · pages {1}–{2}"),
        ["read.sub.range"] = ("عدد الآيات: {0} · من {1} إلى {2}", "{0} versets · de {1} à {2}"),
        ["read.surah.meta"] = ("{0} · {1} · عدد الآيات: {2}", "{0} · {1} · {2} versets"),
        ["read.cont"] = ("(تتمة)", "(suite)"),
        ["read.verse.title"] = ("{0} · الجزء {1} · الحزب {2} · الصفحة {3}", "{0} · Juz {1} · Hizb {2} · Page {3}"),
        ["read.sajda"] = ("سجدة", "Sajda"),
        ["read.listen.verse"] = ("الاستماع ابتداءً من الآية {0}", "Écouter à partir du verset {0}"),
        ["read.err.verse"] = ("الآية {0} غير موجودة (عدد آيات السورة {1} في رواية ورش: {2}).", "Le verset {0} n'existe pas (la sourate {1} compte {2} versets en Warsh)."),
        ["read.err.ref"] = ("المرجع «{0}» غير صالح. مثال: 2:253.", "Référence « {0} » invalide. Exemple : 2:253."),

        // ---------- Lecteur audio (repris par player.js) ----------
        ["pl.region"] = ("مشغّل الصوت", "Lecteur audio"),
        ["pl.reciter"] = ("القارئ", "Récitateur"),
        ["pl.prevVerse"] = ("الآية السابقة", "Verset précédent"),
        ["pl.nextVerse"] = ("الآية التالية", "Verset suivant"),
        ["pl.playPause"] = ("تشغيل / إيقاف مؤقت", "Lecture / pause"),
        ["pl.play"] = ("تشغيل", "Lecture"),
        ["pl.pause"] = ("إيقاف مؤقت", "Pause"),
        ["pl.repeat"] = ("تكرار الآية", "Répéter le verset"),
        ["pl.speed"] = ("السرعة", "Vitesse"),
        ["pl.hint"] = ("اضغط على رقم آية للاستماع", "Touchez un numéro de verset pour écouter"),
        ["pl.loading"] = ("جارٍ تحميل السورة {0}…", "Chargement de la sourate {0}…"),
        ["pl.unavailable"] = ("الصوت غير متاح: {0}", "Audio indisponible : {0}"),
        ["pl.cannotPlay"] = ("تعذّر التشغيل: {0}", "Lecture impossible : {0}"),
        ["pl.fileError"] = ("ملف الصوت غير موجود أو انقطع الاتصال.", "Fichier audio introuvable ou connexion interrompue."),
        ["pl.end"] = ("نهاية المقطع المعروض. استعمل «التالي» للمتابعة.", "Fin du passage affiché. Utilisez « Suivant › » pour continuer."),
        ["pl.reciterSet"] = ("القارئ: {0}", "Récitateur : {0}"),

        // ---------- Recherche ----------
        ["search.title"] = ("البحث", "Recherche"),
        ["search.placeholder"] = ("مثال: الله، رحم، بسم الله", "ex. الله, رحم, بسم الله"),
        ["search.aria"] = ("كلمة أو عبارة", "Mot ou expression"),
        ["search.submit"] = ("ابحث", "Rechercher"),
        ["search.match"] = ("نوع المطابقة", "Correspondance"),
        ["search.form"] = ("شكل النص", "Forme du texte"),
        ["search.hint"] = (
            "تُحوَّل كلمة البحث إلى شكل النص نفسه: «{0}» تطابق «{1}». عدة كلمات = عبارة (كلمات متتالية). يجري البحث في الرسم لا في الجذور: استعمل «تبدأ بـ» أو «تحتوي على» للبحث عن عائلة من الكلمات.",
            "La requête est ramenée à la même forme que le texte : « {0} » trouve « {1} ». Plusieurs mots = expression (mots consécutifs). La recherche porte sur l'écriture, pas sur les racines : utilisez « Commence par » ou « Contient » pour une famille de mots."),
        ["search.occurrences"] = ("مرات الورود", "occurrences"),
        ["search.verses"] = ("آيات", "versets"),
        ["search.surahs"] = ("سور", "sourates"),
        ["search.searchedForm"] = ("الشكل المبحوث عنه", "forme cherchée"),
        ["search.limited"] = ("البحث مقتصر على سورة {0} ({1}).", "Limité à la sourate {1} ({0})."),
        ["search.everywhere"] = ("ابحث في القرآن كاملًا", "Chercher dans tout le Coran"),
        ["search.none"] = ("لا نتائج. جرّب «تبدأ بـ» أو «تحتوي على»، أو الشكل «موحَّد».", "Aucun résultat. Essayez « Commence par » ou « Contient », ou la forme « Normalisé »."),
        ["search.range"] = ("النتائج {0}–{1} من {2}", "Occurrences {0}–{1} sur {2}"),
        ["search.page"] = ("صفحة {0} / {1}", "Page {0} / {1}"),
        ["search.noLetters"] = ("لا تحتوي كلمة البحث على حروف عربية.", "La requête ne contient aucune lettre arabe."),
        ["search.chart.title"] = ("الورود حسب السورة", "Occurrences par sourate"),
        ["search.chart.sub"] = ("{0} سورة من 114 · اضغط على عمود لحصر البحث فيها", "{0} sourates sur 114 · cliquer une barre pour filtrer"),
        ["search.chart.tip"] = ("{0}. {1}: {2}", "{0}. {1} : {2} occurrence(s)"),
        ["match.exact"] = ("كلمة مطابقة", "Mot exact"),
        ["match.startswith"] = ("تبدأ بـ", "Commence par"),
        ["match.endswith"] = ("تنتهي بـ", "Se termine par"),
        ["match.contains"] = ("تحتوي على", "Contient"),
        ["form.normalized"] = ("موحَّد (توحيد الألفات والياءات)", "Normalisé (alifs et yā' unifiés)"),
        ["form.nodiacritics"] = ("دون تشكيل", "Sans diacritiques"),
        ["form.original"] = ("النص كما هو (بالتشكيل)", "Texte exact (avec diacritiques)"),
        ["form.normalized.short"] = ("موحَّدة", "normalisés"),
        ["form.nodiacritics.short"] = ("دون تشكيل", "sans diacritiques"),
        ["form.original.short"] = ("كما في النص", "texte exact"),

        // ---------- Récitation (تسميع) ----------
        ["nav.recite"] = ("التسميع", "Récitation"),
        ["rec.title"] = ("التسميع", "Récitation corrigée"),
        ["rec.intro"] = (
            "اختر المقطع ثم اضغط «ابدأ» واقرأ من حفظك: النص مخفي، وتظهر كل كلمة عند قراءتها صحيحة، وتُعلَّم الأخطاء والكلمات المتروكة.",
            "Choisissez le passage, appuyez sur « Commencer » et récitez de mémoire : le texte est masqué, chaque mot apparaît quand il est bien récité, les erreurs et oublis sont signalés."),
        ["rec.from"] = ("من الآية", "Du verset"),
        ["rec.to"] = ("إلى الآية", "au verset"),
        ["rec.show"] = ("عرض", "Afficher"),
        ["rec.engine"] = ("محرك التعرف", "Reconnaissance"),
        ["rec.engine.browser"] = ("المتصفح (يحتاج إلى الإنترنت)", "Navigateur (Internet requis)"),
        ["rec.engine.whisper"] = ("Whisper المحلي (دون إنترنت)", "Whisper local (hors ligne)"),
        ["rec.start"] = ("🎙 ابدأ", "🎙 Commencer"),
        ["rec.stop"] = ("⏹ توقف", "⏹ Arrêter"),
        ["rec.hint"] = ("تلميح", "Indice"),
        ["rec.hint.title"] = ("إظهار الكلمة التالية (تُحسب تلميحًا)", "Dévoiler le mot suivant (compté comme indice)"),
        ["rec.peek"] = ("إظهار النص", "Voir le texte"),
        ["rec.restart"] = ("إعادة", "Recommencer"),
        ["rec.progress"] = ("التقدم", "Progression"),
        ["rec.ok"] = ("صحيحة", "correctes"),
        ["rec.wrong"] = ("خاطئة", "fausses"),
        ["rec.missed"] = ("متروكة", "oubliées"),
        ["rec.hints"] = ("تلميحات", "indices"),
        ["rec.heard"] = ("المسموع", "Entendu"),
        ["rec.results"] = ("النتيجة", "Résultat"),
        ["rec.score"] = ("نسبة الصواب", "Taux de réussite"),
        ["rec.mistakes"] = ("المواضع التي تحتاج إلى مراجعة", "Passages à revoir"),
        ["rec.noMistakes"] = ("ما شاء الله، لا أخطاء.", "Aucune erreur, bravo."),
        ["rec.col.verse"] = ("الآية", "Verset"),
        ["rec.col.expected"] = ("الصواب", "Attendu"),
        ["rec.col.heard"] = ("المسموع", "Entendu"),
        ["rec.col.type"] = ("النوع", "Type"),
        ["rec.listen"] = ("استماع", "Écouter"),
        ["rec.legend"] = ("صحيحة · خاطئة · متروكة · تلميح", "correct · faux · oublié · indice"),
        ["rec.whisper.missing"] = ("خادم Whisper غير مشغَّل على هذا الحاسوب (راجع tools/whisper-server). سيُستعمل المتصفح.", "Le serveur Whisper n'est pas lancé sur ce PC (voir tools/whisper-server). Le navigateur sera utilisé."),
        ["rec.note"] = (
            "التعرف الآلي على الصوت ليس معصومًا: قد يُعلَّم خطأ لم يقع. هذه أداة مساعدة على المراجعة ولا تغني عن التلقي من شيخ.",
            "La reconnaissance vocale n'est pas infaillible : une erreur peut être signalée à tort. C'est une aide à la révision, qui ne remplace pas un enseignant."),
        // Textes du script (attribut data-i18n)
        ["js.listening"] = ("جارٍ الاستماع… اقرأ الآن", "À l'écoute… récitez"),
        ["js.stopped"] = ("متوقف", "Arrêté"),
        ["js.done"] = ("انتهى المقطع", "Passage terminé"),
        ["js.noSupport"] = ("هذا المتصفح لا يدعم التعرف على الصوت. استعمل Chrome أو Edge، أو خادم Whisper المحلي.", "Ce navigateur ne gère pas la reconnaissance vocale. Utilisez Chrome ou Edge, ou le serveur Whisper local."),
        ["js.micDenied"] = ("لم يُسمح باستعمال الميكروفون.", "Accès au micro refusé."),
        ["js.error"] = ("خطأ: {0}", "Erreur : {0}"),
        ["js.processing"] = ("جارٍ التحليل…", "Analyse…"),
        ["js.type.wrong"] = ("خطأ", "faux"),
        ["js.type.missed"] = ("متروكة", "oublié"),
        ["js.type.hint"] = ("تلميح", "indice"),

        // ---------- Statistiques ----------
        ["stats.title"] = ("إحصاءات القرآن الكريم", "Statistiques du Coran"),
        ["stats.title.surah"] = ("إحصاءات سورة {0}", "Statistiques — sourate {1} ({0})"),
        ["stats.subtitle"] = ("رواية ورش عن نافع · عدّ نافع (البسملة غير معدودة آية)", "Riwayat Warsh ʿan Nāfiʿ · comptage de Nāfiʿ (basmala non comptée)"),
        ["stats.readSurah"] = ("قراءة السورة", "Lire la sourate"),
        ["stats.surahs"] = ("سور", "sourates"),
        ["stats.verses"] = ("آيات", "versets"),
        ["stats.words"] = ("كلمات", "mots"),
        ["stats.letters"] = ("حروف", "lettres"),
        ["stats.distinct"] = ("كلمات مختلفة ({0})", "mots distincts ({0})"),
        ["stats.divisions"] = ("أجزاء · أحزاب · صفحات", "juz · hizb · pages"),
        ["stats.wordsPerVerse"] = ("كلمات لكل آية", "mots par verset"),
        ["stats.pagesJuz"] = ("الصفحات (الجزء {0})", "pages (juz {0})"),
        ["stats.chart.versesBySurah"] = ("الآيات حسب السورة", "Versets par sourate"),
        ["stats.chart.versesBySurah.sub"] = ("اضغط على عمود لعرض إحصاءات السورة", "Cliquer une barre pour les statistiques de la sourate"),
        ["stats.chart.versesBySurah.tip"] = ("{0}. {1}: {2} آية، {3} كلمة", "{0}. {1} : {2} versets, {3} mots"),
        ["stats.chart.wordsByJuz"] = ("الكلمات حسب الجزء", "Mots par juz"),
        ["stats.chart.wordsByJuz.tip"] = ("الجزء {0} ({1} ← {2}): {3} كلمة، {4} آية", "Juz {0} ({1} → {2}) : {3} mots, {4} versets"),
        ["stats.chart.wordsByHizb"] = ("الكلمات حسب الحزب", "Mots par hizb"),
        ["stats.chart.wordsByHizb.tip"] = ("الحزب {0} ({1} ← {2}): {3} كلمة، {4} آية", "Hizb {0} ({1} → {2}) : {3} mots, {4} versets"),
        ["stats.chart.wordsByVerse"] = ("الكلمات حسب الآية", "Mots par verset"),
        ["stats.chart.wordsByVerse.tip"] = ("{0}: {1} كلمة، {2} حرف", "{0} : {1} mots, {2} lettres"),
        ["stats.chart.letters"] = ("تواتر الحروف", "Fréquence des lettres"),
        ["stats.chart.letters.sub"] = ("{0} حرف · دون التشكيل", "{0} lettres · diacritiques exclus"),
        ["stats.topWords"] = ("الكلمات الأكثر ورودًا", "Mots les plus fréquents"),
        ["stats.form"] = ("الشكل", "Forme"),
        ["stats.minLetters"] = ("أدنى عدد حروف", "Lettres min."),
        ["stats.count"] = ("العدد", "Nombre"),
        ["stats.wordsCounted"] = ("{0} كلمة معدودة · {1} مختلفة", "{0} mots comptés · {1} distincts"),
        ["stats.col.rank"] = ("الترتيب", "Rang"),
        ["stats.col.word"] = ("الكلمة", "Mot"),
        ["stats.col.occ"] = ("مرات الورود", "Occurrences"),
        ["stats.extremes"] = ("الأطول والأقصر", "Extrêmes"),
        ["stats.longestSurah"] = ("أطول سورة", "Sourate la plus longue"),
        ["stats.shortestSurah"] = ("أقصر سورة", "Sourate la plus courte"),
        ["stats.longestVerse"] = ("أطول آية", "Verset le plus long"),
        ["stats.shortestVerse"] = ("أقصر آية", "Verset le plus court"),
        ["stats.nVerses"] = ("عدد الآيات: {0}", "{0} versets"),
        ["stats.nWords"] = ("عدد الكلمات: {0}", "{0} mots"),
        ["stats.nLetters"] = ("عدد الحروف: {0}", "{0} lettres"),
        ["stats.bySurah"] = ("حسب السورة", "Par sourate"),
        ["stats.sortHint"] = ("اضغط على عنوان عمود للترتيب.", "Cliquer un en-tête pour trier."),
        ["stats.col.no"] = ("الرقم", "N°"),
        ["stats.col.name"] = ("السورة", "Sourate"),
        ["stats.col.name2"] = ("الاسم اللاتيني", "الاسم"),
        ["stats.col.verses"] = ("الآيات", "Versets"),
        ["stats.col.words"] = ("الكلمات", "Mots"),
        ["stats.col.letters"] = ("الحروف", "Lettres"),
        ["stats.col.wpv"] = ("كلمات/آية", "Mots/verset"),
        ["stats.col.pages"] = ("الصفحات", "Pages"),
        ["stats.exports"] = ("تصدير", "Exports"),
        ["stats.exp.surahs"] = ("السور (CSV)", "Sourates (CSV)"),
        ["stats.exp.juz"] = ("الأجزاء (CSV)", "Juz (CSV)"),
        ["stats.exp.hizb"] = ("الأحزاب (CSV)", "Hizb (CSV)"),
        ["stats.exp.pages"] = ("الصفحات (CSV)", "Pages (CSV)"),
        ["stats.exp.letters"] = ("الحروف (CSV)", "Lettres (CSV)"),
        ["stats.exp.words"] = ("الكلمات (CSV)", "Mots (CSV)"),
    };
}
