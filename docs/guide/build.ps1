# Builds the guide PDFs: build.py draws the figures and writes print-ready HTML of each
# markdown document, and Edge prints them. Run from the repository root:
#   pwsh docs/guide/build.ps1 -OutDir <folder> -PythonProject <uv project with matplotlib>
# -PythonProject is the folder of any uv project with matplotlib (an Optiland checkout has it).
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [Parameter(Mandatory)] [string]$PythonProject
)
$ErrorActionPreference = 'Stop'
$tmp = Join-Path ([IO.Path]::GetTempPath()) 'wec-guide'
& uv run --project $PythonProject python (Join-Path $PSScriptRoot 'build.py') $tmp
if ($LASTEXITCODE -ne 0) { throw 'build.py failed' }

$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
$profileDir = Join-Path ([IO.Path]::GetTempPath()) 'wec-guide-edge'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$start = Get-Date
foreach ($doc in @(@{ Html = 'wec-method.html'; Pdf = 'WEC_method.pdf' }, @{ Html = 'rayces-method.html'; Pdf = 'Rayces_method.pdf' }, @{ Html = 'user-guide.html'; Pdf = 'WEC_user_guide.pdf' })) {
    $out = [IO.Path]::GetFullPath((Join-Path $OutDir $doc.Pdf))
    $src = 'file:///' + ((Join-Path $tmp $doc.Html) -replace '\\', '/')
    # Each print in its own profile, waited for: a second Edge launched on a profile still in use
    # hands its job to the running one and returns without printing.
    $edgeProfile = "$profileDir-" + [IO.Path]::GetFileNameWithoutExtension($doc.Pdf)
    Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @(
        "--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=`"$edgeProfile`"",
        "--no-pdf-header-footer", "--print-to-pdf=`"$out`"", $src)
    # Edge does not fail when it cannot write (a viewer holding the file open): check the date.
    if (-not (Test-Path $out) -or (Get-Item $out).LastWriteTime -lt $start) { throw "$out was not written (is it open in a viewer?)" }
    Write-Host "wrote $out"
}
