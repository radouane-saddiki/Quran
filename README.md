# Quran — Riwayat Warsh ʿan Nāfiʿ

Texte intégral du Coran selon la riwaya de **Warsh ʿan Nāfiʿ** (رواية ورش عن نافع), issu des données
du Complexe du Roi Fahd pour l'impression du Coran (KFGQPC), police *Warsh Uthmanic* v2.1,
et une application .NET qui calcule des statistiques sur ce texte.

- **6 214 versets**, 114 sourates (comptage madanite de Nāfiʿ, différent des 6 236 versets de Hafs)
- Ex. : al-Baqara = 285 versets, at-Tawba = 130 versets
- Statistiques complètes : [`reports/README.md`](reports/README.md)

## Fichiers

| Fichier | Contenu |
|---|---|
| `data/warsh_kfgqpc_v2-1.json` | Données d'origine KFGQPC : id, juz, page, sourate, n° de verset, lignes, texte |
| `data/warsh.txt` | Une ligne par verset : `sourate|verset|texte` |
| `data/warsh.csv` | CSV UTF-8 (avec BOM, s'ouvre directement dans Excel) |
| `data/suras_warsh.json` | Index des 114 sourates avec leur nombre de versets en Warsh |
| `reports/` | Statistiques générées (Markdown, CSV, JSON) |
| `src/`, `tools/` | Application .NET (voir ci-dessous) |

## Application .NET de statistiques

Solution .NET 10 (`Quran.slnx`), sans aucun paquet NuGet externe :

| Projet | Rôle |
|---|---|
| `src/Quran.Core` | Bibliothèque : chargement du texte (embarqué dans l'assembly), normalisation de l'arabe, comptages, fréquences, recherche |
| `src/Quran.Api` | API web ASP.NET Core (minimal API) qui expose ces calculs en JSON |
| `tools/Quran.Reports` | Outil console qui génère les rapports du dossier [`reports/`](reports/README.md) |

### Lancer l'API

```bash
dotnet run --project src/Quran.Api
# puis http://localhost:5080/  (liste des endpoints)
```

Exemples prêts à l'emploi dans `src/Quran.Api/Quran.Api.http` (Visual Studio, Rider ou VS Code REST Client).

| Endpoint | Description |
|---|---|
| `GET /api/summary` | Totaux : sourates, versets, mots, lettres, mots distincts |
| `GET /api/surahs` | Comptages par sourate |
| `GET /api/surahs/{n}` | Une sourate avec ses versets (`?includeVerses=false` pour les stats seules) |
| `GET /api/surahs/{s}/verses/{v}` | Un verset, mot par mot, sous les trois formes |
| `GET /api/juz`, `GET /api/pages` | Comptages par juz et par page |
| `GET /api/extremes` | Plus longues et plus courtes sourates et versets |
| `GET /api/frequencies/letters` | Fréquence des lettres (`form`, `surah`) |
| `GET /api/frequencies/words` | Fréquence des mots (`form`, `top`, `minLetters`, `surah`) |
| `GET /api/search?q=…` | Occurrences d'un mot ou d'une expression, avec positions (`match`, `form`, `surah`, `limit`) |

Paramètres :
- `form` : `Original` (texte imprimé), `NoDiacritics` (lettres seules), `Normalized` (lettres seules, alifs unifiés, `ى` et `ے` → `ي`).
  La requête est convertie dans la même forme : `q=الله` trouve `اَ۬للَّهُ`.
- `match` : `Exact`, `StartsWith`, `EndsWith`, `Contains`. Une requête de plusieurs mots cherche l'expression (mots consécutifs).

### Régénérer les rapports

```bash
dotnet run --project tools/Quran.Reports -- reports
```

### Définitions

- **Mot** : suite de caractères séparée par des espaces et contenant au moins une lettre arabe (les signes isolés comme ۞ ne comptent pas).
- **Lettre** : lettre arabe de base (ء à ي, plus le ے maghrébin) ; voyelles, shadda, sukūn, petites lettres, signes de pause et tatweel exclus.
- La recherche porte sur la forme écrite, pas sur les racines : `StartsWith` et `Contains` permettent d'approcher une famille de mots, mais une vraie recherche par racine demanderait des données morphologiques.
- Numérotation de Nāfiʿ : par exemple, l'Āyat al-Kursī (2:255 en Hafs) correspond en Warsh aux versets **2:253–2:254**, la phrase « اَ۬للَّهُ لَآ إِلَٰهَ إِلَّا هُوَ اَ۬لْحَيُّ اُ۬لْقَيُّومُ » formant un verset à part.

## Affichage

Le texte utilise les signes spécifiques à Warsh (ex. `۬`) et encode les numéros de verset avec des
caractères de la plage U+FC00–U+FDFF (`ﰀ` = 1, `ﰁ` = 2…), que seule la police KFGQPC Warsh affiche comme numéros :
<https://cdn.jsdelivr.net/gh/thetruetruth/quran-data-kfgqpc@main/warsh/font/warsh.10.woff2>

## Source

- Données : [quran-center/quran-meta](https://github.com/quran-center/quran-meta) (licence MIT, voir `LICENSE-source-quran-meta`),
  fichier `examples/data-check/data/warshData_v2-1.json`, lui-même issu du KFGQPC (qurancomplex.gov.sa).
