[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$labOutput = Join-Path $projectRoot 'dist\native-hover-lab'
$labCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $labOutput -Force | Out-Null
$labSources = @('NativeHoverLab.cs', 'LabSpider.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$labArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    ('/out:' + (Join-Path $labOutput 'Halloweb.NativeHoverLab.exe')),
    ('/win32manifest:' + (Join-Path $projectRoot 'src\native\spider.manifest')),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll') + $labSources
& $labCompiler @labArguments
if ($LASTEXITCODE -ne 0) { throw 'Native hover lab compilation failed.' }
Write-Output (Join-Path $labOutput 'Halloweb.NativeHoverLab.exe')
