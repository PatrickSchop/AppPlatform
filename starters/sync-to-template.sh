#!/usr/bin/env bash
# Copies starters/angular into the tinyapp template as WebApp/ (the Frontend=angular content).
# The template copy differs from the starter in three ways, all applied here:
#   - the platform packages come from the registry by version, not from file: paths in this repo
#   - the app title is the template's sourceName, which dotnet new replaces with the app's name
#   - the example page lists /api/items, which the template's tenancy variants serve
#
#   starters/sync-to-template.sh
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src="$repo/starters/angular"
dest="$repo/templates/content/tinyapp/WebApp"
version="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$repo/Directory.Build.props" | head -1)"

rm -rf "$dest"
mkdir -p "$dest"
tar -C "$src" --exclude=node_modules --exclude=dist --exclude=.angular --exclude=package-lock.json -cf - . | tar -C "$dest" -xf -

sed -i -E "s#\"file:[^\"]*app-client\"#\"^$version\"#; s#\"file:[^\"]*app-client-angular/dist\"#\"^$version\"#" "$dest/package.json"
sed -i "s/Angular Starter/TinyApp/" "$dest/public/configuration.json" "$dest/src/index.html"
sed -i "s/const EXAMPLE_PATH = 'notes'/const EXAMPLE_PATH = 'items'/" "$dest/src/app/services/example.service.ts"
printf '@patrickschop:registry=https://npm.pkg.github.com\n' > "$dest/.npmrc"
echo "Synced $dest (platform packages ^$version)"
