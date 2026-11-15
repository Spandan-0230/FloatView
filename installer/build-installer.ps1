param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'FloatView\FloatView.csproj'

[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]($projectXml.Project.PropertyGroup |
    Where-Object { $_.Version } |
    Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "No <Version> found in $project"
}
$version = $version.Trim()
$msiVersion = ($version -split '-')[0]

$wix = Get-Command wix -ErrorAction SilentlyContinue
if ($wix) {
    $wixPath = $wix.Source
} else {
    $wixPath = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
    if (-not (Test-Path $wixPath)) {
        throw 'WiX is not installed. Run: dotnet tool install --global wix --version 4.0.5'
    }
}

# WiX 5+ requires accepting the paid Open Source Maintenance Fee EULA. This
# project targets the last freely usable release, 4.0.5.
$wixVersion = (& $wixPath --version 2>$null) -replace '\+.*$', ''
if ($LASTEXITCODE -ne 0 -or -not $wixVersion) {
    throw "Could not read the WiX version. Install 4.0.5 with: dotnet tool install --global wix --version 4.0.5"
}
if (-not $wixVersion.StartsWith('4.')) {
    throw "WiX $wixVersion is installed. This project builds with 4.0.5; run: dotnet tool install --global wix --version 4.0.5"
}

$utilExtension = 'WixToolset.Util.wixext/4.0.5'
$uiExtension = 'WixToolset.UI.wixext/4.0.5'
$installedExtensions = & $wixPath extension list -g 2>$null
if (-not ($installedExtensions -match 'WixToolset\.Util\.wixext 4\.0\.5')) {
    & $wixPath extension add -g $utilExtension
    if ($LASTEXITCODE -ne 0) { throw "Could not install the WiX Util extension ($LASTEXITCODE)" }
}
if (-not ($installedExtensions -match 'WixToolset\.UI\.wixext 4\.0\.5')) {
    & $wixPath extension add -g $uiExtension
    if ($LASTEXITCODE -ne 0) { throw "Could not install the WiX UI extension ($LASTEXITCODE)" }
}

if (-not $SkipPublish) {
    Write-Host 'Publishing FloatView...'
    dotnet publish (Join-Path $root 'FloatView\FloatView.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o (Join-Path $root 'publish')
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$exe = Join-Path $root 'publish\FloatView.exe'
if (-not (Test-Path $exe)) {
    throw "Missing $exe. Run the publish step first."
}

# The CloseApplication helper comes from WiX's Util extension.
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$msi = Join-Path $dist "FloatView-Setup-$msiVersion-x64.msi"

Push-Location $PSScriptRoot
try {
    & $wixPath build 'FloatView.wxs' `
        -arch x64 `
        -ext $utilExtension `
        -ext $uiExtension `
        -d "ProductVersion=$msiVersion" `
        -o $msi
    if ($LASTEXITCODE -ne 0) { throw "WiX build failed ($LASTEXITCODE)" }
} finally {
    Pop-Location
}

$built = Get-Item -LiteralPath $msi
Write-Host ("Built {0} ({1:N1} MB)" -f $built.FullName, ($built.Length / 1MB))
