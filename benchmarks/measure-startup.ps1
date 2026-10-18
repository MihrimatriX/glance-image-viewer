<#
.SYNOPSIS
  Cold start of the real app: process creation -> first image drawn, against the 400 ms budget (ADR 0001).
.EXAMPLE
  dotnet build src/Glance.App -c Release -r win-x64
  ./benchmarks/measure-startup.ps1 -Exe src/Glance.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Glance.exe -Image photo.jpg
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Image,
    [int] $Runs = 10,
    [int] $BudgetMs = 400
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$Image = (Resolve-Path $Image).Path
$trace = Join-Path ([IO.Path]::GetTempPath()) "glance-startup-$PID.txt"
Remove-Item $trace -ErrorAction SilentlyContinue
$env:GLANCE_STARTUP_TRACE = $trace

# Glance is single-instance: a running copy would take the activation and nothing would be measured.
Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($Exe)) -ErrorAction SilentlyContinue | Stop-Process -Force

try {
    for ($i = 1; $i -le $Runs; $i++) {
        $before = if (Test-Path $trace) { (Get-Content $trace).Count } else { 0 }
        $p = Start-Process -FilePath $Exe -ArgumentList "`"$Image`"" -PassThru
        $deadline = (Get-Date).AddSeconds(15)
        while (((Test-Path $trace) -and (Get-Content $trace).Count -gt $before) -eq $false) {
            if ((Get-Date) -gt $deadline) { throw "Run ${i}: no image drawn within 15 s." }
            Start-Sleep -Milliseconds 50
        }
        Stop-Process -Id $p.Id -Force
        $p.WaitForExit()
        Start-Sleep -Milliseconds 300
    }
    $ms = Get-Content $trace | ForEach-Object { [double]$_ } | Sort-Object
    $median = $ms[[int][math]::Floor($ms.Count / 2)]
    $p90 = $ms[[int][math]::Ceiling($ms.Count * 0.9) - 1]
    # The first run is the only truly cold one (DLLs not yet in the file cache); report it separately.
    $first = [double](Get-Content $trace -TotalCount 1)
    "first run: {0:0} ms   median: {1:0} ms   p90: {2:0} ms   budget: {3} ms" -f $first, $median, $p90, $BudgetMs
    if ($median -gt $BudgetMs) { Write-Warning "Median is over the $BudgetMs ms budget."; exit 1 }
}
finally {
    Remove-Item Env:GLANCE_STARTUP_TRACE
    Remove-Item $trace -ErrorAction SilentlyContinue
}
