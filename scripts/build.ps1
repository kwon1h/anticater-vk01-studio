param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler -Parent
$wpf = Join-Path $framework 'WPF'
$outputRoot = Join-Path $repoRoot 'build'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
function Compile([string[]]$CompilerArgs) {
    & $compiler /nologo /codepage:65001 /platform:x64 @CompilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
}
Compile @('/r:System.Drawing.dll', ('/out:' + (Join-Path $outputRoot 'BuildAssets.exe')), (Join-Path $PSScriptRoot 'BuildAssets.cs'))
& (Join-Path $outputRoot 'BuildAssets.exe') (Join-Path $repoRoot 'assets')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$appArgs = @('/target:winexe','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.Xaml.dll',
    ('/r:' + (Join-Path $wpf 'PresentationFramework.dll')), ('/r:' + (Join-Path $wpf 'PresentationCore.dll')), ('/r:' + (Join-Path $wpf 'WindowsBase.dll')), ('/r:' + (Join-Path $wpf 'WindowsFormsIntegration.dll')),
    ('/win32icon:' + (Join-Path $repoRoot 'assets\app.ico')), ('/win32manifest:' + (Join-Path $repoRoot 'src\app.manifest')),
    ('/resource:' + (Join-Path $repoRoot 'assets\app.ico') + ',app.ico'), ('/resource:' + (Join-Path $repoRoot 'src\Theme.xaml') + ',Theme.xaml'),
    ('/resource:' + (Join-Path $repoRoot 'assets\icon.png') + ',icon.png'),
    ('/out:' + (Join-Path $outputRoot 'VK01Studio.exe')))
$appArgs += @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
Compile $appArgs
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $outputRoot -Force
Write-Host "Built: $outputRoot\VK01Studio.exe"
if ($Test) {
    $src = Join-Path $repoRoot 'src'
    Compile @(('/out:' + (Join-Path $outputRoot 'DetectorTests.exe')), (Join-Path $src 'ClickDetector.cs'), (Join-Path $repoRoot 'tests\DetectorTests.cs'))
    & (Join-Path $outputRoot 'DetectorTests.exe'); if ($LASTEXITCODE -ne 0) { throw 'Gesture tests failed.' }
    $settingsTestArgs = @('/r:System.Web.Extensions.dll','/r:System.Core.dll', ('/out:' + (Join-Path $outputRoot 'SettingsTests.exe')), (Join-Path $src 'ClickDetector.cs'), (Join-Path $src 'KnobInput.cs'), (Join-Path $src 'Settings.cs'), (Join-Path $src 'Ui.cs'), (Join-Path $repoRoot 'tests\SettingsTests.cs'))
    Compile $settingsTestArgs
    & (Join-Path $outputRoot 'SettingsTests.exe'); if ($LASTEXITCODE -ne 0) { throw 'Settings tests failed.' }
    $audioTestArgs = @('/r:System.Web.Extensions.dll','/r:System.Core.dll', ('/out:' + (Join-Path $outputRoot 'AudioTests.exe')), (Join-Path $src 'ClickDetector.cs'), (Join-Path $src 'KnobInput.cs'), (Join-Path $src 'Settings.cs'), (Join-Path $src 'Ui.cs'), (Join-Path $src 'AudioDevices.cs'), (Join-Path $repoRoot 'tests\AudioTests.cs'))
    Compile $audioTestArgs
    & (Join-Path $outputRoot 'AudioTests.exe'); if ($LASTEXITCODE -ne 0) { throw 'Audio tests failed.' }
    Compile @(('/out:' + (Join-Path $outputRoot 'InputTests.exe')), (Join-Path $src 'ClickDetector.cs'), (Join-Path $src 'KnobInput.cs'), (Join-Path $repoRoot 'tests\InputTests.cs'))
    & (Join-Path $outputRoot 'InputTests.exe'); if ($LASTEXITCODE -ne 0) { throw 'Input tests failed.' }
}
