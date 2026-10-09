param([ValidateSet('2057', '1033')][string]$Language = $env:PATCH_LANGUAGE)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$private = Join-Path $env:RUNNER_TEMP 'tpw-patch-private'
$output = Join-Path $env:RUNNER_TEMP 'tpw-patch-output'
if ((Test-Path $private) -or (Test-Path $output)) { throw 'Patch directories already exist.' }
New-Item -ItemType Directory $private, $output | Out-Null
$report = [ordered]@{ schema = 1; status = 'failed'; phase = 'prerequisites'; language = $Language; stages = @() }
function Assert-Hash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw 'Input integrity check failed.'
    }
}
function Get-Manifest([string]$Root) {
    $manifest = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        $name = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        $manifest[$name] = [ordered]@{ bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    return $manifest
}
try {
    foreach ($name in @('DOWNLOAD_URL', 'DOWNLOAD_TOKEN', 'DOWNLOAD_SHA256', 'PATCH_RESULT_KEY')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw 'Required secret is absent.' }
    }
    if ($env:PATCH_RESULT_KEY.Length -lt 32 -or $env:DOWNLOAD_SHA256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid key or digest.' }
    $uri = [Uri]$env:DOWNLOAD_URL
    if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or !$uri.IsAbsoluteUri) { throw 'Download must use authenticated HTTPS.' }
    $openssl = (Get-Command openssl -ErrorAction Stop).Source
    $archive = Join-Path $private 'input.zip'
    $report.phase = 'download'
    # Disable redirects so the bearer credential cannot be sent to another host.
    Invoke-WebRequest -Uri $uri -Headers @{ Authorization = "Bearer $env:DOWNLOAD_TOKEN" } -OutFile $archive -MaximumRedirection 0 -TimeoutSec 180 -ErrorAction Stop
    Assert-Hash $archive $env:DOWNLOAD_SHA256
    $report.inputArchiveSha256 = $env:DOWNLOAD_SHA256.ToLowerInvariant()
    $report.phase = 'extract'
    $bundle = Join-Path $private 'bundle'
    # Preflight ZIP entries before extraction: traversal, links and decompression limits.
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        [long]$total = 0
        if ($zip.Entries.Count -gt 100000) { throw 'Archive entry limit exceeded.' }
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -match '(^/|:|(^|/)\.\.(/|$))' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe archive entry.' }
            $total += $entry.Length
            if ($total -gt 8GB) { throw 'Archive size limit exceeded.' }
        }
    } finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $bundle)
    $engine = Join-Path $bundle 'patch-files'
    Assert-Hash (Join-Path $bundle 'patch.exe') '9ac5b8cf9138b973b58a72185e0cf4a3f027d5d6f6a982e0a2bb44be0ce6b0e1'
    Assert-Hash (Join-Path $engine 'patchw32.dll') 'aa7b2fe54e02dadd74fdde81b49adc01384efd6e9a9fe2842f5effb018e58088'
    Assert-Hash (Join-Path $engine 'PATCH.RTP') '86584beb4e142c5a88e673e6441d9f9f45a801feb3446f6b059d64aae9f94e66'
    Assert-Hash (Join-Path $engine 'EuroAmer/PATCH.RTP') '3cdf985d57983a88a29ee345e1bb91bc409751505de9a23928714032a3042937'
    $languageHashes = @{ '2057' = '749c6fd17099983f2dd582a9df6096083fc43b7f4e8e6d67c178683a3baac1db'; '1033' = 'a968cdf44715c5a8a1d9dc336cdb33c361c4f13c1ec56253ebb0c4919110d1eb' }
    Assert-Hash (Join-Path $engine "$Language/PATCH.RTP") $languageHashes[$Language]
    $installation = Join-Path $bundle 'installation'
    $original = Join-Path $bundle 'original-binary/tp.exe'
    if (!(Test-Path $original -PathType Leaf) -or !(Test-Path (Join-Path $installation 'Data') -PathType Container)) { throw 'Installation or original executable is absent.' }
    $report.phase = 'source-prerequisites'
    # These two common-patch delta records need old root DLLs, not only Data/.
    # Hashes are the exact originals from the supported EuroAmer CD edition.
    Assert-Hash (Join-Path $installation 'weachatr.dll') 'b034b1b492e566e170d4aa0d1671b8652308fe5b701a61f434964101ca14bbe8'
    Assert-Hash (Join-Path $installation 'weauploadr.dll') '5d18f3d7ee96b56fb1dcffa509dc3f6366086b9d735246d2820daa786e74412d'
    $working = Join-Path $private 'working'
    Copy-Item -LiteralPath $installation -Destination $working -Recurse
    Copy-Item -LiteralPath $original -Destination (Join-Path $working 'tp.exe') -Force
    $before = Get-Manifest $working
    $report.originalExecutableSha256 = $before['tp.exe'].sha256
    $report.phase = 'compile'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
    $helper = Join-Path $engine 'ApplyOfficialPatch.exe'
    & $compiler /nologo /target:exe /platform:x86 "/out:$helper" (Join-Path $PSScriptRoot 'ApplyOfficialPatch.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Patch helper compilation failed.' }
    $report.phase = 'apply'
    # This follows the language -> common -> EuroAmer order visible in setup.ins.
    foreach ($relative in @("$Language/PATCH.RTP", 'PATCH.RTP', 'EuroAmer/PATCH.RTP')) {
        $start = [Diagnostics.ProcessStartInfo]::new($helper)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($secret in @('DOWNLOAD_URL', 'DOWNLOAD_TOKEN', 'DOWNLOAD_SHA256', 'PATCH_RESULT_KEY')) { $null = $start.Environment.Remove($secret) }
        $start.ArgumentList.Add($working)
        $start.ArgumentList.Add((Join-Path $engine $relative))
        $process = [Diagnostics.Process]::Start($start)
        # Drain pipes while waiting; no native/proprietary messages enter public logs.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(180000)) { $process.Kill($true); throw 'Patch engine exceeded timeout.' }
        $process.WaitForExit()
        $diagnostic = $stdout.GetAwaiter().GetResult()
        $summary = [regex]::Match($diagnostic, 'engineResult=[0-9]+; diagnostic=(True|False); unsupportedPrompt=(True|False); callbacks=[0-9]+:[0-9]+(?:,[0-9]+:[0-9]+)*; firstUnsupported=[0-9]+').Value
        $report.stages += @{ patch = $relative; exitCode = $process.ExitCode; engine = $summary }
        $null = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or !$summary) { throw 'Official patch engine rejected input.' }
    }
    $report.phase = 'verify'
    $after = Get-Manifest $working
    $changes = @()
    foreach ($name in $after.Keys | Sort-Object) {
        if (!$before.ContainsKey($name) -or $before[$name].sha256 -ne $after[$name].sha256) {
            $changes += @{ path = $name; before = $(if ($before.ContainsKey($name)) { $before[$name] } else { $null }); after = $after[$name] }
        }
    }
    $removed = @($before.Keys | Where-Object { !$after.ContainsKey($_) } | Sort-Object)
    $report.changedFiles = $changes
    $report.removedFiles = $removed
    if ($changes.Count -eq 0) { throw 'Patch reported success without changing files.' }
    foreach ($sentinel in @('Data/_Resolution.sam', 'Data/levels/space/rides/bouncy.wad', 'Data/levels/fantasy/rides/jelly.wad', 'Data/levels/hallow/rides/phantom.wad')) {
        if (!(Test-Path (Join-Path $working $sentinel) -PathType Leaf)) { throw 'Expected patch output is absent.' }
    }
    $report.phase = 'encrypt'
    $resultZip = Join-Path $private 'patched.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($working, $resultZip, [IO.Compression.CompressionLevel]::Fastest, $false)
    $encrypted = Join-Path $output 'patched.zip.enc'
    & $openssl enc -aes-256-cbc -salt -pbkdf2 -iter 200000 -md sha256 -pass env:PATCH_RESULT_KEY -in $resultZip -out $encrypted 2>$null
    if ($LASTEXITCODE -ne 0 -or !(Test-Path $encrypted)) { throw 'Result encryption failed.' }
    # Verify encryption by round trip before publishing any artifact.
    $roundTrip = Join-Path $private 'round-trip.zip'
    & $openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -md sha256 -pass env:PATCH_RESULT_KEY -in $encrypted -out $roundTrip 2>$null
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash $resultZip).Hash -ne (Get-FileHash $roundTrip).Hash) { throw 'Encryption round trip failed.' }
    $report.result = @{ bytes = (Get-Item $encrypted).Length; sha256 = (Get-FileHash $encrypted).Hash.ToLowerInvariant(); cipher = 'AES-256-CBC'; kdf = 'PBKDF2-SHA256'; iterations = 200000 }
    $report.status = 'applied'
    $report.phase = 'complete'
} catch {
    # Only stage and a fixed message are exposed. Exception objects can contain URL,
    # credentials, paths or native data and must stay out of logs/artifacts.
    Write-Host "Official patch failed during $($report.phase)."
    $encrypted = Join-Path $output 'patched.zip.enc'
    if (Test-Path $encrypted) { Remove-Item $encrypted -Force }
} finally {
    $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'metadata.json') -Encoding utf8
    Remove-Item -LiteralPath $private -Recurse -Force -ErrorAction SilentlyContinue
}
if ($report.status -ne 'applied') { exit 1 }
