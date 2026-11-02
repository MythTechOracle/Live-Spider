[CmdletBinding()]
param(
    [switch]$Smoke,
    [switch]$JavaScriptOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Smoke -and $JavaScriptOnly) {
    throw '-Smoke and -JavaScriptOnly cannot be combined.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$windowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if (-not $JavaScriptOnly -and -not $windowsHost) {
    throw 'Native verification requires Windows. Use -JavaScriptOnly to run the behavior tests on another platform.'
}
if ($Smoke -and -not [Environment]::UserInteractive) {
    throw 'The optional overlay smoke test requires an interactive Windows desktop.'
}

$nodeCommand = Get-Command node -CommandType Application -ErrorAction Stop | Select-Object -First 1
$testRoot = Join-Path $projectRoot 'tests\behavior'
$testFiles = @('behavior.test.js', 'adversarial.test.js', 'verified-governance.test.js') |
    ForEach-Object { Join-Path $testRoot $_ }
foreach ($testFile in $testFiles) {
    if (-not (Test-Path -LiteralPath $testFile -PathType Leaf)) {
        throw ('A required behavior test is missing: ' + $testFile)
    }
}

$runName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$resultRoot = Join-Path $projectRoot ('dist\test-results\' + $runName)
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$nodeVersion = & $nodeCommand.Source --version
Write-Output ('Node.js: ' + $nodeVersion)
$nodeOutput = & $nodeCommand.Source --test @testFiles 2>&1
$nodeExitCode = $LASTEXITCODE
$nodeOutput | Set-Content -LiteralPath (Join-Path $resultRoot 'node-tests.txt') -Encoding UTF8
$nodeOutput | ForEach-Object { Write-Output $_ }
if ($nodeExitCode -ne 0) {
    throw ('Behavior tests failed with exit code ' + $nodeExitCode + '. Results: ' + $resultRoot)
}

if ($JavaScriptOnly) {
    Write-Output 'Behavior checks passed. Native self-tests and desktop smoke tests were not requested.'
    Write-Output ('Results: ' + $resultRoot)
    return
}

& (Join-Path $PSScriptRoot 'build.ps1')
$builtExecutable = Join-Path $projectRoot 'dist\Halloweb.exe'

function Invoke-HallowebNativeCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Argument,
        [Parameter(Mandatory = $true)][string]$ExpectedReport
    )

    $checkRoot = Join-Path $resultRoot $Name
    New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
    $checkExecutable = Join-Path $checkRoot 'Halloweb.exe'
    Copy-Item -LiteralPath $builtExecutable -Destination $checkExecutable
    $checkProcess = Start-Process -FilePath $checkExecutable -ArgumentList $Argument `
        -WorkingDirectory $checkRoot -WindowStyle Hidden -Wait -PassThru
    if ($checkProcess.ExitCode -ne 0) {
        throw ($Name + ' failed with exit code ' + $checkProcess.ExitCode + '. Reports: ' + $checkRoot)
    }
    $reportPath = Join-Path $checkRoot $ExpectedReport
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw ($Name + ' did not produce its verification report: ' + $reportPath)
    }
    $reportText = Get-Content -LiteralPath $reportPath -Raw
    if ($reportText -notmatch '^PASS:') {
        throw ($Name + ' did not report success. Report: ' + $reportPath)
    }
    Write-Output $reportText.Trim()
}

Invoke-HallowebNativeCheck -Name 'native' -Argument '--self-test' -ExpectedReport 'self-test-results.txt'
if ($Smoke) {
    Invoke-HallowebNativeCheck -Name 'smoke' -Argument '--smoke-test' -ExpectedReport 'smoke-test-results.txt'
    Invoke-HallowebNativeCheck -Name 'hover' -Argument '--hover-smoke-test' -ExpectedReport 'hover-smoke-test-results.txt'
    Invoke-HallowebNativeCheck -Name 'hanging' -Argument '--hanging-smoke-test' -ExpectedReport 'hanging-smoke-test-results.txt'
} else {
    Write-Output 'Desktop smoke test was not requested. Run with -Smoke only from an interactive Windows desktop.'
}

Write-Output ('Verification results: ' + $resultRoot)
