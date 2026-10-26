param([string]$Python = '', [switch]$NoShortcuts, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$appRoot = Join-Path $projectRoot 'app'
if (-not $BuildOnly) {
    if (-not $Python) {
        $pyLauncher = Get-Command py -ErrorAction SilentlyContinue
        if (-not $pyLauncher) { throw 'Install Python 3.12 x64 from python.org, or pass -Python with its executable path.' }
        $Python = (& $pyLauncher.Source -3.12 -c 'import sys; print(sys.executable)').Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Python 3.12 is required.' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.venv\Scripts\python.exe'))) {
        & $Python -m venv (Join-Path $projectRoot '.venv')
        if ($LASTEXITCODE -ne 0) { throw 'Could not create virtual environment.' }
    }
    $venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'
    & $venvPython -m pip install -r (Join-Path $projectRoot 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
    & $venvPython -X utf8 (Join-Path $projectRoot 'tools\configure.py') --init
    if ($LASTEXITCODE -ne 0) { throw 'Configuration initialization failed.' }
}
$emblem = Join-Path $appRoot 'assets\fairy-emblem.json'
if (-not (Test-Path -LiteralPath $emblem)) {
    Copy-Item -LiteralPath (Join-Path $appRoot 'assets\default-emblem.json') -Destination $emblem
}
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 x64 is required.' }
$wpf = Join-Path $framework 'WPF'
$references = @('/reference:System.Xaml.dll','/reference:System.Web.Extensions.dll',
    ('/reference:' + (Join-Path $wpf 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $wpf 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpf 'WindowsBase.dll')))
Push-Location $appRoot
try {
    & $compiler /nologo /codepage:65001 /target:winexe /out:ExportFairyIcon.exe @references /reference:System.Drawing.dll ExportFairyIcon.cs FairyEmblem.cs
    if ($LASTEXITCODE -ne 0) { throw 'Icon builder failed.' }
    # Preserve an imported icon. A fresh install generates its neutral placeholder.
    if (-not (Test-Path -LiteralPath 'assets\fairy.ico')) {
        $iconBuilder = Start-Process -FilePath (Join-Path $appRoot 'ExportFairyIcon.exe') -Wait -PassThru -WindowStyle Hidden
        if ($iconBuilder.ExitCode -ne 0) { throw 'Icon export failed.' }
    }
    & $compiler /nologo /codepage:65001 /target:winexe /out:FairyCompanion.exe /win32manifest:FairyCompanion.manifest /win32icon:assets\fairy.ico @references /reference:System.Net.Http.dll FairyCompanion.cs FairyEmblem.cs OpeningPlayer.cs
    if ($LASTEXITCODE -ne 0) { throw 'Companion build failed. Close this installation before rebuilding.' }
    & $compiler /nologo /codepage:65001 /target:winexe /out:FairyLauncher.exe /win32icon:assets\fairy.ico /reference:System.Windows.Forms.dll FairyLauncher.cs
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    & $compiler /nologo /codepage:65001 /target:winexe /out:FairyStopper.exe /reference:System.Windows.Forms.dll FairyStopper.cs
    if ($LASTEXITCODE -ne 0) { throw 'Stopper build failed.' }
} finally { Pop-Location }
if (-not $NoShortcuts -and -not $BuildOnly) {
    $desktopPath = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    foreach ($entry in @(@('Fairy Companion.lnk','FairyLauncher.exe'),@('Stop Fairy Companion.lnk','FairyStopper.exe'))) {
        $shortcut = $shell.CreateShortcut((Join-Path $desktopPath $entry[0]))
        $shortcut.TargetPath = Join-Path $appRoot $entry[1]
        $shortcut.WorkingDirectory = $appRoot
        $shortcut.IconLocation = Join-Path $appRoot 'assets\fairy.ico'
        $shortcut.Save()
    }
}
Write-Host 'Build complete. Use Configure.cmd, then Start.cmd. No administrator access required.'
