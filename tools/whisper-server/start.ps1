# Lance le serveur Whisper local pour le mode تسميع (Windows PowerShell).
# Premier lancement : crée un environnement Python, installe les dépendances (~1-2 Go) et télécharge le modèle.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (-not (Test-Path ".venv")) {
    Write-Host "Création de l'environnement Python (.venv)…"
    python -m venv .venv
    .\.venv\Scripts\python -m pip install --upgrade pip
    .\.venv\Scripts\python -m pip install -r requirements.txt
}
.\.venv\Scripts\python server.py @args
