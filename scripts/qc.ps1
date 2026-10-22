$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$files = @(git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository sources.' }
$errors = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    if ($file -match '\.(xaml|csproj|manifest)$') {
        try { $null = [xml](Get-Content -LiteralPath $file -Raw) } catch { $errors.Add("Invalid XML: $file : $_") }
    }
    if ($file -match '\.ps1$') {
        $tokens = $null; $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PWD $file), [ref]$tokens, [ref]$parseErrors)
        foreach ($err in $parseErrors) { $errors.Add("PowerShell syntax: $file : $err") }
    }
    if ($file -match '\.(cs|xaml|cpp|h)$') {
        $content = Get-Content -LiteralPath $file -Raw
        if ($content -match '(MessageBox\s*\.|MessageBox[AW]?\s*\(|OpenFileDialog|SaveFileDialog|FolderBrowserDialog|TaskDialog\s*\(|GetOpenFileName|GetSaveFileName)') { $errors.Add("Stock dialog API: $file") }
    }
    if ($file -match '(^|/)(bin|obj|publish|artifacts)/|installer/(payload\.cab|payload_hashes\.h|build/)') { $errors.Add("Generated output must be ignored: $file") }
}
[xml]$project = Get-Content Notch.csproj -Raw
$version = $project.Project.PropertyGroup.Version | Where-Object { $_ }
if ((Get-Content installer/Version.h -Raw) -notmatch [regex]::Escape('L"' + $version + '"')) { $errors.Add('Installer and app versions differ.') }
foreach ($source in @('docs/site.js','scripts/check-web.mjs','scripts/preview.mjs')) {
    & node --check $source
    if ($LASTEXITCODE -ne 0) { $errors.Add("JavaScript syntax: $source") }
}
& node scripts/check-web.mjs
if ($LASTEXITCODE -ne 0) { $errors.Add('Website source checks failed.') }
& git diff --check
if ($LASTEXITCODE -ne 0) { $errors.Add('Whitespace check failed.') }
if ($errors.Count) { $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }; throw "$($errors.Count) source QC failure(s)." }
Write-Host "PASS: source-only QC ($($files.Count) files), version $version. No compilation or restore performed." -ForegroundColor Green
