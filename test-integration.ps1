$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $csc)) { throw 'C# compiler not available' }
$output = Join-Path $project 'test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$testExe = Join-Path $output 'ClockIntegrationTests.exe'

& $csc '/nologo' '/target:exe' '/platform:anycpu' '/optimize+' '/warn:4' '/codepage:65001' '/main:TinyDesktopClock.IntegrationTests' "/out:$testExe" '/reference:System.dll' '/reference:System.Core.dll' '/reference:System.Drawing.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Runtime.Serialization.dll' '/reference:System.Net.dll' (Join-Path $project 'IntegrationTests.cs') (Join-Path $project 'Program.cs') (Join-Path $project 'ClockForm.cs') (Join-Path $project 'SettingsForm.cs') (Join-Path $project 'WifiTrafficMeter.cs')
if ($LASTEXITCODE -ne 0) { throw "Integration tests compilation failed: $LASTEXITCODE" }

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('TinyClock-日本語 検証-' + [Guid]::NewGuid().ToString('N'))
$logPath = Join-Path $output 'integration-result.txt'
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
    $copiedExe = Join-Path $root 'ClockIntegrationTests.exe'
    Copy-Item -LiteralPath $testExe -Destination $copiedExe
    $config = Join-Path $root '日本語 設定.json'
    $lines = @()
    foreach ($phase in @('phase1', 'phase2')) {
        $lines += "PHASE: $phase"
        $lines += & $copiedExe $phase $config
        if ($LASTEXITCODE -ne 0) { throw "Integration $phase failed with code $LASTEXITCODE`n$($lines -join [Environment]::NewLine)" }
    }
    $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
    $lines
    Write-Output "INTEGRATION_SUCCEEDED=true"
}
finally {
    $prefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\TinyClock-日本語 検証-'
    if ($root.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $root)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
