// Soumission automatique des listes déroulantes marquées data-autosubmit.
document.querySelectorAll("select[data-autosubmit]").forEach(sel => {
    sel.addEventListener("change", () => { if (sel.value !== "" || sel.name === "surah") sel.form.submit(); });
});

// Taille du texte coranique (mémorisée localement si possible).
(() => {
    const root = document.documentElement;
    const key = "quran.qsize";
    const clamp = v => Math.min(3.2, Math.max(1.2, v));
    let size = null;
    try { size = parseFloat(localStorage.getItem(key)); } catch { }
    if (size) root.style.setProperty("--qsize", size + "rem");
    document.querySelectorAll("[data-font]").forEach(btn => {
        btn.addEventListener("click", () => {
            const current = parseFloat(getComputedStyle(root).getPropertyValue("--qsize")) || 1.9;
            const next = clamp(current + 0.2 * parseInt(btn.dataset.font, 10));
            root.style.setProperty("--qsize", next.toFixed(1) + "rem");
            try { localStorage.setItem(key, next.toFixed(1)); } catch { }
        });
    });
})();

// Tri des tableaux .sortable (valeur data-v si présente, sinon texte).
document.querySelectorAll("table.sortable").forEach(table => {
    const headers = table.querySelectorAll("thead th");
    headers.forEach((th, col) => {
        th.addEventListener("click", () => {
            const asc = th.getAttribute("aria-sort") !== "ascending";
            headers.forEach(h => h.removeAttribute("aria-sort"));
            th.setAttribute("aria-sort", asc ? "ascending" : "descending");
            const body = table.tBodies[0];
            const rows = Array.from(body.rows);
            const val = r => {
                const c = r.cells[col];
                const v = c.dataset.v ?? c.textContent.trim();
                const n = parseFloat(v);
                return isNaN(n) ? v : n;
            };
            rows.sort((a, b) => {
                const x = val(a), y = val(b);
                const cmp = typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), "ar");
                return asc ? cmp : -cmp;
            });
            rows.forEach(r => body.appendChild(r));
        });
    });
});

// Défilement vers le verset mis en évidence.
(() => {
    const hl = document.querySelector(".aya.hl");
    if (hl && !location.hash) hl.scrollIntoView({ block: "center" });
})();
