# Contexte du projet Quran (Warsh ʿan Nāfiʿ)

Dépôt GitHub `radouane-saddiki/Quran`, cloné sur le PC de Radouane dans `E:\MesProjets\Quran\Quran`.
Propriétaire : Radouane, consultant .NET, francophone : **répondre en français**.
Le site est en **arabe par défaut** (RTL), français en option.

## Environnement de Radouane
- Windows, **.NET 8** (SDK 8) : tous les projets ciblent `net8.0`, solution classique `Quran.sln`, aucun paquet NuGet.
- Visual Studio 2022 ou `dotnet run`. PowerShell 5.1 par défaut (scripts `.ps1` en UTF-8 **avec BOM**, CRLF).
- Carte graphique GeForce GTX 1050 Ti (Pascal, capacité 6.1, 4 Go), pilote 581.80 (CUDA 13.0).
- Python 3.14 installé (pour le serveur Whisper facultatif).

## Structure
| Chemin | Rôle |
|---|---|
| `data/warsh_kfgqpc_v2-1.json` | Texte KFGQPC Warsh v2.1 : 6 214 versets (décompte de Nāfiʿ), page, lignes début/fin, juz |
| `data/warsh_hizb.json` | Début des 60 hizb (repères ۞) |
| `data/warsh_lines.json` | Découpage en lignes des 604 pages du mushaf (généré par `tools/mushaf-layout/layout.py`) |
| `src/Quran.Core` | Chargement du texte (`QuranCorpus`), normalisation arabe (`ArabicText`), statistiques, recherche, `MushafLayout` |
| `src/Quran.Api` | API minimale ASP.NET Core |
| `src/Quran.Web` | Site Razor Pages : Lecture, التسميع (récitation), Recherche, Statistiques |
| `tools/Quran.Reports` | Génère `reports/` (Markdown, CSV, JSON) |
| `tools/whisper-server` | Serveur Python Whisper local facultatif (`start.ps1`) |
| `tools/mushaf-layout` | Calcul des coupures de lignes du mushaf (HarfBuzz + programmation dynamique) |

Les JSON de `data/` sont embarqués dans `Quran.Core` (EmbeddedResource).

## Points techniques à connaître
- Numéros de verset : glyphes U+FC00… (`ﰀ` = 1), affichés seulement par la police `wwwroot/fonts/warsh.10.woff2`.
- `Verse.Text` = texte sans numéro ; `Verse.Words` = mots (sans ۞) sous 3 formes ; `Verse.DisplayWords` = texte découpé sur les espaces (base de `warsh_lines.json`).
- Traductions de l'interface : `src/Quran.Web/Localization/Loc.cs` (dictionnaire ar/fr, cookie de langue). En arabe, séparateur de milliers = virgule.
- Lecture, vue mushaf : pages de 15 lignes (8 pour les pages 1 et 2), taille de police calculée pour qu'une ligne pleine remplisse la page (`--mw`), versets hors sélection estompés (`.aya.out`). Un verset sur plusieurs lignes = plusieurs `.aya` de même `data-s`/`data-v` (regroupés dans `player.js`).
- Audio : récitateurs Omar Al-Qazabri (MP3Quran read 80) et Laayoun El Kouchi (read 16). MP3 locaux dans `wwwroot/audio/<id>/NNN.mp3` (non versionnés, ~plusieurs Go, téléchargés par `--telecharger-audio`), minutages en cache dans `App_Data/timings`.
- Récitation (التسميع) : moteur du navigateur (Web Speech, ar-SA) **préféré par Radouane** ; Whisper local testé (turbo sur GPU) mais moins bon. Alignement tolérant dans `wwwroot/js/tasmee-align.js`.

## Commandes
```powershell
dotnet run --project src/Quran.Web                      # site : http://localhost:5090
dotnet run --project src/Quran.Api                      # API
dotnet run --project tools/Quran.Reports                # régénère reports/
dotnet run --project src/Quran.Web -- --telecharger-audio   # MP3 + minutages en local
powershell -ExecutionPolicy Bypass -File tools\whisper-server\start.ps1
```

## Conventions
- Code, commentaires, messages de commit et textes en **français** ; interface en arabe + français.
- Toute nouvelle chaîne d'interface passe par `Loc.cs` (les deux langues).
- Vérifier l'affichage en arabe (RTL) et sur mobile (~390 px).
- Ne pas versionner les MP3 ni `tools/whisper-server/.venv`.
