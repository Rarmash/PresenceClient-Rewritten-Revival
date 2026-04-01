Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pythonDir = Join-Path $root "PresenceClient-Py"
$guiProject = Join-Path $root "PresenceClient-GUI\PresenceClient-GUI.csproj"
$releaseDir = Join-Path $root "release"
$backendDistDir = Join-Path $pythonDir "dist"
$backendBuildDir = Join-Path $pythonDir "build"
$backendSpec = Join-Path $pythonDir "PresenceClient-Backend.spec"
$backendExe = Join-Path $backendDistDir "PresenceClient-Backend.exe"

function Resolve-PythonCommand {
    if (Get-Command python -ErrorAction SilentlyContinue) {
        return ,([string[]]@("python"))
    }

    if (Get-Command py -ErrorAction SilentlyContinue) {
        return ,([string[]]@("py", "-3"))
    }

    throw "Python was not found in PATH."
}

function Invoke-Python {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $python = Resolve-PythonCommand
    $command = $python[0]
    $prefixArguments = @()
    if ($python.Length -gt 1) {
        $prefixArguments = $python[1..($python.Length - 1)]
    }

    & $command @prefixArguments @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Python command failed: $($Arguments -join ' ')"
    }
}

Write-Host "Preparing release directory..."
if (Test-Path $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir | Out-Null

Write-Host "Installing Python backend build dependencies..."
Invoke-Python -Arguments @("-m", "pip", "install", "-r", (Join-Path $pythonDir "requirements.txt"))

Write-Host "Building Python backend executable..."
if (Test-Path $backendDistDir) { Remove-Item -LiteralPath $backendDistDir -Recurse -Force }
if (Test-Path $backendBuildDir) { Remove-Item -LiteralPath $backendBuildDir -Recurse -Force }
if (Test-Path $backendSpec) { Remove-Item -LiteralPath $backendSpec -Force }

Invoke-Python -Arguments @(
    "-m", "PyInstaller",
    "--noconfirm",
    "--onefile",
    "--name", "PresenceClient-Backend",
    "--distpath", $backendDistDir,
    "--workpath", $backendBuildDir,
    "--specpath", $pythonDir,
    (Join-Path $pythonDir "presence-client.py")
)

if (-not (Test-Path $backendExe)) {
    throw "Python backend executable was not produced."
}

Write-Host "Publishing GUI client..."
dotnet publish $guiProject -c Release -r win-x64 --self-contained true -o $releaseDir /p:PublishAot=false /p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "Copying Python backend into release folder..."
Copy-Item -LiteralPath $backendExe -Destination (Join-Path $releaseDir "PresenceClient-Backend.exe") -Force

Write-Host "Release ready:"
Write-Host $releaseDir
