param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The .NET Framework C# compiler was not found.' }
$sourceDir = $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path -Parent $sourceDir) 'WindowLayers.exe' }
$renderSource = Join-Path $sourceDir 'LayeredSurface.cs'
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "/win32icon:$(Join-Path $sourceDir 'app.ico')" "/out:$OutputPath" $renderSource (Join-Path $sourceDir 'WindowLayers.cs') (Join-Path $sourceDir 'CompactForm.cs') (Join-Path $sourceDir 'SelfTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built: $OutputPath"
