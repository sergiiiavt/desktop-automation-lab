$ErrorActionPreference = "Stop"
$env:DESKTOP_TEST_ENVIRONMENT = "local-windows"

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
Write-Host "Running FULL FlaUI suite (Baseline + ModernNotepad)..."
dotnet test flaui/Notepad.Tests/Notepad.Tests.csproj --configuration Debug

Write-Host "Running pywinauto suite..."
python -m pytest python/tests -vv
