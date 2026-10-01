#!/usr/bin/env bash
# Builds one mod DLL and audits that same binary against both supported games.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ $# -ne 3 ]]; then
  echo "Usage: verify.sh <Valheim Old root> <Valheim Latest root> <BepInEx root>" >&2
  exit 2
fi

old_game="$1"
latest_game="$2"
bepinex_root="$3"
project="Landoria.WorldCrawler.csproj"
assembly="bin/Universal/Landoria.WorldCrawler.dll"

dotnet msbuild "$project" -restore -t:Rebuild -p:Configuration=Release \
  "-p:ValheimGamePath=$old_game" "-p:BepInExPath=$bepinex_root" \
  -p:OutputPath=bin/Universal/ -verbosity:minimal

for game_root in "$old_game" "$latest_game"; do
  dotnet msbuild "$project" -t:AuditExport -p:Configuration=Release \
    "-p:ValheimGamePath=$game_root" "-p:BepInExPath=$bepinex_root" \
    "-p:AuditAssembly=$assembly" -verbosity:minimal
  dotnet run --project tools/BindingAudit/BindingAudit.csproj -c Release -- \
    "$assembly" "$game_root/valheim_Data/Managed" "$bepinex_root/core"
done

dotnet run --project tests/CoreTests/CoreTests.csproj -c Release
