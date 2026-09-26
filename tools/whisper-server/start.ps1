# Lance le serveur Whisper local pour le mode تسميع (Windows PowerShell 5 ou 7).
# Premier lancement : crée un environnement Python (.venv), installe les dépendances (1-2 Go)
# puis télécharge le modèle. Les lancements suivants démarrent directement.
# « Continue » : sous Windows PowerShell 5, un simple avertissement écrit par Python ou pip sur stderr
# serait sinon traité comme une erreur fatale. Les échecs sont vérifiés explicitement ($LASTEXITCODE).
$ErrorActionPreference = "Continue"
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
# La version CUDA doit être compatible avec le pilote : « nvidia-smi » indique la version maximale qu'il accepte.
function Test-Cuda {
    $ErrorActionPreference = "Continue"   # les avertissements de PyTorch (stderr) ne doivent pas arrêter le script
    $r = & $venvPython -W ignore -c "import torch; print(torch.cuda.is_available())" 2>$null
    return ($r -eq "True")
}

if ((Get-Command nvidia-smi -ErrorAction SilentlyContinue) -and -not (Test-Cuda)) {
    $smi = (& nvidia-smi 2>$null) -join "`n"
    $driverCuda = if ($smi -match "CUDA Version:\s*(\d+)\.(\d+)") { [double]("$($Matches[1]).$($Matches[2])") } else { 0 }
    Write-Host "Carte NVIDIA détectée ; le pilote accepte CUDA jusqu'à $driverCuda."

    # Versions de PyTorch CUDA disponibles, de la plus récente à la plus ancienne, limitées à ce que le pilote accepte.
    $builds = @(@{ Tag = "cu130"; Cuda = 13.0 }, @{ Tag = "cu129"; Cuda = 12.9 }, @{ Tag = "cu128"; Cuda = 12.8 }, @{ Tag = "cu126"; Cuda = 12.6 }) |
        Where-Object { $_.Cuda -le $driverCuda }

    $ok = $false
    foreach ($b in $builds) {
        Write-Host "Installation de PyTorch $($b.Tag) (~2,5 Go, une seule fois)..."
        & $venvPython -m pip install --upgrade --force-reinstall --no-deps torch --index-url "https://download.pytorch.org/whl/$($b.Tag)"
        if ($LASTEXITCODE -eq 0 -and (Test-Cuda)) { $ok = $true; Write-Host "Carte graphique activée (PyTorch $($b.Tag))." -ForegroundColor Green; break }
    }
    if (-not $ok) {
        Write-Host "La carte graphique n'a pas pu être activée : le serveur utilisera le processeur." -ForegroundColor Yellow
        if ($driverCuda -lt 12.6) {
            Write-Host "Votre pilote NVIDIA est trop ancien (CUDA $driverCuda). Mettez-le à jour depuis"
            Write-Host "https://www.nvidia.com/Download/index.aspx puis relancez ce script."
        }
    }
}

& $venvPython server.py @args
