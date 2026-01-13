$Project = "./SteamWorkshopManager/SteamWorkshopManager.csproj"
$Config  = "Release"

Write-Host "🔄 Restore..." -ForegroundColor Cyan
dotnet restore $Project

Write-Host "🧹 Clean..." -ForegroundColor Cyan
dotnet clean $Project -c $Config

# =====================
# Windows
# =====================
Write-Host "🪟 Publish Windows..." -ForegroundColor Cyan
dotnet publish $Project `
  -c $Config `
  -r win-x64 `
  --self-contained false `
  /p:DebugType=None `
  /p:DebugSymbols=false `
  -o ./dist/windows

# =====================
# macOS
# =====================
Write-Host "🍎 Publish macOS..." -ForegroundColor Cyan
dotnet publish $Project `
  -c $Config `
  -r osx-x64 `
  --self-contained false `
  /p:DebugType=None `
  /p:DebugSymbols=false `
  -o ./dist/macos

# =====================
# Linux
# =====================
Write-Host "🐧 Publish Linux..." -ForegroundColor Cyan
dotnet publish $Project `
  -c $Config `
  -r linux-x64 `
  --self-contained false `
  /p:DebugType=None `
  /p:DebugSymbols=false `
  -o ./dist/linux

Write-Host "✅ Publish terminé !" -ForegroundColor Green
