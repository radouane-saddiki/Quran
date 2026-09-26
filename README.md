# Quran — Riwayat Warsh ʿan Nāfiʿ

Texte intégral du Coran selon la riwaya de **Warsh ʿan Nāfiʿ** (رواية ورش عن نافع), issu des données
du Complexe du Roi Fahd pour l'impression du Coran (KFGQPC), police *Warsh Uthmanic* v2.1.

- **6 214 versets**, 114 sourates (comptage madanite de Nāfiʿ, différent des 6 236 versets de Hafs)
- Ex. : al-Baqara = 285 versets, at-Tawba = 130 versets

## Fichiers

| Fichier | Contenu |
|---|---|
| `data/warsh_kfgqpc_v2-1.json` | Données d'origine KFGQPC : id, juz, page, sourate, n° de verset, lignes, texte |
| `data/warsh.txt` | Une ligne par verset : `sourate|verset|texte` |
| `data/warsh.csv` | CSV UTF-8 (avec BOM, s'ouvre directement dans Excel) |
| `data/suras_warsh.json` | Index des 114 sourates avec leur nombre de versets en Warsh |

## Affichage

Le texte utilise les signes spécifiques à Warsh (ex. `۬`) et les numéros de verset encodés en caractères
de la zone privée Unicode (`ﰀ`, `ﰁ`…). Pour un rendu correct, utilisez la police KFGQPC Warsh :
<https://cdn.jsdelivr.net/gh/thetruetruth/quran-data-kfgqpc@main/warsh/font/warsh.10.woff2>

## Source

- Données : [quran-center/quran-meta](https://github.com/quran-center/quran-meta) (licence MIT, voir `LICENSE-source-quran-meta`),
  fichier `examples/data-check/data/warshData_v2-1.json`, lui-même issu du KFGQPC (qurancomplex.gov.sa).
