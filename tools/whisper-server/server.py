"""
Serveur local de reconnaissance de la récitation (mode تسميع du site Quran.Web).

Modèles (téléchargés depuis Hugging Face au premier usage, puis utilisables hors ligne) :
    base   tarteel-ai/whisper-base-ar-quran   74 M paramètres, affiné Coran (défaut sans carte graphique)
    tiny   tarteel-ai/whisper-tiny-ar-quran   39 M, encore plus léger
    turbo  openai/whisper-large-v3-turbo      809 M, multilingue, rapide sur GPU (défaut avec carte NVIDIA)
    large  openai/whisper-large-v3            1,55 G, le plus précis, ~4 Go de mémoire graphique
Tous sous licence libre (Apache 2.0 ou MIT).

    python server.py                    # choix automatique selon la présence d'un GPU
    python server.py --model large      # alias ci-dessus ou nom complet Hugging Face

Routes :
    GET  /health       -> {"ok": true, "model": "...", "device": "..."}
    POST /transcribe   corps = WAV mono PCM 16 bits  -> {"text": "..."}
"""
import argparse
import io
import json
import threading
import time
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np

MAX_BYTES = 8 * 1024 * 1024
SAMPLE_RATE = 16000

MODELS = {
    "tiny": "tarteel-ai/whisper-tiny-ar-quran",
    "base": "tarteel-ai/whisper-base-ar-quran",
    "turbo": "openai/whisper-large-v3-turbo",
    "large": "openai/whisper-large-v3",
}


