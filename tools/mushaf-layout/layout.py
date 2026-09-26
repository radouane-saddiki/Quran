"""
Répartition des mots du mushaf Warsh (KFGQPC v2.1) en lignes, page par page.

Les données KFGQPC donnent, pour chaque verset, sa page et ses lignes de début et de fin (15 lignes par page),
mais pas l'endroit où un verset long passe à la ligne suivante. Ce script retrouve ces coupures :
les largeurs réelles des mots sont mesurées avec la police du mushaf (HarfBuzz), puis une programmation
dynamique choisit, pour chaque verset sur plusieurs lignes, les coupures qui donnent les lignes les plus
régulières (largeur la plus proche de celle d'une ligne pleine), en respectant exactement les lignes
de début et de fin de chaque verset.

    pip install uharfbuzz fonttools brotli
    python tools/mushaf-layout/layout.py      # écrit data/warsh_lines.json

Format de sortie :
    {"width": <largeur d'une ligne pleine, en em>,
     "lines": [[page, ligne, largeur_em, [[id_verset, premier_mot, dernier_mot], ...]], ...]}
Les indices de mots se rapportent au texte du verset (sans numéro) découpé sur les espaces.
"""
import io
import json
import statistics
from pathlib import Path

import uharfbuzz as hb
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "data" / "warsh_kfgqpc_v2-1.json"
FONT = ROOT / "src" / "Quran.Web" / "wwwroot" / "fonts" / "warsh.10.woff2"
OUT = ROOT / "data" / "warsh_lines.json"
LINES_PER_PAGE = 15


def display_tokens(aya_text: str) -> list[str]:
    """Identique à Verse.Text découpé sur les espaces (Quran.Core)."""
    text = "".join(c for c in aya_text if not (0xFC00 <= ord(c) <= 0xFDFF))
    text = text.replace(" ", " ").replace("‏", "").strip()
    return text.split()


class Measure:
    def __init__(self, path: Path):
        f = TTFont(str(path))
        f.flavor = None
        buf = io.BytesIO()
        f.save(buf)
        self.face = hb.Face(buf.getvalue())
        self.font = hb.Font(self.face)
        self.upem = self.face.upem
        self.cache: dict[str, float] = {}
        self.space = self.width(" ")

    def width(self, text: str) -> float:
        if text not in self.cache:
            b = hb.Buffer()
            b.add_str(text)
            b.guess_segment_properties()
            hb.shape(self.font, b, {})
            self.cache[text] = sum(p.x_advance for p in b.glyph_positions) / self.upem
        return self.cache[text]


