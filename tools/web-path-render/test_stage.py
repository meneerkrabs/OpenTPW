"""Synthetic parser/staging tests only. Fake DLL/report records are NOT PE/GPU evidence."""
import base64
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('stage', Path(__file__).with_name('stage.py'))
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


class StageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / 'repo'
        self.web = self.root / 'published/wwwroot'
        self.sdk = self.root / 'sdk'
        self.inputs = self.root / 'inputs'
        self.build = self.root / 'build'
        for path in [self.repo, self.web, self.sdk, self.inputs, self.build]:
            path.mkdir(parents=True)
        self.allow = {'schema': 1, 'projectAssemblies': sorted(stage.APP_ASSEMBLIES), 'shaders': [], 'static': []}
        for name in sorted(stage.SHADERS):
            data = ('synthetic text fixture ' + name).encode()
            self.write(self.repo / name, data)
            self.allow['shaders'].append({'path': name, 'logicalName': name, 'sha256': stage.sha(data)})
        for name, output in stage.STATIC.items():
            data = ('synthetic text fixture ' + name).encode()
            self.write(self.repo / name, data)
            self.write(self.web / output, data)
            self.allow['static'].append({'path': name, 'output': output, 'sha256': stage.sha(data)})
        self.write(self.repo / stage.HELPER, b'// synthetic helper\n')
        self.allow['syntheticHelper'] = {'path': stage.HELPER, 'sha256': stage.sha(stage.read(self.repo / stage.HELPER))}
        self.allow_bytes = json.dumps(self.allow).encode()
        self.write(self.repo / 'tools/web-path-render/artifact-allowlist.json', self.allow_bytes)
        for name in stage.TOOLS:
            if name.endswith('artifact-allowlist.json'):
                continue
            self.write(self.repo / name, ('// synthetic fixture ' + name).encode())
        oracle = 'source/OpenTPW.Web/Program.PathRenderOracle.cs'
        self.write(self.repo / oracle, b'// synthetic compile fixture\n')
        self.write(self.inputs / 'compile.txt', '\n'.join(str(self.repo / name) for name in [stage.HELPER, oracle]).encode())
        self.write(self.inputs / 'resources.txt', '\n'.join(f'{self.repo / x["path"]}|{x["logicalName"]}' for x in self.allow['shaders']).encode())
        self.write(self.inputs / 'content.txt', '\n'.join(f'{self.repo / name}|wwwroot/{out}' if '/PathOracle/' in name else f'{self.repo / name}|'
                                                     for name, out in stage.STATIC.items()).encode())
        self.boot = {'mainAssemblyName': 'OpenTPW.Web', 'debugLevel': 0, 'resources': {}}
        self.pe = {'passed': True, 'errors': [], 'allowlistSha256': stage.sha(self.allow_bytes), 'assemblies': []}
        for name in sorted(stage.APP_ASSEMBLIES):
            self.asset('assembly', name + '.dll', ('FAKE-PE-parser-fixture-' + name).encode(), app=name)
        self.asset('coreAssembly', 'System.Private.CoreLib.dll', b'FAKE-public-runtime-DLL')
        self.asset('jsModuleNative', 'dotnet.native.fake.js', b'// fake SDK native module\n')
        self.asset('jsModuleRuntime', 'dotnet.runtime.fake.js', b'// fake SDK runtime module\n')
        self.asset('wasmNative', 'dotnet.native.fake.wasm', b'FAKE-sdk-runtime-wasm')
        self.asset('icu', 'icudt.fake.dat', b'FAKE-sdk-icu-data')
        self.template = b'const x = /*! dotnetBootConfig */ {}; /*! bundlerFriendlyImports */\n'
        self.write(self.sdk / 'dotnet.js', self.template)
        self.save_boot()
        self.pe_path = self.root / 'audit.json'
        self.write(self.pe_path, json.dumps(self.pe).encode())
        self.args = types.SimpleNamespace(repository_root=str(self.repo), web_root=str(self.web), inputs=str(self.inputs),
            build_root=str(self.build), pe_report=str(self.pe_path), output=str(self.root / 'stage'), source_sha='1' * 40,
            runtime_root=[str(self.sdk)])

    def write(self, path, data):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def asset(self, category, name, data, app=None):
        self.write(self.web / '_framework' / name, data)
        entry = {'name': name}
        if not name.endswith('.js'):
            entry['hash'] = 'sha256-' + base64.b64encode(bytes.fromhex(stage.sha(data))).decode()
        self.boot['resources'].setdefault(category, []).append(entry)
        if name.endswith('.dll'):
            self.pe['assemblies'].append({'path': '_framework/' + name, 'assemblyName': app or name[:-4],
                                         'sha256': stage.sha(data), 'resources': []})
        if app is None:
            self.write(self.sdk / name, data)

    def save_boot(self):
        config = '/*json-start*/' + json.dumps(self.boot) + '/*json-end*/'
        self.write(self.web / '_framework/dotnet.js', ('const x = ' + config + '; \n').encode())

    def assets(self):
        return stage.boot_assets(self.boot, self.web)

    def mock_git(self, command):
        if 'rev-parse' in command:
            return ('1' * 40 + '\n').encode()
        if 'status' in command:
            return b''
        if 'ls-tree' in command:
            return b'100644 blob 2222222222222222222222222222222222222222\tREADME.md\0'
        self.fail(f'unexpected command {command}')

    def run_stage(self):
        with patch.object(stage.subprocess, 'check_output', side_effect=self.mock_git):
            stage.stage(self.args)

    def test_synthetic_full_staging_preserves_exact_allowlisted_closure(self):
        self.write(self.web / '_framework/ignored.pdb', b'NEVER-UPLOAD')
        self.run_stage()
        output = Path(self.args.output)
        self.assertTrue((output / 'VERIFIED.txt').exists())
        self.assertFalse((output / 'wwwroot/_framework/ignored.pdb').exists())
        manifest = json.loads((output / 'artifact-manifest.json').read_text())
        for item in manifest['files']:
            self.assertEqual(stage.sha((output / item['path']).read_bytes()), item['sha256'])
        self.assertFalse(json.loads((output / 'provenance.json').read_text())['rendered'])

    def test_failure_does_not_create_stage(self):
        self.write(self.web / 'copied-save.sav', b'forbidden synthetic fixture')
        with self.assertRaisesRegex(ValueError, 'unexpected published'):
            self.run_stage()
        self.assertFalse(Path(self.args.output).exists())

    def test_unknown_resource_category_rejected(self):
        self.boot['resources']['newSdkCategory'] = []
        with self.assertRaisesRegex(ValueError, 'unknown boot resource'):
            self.assets()

    def test_vfs_corpus_rejected(self):
        self.boot['resources']['vfs'] = [{'name': 'game.dat'}]
        with self.assertRaisesRegex(ValueError, 'forbidden boot resource'):
            self.assets()

    def test_external_resource_rejected(self):
        self.boot['resources']['assembly'][0]['resolvedUrl'] = 'https://example.invalid/game.dll'
        with self.assertRaisesRegex(ValueError, 'external/bundler'):
            self.assets()

    def test_traversal_rejected(self):
        self.boot['resources']['assembly'][0]['name'] = '../game.dll'
        with self.assertRaisesRegex(ValueError, 'unsafe local'):
            self.assets()

    def test_extra_wasm_rejected(self):
        self.asset('wasmNative', 'game.wasm', b'forbidden synthetic app image')
        with self.assertRaisesRegex(ValueError, 'only interpreter'):
            self.assets()

    def test_webcil_metadata_rejected(self):
        self.boot['resources']['assembly'][0]['payloadSize'] = 42
        with self.assertRaisesRegex(ValueError, 'unknown asset fields'):
            self.assets()

    def test_mismatched_integrity_rejected(self):
        self.boot['resources']['assembly'][0]['hash'] = 'sha256-wrong'
        with self.assertRaisesRegex(ValueError, 'integrity mismatch'):
            self.assets()

    def test_unlisted_resource_file_rejected(self):
        self.write(self.web / 'renamed-original.bin', b'forbidden synthetic fixture')
        with self.assertRaisesRegex(ValueError, 'unexpected published'):
            self.run_stage()

    def test_existing_stage_rejected(self):
        Path(self.args.output).mkdir()
        with self.assertRaisesRegex(ValueError, 'must not exist'):
            self.run_stage()

    def test_symlink_rejected(self):
        (self.web / 'link.js').symlink_to(self.sdk / 'dotnet.js')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            self.run_stage()

    def test_tampered_sdk_module_rejected(self):
        self.asset('jsModuleDiagnostics', 'dotnet.diagnostics.fake.js', b'tampered')
        (self.sdk / 'dotnet.diagnostics.fake.js').unlink()
        self.save_boot()
        with self.assertRaisesRegex(ValueError, 'no exact SDK/NuGet'):
            self.run_stage()

    def test_tampered_loader_rejected(self):
        file = self.web / '_framework/dotnet.js'
        file.write_bytes(file.read_bytes() + b'evil();')
        with self.assertRaisesRegex(ValueError, 'exact SDK template'):
            self.run_stage()

    def test_dependency_dll_not_from_package_rejected(self):
        (self.sdk / 'System.Private.CoreLib.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'no exact SDK/NuGet'):
            self.run_stage()

    def test_failed_pe_report_rejected(self):
        self.pe['passed'] = False
        with self.assertRaisesRegex(ValueError, 'did not pass'):
            stage.bind_pe_report(self.web, self.pe, self.assets(), self.allow_bytes)

    def test_pe_report_hash_binding(self):
        self.pe['assemblies'][0]['sha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'not bound'):
            stage.bind_pe_report(self.web, self.pe, self.assets(), self.allow_bytes)

    def test_missing_pe_record_rejected(self):
        self.pe['assemblies'].pop()
        with self.assertRaisesRegex(ValueError, 'exact boot DLL'):
            stage.bind_pe_report(self.web, self.pe, self.assets(), self.allow_bytes)

    def test_extra_evaluated_resource_rejected(self):
        file = self.inputs / 'resources.txt'
        file.write_text(file.read_text() + '\n/game/media|content/game')
        with self.assertRaisesRegex(ValueError, 'exactly four shaders'):
            stage.audit_inputs(self.repo, self.inputs, self.build, self.allow)

    def test_test_corpus_source_rejected(self):
        file = self.inputs / 'compile.txt'
        file.write_text(file.read_text() + '\n' + str(self.repo / 'source/OpenTPW.Tests/Corpus.cs'))
        with self.assertRaisesRegex(ValueError, 'forbidden compiler'):
            stage.audit_inputs(self.repo, self.inputs, self.build, self.allow)

    def test_extra_evaluated_content_rejected(self):
        file = self.inputs / 'content.txt'
        file.write_text(file.read_text() + '\n/content/media|wwwroot/media')
        with self.assertRaisesRegex(ValueError, 'content differs'):
            stage.audit_inputs(self.repo, self.inputs, self.build, self.allow)

    def test_pin_mismatch_rejected(self):
        self.write(self.repo / stage.HELPER, b'changed')
        with self.assertRaisesRegex(ValueError, 'source pin differs'):
            stage.source_pins(self.repo, self.allow)

    def test_duplicate_json_rejected(self):
        with self.assertRaisesRegex(ValueError, 'duplicate JSON'):
            stage.parse_json('{"resources":{},"resources":{}}')

    def test_external_boot_cannot_execute_code(self):
        self.write(self.web / '_framework/dotnet.js', b'// noninline loader')
        self.write(self.web / '_framework/dotnet.boot.js', b'export const config = /*json-start*/{}/*json-end*/;evil();')
        with self.assertRaisesRegex(ValueError, 'unexpected executable'):
            stage.load_boot(self.web)

    def test_inline_boot_round_trip(self):
        self.assertEqual(stage.load_boot(self.web), (self.boot, None))

    def test_runtime_config_rejects_arbitrary_payload(self):
        self.boot['runtimeConfig'] = {'runtimeOptions': {'configProperties': {'System.Bad': 'opaque payload'}}}
        with self.assertRaisesRegex(ValueError, 'unsupported runtime config'):
            self.assets()

    def test_boot_scalar_payloads_rejected(self):
        variants = [
            ('applicationEnvironment', 'SYNTHETIC_OPAQUE_PAYLOAD_' + 'x' * 4096),
            ('appendElementOnExit', {'opaque': 'payload'}),
            ('debugLevel', False),
            ('globalizationMode', ['sharded']),
            ('environmentVariables', 0),
            ('runtimeOptions', ''),
        ]
        for key, value in variants:
            with self.subTest(key=key):
                boot = copy.deepcopy(self.boot)
                boot[key] = value
                with self.assertRaises(ValueError):
                    stage.boot_assets(boot, self.web)

    def test_aggregate_hash_payload_rejected(self):
        self.boot['resources']['hash'] = 'SYNTHETIC_OPAQUE_PAYLOAD_' + 'x' * 4096
        with self.assertRaisesRegex(ValueError, 'aggregate resource hash'):
            self.assets()

    def test_asset_cache_payload_rejected(self):
        self.boot['resources']['assembly'][0]['cache'] = {'opaque': 'payload'}
        with self.assertRaisesRegex(ValueError, 'cache value'):
            self.assets()

    def test_unknown_runtime_property_rejected(self):
        self.boot['runtimeConfig'] = {'runtimeOptions': {'configProperties': {'System.ArbitraryOpaque': True}}}
        with self.assertRaisesRegex(ValueError, 'runtime config properties'):
            self.assets()

    def test_dirty_checkout_rejected(self):
        with patch.object(stage.subprocess, 'check_output', side_effect=[b'1' * 40 + b'\n', b'?? original-media.bin\n']):
            with self.assertRaisesRegex(ValueError, 'not clean'):
                stage.stage(self.args)


if __name__ == '__main__':
    unittest.main()
