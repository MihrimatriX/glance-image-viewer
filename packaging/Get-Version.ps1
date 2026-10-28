# Prints VERSION=x.y.z for $GITHUB_ENV: from a v* tag, otherwise from <Version> in Directory.Build.props.
param([string]$Ref = '')

if ($Ref -match '^refs/tags/v(\d+\.\d+\.\d+)$') {
    $v = $Matches[1]
} else {
    [xml]$props = Get-Content (Join-Path $PSScriptRoot '..\Directory.Build.props')
    $v = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
}
if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$v' is not x.y.z (the Store needs four numeric parts ending in .0)" }
"VERSION=$v"
