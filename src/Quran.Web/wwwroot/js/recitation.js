// Mode تسميع : écoute de la récitation, alignement sur le texte masqué, correction mot à mot.
(() => {
    const box = document.getElementById("rec-text");
    if (!box) return;

    let T = {};
    try { T = JSON.parse(box.dataset.i18n || "{}"); } catch { }
    const tr = (k, ...a) => (T[k] ?? k).replace(/\{(\d+)\}/g, (_, i) => a[+i] ?? "");

    const words = JSON.parse(box.dataset.words || "[]");          // [{s, v, n, o}]
    const spans = Array.from(box.querySelectorAll(".w"));
    const $ = id => document.getElementById(id);
    const btnStart = $("rec-start"), btnHint = $("rec-hint"), btnPeek = $("rec-peek"), btnRestart = $("rec-restart");
    const engineSel = $("rec-engine"), stateEl = $("rec-state"), heardEl = $("rec-heard");
    const surahSel = $("rec-surah");
    const labelStart = btnStart.textContent;

    const store = {
        get(k) { try { return localStorage.getItem(k); } catch { return null; } },
        set(k, v) { try { localStorage.setItem(k, v); } catch { } },
    };

    // Changer de sourate repart du verset 1.
    surahSel?.addEventListener("change", () => {
        const f = surahSel.form;
        f.elements.from.value = 1;
        f.elements.to.value = "";
        f.submit();
    });

    // ---------- État de la correction ----------
    let aligner, counts, heardAt, engine = null, processed;

    function reset() {
        counts = { ok: 0, wrong: 0, missed: 0, hint: 0 };
        heardAt = {};
        processed = new Map();
        spans.forEach(s => s.classList.remove("ok", "wrong", "missed", "hint", "cur"));
        aligner = new TasmeeAlign.Aligner(words.map(w => w.n), mark);
        $("rec-summary").hidden = true;
        heardEl.textContent = "";
        updateMeter();
    }

    function mark(i, state, heard) {
        const el = spans[i];
        if (!el) return;
        el.classList.remove("cur");
        el.classList.add(state);
        counts[state]++;
        if (heard && state !== "ok") heardAt[i] = heard;
        if (state !== "ok") el.title = heard ? `${tr("type." + state)} — ${heard}` : tr("type." + state);
        updateMeter();
    }

    function updateMeter() {
        const done = aligner ? aligner.pos : 0;
        const pct = words.length ? Math.round(100 * done / words.length) : 0;
        $("rec-bar").style.width = pct + "%";
        $("rec-bar").parentElement.setAttribute("aria-valuenow", pct);
        for (const k of ["ok", "wrong", "missed", "hint"]) $("n-" + k).textContent = counts[k];
        spans.forEach(s => s.classList.remove("cur"));
        if (aligner && !aligner.done()) {
            const cur = spans[aligner.pos];
            cur.classList.add("cur");
            keepVisible(cur);
        }
        if (aligner && aligner.done() && engine) finish(tr("done"));
    }

    function keepVisible(el) {
        const r = el.getBoundingClientRect();
        if (r.top < 90 || r.bottom > window.innerHeight - 40) el.scrollIntoView({ block: "center", behavior: "smooth" });
    }

    // Texte reconnu → mots nouveaux → alignement.
    // stablePrefix : les mots d'un résultat provisoire (sauf le dernier) sont considérés comme acquis.
    function onResult(index, text, isFinal, stablePrefix) {
        heardEl.textContent = text;
        const ws = text.split(/\s+/).filter(Boolean);
        const stable = isFinal ? ws.length : (stablePrefix ? Math.max(0, ws.length - 1) : 0);
        const from = processed.get(index) || 0;
        for (let k = from; k < stable; k++) aligner.push(ws[k]);
        if (stable > from) processed.set(index, stable);
        updateMeter();
    }

    // ---------- Moteur 1 : reconnaissance vocale du navigateur ----------
    function BrowserEngine() {
        const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SR) throw new Error(tr("noSupport"));
        let rec, active = false, offset = 0, maxIndex = -1;
        this.start = () => {
            active = true;
            const run = () => {
                rec = new SR();
                rec.lang = "ar-SA";
                rec.continuous = true;
                rec.interimResults = true;
                rec.maxAlternatives = 1;
                rec.onresult = e => {
                    for (let i = e.resultIndex; i < e.results.length; i++) {
                        const r = e.results[i];
                        maxIndex = Math.max(maxIndex, offset + i);
                        onResult(offset + i, r[0].transcript, r.isFinal, true);
                    }
                };
                rec.onerror = e => {
                    if (e.error === "not-allowed" || e.error === "service-not-allowed") { setState(tr("micDenied")); stop(); }
                    else if (e.error !== "no-speech" && e.error !== "aborted") setState(tr("error", e.error));
                };
                // Chrome coupe la session au bout d'un moment : on relance tant que l'utilisateur n'a pas arrêté.
                rec.onend = () => { if (active) { offset = maxIndex + 1; run(); } };
                rec.start();
            };
            run();
        };
        this.stop = () => { active = false; try { rec && rec.stop(); } catch { } };
    }

    // ---------- Moteur 2 : Whisper local (via le site, /recitation/transcribe) ----------
    function WhisperEngine() {
        const RATE = 16000;
        let ctx, stream, node, source, active = false;
        let utter = [], speaking = false, silenceMs = 0, noise = 0.005, index = 0, lastSent = 0, busy = false;
        const queue = [];

        this.start = async () => {
            stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } });
            ctx = new (window.AudioContext || window.webkitAudioContext)();
            source = ctx.createMediaStreamSource(stream);
            node = ctx.createScriptProcessor(4096, 1, 1);
            const ratio = ctx.sampleRate / RATE;
            node.onaudioprocess = e => {
                if (!active) return;
                const input = e.inputBuffer.getChannelData(0);
                // Sous-échantillonnage simple vers 16 kHz.
                const out = new Float32Array(Math.floor(input.length / ratio));
                let rms = 0;
                for (let i = 0; i < out.length; i++) {
                    const v = input[Math.floor(i * ratio)];
                    out[i] = v; rms += v * v;
                }
                rms = Math.sqrt(rms / Math.max(1, out.length));
                const ms = 1000 * input.length / ctx.sampleRate;
                const loud = rms > Math.max(0.012, noise * 3);
                if (!speaking && !loud) noise = noise * 0.95 + rms * 0.05;   // bruit de fond adaptatif
                if (loud) { speaking = true; silenceMs = 0; }
                if (speaking) {
                    utter.push(out);
                    if (!loud) silenceMs += ms;
                    const dur = utter.reduce((a, b) => a + b.length, 0) / RATE;
                    // Fin de phrase : 0,8 s de silence, ou 15 s de parole continue.
                    if (silenceMs > 800 || dur > 15) flush(true);
                    else if (dur - lastSent > 2.5 && !busy) { lastSent = dur; send(concat(utter), index, false); }
                }
            };
            source.connect(node); node.connect(ctx.destination);
            active = true;
        };

        this.stop = () => {
            if (speaking) flush(true);
            active = false;
            try { node && node.disconnect(); source && source.disconnect(); } catch { }
            stream && stream.getTracks().forEach(t => t.stop());
            ctx && ctx.close();
        };

        function flush(final) {
            const audio = concat(utter);
            utter = []; speaking = false; silenceMs = 0; lastSent = 0;
            if (audio.length > RATE * 0.3) queue.push({ audio, idx: index, final });
            index++;
            pump();
        }

        function send(audio, idx, final) { queue.push({ audio, idx, final }); pump(); }

        async function pump() {
            if (busy || !queue.length) return;
            // Les résultats provisoires périmés sont abandonnés au profit du final.
            while (queue.length > 1 && !queue[0].final && queue[1].idx === queue[0].idx) queue.shift();
            const job = queue.shift();
            busy = true;
            setState(tr("processing"));
            try {
                const res = await fetch("/recitation/transcribe", { method: "POST", headers: { "Content-Type": "audio/wav" }, body: wav(job.audio) });
                const data = await res.json();
                if (!res.ok) throw new Error(data.error || res.status);
                // Whisper retranscrit toute la phrase à chaque fois : seuls les résultats finaux font avancer.
                onResult(job.idx, data.text || "", job.final, false);
                if (engine) setState(tr("listening"));
            } catch (e) {
                setState(tr("error", e.message));
            } finally {
                busy = false;
                pump();
            }
        }

        function concat(chunks) {
            const n = chunks.reduce((a, b) => a + b.length, 0);
            const out = new Float32Array(n);
            let o = 0;
            for (const c of chunks) { out.set(c, o); o += c.length; }
            return out;
        }

        function wav(samples) {
            const buf = new ArrayBuffer(44 + samples.length * 2);
            const v = new DataView(buf);
            const w = (o, s) => { for (let i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)); };
            w(0, "RIFF"); v.setUint32(4, 36 + samples.length * 2, true); w(8, "WAVE");
            w(12, "fmt "); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 1, true);
            v.setUint32(24, RATE, true); v.setUint32(28, RATE * 2, true); v.setUint16(32, 2, true); v.setUint16(34, 16, true);
            w(36, "data"); v.setUint32(40, samples.length * 2, true);
            for (let i = 0; i < samples.length; i++) {
                const s = Math.max(-1, Math.min(1, samples[i]));
                v.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
            }
            return new Blob([buf], { type: "audio/wav" });
        }
    }

    // ---------- Commandes ----------
    function setState(t) { stateEl.textContent = t; }

    async function start() {
        if (aligner.done()) reset();
        try {
            engine = engineSel.value === "whisper" ? new WhisperEngine() : new BrowserEngine();
            await engine.start();
        } catch (e) {
            engine = null;
            setState(e && e.name === "NotAllowedError" ? tr("micDenied") : (e.message || String(e)));
            return;
        }
        btnStart.textContent = btnStart.dataset.stop || "⏹";
        btnStart.classList.add("recording");
        setState(tr("listening"));
        updateMeter();
    }

    function stop() {
        if (engine) { engine.stop(); engine = null; }
        btnStart.textContent = labelStart;
        btnStart.classList.remove("recording");
        setState(tr("stopped"));
        showSummary();
    }

    function finish(msg) {
        stop();
        setState(msg);
    }

    function showSummary() {
        const done = aligner.pos;
        if (!done) return;
        const good = counts.ok;
        $("s-score").textContent = Math.round(100 * good / done) + "%";
        for (const k of ["ok", "wrong", "missed", "hint"]) $("s-" + k).textContent = counts[k];
        const tbody = $("s-table").tBodies[0];
        tbody.innerHTML = "";
        const tpl = $("s-listen");
        aligner.states.forEach((st, i) => {
            if (!st || st === "ok") return;
            const w = words[i];
            const tr_ = tbody.insertRow();
            const ref = `${w.s}:${w.v}`;
            tr_.insertCell().textContent = ref;
            const c1 = tr_.insertCell(); c1.textContent = w.o; c1.className = "quran";
            const c2 = tr_.insertCell(); c2.textContent = heardAt[i] || "—"; c2.className = "ar";
            tr_.insertCell().textContent = tr("type." + st);
            const a = tpl.content.firstElementChild.cloneNode(true);
            a.href = `/?mode=sourate&n=${w.s}&hl=${ref}#v${w.s}-${w.v}`;
            tr_.insertCell().appendChild(a);
        });
        $("s-none").hidden = tbody.rows.length > 0;
        $("s-table").hidden = tbody.rows.length === 0;
        $("rec-summary").hidden = false;
    }

    btnStart.addEventListener("click", () => engine ? stop() : start());
    btnHint.addEventListener("click", () => { aligner.hint(); updateMeter(); });
    btnRestart.addEventListener("click", () => { if (engine) stop(); reset(); setState("—"); });
    btnPeek.addEventListener("click", () => {
        const on = box.classList.toggle("peek");
        btnPeek.setAttribute("aria-pressed", String(on));
    });

    // Moteur mémorisé ; Whisper proposé seulement s'il répond.
    const savedEngine = store.get("quran.recEngine");
    if (savedEngine) engineSel.value = savedEngine;
    engineSel.addEventListener("change", () => store.set("quran.recEngine", engineSel.value));
    fetch("/recitation/whisper").then(r => r.json()).then(d => {
        const opt = engineSel.querySelector('option[value="whisper"]');
        if (!d.available) {
            opt.disabled = true;
            if (engineSel.value === "whisper") engineSel.value = "browser";
        } else if (!savedEngine) {
            engineSel.value = "whisper";   // plus précis sur le Coran quand il est disponible
        }
    }).catch(() => { });

    reset();
    // Exposé pour les tests automatisés.
    window.__tasmee = { onResult, get aligner() { return aligner; }, counts: () => counts, showSummary };
})();
