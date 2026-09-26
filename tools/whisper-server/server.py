"""
Serveur local de reconnaissance de la récitation (mode تسميع du site Quran.Web).

Modèle par défaut : tarteel-ai/whisper-base-ar-quran (Whisper affiné sur des récitations du Coran).
Téléchargé automatiquement depuis Hugging Face au premier lancement, puis utilisable hors ligne.

    python server.py                       # http://127.0.0.1:5095
    python server.py --model tarteel-ai/whisper-tiny-ar-quran   # plus léger, moins précis

Routes :
    GET  /health       -> {"ok": true, "model": "..."}
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

        if device == "auto":
            device = "cuda:0" if torch.cuda.is_available() else "cpu"
        self.model = model
        self.lock = threading.Lock()  # le modèle n'est pas prévu pour des appels simultanés
        print(f"Chargement de {model} sur {device}…", flush=True)
        self.pipe = pipeline("automatic-speech-recognition", model=model, device=device)

        # Langue et tâche : les modèles Whisper récents les acceptent en paramètre ; les modèles affinés
        # plus anciens (dont tarteel-ai/whisper-*-ar-quran) ont une configuration de génération « ancienne »
        # (sans lang_to_id) qui fixe déjà l'arabe : leur passer language= provoque une erreur.
        gen = self.pipe.model.generation_config
        if getattr(gen, "lang_to_id", None):
            self.generate_kwargs = {"language": "arabic", "task": "transcribe"}
        else:
            self.generate_kwargs = {}
        print(f"Options de génération : {self.generate_kwargs or 'celles du modèle'}", flush=True)

        # Premier appel « à vide » : charge tout en mémoire et vérifie que la transcription fonctionne.
        self.transcribe(np.zeros(SAMPLE_RATE // 2, dtype=np.float32))
        print("Modèle prêt.", flush=True)

    def transcribe(self, audio: np.ndarray) -> str:
        if len(audio) < SAMPLE_RATE // 4:
            return ""
        with self.lock:
            try:
                out = self._run(audio, self.generate_kwargs)
            except (ValueError, TypeError):
                if not self.generate_kwargs:
                    raise
                # Option de langue refusée par ce modèle : on s'en passe désormais.
                self.generate_kwargs = {}
                out = self._run(audio, {})
        return (out.get("text") or "").strip()

    def _run(self, audio: np.ndarray, generate_kwargs: dict) -> dict:
        # Dictionnaire neuf à chaque appel : le pipeline le modifie (il en retire la clé « raw »).
        inputs = {"raw": audio, "sampling_rate": SAMPLE_RATE}
        if generate_kwargs:
            return self.pipe(inputs, generate_kwargs=generate_kwargs)
        return self.pipe(inputs)


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
                self._json(200, {"ok": True, "model": engine.model})
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
    ap.add_argument("--model", default="tarteel-ai/whisper-base-ar-quran")
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
