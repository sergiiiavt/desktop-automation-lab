$ErrorActionPreference = "Stop"
$env:DESKTOP_TEST_ENVIRONMENT = "local-windows"
$env:DESKTOP_TEST_ARTIFACTS_DIR = (Join-Path $PWD "test-artifacts")

Write-Host "=== Local desktop automation environment ==="
Write-Host "Computer: $env:COMPUTERNAME"
Write-Host "User: $env:USERNAME"
Write-Host "OS: $([System.Environment]::OSVersion.VersionString)"
Write-Host "dotnet: $(dotnet --version)"
Write-Host "python: $(python --version)"

$notepadPackage = Get-AppxPackage Microsoft.WindowsNotepad -ErrorAction SilentlyContinue
if ($notepadPackage) {
    Write-Host "Modern Notepad package: $($notepadPackage.Version)"
} else {
    $classicNotepad = Get-Item "$env:WINDIR\System32\notepad.exe" -ErrorAction SilentlyContinue
    if ($classicNotepad) {
        Write-Host "Classic Notepad version: $($classicNotepad.VersionInfo.FileVersion)"
    }
}

Write-Host "============================================"

Remove-Item -Recurse -Force allure-results, allure-report, test-artifacts -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path allure-results | Out-Null

Write-Host "Running FlaUI suite..."
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --configuration Debug
if ($LASTEXITCODE -ne 0) {
    throw "FlaUI tests failed with exit code $LASTEXITCODE."
}

Write-Host "Running pywinauto suite with Allure results..."
python -m pytest python/tests -vv --alluredir=allure-results
if ($LASTEXITCODE -ne 0) {
    throw "pywinauto tests failed with exit code $LASTEXITCODE."
}

$results = @(Get-ChildItem allure-results -Filter "*-result.json" -File)
Write-Host "Allure test result files: $($results.Count)"
if ($results.Count -eq 0) {
    throw "No Allure result files were produced."
}

if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
    throw "npx was not found. Install Node.js, then run this script again."
}

Write-Host "Generating Allure 3 report..."
npx -y allure@3.16.0 generate allure-results --config ./allurerc.mjs
if ($LASTEXITCODE -ne 0) {
    throw "Allure report generation failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Allure report generated: $PWD\allure-report"
Write-Host "Open it with: npx -y allure@3.16.0 open allure-report"
Write-Host "Raw screenshots: $PWD\test-artifacts"
