#!/usr/bin/env bash
set -euo pipefail

release_id="${1:?release id is required}"
web_port="${2:-8089}"
root=/opt/desktop-automation-allure
archive=/tmp/desktop-automation-allure.tgz
release="$root/releases/$release_id"
site="$root/site"
link_tmp="$root/.site-$release_id"

mkdir -p "$root/releases"
rm -rf "$release"
mkdir -p "$release"
tar -xzf "$archive" -C "$release"
rm -f "$archive"

if [[ -d "$site" && ! -L "$site" ]]; then
  legacy="$root/releases/bootstrap-$(date +%s)"
  mv "$site" "$legacy"
fi

rm -f "$link_tmp"
ln -s "$release" "$link_tmp"
mv -Tf "$link_tmp" "$site"

curl --fail --silent --show-error "http://127.0.0.1:${web_port}/" >/dev/null

# Keep the five newest release directories. The live symlink always targets
# the newest release, so pruning old releases does not interrupt serving.
mapfile -t old_releases < <(
  find "$root/releases" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' \
    | sort -nr \
    | tail -n +6 \
    | cut -d' ' -f2-
)

for old_release in "${old_releases[@]}"; do
  rm -rf "$old_release"
done

echo "Allure release $release_id is live via $site"
