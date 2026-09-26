// Lecteur audio synchronisé verset par verset.
// Le fichier audio d'une sourate vient du serveur du récitateur ; les minutages (début/fin de chaque
// verset en ms) viennent de /audio/timings/{récitateur}/{sourate}.
(() => {
    const bar = document.getElementById("player");
    if (!bar) return;

    const audio = document.getElementById("pl-audio");
    const reciterSel = document.getElementById("pl-reciter");
    const btnPlay = document.getElementById("pl-play");
    const btnPrev = document.getElementById("pl-prev");
    const btnNext = document.getElementById("pl-next");
    const btnRepeat = document.getElementById("pl-repeat");
    const speedSel = document.getElementById("pl-speed");
    const status = document.getElementById("pl-status");
    const listenBtn = document.getElementById("listen-btn");

    // Textes traduits fournis par le serveur (arabe ou français).
    let T = {};
    try { T = JSON.parse(bar.dataset.i18n || "{}"); } catch { }
    const tr = (k, ...a) => (T[k] ?? k).replace(/\{(\d+)\}/g, (_, i) => a[+i] ?? "");

    const verses = Array.from(document.querySelectorAll(".aya[data-s]"));
    if (verses.length === 0) return;

    const store = {
        get(k) { try { return localStorage.getItem(k); } catch { return null; } },
        set(k, v) { try { localStorage.setItem(k, v); } catch { } },
    };
    const saved = store.get("quran.reciter");
    if (saved && [...reciterSel.options].some(o => o.value === saved)) reciterSel.value = saved;

    let loaded = null;        // { reciter, surah, timings: Map(ayah -> {start,end}), ordered: [] }
    let index = -1;           // verset en cours (indice dans verses)
    let repeat = false;
    let loading = false;
    let seekTarget = null;    // position à atteindre une fois les métadonnées chargées

    const key = el => `${el.dataset.s}:${el.dataset.v}`;
    const reciterName = () => reciterSel.options[reciterSel.selectedIndex].text.split(" — ")[0];

    function setStatus(text) { status.textContent = text; }

    function setPlaying(on) {
        btnPlay.textContent = on ? "⏸" : "▶";
        btnPlay.setAttribute("aria-label", on ? tr("pause") : tr("play"));
    }

    function markVerse(i, scroll) {
        verses.forEach(v => v.classList.remove("playing"));
        index = i;
        if (i < 0) return;
        const el = verses[i];
        el.classList.add("playing");
        setStatus(`${key(el)} · ${reciterName()}`);
        if (scroll) {
            const r = el.getBoundingClientRect();
            const barH = bar.offsetHeight;
            if (r.top < 70 || r.bottom > window.innerHeight - barH - 10)
                el.scrollIntoView({ block: "center", behavior: "smooth" });
        }
    }

    async function loadSurah(surah) {
        const reciter = reciterSel.value;
        if (loaded && loaded.reciter === reciter && loaded.surah === surah) return loaded;
        loading = true;
        setStatus(tr("loading", surah));
        try {
            const res = await fetch(`/audio/timings/${encodeURIComponent(reciter)}/${surah}`);
            const data = await res.json();
            if (!res.ok) throw new Error(data.error || `Erreur ${res.status}`);
            const map = new Map(data.timings.map(t => [t.ayah, t]));
            loaded = { reciter, surah, timings: map, ordered: data.timings };
            audio.src = data.audio;
            audio.playbackRate = parseFloat(speedSel.value);
            return loaded;
        } catch (e) {
            loaded = null;
            setStatus(tr("unavailable", e.message));
            throw e;
        } finally {
            loading = false;
        }
    }

    async function playVerse(i) {
        if (i < 0 || i >= verses.length) return;
        const el = verses[i];
        const surah = parseInt(el.dataset.s, 10);
        const ayah = parseInt(el.dataset.v, 10);
        bar.hidden = false;
        try { await loadSurah(surah); } catch { return; }
        const t = loaded.timings.get(ayah);
        // Le verset 1 démarre au début du fichier pour inclure l'isti'âdha et la basmala.
        const start = ayah === 1 || !t ? 0 : t.start / 1000;
        markVerse(i, true);
        if (audio.readyState >= 1) audio.currentTime = start; else seekTarget = start;
        try { await audio.play(); } catch (e) { setStatus(tr("cannotPlay", e.message)); }
    }

    audio.addEventListener("loadedmetadata", () => {
        if (seekTarget !== null) { audio.currentTime = seekTarget; seekTarget = null; }
    });
    audio.addEventListener("play", () => setPlaying(true));
    audio.addEventListener("pause", () => setPlaying(false));
    audio.addEventListener("error", () => {
        if (audio.src) setStatus(tr("fileError"));
        setPlaying(false);
    });

    audio.addEventListener("timeupdate", () => {
        if (!loaded || loading || index < 0) return;
        const ms = audio.currentTime * 1000;
        const cur = verses[index];
        const curAyah = parseInt(cur.dataset.v, 10);
        const t = loaded.timings.get(curAyah);

        if (repeat && t && ms >= t.end - 60) {
            audio.currentTime = curAyah === 1 ? 0 : t.start / 1000;
            return;
        }

        // Verset effectivement récité à cet instant.
        const playing = loaded.ordered.find(x => ms >= x.start && ms < x.end);
        if (!playing || playing.ayah === curAyah) return;

        const j = verses.findIndex(v => parseInt(v.dataset.s, 10) === loaded.surah && parseInt(v.dataset.v, 10) === playing.ayah);
        if (j >= 0) {
            markVerse(j, true);
        } else if (playing.ayah > curAyah) {
            // On a dépassé le dernier verset affiché de cette sourate : verset suivant de la page, ou arrêt.
            goNextFrom(index);
        }
    });

    audio.addEventListener("ended", () => {
        if (repeat && index >= 0) { playVerse(index); return; }
        goNextFrom(index);
    });

    function goNextFrom(i) {
        if (i + 1 < verses.length) {
            playVerse(i + 1);
        } else {
            audio.pause();
            setStatus(tr("end"));
        }
    }

    // Clic sur un numéro de verset : lecture à partir de ce verset.
    verses.forEach((el, i) => {
        const num = el.querySelector(".num");
        if (!num) return;
        const go = e => { e.preventDefault(); playVerse(i); };
        num.addEventListener("click", go);
        num.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") go(e); });
    });

    listenBtn?.addEventListener("click", () => {
        const hl = verses.findIndex(v => v.classList.contains("hl"));
        playVerse(hl >= 0 ? hl : 0);
    });

    btnPlay.addEventListener("click", () => {
        if (index < 0) { playVerse(0); return; }
        if (audio.paused) audio.play(); else audio.pause();
    });
    btnPrev.addEventListener("click", () => playVerse(Math.max(0, index - 1)));
    btnNext.addEventListener("click", () => { if (index + 1 < verses.length) playVerse(index + 1); });
    btnRepeat.addEventListener("click", () => {
        repeat = !repeat;
        btnRepeat.setAttribute("aria-pressed", String(repeat));
    });
    speedSel.addEventListener("change", () => { audio.playbackRate = parseFloat(speedSel.value); });
    reciterSel.addEventListener("change", () => {
        store.set("quran.reciter", reciterSel.value);
        const wasPlaying = !audio.paused;
        const i = index;
        audio.pause();
        loaded = null;
        if (i >= 0 && wasPlaying) playVerse(i);
        else setStatus(tr("reciterSet", reciterName()));
    });

    // Barre d'espace = lecture/pause (hors champs de saisie).
    document.addEventListener("keydown", e => {
        if (e.code !== "Space" || e.target.closest("input, select, textarea, button, [role=button]")) return;
        if (index < 0) return;
        e.preventDefault();
        if (audio.paused) audio.play(); else audio.pause();
    });

    bar.hidden = false;
})();