def read_wav(data: bytes) -> np.ndarray:
    """WAV PCM 16 bits -> float32 mono 16 kHz."""
    with wave.open(io.BytesIO(data), "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        frames = w.readframes(w.getnframes())
    if width != 2:
        raise ValueError("WAV 16 bits attendu")
    audio = np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0
    if channels > 1:
        audio = audio.reshape(-1, channels).mean(axis=1)
    if rate != SAMPLE_RATE and len(audio):
        # Rééchantillonnage linéaire, suffisant pour la parole.
        n = int(round(len(audio) * SAMPLE_RATE / rate))
        audio = np.interp(np.linspace(0, len(audio) - 1, n), np.arange(len(audio)), audio).astype(np.float32)
    return audio


class Transcriber:
    def __init__(self, model: str, device: str):
        import torch
        from transformers import pipeline
        from transformers.utils import logging as hf_logging

        hf_logging.set_verbosity_error()  # masque les avertissements sans conséquence (dépréciations…)

        if device == "auto":
            device = "cuda:0" if torch.cuda.is_available() else "cpu"
        gpu = device.startswith("cuda")
        if model == "auto":
            model = "turbo" if gpu else "base"
        model = MODELS.get(model, model)
        self.model, self.device = model, device
        self.lock = threading.Lock()  # le modèle n'est pas prévu pour des appels simultanés

        if gpu:
            print(f"Carte graphique : {torch.cuda.get_device_name(0)}", flush=True)
        elif model.startswith("openai/whisper-large"):
            print("Attention : grand modèle sans carte graphique, chaque phrase prendra plusieurs secondes.", flush=True)
        print(f"Chargement de {model} sur {device}…", flush=True)

        # Demi-précision sur GPU récent : deux fois moins de mémoire, plus rapide, même qualité.
        # Les cartes Pascal (GTX 10xx, génération 6.x) calculent très lentement en demi-précision : pleine précision.
        half = gpu and torch.cuda.get_device_capability(0)[0] >= 7
        dtype = torch.float16 if half else torch.float32
        if gpu and not half and model == MODELS["large"]:
            print("Attention : « large » en pleine précision demande ~6 Go de mémoire graphique ; préférez « turbo ».", flush=True)
        try:
            self.pipe = pipeline("automatic-speech-recognition", model=model, device=device, dtype=dtype)
        except TypeError:  # anciennes versions de Transformers
            self.pipe = pipeline("automatic-speech-recognition", model=model, device=device, torch_dtype=dtype)

        # Langue et tâche : les modèles Whisper récents les acceptent en paramètre ; les modèles affinés
        # plus anciens (dont tarteel-ai/whisper-*-ar-quran) ont une configuration de génération « ancienne »
        # (sans lang_to_id) qui fixe déjà l'arabe : leur passer language= provoque une erreur.
        gen = self.pipe.model.generation_config
        if getattr(gen, "lang_to_id", None):
            self.generate_kwargs = {"language": "arabic", "task": "transcribe"}
        else:
            self.generate_kwargs = {}
        print(f"Options de génération : {self.generate_kwargs or 'celles du modèle'}", flush=True)

        # Premier appel court (1 s de son de test, 8 jetons max) : charge tout en mémoire et vérifie que tout fonctionne.
        print("Test du modèle (quelques secondes)…", flush=True)
        t0 = time.perf_counter()
        tone = (0.1 * np.sin(2 * np.pi * 220 * np.arange(SAMPLE_RATE) / SAMPLE_RATE)).astype(np.float32)
        self.transcribe(tone, max_new_tokens=8)
        print(f"Modèle prêt ({time.perf_counter() - t0:.1f} s pour le test).", flush=True)

    def transcribe(self, audio: np.ndarray, max_new_tokens: int | None = None) -> str:
        if len(audio) < SAMPLE_RATE // 4:
            return ""
        # Silence : rien à transcrire (Whisper a tendance à « inventer » du texte sur du silence).
        if float(np.sqrt(np.mean(audio ** 2))) < 0.003:
            return ""
        # Longueur maximale proportionnelle à la durée : une récitation dépasse rarement ~12 jetons/s.
        # Évite qu'une hallucination (texte répété) ne tourne jusqu'à 440 jetons, très lent sur CPU.
        seconds = len(audio) / SAMPLE_RATE
        limit = max_new_tokens or int(min(440, 24 + 14 * seconds))
        with self.lock:
            try:
                out = self._run(audio, {**self.generate_kwargs, "max_new_tokens": limit})
            except (ValueError, TypeError):
                if not self.generate_kwargs:
                    raise
                # Option de langue refusée par ce modèle : on s'en passe désormais.
                self.generate_kwargs = {}
                out = self._run(audio, {"max_new_tokens": limit})
        return (out.get("text") or "").strip()

    def _run(self, audio: np.ndarray, generate_kwargs: dict) -> dict:
        # Dictionnaire neuf à chaque appel : le pipeline le modifie (il en retire la clé « raw »).
        inputs = {"raw": audio, "sampling_rate": SAMPLE_RATE}
        return self.pipe(inputs, generate_kwargs=generate_kwargs)


def make_handler(engine: Transcriber):
    class Handler(BaseHTTPRequestHandler):
        def _json(self, status: int, payload: dict):
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path == "/health":
                self._json(200, {"ok": True, "model": engine.model, "device": engine.device})
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self):
            if self.path != "/transcribe":
                self._json(404, {"error": "not found"})
                return
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0 or length > MAX_BYTES:
                self._json(400, {"error": "audio absent ou trop long"})
                return
            try:
                audio = read_wav(self.rfile.read(length))
                t0 = time.perf_counter()
                text = engine.transcribe(audio)
                ms = int((time.perf_counter() - t0) * 1000)
                self._json(200, {"text": text, "seconds": round(len(audio) / SAMPLE_RATE, 2), "ms": ms})
            except (ValueError, wave.Error, EOFError) as e:
                self._json(400, {"error": f"WAV invalide : {e}"})
            except Exception as e:  # noqa: BLE001 - l'erreur est renvoyée au site
                self._json(500, {"error": str(e)})

        def log_message(self, fmt, *args):
            pass  # silencieux ; les erreurs sont renvoyées en JSON

    return Handler


def main():
    ap = argparse.ArgumentParser(description="Serveur Whisper pour le mode تسميع")
    ap.add_argument("--model", default="auto", help="auto, tiny, base, turbo, large ou nom Hugging Face")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5095)
    ap.add_argument("--device", default="auto", help="auto, cpu ou cuda:0")
    args = ap.parse_args()

    engine = Transcriber(args.model, args.device)
    server = ThreadingHTTPServer((args.host, args.port), make_handler(engine))
    print(f"À l'écoute sur http://{args.host}:{args.port}  (Ctrl+C pour arrêter)", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
