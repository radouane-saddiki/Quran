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
| `data/warsh_hizb.json` | Début de chacun des 60 hizb (division maghrébine) |
| `data/warsh_lines.json` | Découpage en lignes des 604 pages du mushaf (généré par `tools/mushaf-layout/layout.py`) |
| `reports/` | Statistiques générées (Markdown, CSV, JSON) |
| `src/`, `tools/` | Application .NET (voir ci-dessous) |

## Application .NET de statistiques

Solution **.NET 8** (`Quran.sln`), sans aucun paquet NuGet externe (fonctionne aussi avec un SDK plus récent) :

| Projet | Rôle |
|---|---|
| `src/Quran.Core` | Bibliothèque : chargement du texte (embarqué dans l'assembly), normalisation de l'arabe, comptages, fréquences, recherche |
| `src/Quran.Api` | API web ASP.NET Core (minimal API) qui expose ces calculs en JSON |
| `src/Quran.Web` | Site web (Razor Pages) : lecture du Coran, recherche, statistiques et graphiques |
| `tools/Quran.Reports` | Outil console qui génère les rapports du dossier [`reports/`](reports/README.md) |

### Lancer le site web en local

```bash
dotnet run --project src/Quran.Web
# puis ouvrir http://localhost:5090/
```

Dans Visual Studio 2022 : ouvrir `Quran.sln`, définir `Quran.Web` comme projet de démarrage, puis F5.

Interface bilingue : **arabe par défaut** (de droite à gauche) et français, au choix via le lien en haut à droite
(le choix est mémorisé dans un cookie). Les textes de l'interface sont dans `src/Quran.Web/Localization/Loc.cs`.

| Page | Contenu |
|---|---|
| **Lecture** (`/`) | Affichage du texte par sourate, juz, hizb ou page du mushaf ; accès direct à un verset (`2:253`) ; **vue mushaf** (pages de 15 lignes comme le mushaf imprimé, deux pages côte à côte sur grand écran, flèches ← → pour tourner les pages) ou vue liste ; taille réglable |
| **Recherche** (`/Recherche`) | Mot ou expression, mode (exact, commence par, se termine par, contient), forme du texte, limitation à une sourate ; répartition par sourate, résultats surlignés, pagination |
| **Statistiques** (`/Statistiques`) | Chiffres clés, graphiques (versets par sourate, mots par juz et par hizb, fréquence des lettres), mots les plus fréquents, extrêmes, tableau triable des sourates, statistiques d'une sourate, exports CSV |

#### Audio synchronisé

La page Lecture comporte un lecteur : **Omar Al-Qazabri** (عمر القزابري) et **Laayoun El Kouchi** (العيون الكوشي), riwayat Warsh.
Toucher un numéro de verset lance la récitation à partir de ce verset ; le verset récité est surligné et suivi à l'écran.
Boutons verset précédent/suivant, répétition d'un verset, vitesse, barre d'espace = lecture/pause.
En vue juz, hizb ou page, la lecture enchaîne les sourates du passage affiché.

- Par défaut, les fichiers audio sont lus sur les serveurs de [MP3Quran](https://mp3quran.net) (ils ne sont pas dans le dépôt : plusieurs Go).
- Les minutages (début et fin de chaque verset, numérotation Warsh) viennent de l'API MP3Quran
  (`ayat_timing`, récitations n° 80 et 16). Ils sont téléchargés au premier usage de chaque sourate puis conservés dans
  `src/Quran.Web/App_Data/timings/`.
- **Tout télécharger sur le PC** (MP3 + minutages, écoute hors ligne ensuite) :
  `dotnet run --project src/Quran.Web -- --telecharger-audio`
  (ou `--telecharger-audio koshi` pour un seul récitateur). Les MP3 vont dans `src/Quran.Web/wwwroot/audio/<récitateur>/`,
  le site les utilise automatiquement quand ils sont présents. La commande reprend là où elle s'est arrêtée si on la relance.
- Minutages seuls (avec contrôle du nombre de versets) : `dotnet run --project src/Quran.Web -- --telecharger-minutages`
- Récitateurs et adresses se règlent dans `src/Quran.Web/appsettings.json` (section `Audio`).

Le texte est affiché avec la police KFGQPC Warsh (`src/Quran.Web/wwwroot/fonts`), incluse : lecture, recherche et statistiques fonctionnent sans connexion Internet.

#### Récitation corrigée (التسميع)

Page **التسميع / Récitation** : choisir une sourate et une plage de versets, appuyer sur « ابدأ » et réciter de mémoire.
Le texte est masqué ; chaque mot apparaît quand il est bien récité, les mots **faux** (rouge) et **oubliés** (orange)
sont signalés, « تلميح » dévoile le mot suivant. À l'arrêt : taux de réussite et liste des passages à revoir,
avec un lien pour écouter le verset.

Deux moteurs de reconnaissance :

| Moteur | Installation | Remarques |
|---|---|---|
| **Navigateur** (par défaut) | aucune | Chrome ou Edge ; reconnaissance vocale arabe de Google/Microsoft, connexion Internet nécessaire ; non spécialisée Coran |
| **Whisper local** | Python 3.10+ | modèles Whisper sur votre PC, hors ligne ; avec une carte NVIDIA, le grand modèle `turbo` est choisi automatiquement |

Lancer le serveur Whisper (Windows, dans un second terminal, à laisser ouvert) :

```powershell
powershell -ExecutionPolicy Bypass -File tools\whisper-server\start.ps1
```

Le premier lancement crée `tools/whisper-server/.venv` et installe PyTorch et Transformers. Si une carte **NVIDIA**
est présente (commande `nvidia-smi`), le script installe la version CUDA de PyTorch pour utiliser la carte graphique.
Le modèle est téléchargé au premier usage, puis fonctionne hors ligne. Le site détecte le serveur
(`http://127.0.0.1:5095`, réglable dans `appsettings.json`, section `Recitation`) et affiche le modèle et GPU/CPU
dans la liste des moteurs.

| `--model` | Modèle | Taille | Remarques |
|---|---|---|---|
| `base` | [tarteel-ai/whisper-base-ar-quran](https://huggingface.co/tarteel-ai/whisper-base-ar-quran) | 74 M, ~300 Mo | affiné Coran ; défaut sans carte graphique |
| `tiny` | [tarteel-ai/whisper-tiny-ar-quran](https://huggingface.co/tarteel-ai/whisper-tiny-ar-quran) | 39 M | plus léger, moins précis |
| `turbo` | [openai/whisper-large-v3-turbo](https://huggingface.co/openai/whisper-large-v3-turbo) | 809 M, ~1,6 Go | multilingue, robuste ; défaut avec carte NVIDIA (~2 Go de mémoire graphique) |
| `large` | [openai/whisper-large-v3](https://huggingface.co/openai/whisper-large-v3) | 1,55 G, ~3 Go | le plus précis, plus lent (~4 Go de mémoire graphique) |

Exemple : `powershell -ExecutionPolicy Bypass -File tools\whisper-server\start.ps1 --model large`.
Tous ces modèles sont gratuits (licences Apache 2.0 ou MIT).

La correction compare les mots après normalisation (sans voyelles, alifs et hamzas gommés) pour tolérer les écarts
d'orthographe entre le texte Warsh et l'arabe standard que renvoie la reconnaissance (ex. « مالك » entendu = « مَلِكِ » écrit).
Elle porte sur les **mots**, pas sur la prononciation fine (tajwīd, madd, imāla…) : c'est une aide à la mémorisation,
qui peut se tromper.

### Lancer l'API en local

Prérequis : SDK .NET 8 (`dotnet --list-sdks`).

```bash
git clone https://github.com/radouane-saddiki/Quran.git
cd Quran
dotnet run --project src/Quran.Api
# puis ouvrir http://localhost:5080/  (liste des endpoints)
```

Ou ouvrir `Quran.sln` dans Visual Studio 2022, définir `Quran.Api` comme projet de démarrage et lancer (F5).

Pour y accéder depuis un téléphone sur le même Wi-Fi : `dotnet run --project src/Quran.Api --urls http://0.0.0.0:5080`,
autoriser le port 5080 dans le pare-feu Windows, puis ouvrir `http://<IP-du-PC>:5080/api/summary` (IP donnée par `ipconfig`).

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
- **Hizb** : les données KFGQPC ne donnent que le juz et la page. Les débuts des 60 hizb (`data/warsh_hizb.json`) ont été
  déduits des signes ۞ du texte Warsh (qui marquent les huitièmes de hizb) : le 1ᵉʳ hizb d'un juz commence avec le juz,
  le 2ᵉ au 9ᵉ huitième. Chaque début a été vérifié (signe ۞ ou début de sourate). Ex. : hizb 2 = 2:75 « أَفَتَطْمَعُونَ ».
- Numérotation de Nāfiʿ : par exemple, l'Āyat al-Kursī (2:255 en Hafs) correspond en Warsh aux versets **2:253–2:254**, la phrase « اَ۬للَّهُ لَآ إِلَٰهَ إِلَّا هُوَ اَ۬لْحَيُّ اُ۬لْقَيُّومُ » formant un verset à part.

## Affichage

Le texte utilise les signes spécifiques à Warsh (ex. `۬`) et encode les numéros de verset avec des
caractères de la plage U+FC00–U+FDFF (`ﰀ` = 1, `ﰁ` = 2…), que seule la police KFGQPC Warsh affiche comme numéros :
<https://cdn.jsdelivr.net/gh/thetruetruth/quran-data-kfgqpc@main/warsh/font/warsh.10.woff2>

### Mise en page du mushaf

Les données KFGQPC donnent la page et les lignes de début et de fin de chaque verset, mais pas l'endroit où un verset
long passe à la ligne. `tools/mushaf-layout/layout.py` mesure la largeur réelle de chaque mot avec la police du mushaf
(HarfBuzz) et choisit les coupures qui donnent des lignes régulières, en respectant exactement les lignes de chaque
verset. Les coupures sont donc très proches du mushaf imprimé, sans être garanties mot pour mot.

```bash
pip install uharfbuzz fonttools brotli
python tools/mushaf-layout/layout.py   # régénère data/warsh_lines.json (~1 min 30)
```

## Source

- Audio et minutages : [MP3Quran.net](https://mp3quran.net) — [Omar Al-Qazabri (Warsh)](https://www.mp3quran.net/eng/omar_warsh), [Laayoun El Kouchi (Warsh)](https://www.mp3quran.net/ar/koshi).
- Police : KFGQPC Warsh Uthmanic (Complexe du Roi Fahd), via [thetruetruth/quran-data-kfgqpc](https://github.com/thetruetruth/quran-data-kfgqpc).
- Données : [quran-center/quran-meta](https://github.com/quran-center/quran-meta) (licence MIT, voir `LICENSE-source-quran-meta`),
  fichier `examples/data-check/data/warshData_v2-1.json`, lui-même issu du KFGQPC (qurancomplex.gov.sa).
