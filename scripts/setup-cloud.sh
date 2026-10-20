#!/usr/bin/env bash
set -euo pipefail
cd /workspace/ARMpulse
source scripts/cloud-env.sh
mkdir -p /workspace/tooling
if [[ ! -x "$DOTNET_ROOT/dotnet" ]] || [[ "$(dotnet --version)" != "10.0.402" ]]; then
  metadata=$(mktemp /tmp/armpulse-dotnet-metadata.XXXXXX)
  archive=$(mktemp /tmp/armpulse-dotnet-sdk.XXXXXX)
  trap 'rm -f "$metadata" "$archive"' EXIT
  curl --fail --location --silent --show-error \
    https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json -o "$metadata"
  python - "$metadata" "$archive" "$DOTNET_ROOT" <<'PY'
import hashlib, json, subprocess, sys, tarfile
metadata, archive, destination = sys.argv[1:]
releases = json.load(open(metadata))['releases']
sdk = next(s for r in releases for s in r.get('sdks', []) if s['version'] == '10.0.402')
artifact = next(f for f in sdk['files'] if f['rid'] == 'linux-x64' and f['name'].endswith('.tar.gz'))
assert artifact['url'].startswith('https://builds.dotnet.microsoft.com/dotnet/')
subprocess.run(['curl', '--fail', '--location', '--silent', '--show-error', artifact['url'], '-o', archive], check=True)
with open(archive, 'rb') as stream:
    actual = hashlib.file_digest(stream, 'sha512').hexdigest()
if actual.lower() != artifact['hash'].lower():
    raise RuntimeError('Official SDK SHA-512 verification failed')
with tarfile.open(archive) as package:
    package.extractall(destination, filter='data')
PY
fi
dotnet restore ARMpulse.slnx --locked-mode
dotnet build ARMpulse.slnx -c Release --no-restore
dotnet test tests/ARMpulse.Tests -c Release --no-build --no-restore
