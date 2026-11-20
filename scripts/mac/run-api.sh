#!/usr/bin/env zsh
# Arranca la API en macOS usando el SDK de ~/.dotnet y la conexión de .env.mac.local (no versionado).
set -e
ROOT="${0:A:h}/../.."
cd "$ROOT"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
if [[ -f .env.mac.local ]]; then
  source .env.mac.local
else
  echo "Falta .env.mac.local con ConnectionStrings__DefaultConnection (ver docs/AL00_iOS_macOS.md)." >&2
  exit 1
fi
docker start univalle-sql >/dev/null 2>&1 || true
exec dotnet run --project src/Univalle.Emergencias.Api --launch-profile http
