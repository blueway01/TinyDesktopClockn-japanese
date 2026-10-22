$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = Join-Path $project 'dist'
New-Item -ItemType Directory -Path $release -Force | Out-Null

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Windows .NET Framework C# compiler (csc.exe) was not found.'
}

$exe = Join-Path $release 'TinyDesktopClockn.exe'
$icon = Join-Path $project 'clock.ico'
if (-not (Test-Path -LiteralPath $icon)) {
    throw 'clock.ico is missing. Generate it with: python make_icon.py'
}

$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/codepage:65001',
    "/out:$exe", "/win32icon:$icon",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Net.dll',
    (Join-Path $project 'Program.cs'),
    (Join-Path $project 'ClockForm.cs'),
    (Join-Path $project 'SettingsForm.cs'),
    (Join-Path $project 'WifiTrafficMeter.cs')
)

& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "SUCCESS: $exe"
Get-Item -LiteralPath $exe | Select-Object FullName, Length, LastWriteTime
