# Lance le serveur Whisper local pour le mode تسميع (Windows PowerShell 5 ou 7).
# Premier lancement : crée un environnement Python (.venv), installe les dépendances (1-2 Go)
# puis télécharge le modèle. Les lancements suivants démarrent directement.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"

function Find-Python {
    # 1) Lanceur officiel « py » (installé avec Python depuis python.org)
    if (Get-Command py -ErrorAction SilentlyContinue) {
        $v = & py -3 -c "import sys; print('%d.%d' % sys.version_info[:2])" 2>$null
        if ($LASTEXITCODE -eq 0 -and $v) { return @{ Exe = "py"; Args = @("-3"); Version = $v } }
    }
    # 2) « python » dans le PATH (en ignorant le raccourci du Microsoft Store, qui ne fait rien)
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -notlike "*WindowsApps*") {
        $v = & python -c "import sys; print('%d.%d' % sys.version_info[:2])" 2>$null
        if ($LASTEXITCODE -eq 0 -and $v) { return @{ Exe = "python"; Args = @(); Version = $v } }
    }
    return $null
}

if (-not (Test-Path $venvPython)) {
    $py = Find-Python
    if (-not $py) {
        Write-Host ""
        Write-Host "Python est introuvable sur ce PC." -ForegroundColor Red
        Write-Host "Installez Python 3.10 à 3.12 depuis https://www.python.org/downloads/windows/"
        Write-Host "en cochant « Add python.exe to PATH », puis relancez ce script."
        exit 1
    }
    $major, $minor = $py.Version.Split(".") | ForEach-Object { [int]$_ }
    if ($major -lt 3 -or ($major -eq 3 -and $minor -lt 10)) {
        Write-Host "Python $($py.Version) trouvé : version 3.10 ou plus récente nécessaire." -ForegroundColor Red
        exit 1
    }
    Write-Host "Python $($py.Version) trouvé. Création de l'environnement (.venv)..."
    if (Test-Path ".venv") { Remove-Item ".venv" -Recurse -Force }   # reste d'une tentative ratée
    & $py.Exe @($py.Args + @("-m", "venv", ".venv"))
    if (-not (Test-Path $venvPython)) {
        Write-Host "La création de .venv a échoué." -ForegroundColor Red
        exit 1
    }
    Write-Host "Installation des dépendances (PyTorch, Transformers : quelques minutes)..."
    & $venvPython -m pip install --upgrade pip
    & $venvPython -m pip install -r requirements.txt
    if ($LASTEXITCODE -ne 0) {
        Remove-Item ".venv" -Recurse -Force
        Write-Host "L'installation des dépendances a échoué (voir les messages ci-dessus)." -ForegroundColor Red
        exit 1
    }
}

# Carte NVIDIA présente : il faut la version CUDA de PyTorch (celle de PyPI, sous Windows, n'utilise que le processeur).
if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    $cuda = & $venvPython -c "import torch; print(torch.cuda.is_available())" 2>$null
    if ($cuda -ne "True") {
        Write-Host "Carte NVIDIA détectée : installation de PyTorch pour CUDA (~2,5 Go, une seule fois)..."
        $ok = $false
        foreach ($cu in @("cu130", "cu129", "cu128", "cu126")) {
            & $venvPython -m pip install --upgrade --force-reinstall torch --index-url "https://download.pytorch.org/whl/$cu"
            if ($LASTEXITCODE -eq 0) {
                $cuda = & $venvPython -c "import torch; print(torch.cuda.is_available())" 2>$null
                if ($cuda -eq "True") { $ok = $true; Write-Host "PyTorch CUDA ($cu) installé."; break }
            }
        }
        if (-not $ok) {
            Write-Host "Impossible d'activer la carte graphique : le serveur utilisera le processeur." -ForegroundColor Yellow
            Write-Host "Vérifiez que le pilote NVIDIA est à jour (commande nvidia-smi)."
        }
    }
}

& $venvPython server.py @args
