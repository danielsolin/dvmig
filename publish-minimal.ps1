# Build a minimal, single-file release of the dvmig CLI
dotnet publish src/dvmig.Cli/dvmig.Cli.csproj -p:Minimal=true
Write-Host "`nDone! Executable is at: src/dvmig.Cli/bin/Minimal/dvmig.Cli.exe" -ForegroundColor Green
