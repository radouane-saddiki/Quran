// Alignement de la récitation entendue sur le texte attendu (mode تسميع).
// Module sans dépendance : utilisable dans le navigateur (window.TasmeeAlign) et testable sous Node.
(function (root) {
    "use strict";

    // --- Normalisation -------------------------------------------------------------------------
    // Les mots attendus arrivent déjà « normalisés » par le serveur (lettres seules, alifs unifiés).
    // La reconnaissance vocale renvoie de l'arabe courant (souvent orthographe Hafs / standard).
    // La forme « lâche » gomme les différences d'orthographe du rasm : alifs, hamzas, tā' marbūṭa…
    const DIACRITICS = /[ؐ-ًؚ-ٰٟۖ-ۭـ]/g;

    function normalize(w) {
        return (w || "")
            .replace(DIACRITICS, "")
            .replace(/[آأإٱٲٳ]/g, "ا") // آ أ إ ٱ → ا
            .replace(/[ىےی]/g, "ي")                   // ى ے ی → ي
            .replace(/[^ء-ي]/g, "");                             // lettres arabes seules
    }

    function loose(w) {
        return normalize(w)
            .replace(/[اء]/g, "")   // ا ء supprimés (alifs de prolongation, hamza isolée)
            .replace(/ة/g, "ه")     // ة → ه
            .replace(/ؤ/g, "و")     // ؤ → و
            .replace(/ئ/g, "ي");    // ئ → ي
    }

    function lev(a, b) {
        if (a === b) return 0;
        const m = a.length, n = b.length;
        if (!m) return n; if (!n) return m;
        let prev = Array.from({ length: n + 1 }, (_, j) => j);
        for (let i = 1; i <= m; i++) {
            const cur = [i];
            for (let j = 1; j <= n; j++)
                cur[j] = Math.min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[n];
    }

    /** Vrai si le mot entendu correspond au mot attendu (tolérance selon la longueur). */
    function same(expectedLoose, heardLoose) {
        if (!expectedLoose || !heardLoose) return false;
        if (expectedLoose === heardLoose) return true;
        const len = Math.min(expectedLoose.length, heardLoose.length);
        const d = lev(expectedLoose, heardLoose);
        if (len >= 7) return d <= 2;
        if (len >= 4) return d <= 1;
        return false;
    }

    // Isti'âdha et basmala : ignorées tant que la récitation n'a pas commencé.
    // Comparaison sur la forme normalisée (et non « lâche ») : « لله » (de الحمد لله) ≠ « الله ».
    const PREAMBLE = new Set(["اعوذ", "بالله", "من", "الشيطان", "الرجيم", "بسم", "الله", "الرحمن", "الرحيم"].map(normalize));

    const LOOKAHEAD = 3;      // mots sautés tolérés avant de retrouver le fil
    const MAX_PENDING = 3;    // mots non reconnus avant de marquer le mot attendu comme faux

    /**
     * @param {string[]} expected  mots attendus (forme normalisée)
     * @param {(i:number, state:string, heard?:string)=>void} onMark  état : "ok" | "wrong" | "missed"
     */
    function Aligner(expected, onMark) {
        this.exp = expected.map(loose);
        this.len = expected.map(w => normalize(w).length);
        this.pos = 0;
        this.pending = [];
        this.started = false;
        this.onMark = onMark || (() => { });
        this.states = new Array(expected.length).fill(null);
    }

    Aligner.prototype.done = function () { return this.pos >= this.exp.length; };

    Aligner.prototype._mark = function (i, state, heard) {
        this.states[i] = state;
        this.onMark(i, state, heard);
    };

    /** Mot entendu → avance l'alignement. */
    Aligner.prototype.push = function (word) {
        const h = loose(word);
        if (!h || this.done()) return;
        const e = this.exp;
        const p = this.pos;
        const pend = this.pending;

        // 1. Mot attendu, seul ou reconstitué avec 1–2 fragments en attente (« يا » + « أيها » = « يٰأيها »).
        const cands = [h];
        if (pend.length >= 1) cands.push(pend[pend.length - 1].l + h);
        if (pend.length >= 2) cands.push(pend[pend.length - 2].l + pend[pend.length - 1].l + h);
        if (cands.some(c => same(e[p], c))) {
            this._accept(p, 1, word);
            return;
        }
        // 2. Le mot entendu couvre deux mots attendus.
        if (p + 1 < e.length && same(e[p] + e[p + 1], h)) {
            this._accept(p, 2, word);
            return;
        }
        // 3. Préambule (isti'âdha, basmala) avant le début : ignoré.
        if (!this.started && PREAMBLE.has(normalize(word))) return;

        // 4. Mots sautés : le mot entendu est un peu plus loin.
        for (let k = 1; k <= LOOKAHEAD && p + k < e.length; k++) {
            if (this.len[p + k] >= 3 && same(e[p + k], h)) {
                // Mots entendus en attente = mots remplacés (faux) ; sinon = oubliés.
                const state = pend.length && this.started ? "wrong" : "missed";
                for (let j = p; j < p + k; j++) this._mark(j, state);
                this._accept(p + k, 1, word);
                return;
            }
        }
        // 5. Répétition d'un mot déjà récité (l'utilisateur reprend) : ignorée.
        for (let k = 1; k <= 3 && p - k >= 0; k++) if (same(e[p - k], h)) return;

        // 6. Mot non reconnu : en attente.
        pend.push({ w: word, l: h });
        if (pend.length < MAX_PENDING) return;
        if (!this.started) { pend.shift(); return; }   // avant le début : on oublie les plus anciens

        // Trop de mots non reconnus : le mot attendu est faux ; les mots suivants sont réexaminés.
        const replay = pend.slice(1);
        this._mark(p, "wrong", pend.map(x => x.w).join(" "));
        this.pos++; this.pending = [];
        replay.forEach(x => this.push(x.w));
    };

    Aligner.prototype._accept = function (p, count, word) {
        for (let i = 0; i < count; i++) this._mark(p + i, "ok", word);
        this.pos = p + count; this.pending = []; this.started = true;
    };

    /** Aide : le mot courant est dévoilé et compté comme indice. */
    Aligner.prototype.hint = function () {
        if (this.done()) return;
        this._mark(this.pos, "hint");
        this.pos++; this.pending = []; this.started = true;
    };

    const api = { normalize, loose, lev, same, Aligner };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.TasmeeAlign = api;
})(typeof window !== "undefined" ? window : globalThis);
