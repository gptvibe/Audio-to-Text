param(
    [string]$Version = "0.2.1",
    [string]$PythonVersion = "3.12.10",
    [switch]$BuildSetup
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\publish\QuietScribe-win-x64"
$releaseDir = Join-Path $repoRoot "artifacts\release"
$cacheDir = Join-Path $repoRoot "artifacts\cache"
$workerDir = Join-Path $publishDir "workers\transcription-worker"
$pythonDir = Join-Path $workerDir "python"
$pythonZip = Join-Path $cacheDir "python-$PythonVersion-embed-amd64.zip"
$getPip = Join-Path $cacheDir "get-pip.py"
$portableZip = Join-Path $releaseDir "QuietScribe-v$Version-win-x64-portable.zip"
$setupExe = Join-Path $releaseDir "QuietScribe-v$Version-win-x64-setup.exe"
$releaseNotes = Join-Path $repoRoot "docs\release-notes\v$Version.md"

function Remove-DirectoryIfExists([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $releaseDir, $cacheDir | Out-Null
Remove-DirectoryIfExists $publishDir

dotnet publish (Join-Path $repoRoot "src\App.Desktop\App.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    -p:WindowsPackageType=None `
    -p:SelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:Version=$Version `
    -o $publishDir

if (!(Test-Path -LiteralPath $pythonZip)) {
    $pythonUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    Invoke-WebRequest -Uri $pythonUrl -OutFile $pythonZip
}

Remove-DirectoryIfExists $pythonDir
New-Item -ItemType Directory -Force -Path $pythonDir | Out-Null
Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDir -Force

$pthFile = Get-ChildItem -LiteralPath $pythonDir -Filter "python*._pth" | Select-Object -First 1
if ($null -eq $pthFile) {
    throw "Could not find Python ._pth file in $pythonDir."
}

$pythonZipName = Split-Path -Leaf ((Get-ChildItem -LiteralPath $pythonDir -Filter "python*.zip" | Select-Object -First 1).FullName)
Set-Content -LiteralPath $pthFile.FullName -Value @(
    $pythonZipName,
    ".",
    "Lib\site-packages",
    "import site"
)

if (!(Test-Path -LiteralPath $getPip)) {
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip
}

$pythonExe = Join-Path $pythonDir "python.exe"
& $pythonExe $getPip --no-warn-script-location
& $pythonExe -m pip install --no-cache-dir --no-warn-script-location -r (Join-Path $repoRoot "workers\transcription-worker\requirements.txt")
& $pythonExe -c "from faster_whisper import WhisperModel; import av, ctranslate2; print('QuietScribe worker runtime OK')"

Get-ChildItem -LiteralPath $pythonDir -Recurse -Directory -Include "__pycache__","tests","test" |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $pythonDir -Recurse -Include "*.pyc","*.pyo" |
    Remove-Item -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $releaseNotes) {
    Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $publishDir "RELEASE-NOTES.txt") -Force
}

if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZip -Force

if ($BuildSetup) {
    & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" (Join-Path $repoRoot "installer\QuietScribe.iss")

    if (!(Test-Path -LiteralPath $setupExe)) {
        throw "Expected installer was not produced: $setupExe"
    }
}
elseif (Test-Path -LiteralPath $setupExe) {
    Remove-Item -LiteralPath $setupExe -Force
}

Get-ChildItem -LiteralPath $releaseDir -Filter "QuietScribe-v$Version-win-x64-*" |
    Select-Object Name, Length
