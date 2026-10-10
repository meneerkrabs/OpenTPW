"""Runs OpenTPW's C# side of the oracle (oracle/dotnet/OracleBridge) on batches of JSON requests.

The bridge is built once per checkout state into a cache directory outside the checkout
($XDG_CACHE_HOME/opentpw-ppc/oracle-bridge or ~/.cache/...). The SDK comes from OPENTPW_DOTNET, else
the default SDK location used by this project's tooling, else `dotnet` on PATH. `available()` is
False when no SDK is found; tests then skip their C# comparison.
"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE / 'dotnet' / 'OracleBridge.csproj'
DEFAULT_SDK = Path.home() / '.local' / 'share' / 'opentpw-dotnet10' / 'dotnet'
REPO = HERE.parents[2]


def dotnet() -> Path | None:
    configured = os.environ.get('OPENTPW_DOTNET')
    if configured:
        return Path(configured) if Path(configured).is_file() else None
    if DEFAULT_SDK.is_file():
        return DEFAULT_SDK
    found = shutil.which('dotnet')
    return Path(found) if found else None


def _cache() -> Path:
    base = os.environ.get('XDG_CACHE_HOME') or str(Path.home() / '.cache')
    return Path(base) / 'opentpw-ppc' / 'oracle-bridge'


def _source_digest() -> str:
    """Digest of the bridge and the C# sources it compiles against (rebuild when any changes)."""
    digest = hashlib.sha256()
    roots = [HERE / 'dotnet', REPO / 'source' / 'OpenTPW', REPO / 'source' / 'OpenTPW.Files',
             REPO / 'source' / 'OpenTPW.Tests' / 'EconomyTestData.cs']
    for root in roots:
        files = [root] if root.is_file() else sorted(p for p in root.rglob('*')
                                                      if p.suffix in ('.cs', '.csproj') and not {'bin', 'obj'} & set(p.parts))
        for file in files:
            digest.update(str(file.relative_to(REPO)).encode())
            digest.update(file.read_bytes())
    return digest.hexdigest()[:16]


class Bridge:
    def __init__(self, sdk: Path, project: Path = PROJECT, output: Path | None = None):
        """`project`/`output` let a test build the bridge against a mutated copy of the C# sources."""
        self.sdk = sdk
        self.project = project
        self.output = output or _cache() / _source_digest()
        self.env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
                    'DOTNET_ROOT': str(sdk.resolve().parent)}
        self._capabilities: list[str] | None = None

    def build(self) -> None:
        if (self.output / 'OracleBridge.dll').is_file():
            return
        process = subprocess.run([str(self.sdk), 'build', str(self.project), '-c', 'Release', '-o', str(self.output)],
                                 env=self.env, capture_output=True, text=True, timeout=900)
        if process.returncode != 0:
            raise RuntimeError('oracle bridge build failed:\n' + (process.stdout + process.stderr)[-4000:])

    def run(self, requests: list[dict]) -> list:
        self.build()
        payload = ''.join(json.dumps(request) + '\n' for request in requests)
        process = subprocess.run([str(self.sdk), str(self.output / 'OracleBridge.dll')], input=payload, env=self.env,
                                 capture_output=True, text=True, timeout=900)
        if process.returncode != 0:
            raise RuntimeError('oracle bridge failed:\n' + process.stderr[-4000:])
        lines = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
        if len(lines) != len(requests):
            raise RuntimeError(f'oracle bridge answered {len(lines)} of {len(requests)} requests')
        return lines

    def capabilities(self) -> list[str]:
        if self._capabilities is None:
            self._capabilities = self.run([{'fn': 'capabilities'}])[0]
        return self._capabilities


_bridge: Bridge | None = None


def get() -> Bridge | None:
    global _bridge
    if _bridge is None:
        sdk = dotnet()
        if sdk is None:
            return None
        _bridge = Bridge(sdk)
    return _bridge
