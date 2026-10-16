#!/usr/bin/env bash
set -euo pipefail

directory="${1:?release archive directory required}"
version="${RELEASE_VERSION:?release version required}"
preview="${RELEASE_PRERELEASE:-false}"
repository="${GITHUB_REPOSITORY:?repository required}"
source_commit="$(git rev-parse HEAD)"
if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ || ! "$preview" =~ ^(true|false)$ ]]; then
  echo 'Invalid release version or preview flag.' >&2
  exit 1
fi
if [[ "$source_commit" != "$(git rev-parse "${RELEASE_COMMIT:-${GITHUB_SHA:?source SHA required}}^{commit}")" ]]; then
  echo 'Release checkout does not match the built commit.' >&2
  exit 1
fi

archives=(OpenTPW-osx-arm64.tar.gz OpenTPW-win-arm64.zip OpenTPW-win-x64.zip OpenTPW-win-x86.zip OpenTPW-linux-x64.tar.gz OpenTPW-linux-arm64.tar.gz)
for archive in "${archives[@]}"; do
  if [[ ! -s "$directory/$archive" ]]; then
    echo "Missing or empty release archive: $archive" >&2
    exit 1
  fi
done

if tag_commit="$(git rev-parse --verify "refs/tags/$version^{commit}" 2>/dev/null)"; then
  if [[ "$tag_commit" != "$source_commit" ]]; then
    echo 'Existing tag points at a different commit; use a new version.' >&2
    exit 1
  fi
else
  gh api --method POST "repos/$repository/git/refs" -f "ref=refs/tags/$version" -f "sha=$source_commit" >/dev/null
fi

state="$(mktemp)"
trap 'rm -f "$state"' EXIT
if gh release view "$version" --repo "$repository" --json isDraft,isPrerelease,assets > "$state" 2>/dev/null; then
  status="$(python3 - "$state" "${archives[@]}" SHA256SUMS.txt <<'PY'
import json,sys
release=json.load(open(sys.argv[1]))
complete=set(sys.argv[2:]).issubset({asset['name'] for asset in release['assets']})
print('complete' if complete and not release['isDraft'] else 'repair')
PY
)"
  if [[ "$status" == complete ]]; then
    echo "$version already has all six builds; nothing to publish."
    exit 0
  fi
  # Incomplete versions must not advertise a partial set of downloadable builds.
  gh release edit "$version" --repo "$repository" --draft=true
else
  notes=()
  if [[ -n "${RELEASE_EXTRA_NOTES:-}" ]]; then notes=(--notes "$RELEASE_EXTRA_NOTES"); fi
  gh release create "$version" --repo "$repository" --verify-tag --draft --title "OpenTPW $version" --generate-notes ${notes[@]+"${notes[@]}"}
fi

python3 - "$directory" "$source_commit" "${archives[@]}" <<'PY'
import hashlib,pathlib,sys
directory=pathlib.Path(sys.argv[1])
with (directory/'SHA256SUMS.txt').open('w') as output:
    output.write('# Source commit: '+sys.argv[2]+'\n')
    for name in sys.argv[3:]:
        digest=hashlib.sha256()
        with (directory/name).open('rb') as archive:
            for block in iter(lambda: archive.read(1024*1024), b''):
                digest.update(block)
        output.write(digest.hexdigest()+'  '+name+'\n')
PY
files=()
for archive in "${archives[@]}"; do files+=("$directory/$archive"); done
gh release upload "$version" "${files[@]}" "$directory/SHA256SUMS.txt" --repo "$repository" --clobber
gh release view "$version" --repo "$repository" --json assets > "$state"
python3 - "$state" "${archives[@]}" SHA256SUMS.txt <<'PY'
import json,sys
assets={asset['name'] for asset in json.load(open(sys.argv[1]))['assets']}
missing=set(sys.argv[2:])-assets
if missing:
    raise SystemExit('Uploaded release is incomplete: '+', '.join(sorted(missing)))
PY
latest=true
if [[ "$preview" == true ]]; then latest=false; fi
gh release edit "$version" --repo "$repository" --draft=false --prerelease="$preview" --latest="$latest"
echo "Published $version from $source_commit with all six builds and checksums."
