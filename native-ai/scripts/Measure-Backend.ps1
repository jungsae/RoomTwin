# Run from PowerShell with one USB device selected (or set ANDROID_SERIAL).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('CPU','GPU')][string]$Backend,
    [Parameter(Mandatory)][string]$OutputFile,
    [switch]$TestSwitch
)
$ErrorActionPreference = 'Stop'
$package = 'com.roomtwin.nativeai.field'
$backendName = $Backend.ToUpperInvariant()
$directory = "/sdcard/Android/data/$package/files"
if (Test-Path -LiteralPath $OutputFile) { throw "Result file already exists: $OutputFile" }

function Invoke-Adb {
    $result = & adb @args
    if ($LASTEXITCODE -ne 0) { throw "ADB command failed ($LASTEXITCODE)" }
    $result
}

$hashes = @{
    'gemma-4-E2B-it.litertlm' = '181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c'
    'bedroom.jpg' = 'c5db919661ef517085764e0e60d351e7068758be65b789348ee77f4831bd1da6'
    'living.jpg' = '98e196f52a67abb96e04c6a33694d1710ea885797e01ae2d52b7bba9f747893b'
}
foreach ($name in $hashes.Keys) {
    $digest = Invoke-Adb shell sha256sum "$directory/$name"
    if (($digest -split '\s+')[0] -ne $hashes[$name]) { throw "Input hash mismatch: $name" }
}

Invoke-Adb shell am force-stop $package
# Preserve existing compiled files. Only the field package's selected cache is rotated.
$cache = "cache/$backendName"
$suffix = [guid]::NewGuid().ToString('N')
Invoke-Adb shell run-as $package sh -c "'if [ -d $cache ]; then mv $cache $cache-$suffix; fi'"
$switchValue = $TestSwitch.IsPresent.ToString().ToLowerInvariant()
$response = Invoke-Adb shell am instrument -w -r -e backend $backendName -e switch $switchValue "$package/com.roomtwin.nativeai.BenchmarkInstrumentation"
$response | Out-Host
$report = Invoke-Adb exec-out run-as $package cat "files/bench-$backendName.jsonl"
$report | Set-Content -Encoding utf8 -LiteralPath $OutputFile
$rows = @($report | ForEach-Object { $_ | ConvertFrom-Json })
$ok = @($rows | Where-Object { $_.state -eq 'ok' -and $_.backend -eq $backendName -and $_.schemaValid })
if (($response -join ' ') -notmatch 'status=completed' -or $ok.Count -ne 6) {
    throw "Incomplete run. Inspect $OutputFile for the last started request and error."
}
$ok | Select-Object index,image,initMs,imageMs,inferMs,totalMs,decodeTokens,pssKiB,sampleMaxPssKiB | Format-Table
