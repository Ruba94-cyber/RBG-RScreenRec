[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'release'
}
& (Join-Path $PSScriptRoot 'build-check.ps1')
if ($LASTEXITCODE -ne 0) { throw "Release x64 build check failed (exit $LASTEXITCODE)." }

$exe = Join-Path $root 'bin\Release\RBG - RScreenRec.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Release executable is missing: $exe" }
$version = '1.3.0'
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($info.FileVersion -ne '1.3.0.0' -or $info.ProductVersion -ne $version) {
    throw "Unexpected executable version metadata: file=$($info.FileVersion), product=$($info.ProductVersion)"
}
if ($info.CompanyName -ne 'Paolo Rubagotti' -or
    $info.FileDescription -notlike '*Paolo Rubagotti*' -or
    $info.FileDescription -notlike '*paolo.ruba.1913@gmail.com*') {
    throw "Executable author/contact metadata is incomplete: $($info.FileDescription)"
}

$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$runId = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $tempRoot ('RBG-RScreenRec-Release-' + $runId)
$archiveTemp = Join-Path $tempRoot ("RBG-RScreenRec-$runId.zip")
$archiveName = "RBG-RScreenRec-$version-windows-x64.zip"
New-Item -ItemType Directory -Path $stage | Out-Null

try {
    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'RBG - RScreenRec.exe')
    foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
        $source = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required release file is missing: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name)
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archiveTemp -CompressionLevel Optimal

    $archive = Join-Path $output $archiveName
    Move-Item -LiteralPath $archiveTemp -Destination $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $archiveName" -Encoding ascii
    Write-Host "Package: $archive"
    Write-Host "SHA-256: $hash"
    Write-Host "Executable: $($info.FileDescription) | $($info.CompanyName) | $($info.ProductVersion)"
}
finally {
    $stageFull = [System.IO.Path]::GetFullPath($stage)
    if (-not $stageFull.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove staging path outside the system temp directory: $stageFull"
    }
    if (Test-Path -LiteralPath $stageFull) { Remove-Item -LiteralPath $stageFull -Recurse -Force }
    if (Test-Path -LiteralPath $archiveTemp) { Remove-Item -LiteralPath $archiveTemp -Force }
}