def main():
    rows = json.loads(DATA.read_text(encoding="utf-8"))
    m = Measure(FONT)

    # Éléments insécables dans l'ordre du texte : un mot (le dernier porte le numéro du verset).
    items = []  # (verse_id, word_index, width)
    verses = []  # (id, first_item, last_item, G1, G2)
    for r in rows:
        toks = display_tokens(r["aya_text"])
        num = chr(0xFC00 + r["aya_no"] - 1)
        first = len(items)
        for i, t in enumerate(toks):
            w = m.width(t)
            if i == len(toks) - 1:
                w += m.space + m.width(num)
            items.append((r["id"], i, w))
        pages = [int(p) for p in str(r["page"]).split("-")]
        g1 = (pages[0] - 1) * LINES_PER_PAGE + r["line_start"]
        g2 = (pages[-1] - 1) * LINES_PER_PAGE + r["line_end"]
        assert g2 >= g1, r
        verses.append((r["id"], first, len(items) - 1, g1, g2))

    text_lines = sorted({g for (_, _, _, g1, g2) in verses for g in range(g1, g2 + 1)})
    nxt = {text_lines[i]: (text_lines[i + 1] if i + 1 < len(text_lines) else None) for i in range(len(text_lines))}

    # Fin de ligne imposée ou libre : pour chaque ligne, le verset qui continue sur la ligne suivante (s'il existe).
    ends_at = {}      # ligne -> dernier élément du dernier verset qui s'y termine
    spanning = {}     # ligne -> verset qui passe à la ligne suivante
    for v in verses:
        _, first, last, g1, g2 = v
        ends_at[g2] = max(ends_at.get(g2, -1), last)
        for g in text_lines_between(text_lines, g1, g2)[:-1]:
            spanning[g] = v

    prefix = [0.0]
    for _, _, w in items:
        prefix.append(prefix[-1] + w)

    def width(s, e):  # éléments s..e inclus, séparés par des espaces
        return prefix[e + 1] - prefix[s] + (e - s) * m.space

    # Largeur cible : médiane des lignes dont les deux extrémités sont imposées (hors pages 1 et 2).
    fixed = []
    pos = 0
    prev_fixed = True
    for g in text_lines:
        if g in spanning:
            prev_fixed = False
            continue
        e = ends_at[g]
        if prev_fixed and g > 2 * LINES_PER_PAGE:
            fixed.append(width(pos, e))
        pos = e + 1
        prev_fixed = True
    target = statistics.median(fixed)
    # Pages 1 et 2 (al-Fâtiha, début d'al-Baqara) : lignes courtes, centrées.
    target_small = target * 0.55

    def cost(s, e, g):
        w = width(s, e)
        t = target_small if g <= 2 * LINES_PER_PAGE else target
        d = w - t
        return d * d * (4 if d > 0 else 1)  # déborder oblige à réduire la police : plus pénalisé

    def remaining_lines(g, g2):
        return len(text_lines_between(text_lines, g, g2)) - 1  # lignes après g jusqu'à g2

    # Programmation dynamique ligne par ligne : état = dernier élément de la ligne.
    INF = float("inf")
    best = {-1: (0.0, None)}  # fin -> (coût, fin précédente)
    history = []
    for g in text_lines:
        cur = {}
        if g in spanning:
            _, vfirst, vlast, g1, g2 = spanning[g]
            lo = vfirst if g1 == g else 0
            hi = vlast - remaining_lines(g, g2)
            candidates = range(lo, hi + 1)
        else:
            candidates = [ends_at[g]]
        for e in candidates:
            b = (INF, None)
            for pe, (c, _) in best.items():
                s = pe + 1
                if s > e:
                    continue
                total = c + cost(s, e, g)
                if total < b[0]:
                    b = (total, pe)
            if b[0] < INF:
                cur[e] = b
        assert cur, f"aucune coupure possible à la ligne {g}"
        history.append((g, cur))
        best = cur

    # Remontée des choix.
    ends = {}
    e = min(best, key=lambda k: best[k][0])
    for g, table in reversed(history):
        ends[g] = e
        e = table[e][1]

    lines_out = []
    s = 0
    widths = []
    for g in text_lines:
        e = ends[g]
        parts = []
        for i in range(s, e + 1):
            vid, wi, _ = items[i]
            if parts and parts[-1][0] == vid:
                parts[-1][2] = wi
            else:
                parts.append([vid, wi, wi])
        w = width(s, e)
        widths.append(w / (target_small if g <= 2 * LINES_PER_PAGE else target))
        page, line = (g - 1) // LINES_PER_PAGE + 1, (g - 1) % LINES_PER_PAGE + 1
        lines_out.append([page, line, round(w, 3), parts])
        s = e + 1
    assert s == len(items)

    OUT.write_text(json.dumps({"width": round(target, 3), "lines": lines_out}, ensure_ascii=False, separators=(",", ":")),
                   encoding="utf-8")
    widths.sort()
    q = lambda p: widths[int(p * (len(widths) - 1))]
    print(f"{len(lines_out)} lignes, largeur cible {target:.2f} em ({len(fixed)} lignes de référence)")
    print(f"rapport largeur/cible : min {widths[0]:.2f}  p5 {q(.05):.2f}  médiane {q(.5):.2f}  p95 {q(.95):.2f}  max {widths[-1]:.2f}")
    print(f"écrit : {OUT.relative_to(ROOT)}")


def text_lines_between(text_lines, g1, g2):
    import bisect
    i = bisect.bisect_left(text_lines, g1)
    j = bisect.bisect_right(text_lines, g2)
    return text_lines[i:j]


if __name__ == "__main__":
    main()
