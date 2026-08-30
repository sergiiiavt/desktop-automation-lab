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

$flauiExitCode = 0
$pythonExitCode = 0
$dependencyExitCode = 0
$reportExitCode = 0

Write-Host "Running FlaUI suite (TC0001-TC0004, TC0006)..."
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --configuration Debug
$flauiExitCode = $LASTEXITCODE
if ($flauiExitCode -ne 0) {
    Write-Warning "FlaUI suite failed with exit code $flauiExitCode. Continuing so Python tests can still run."
}

Write-Host "Installing/verifying Python test dependencies..."
python -m pip install -r python/requirements.txt
$dependencyExitCode = $LASTEXITCODE
if ($dependencyExitCode -ne 0) {
    Write-Warning "Python dependency installation failed with exit code $dependencyExitCode. Python tests cannot run."
    $pythonExitCode = $dependencyExitCode
} else {
    Write-Host ""
    Write-Host "=== Starting Python - Notepad / TC0005 + TC0007 ==="
    Write-Host "pytest output capture is disabled so the Python tests are visible below."
    python -m pytest python/tests -vv -s --alluredir=allure-results
    $pythonExitCode = $LASTEXITCODE
    if ($pythonExitCode -ne 0) {
        Write-Warning "pywinauto suite failed with exit code $pythonExitCode."
    }
    Write-Host "=== Finished Python - Notepad / TC0005 + TC0007 ==="
}

$results = @(Get-ChildItem allure-results -Filter "*-result.json" -File)
Write-Host "Allure test result files: $($results.Count)"

if ($results.Count -gt 0) {
    Write-Host "Normalizing Allure Tests-tree hierarchy..."
    python scripts/normalize-allure-hierarchy.py allure-results
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Allure hierarchy normalization failed with exit code $LASTEXITCODE."
        $reportExitCode = 1
    }

    if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
        Write-Warning "npx was not found, so the Allure report cannot be generated."
        $reportExitCode = 1
    } elseif ($reportExitCode -eq 0) {
        Write-Host "Generating Allure 3 report..."
        npx -y allure@3.16.0 generate allure-results --config ./allurerc.mjs
        $reportExitCode = $LASTEXITCODE
        if ($reportExitCode -eq 0) {
            Write-Host ""
            Write-Host "Allure report generated: $PWD\allure-report"
            Write-Host "Open it with: npx -y allure@3.16.0 open allure-report"
            Write-Host "Raw screenshots: $PWD\test-artifacts"
        } else {
            Write-Warning "Allure report generation failed with exit code $reportExitCode."
        }
    }
} else {
    Write-Warning "No Allure result files were produced."
    $reportExitCode = 1
}

Write-Host ""
Write-Host "=== Local suite summary ==="
Write-Host "FlaUI (TC0001-TC0004, TC0006): exit $flauiExitCode"
Write-Host "Python / pywinauto (TC0005, TC0007): exit $pythonExitCode"
Write-Host "Allure report: exit $reportExitCode"
Write-Host "==========================="

if ($flauiExitCode -ne 0 -or $pythonExitCode -ne 0 -or $reportExitCode -ne 0) {
    throw "Local desktop automation run failed. See the stack summary above."
}
