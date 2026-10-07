param(
    [ValidateSet('Build','Test','Publish')][string]$Action = 'Build',
    [string]$Python = 'python',
    [string]$Node = 'node'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $Python build-ui.py
    if ($LASTEXITCODE -ne 0) { throw 'UI generation failed.' }
    & $Python package-ui.py
    if ($LASTEXITCODE -ne 0) { throw 'UI resource packaging failed.' }
    dotnet restore -r win-x64 -p:RuntimeFrameworkVersion=10.0.8 -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    if ($Action -eq 'Publish') {
        dotnet publish --no-restore -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.8 -p:PortableRelease=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o publish-build/win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        New-Item -ItemType Directory -Path export/Windows-x64 -Force | Out-Null
        Copy-Item -LiteralPath publish-build/win-x64/CodexReceipt.exe -Destination export/Windows-x64/CodexReceipt.exe -Force
        Write-Host 'EXE: export/Windows-x64/CodexReceipt.exe'
    } else {
        dotnet build --no-restore -c Release -r win-x64 -p:RuntimeFrameworkVersion=10.0.8 -o bin/Development
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        if ($Action -eq 'Test') {
            foreach ($test in @('test-special.cjs','test-paper.cjs','test-folds.cjs','test-tear.cjs')) {
                & $Node $test
                if ($LASTEXITCODE -ne 0) { throw "Test failed: $test" }
            }
            dotnet bin/Development/CodexReceipt.dll --self-test
            if ($LASTEXITCODE -ne 0) { throw 'C# self-tests failed.' }
            Write-Host 'Tests passed. Results: .verification/self-test/self-test.json'
        }
    }
} finally { Pop-Location }
