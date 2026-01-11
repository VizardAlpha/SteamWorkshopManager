# Restore
dotnet restore

# Clean
dotnet clean -c Release

# Publish Windows
dotnet publish ./SteamWorkshopManager/SteamWorkshopManager.csproj -c Release -r win-x64 -o ./SteamWorkshopManager/bin/Publish/win
Remove-Item ./SteamWorkshopManager/bin/Publish/win/libsteam_api.dylib -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/win/libsteam_api.so -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/win/*.pdb -ErrorAction SilentlyContinue

# Publish Mac
dotnet publish ./SteamWorkshopManager/SteamWorkshopManager.csproj -c Release -r osx-x64 -o ./SteamWorkshopManager/bin/Publish/mac
Remove-Item ./SteamWorkshopManager/bin/Publish/mac/steam_api64.dll -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/mac/libsteam_api.so -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/mac/*.pdb -ErrorAction SilentlyContinue

# Publish Linux
dotnet publish ./SteamWorkshopManager/SteamWorkshopManager.csproj -c Release -r linux-x64 -o ./SteamWorkshopManager/bin/Publish/linux
Remove-Item ./SteamWorkshopManager/bin/Publish/linux/steam_api64.dll -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/linux/libsteam_api.dylib -ErrorAction SilentlyContinue
Remove-Item ./SteamWorkshopManager/bin/Publish/linux/*.pdb -ErrorAction SilentlyContinue

Write-Host "✅ Publish terminé !" -ForegroundColor Green