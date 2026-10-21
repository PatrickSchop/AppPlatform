#!/usr/bin/env bash
# Instantiates the tinyapp template once per tenancy choice and checks what each variant contains,
# that it builds, and which functions it exposes. Uses a throwaway template hive and a local package
# feed built from this checkout, so it changes nothing on the machine and needs no published packages.
#
#   templates/test-template.sh
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# dotnet is a native program: give it native paths (git-bash on Windows rewrites them otherwise).
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }

fail() { echo "FAIL: $*" >&2; exit 1; }
has()  { grep -q -- "$2" "$1" || fail "$1 should contain '$2'"; }
lacks(){ if grep -q -- "$2" "$1"; then fail "$1 should not contain '$2'"; fi; }
exists(){ [ -e "$1" ] || fail "missing $1"; }
absent(){ [ ! -e "$1" ] || fail "unexpected $1"; }

version="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$repo/Directory.Build.props" | head -1)"
platform="$version-templatetest"   # explicit, so it does not depend on the CI/local suffix rules
echo "Packing PS.AppPlatform $platform into a local feed"
dotnet pack "$repo/src/PS.AppPlatform/PS.AppPlatform.csproj" -c Release -p:Version="$platform" -o "$work/feed" >/dev/null
dotnet pack "$repo/src/PS.AppPlatform.Functions/PS.AppPlatform.Functions.csproj" -c Release -p:Version="$platform" -o "$work/feed" >/dev/null

hive="$(native "$work/hive")"
dotnet new install "$(native "$repo/templates")" --debug:custom-hive "$hive" >/dev/null

for tenancy in none single multi; do
  echo "=== --tenancy $tenancy"
  app="$work/$tenancy"
  dotnet new tinyapp -n "TT$tenancy" -o "$(native "$app")" --tenancy "$tenancy" \
    --azure-app-name "tt$tenancy" --platform-version "$platform" --skipRestore true \
    --debug:custom-hive "$hive" >/dev/null

  # Never any template syntax left behind.
  if grep -rIlE '^\s*(//|#)\s*#?(if|else|endif)\b.*(TenancyEnabled)|<!--#|TENANCY-MODE-PLACEHOLDER|MANAGEMENT-(URL|AUDIENCE)-PLACEHOLDER' "$app" >/dev/null; then
    fail "template markers were left in the generated app"
  fi

  csproj="$app/TT$tenancy.csproj"
  if [ "$tenancy" = none ]; then
    absent "$app/Manifest.cs"; absent "$app/Data/Item.cs"; absent "$app/Api"; absent "$app/Database/Scripts/110_CreateItems.sql"
    lacks "$csproj" PlatformTenancy
    lacks "$app/Program.cs" AddPlatformTenancy
    lacks "$app/appsettings.json" tenancy
    lacks "$app/appsettings.development.json" tenancy
    lacks "$app/.github/workflows/deploy.yaml" register
    lacks "$app/Data/AppDbContext.cs" "DbSet<Item>"
    lacks "$app/README.md" "## Tenancy"
    lacks "$app/docs/deployment.md" "registering with the management app"
  else
    mode="Single"; [ "$tenancy" = multi ] && mode="Multi"
    exists "$app/Manifest.cs"; exists "$app/Data/Item.cs"; exists "$app/Api/ItemsEndpoints.cs"
    exists "$app/Api/ItemsFunctions.cs"; exists "$app/Database/Scripts/110_CreateItems.sql"
    has "$csproj" "<PlatformTenancy>true</PlatformTenancy>"
    has "$app/Program.cs" AddPlatformTenancy
    has "$app/Manifest.cs" "TenancyMode.$mode"
    has "$app/Manifest.cs" "\"tt$tenancy\""
    has "$app/appsettings.json" "\"mode\": \"$mode\""
    has "$app/appsettings.development.json" "devDirectory"
    has "$app/.github/workflows/deploy.yaml" "register: true"
    has "$app/Data/AppDbContext.cs" "DbSet<Item>"
    has "$app/README.md" "tenancy $mode"
    has "$app/docs/deployment.md" "registering with the management app"
  fi

  # Valid JSON, whatever the conditionals did.
  for json in appsettings.json appsettings.development.json; do
    python3 -c "import json,sys; json.load(open(sys.argv[1], encoding='utf-8-sig'))" "$app/$json" 2>/dev/null \
      || python -c "import json,sys; json.load(open(sys.argv[1], encoding='utf-8-sig'))" "$app/$json" \
      || fail "$json is not valid JSON"
  done

  # Restore from the local feed (platform packages) and nuget.org (everything else).
  cat > "$app/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(native "$work/feed")" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="PS.AppPlatform*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF

  dotnet build "$(native "$app")" -c Release >"$work/build-$tenancy.log" 2>&1 \
    || { tail -30 "$work/build-$tenancy.log"; fail "generated app ($tenancy) does not build"; }

  metadata="$(find "$app/bin" -name functions.metadata | head -1)"
  [ -n "$metadata" ] || fail "no functions.metadata for $tenancy"
  if [ "$tenancy" = none ]; then
    lacks "$metadata" GetMyTenants
    lacks "$metadata" GetItems
  else
    has "$metadata" GetMyTenants
    has "$metadata" GetItems
    has "$metadata" CreateItem
  fi
  echo "ok"
done

echo "All three variants are as expected."
