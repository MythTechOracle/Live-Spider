[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The native Halloweb build requires Windows and the existing .NET Framework C# compiler.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot 'src\native'
$distRoot = Join-Path $projectRoot 'dist'
$outputPath = Join-Path $distRoot 'Halloweb.exe'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'The Windows .NET Framework 4.x x64 compiler was not found. No SDK or installer was downloaded.'
}

$manifestPath = Join-Path $nativeRoot 'spider.manifest'
$sourceNames = @('AssemblyInfo.cs', 'SpookyDesktopSpider.cs', 'EcologyMotion.cs', 'EcologyChecks.cs', 'EcologyV11Checks.cs', 'WebOverlay.cs', 'HoverProbe.cs', 'HoverGlow.cs', 'HoverGlowChecks.cs', 'HoverIntegrationChecks.cs')
$sourcePaths = @($sourceNames | ForEach-Object { Join-Path $nativeRoot $_ })
foreach ($requiredPath in @($manifestPath) + $sourcePaths) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw ('A required native source file is missing: ' + $requiredPath)
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$compilerArguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    ('/out:' + $outputPath), ('/win32manifest:' + $manifestPath),
    '/reference:System.dll', '/reference:System.Core.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\UIAutomationClient.dll')),
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\UIAutomationTypes.dll')),
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\WindowsBase.dll'))
) + $sourcePaths

& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw ('Halloweb compilation failed with exit code ' + $LASTEXITCODE + '.')
}
if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw 'The compiler did not produce dist/Halloweb.exe.'
}

Write-Output $outputPath
