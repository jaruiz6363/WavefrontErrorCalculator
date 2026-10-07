# Builds the PDF report of the OPD comparison: report.py writes HTML from the tables generate.sh
# made, and Edge prints it. Run from the repository root:
#   pwsh docs/opd/compare/report.ps1 -Out <file.pdf> -PythonProject <uv project with matplotlib>
# -PythonProject is the folder of any uv project with matplotlib (an Optiland checkout has it).
param(
    [Parameter(Mandatory)] [string]$Out,
    [Parameter(Mandatory)] [string]$PythonProject
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$html = Join-Path ([IO.Path]::GetTempPath()) 'opd_comparison_report.html'
& uv run --project $PythonProject python (Join-Path $here 'report.py') $html
if ($LASTEXITCODE -ne 0) { throw 'report.py failed' }

$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
$profileDir = Join-Path ([IO.Path]::GetTempPath()) 'opd-report-edge'
$outFull = [IO.Path]::GetFullPath($Out)
$start = Get-Date
# Waited for, so the check below sees the finished file.
Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @(
    "--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=`"$profileDir`"",
    "--no-pdf-header-footer", "--print-to-pdf=`"$outFull`"", ('file:///' + ($html -replace '\\', '/')))
# Edge does not fail when it cannot write (a viewer holding the file open): check the date.
if (-not (Test-Path $outFull) -or (Get-Item $outFull).LastWriteTime -lt $start) { throw "$outFull was not written (is it open in a viewer?)" }
Write-Host "wrote $outFull"
