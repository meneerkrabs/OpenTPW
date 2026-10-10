"""Runs OpenTPW's C# side of the oracle (oracle/dotnet/OracleBridge) on batches of JSON requests.

The bridge is built once per checkout state into a cache directory outside the checkout
($XDG_CACHE_HOME/opentpw-ppc/oracle-bridge or ~/.cache/...), keyed by a digest of every input of the
build (`_source_digest`). The build runs in a scratch copy of those inputs, so no bin/ or obj/ lands in
the checkout.

The bridge targets net10.0, so it needs a .NET 10 SDK. The candidates, in order: OPENTPW_ORACLE_DOTNET
(set by run_evidence_checks.py from its --dotnet flag), OPENTPW_DOTNET, the project's default SDK
location, `dotnet` on PATH. The first whose `--list-sdks` shows a 10.x SDK is used. `get()` returns
None when there is none, and a bridge that fails to build or run raises `BridgeError`; either way the
C# comparisons are skipped with the reason, and the plan comparisons still run.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
PROJECT = Path('tools') / 'ppc-analysis' / 'oracle' / 'dotnet' / 'OracleBridge.csproj'
DEFAULT_SDK = Path.home() / '.local' / 'share' / 'opentpw-dotnet10' / 'dotnet'
SDK_MAJOR = 10
# Every input of the bridge build, relative to the checkout: the bridge, the transitive ProjectReference closure of
# source/OpenTPW, the linked test data file and the SDK pin. `_check_inputs` fails when a project reference is missing.
INPUTS = [Path() / 'tools' / 'ppc-analysis' / 'oracle' / 'dotnet', Path() / 'source' / 'OpenTPW',
          Path() / 'source' / 'OpenTPW.Files', Path() / 'source' / 'OpenTPW.Common', Path() / 'source' / 'OpenTPW.ModKit',
          Path() / 'source' / 'OpenTPW.Online', Path() / 'source' / 'OpenTPW.Tests' / 'EconomyTestData.cs',
          Path('global.json')]
REFERENCE = re.compile(r'<ProjectReference\s+Include="([^"]+)"')


class BridgeError(RuntimeError):
    """The bridge could not be built or run; the C# comparisons are skipped with this message."""


def sdk_major_versions(sdk: Path) -> set[int]:
    try:
        process = subprocess.run([str(sdk), '--list-sdks'], capture_output=True, text=True, timeout=120,
                                 env={**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1'})
    except (OSError, subprocess.SubprocessError):
        return set()
    return {int(match) for match in re.findall(r'^(\d+)\.', process.stdout, re.MULTILINE)}


def candidates() -> list[Path]:
    found = [os.environ.get('OPENTPW_ORACLE_DOTNET'), os.environ.get('OPENTPW_DOTNET'), str(DEFAULT_SDK),
             shutil.which('dotnet')]
    return [Path(value) for value in dict.fromkeys(found) if value and Path(value).is_file()]


def dotnet() -> Path | None:
    """The first candidate SDK with a .NET 10 SDK, or None."""
    for sdk in candidates():
        if SDK_MAJOR in sdk_major_versions(sdk):
            return sdk
    return None


def _cache() -> Path:
    base = os.environ.get('XDG_CACHE_HOME') or str(Path.home() / '.cache')
    return Path(base) / 'opentpw-ppc' / 'oracle-bridge'


def _files(root: Path, entry: Path) -> list[Path]:
    path = root / entry
    if path.is_file():
        return [path]
    return sorted(p for p in path.rglob('*') if p.is_file() and not {'bin', 'obj'} & set(p.relative_to(root).parts))


def _check_inputs(root: Path) -> None:
    """Every project reachable from the bridge through ProjectReference must be one of INPUTS."""
    pending, seen = [root / PROJECT], set()
    while pending:
        project = pending.pop().resolve()
        if project in seen:
            continue
        seen.add(project)
        for include in REFERENCE.findall(project.read_text(encoding='utf-8')):
            pending.append(project.parent / include.replace('\\', '/'))
    inputs = [(root / entry).resolve() for entry in INPUTS]
    missing = [p for p in seen if not any(p == i or i in p.parents for i in inputs)]
    if missing:
        raise BridgeError('oracle bridge inputs miss ' + ', '.join(sorted(p.relative_to(root.resolve()).as_posix()
                                                                           for p in missing)))


def _source_digest(root: Path = REPO) -> str:
    """Digest of every bridge build input (rebuild when any changes); paths as POSIX so the key is OS-independent."""
    _check_inputs(root)
    digest = hashlib.sha256()
    for entry in INPUTS:
        for file in _files(root, entry):
            digest.update(file.relative_to(root).as_posix().encode() + b'\0')
            digest.update(file.read_bytes())
    return digest.hexdigest()[:16]


class Bridge:
    def __init__(self, sdk: Path, root: Path = REPO, output: Path | None = None):
        """`root`/`output` let a test build the bridge against a mutated copy of the C# sources."""
        self.sdk = sdk
        self.root = root
        self.output = output or _cache() / _source_digest(root)
        self.env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1',
                    'DOTNET_ROOT': str(sdk.resolve().parent)}
        self._capabilities: list[str] | None = None
        self._failure: str | None = None

    def build(self) -> None:
        if self._failure:
            raise BridgeError(self._failure)
        if (self.output / 'OracleBridge.dll').is_file():
            return
        with tempfile.TemporaryDirectory(prefix='oracle-bridge-') as scratch:
            scratch = Path(scratch)
            for entry in INPUTS:
                for file in _files(self.root, entry):
                    target = scratch / file.relative_to(self.root)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(file, target)
            staging = scratch / 'out'
            try:
                process = subprocess.run([str(self.sdk), 'build', str(scratch / PROJECT), '-c', 'Release', '-o', str(staging)],
                                         env=self.env, capture_output=True, text=True, timeout=900, cwd=scratch)
            except (OSError, subprocess.SubprocessError) as error:
                self._failure = f'oracle bridge build failed: {error}'
                raise BridgeError(self._failure) from error
            if process.returncode != 0:
                self._failure = 'oracle bridge build failed:\n' + (process.stdout + process.stderr)[-4000:]
                raise BridgeError(self._failure)
            self.output.parent.mkdir(parents=True, exist_ok=True)
            shutil.rmtree(self.output, ignore_errors=True)
            shutil.copytree(staging, self.output)

    def run(self, requests: list[dict]) -> list:
        self.build()
        payload = ''.join(json.dumps(request) + '\n' for request in requests)
        try:
            process = subprocess.run([str(self.sdk), str(self.output / 'OracleBridge.dll')], input=payload, env=self.env,
                                     capture_output=True, text=True, timeout=900)
        except (OSError, subprocess.SubprocessError) as error:
            raise BridgeError(f'oracle bridge failed: {error}') from error
        if process.returncode != 0:
            raise BridgeError('oracle bridge failed:\n' + process.stderr[-4000:])
        lines = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
        if len(lines) != len(requests):
            raise BridgeError(f'oracle bridge answered {len(lines)} of {len(requests)} requests')
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
