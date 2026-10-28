#!/usr/bin/env python3
"""Fail-closed staging of the isolated, interpreter-only path diagnostic.

No generated JS is executed. No browser is launched. Raw publish output is never
uploaded. Unknown SDK boot shapes, source items or resource provenance block staging.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

APP_ASSEMBLIES = {'OpenTPW.Web', 'OpenTPW.Web.Veldrid', 'OpenTPW.Online'}
SHADERS = {'content/shaders/blit.shader', 'content/shaders/web/blit.json',
           'content/shaders/test.shader', 'content/shaders/web/test.json'}
STATIC = {'source/OpenTPW.Web/PathOracle/index.html': 'index.html',
          'source/OpenTPW.Web/PathOracle/main.js': 'main.js',
          'source/OpenTPW.Web/wwwroot/opentpw-gl.js': 'opentpw-gl.js',
          'source/OpenTPW.Web/wwwroot/opentpw-text.js': 'opentpw-text.js'}
HELPER = 'source/OpenTPW.Tests/SyntheticLnd.cs'
TOOLS = ['tools/web-path-render/run.mjs', 'tools/web-path-render/assertions.mjs',
         'tools/web-path-render/README.md', 'tools/web-path-render/artifact-allowlist.json',
         'tools/web-perf/lib.mjs']
CATEGORIES = {'assembly': '.dll', 'coreAssembly': '.dll', 'jsModuleNative': '.js',
              'jsModuleRuntime': '.js', 'jsModuleDiagnostics': '.js',
              'wasmNative': '.wasm', 'icu': '.dat'}
EMPTY_CATEGORIES = {'jsModuleWorker', 'wasmSymbols', 'corePdb', 'pdb', 'satelliteResources',
                    'lazyAssembly', 'libraryInitializers', 'modulesAfterConfigLoaded',
                    'modulesAfterRuntimeReady', 'extensions', 'coreVfs', 'vfs'}
TOP_FIELDS = {'mainAssemblyName', 'resources', 'cacheBootResources', 'debugBuild', 'debugLevel',
              'globalizationMode', 'runtimeOptions', 'runtimeConfig', 'applicationEnvironment',
              'config', 'appsettings', 'extensions', 'environmentVariables', 'diagnosticTracing',
              'pthreadPoolInitialSize', 'pthreadPoolUnusedSize', 'exitOnUnhandledError',
              'appendElementOnExit', 'logExitCode', 'asyncFlushOnExit'}
JSON_MARKER = re.compile(r'/\*json-start\*/(.*?)/\*json-end\*/', re.S)
TEMPLATE_MARKER = re.compile(r'/\*!\s*dotnetBootConfig\s*\*/\s*\{\}')
BUNDLER_MARKER = re.compile(r'/\*!\s*bundlerFriendlyImports\s*\*/')
RUNTIME_PROPERTIES = {
    'System.Globalization.Invariant', 'System.Globalization.PredefinedCulturesOnly',
    'System.Resources.UseSystemResourceKeys', 'System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault',
    'System.Diagnostics.Debugger.IsSupported', 'System.Diagnostics.Tracing.EventSource.IsSupported',
    'System.Reflection.Metadata.MetadataUpdater.IsSupported', 'System.Reflection.NullabilityInfoContext.IsSupported',
    'System.Runtime.InteropServices.BuiltInComInterop.IsSupported',
    'System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting',
    'System.Runtime.InteropServices.EnableCppCLIHostActivation',
    'System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization',
    'System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported',
    'System.ComponentModel.TypeDescriptor.IsReflectionSupported', 'System.Linq.Enumerable.IsSizeOptimized',
    'System.Net.Http.EnableActivityPropagation', 'System.Net.Http.UseNativeHttpHandler',
    'System.Threading.Thread.EnableAutoreleasePool', 'System.Threading.ThreadPool.MaxThreads',
    'System.Threading.ThreadPool.MinThreads', 'System.Xml.XmlResolver.IsNetworkingEnabled',
}
MAX_FILE = 256 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f'duplicate JSON key: {key}')
        result[key] = value
    return result


def parse_json(text):
    return json.loads(text, object_pairs_hook=unique_object)


def no_links(path):
    path = Path(os.path.abspath(path))
    for part in [path, *path.parents]:
        require(not part.is_symlink(), f'symlink forbidden: {part}')
    return path


def read(path):
    path = no_links(path)
    require(path.is_file() and path.stat().st_size <= MAX_FILE, f'invalid/oversized file: {path}')
    return path.read_bytes()


def sha(data):
    return hashlib.sha256(data).hexdigest()


def relative_name(value):
    require(isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9_.-]+', value)
            and len(value) <= 255 and value not in {'.', '..'}, f'unsafe local asset name: {value}')
    return value


def walk_files(root):
    no_links(root)
    for directory, dirs, files in os.walk(root, followlinks=False):
        for name in dirs + files:
            no_links(Path(directory) / name)
        for name in sorted(files):
            yield Path(directory) / name


def load_boot(web):
    """.NET 10+ JSON markers only, both inline and external; never eval/import JS."""
    loader = read(web / '_framework/dotnet.js').decode('utf-8')
    matches = JSON_MARKER.findall(loader)
    manifest = None
    if len(matches) == 1:
        require(not (web / '_framework/dotnet.boot.js').exists(), 'ambiguous inline/external boot')
        boot = parse_json(matches[0])
    else:
        require(not matches, 'multiple inline boot configurations')
        manifest = '_framework/dotnet.boot.js'
        text = read(web / manifest).decode('utf-8')
        matches = JSON_MARKER.findall(text)
        require(len(matches) == 1, 'expected one JSON boot config marker')
        # Prevent arbitrary executable code in the generated configuration module.
        require(JSON_MARKER.sub('CONFIG', text).strip() == 'export const config = CONFIG;',
                'unexpected executable external boot module')
        boot = parse_json(matches[0])
    return boot, manifest


def boot_assets(boot, web):
    require(isinstance(boot, dict) and not set(boot) - TOP_FIELDS, 'unknown boot config fields')
    require(boot.get('mainAssemblyName') in {'OpenTPW.Web', 'OpenTPW.Web.dll'}, 'wrong boot assembly')
    for key in ['config', 'appsettings', 'runtimeOptions']:
        require(key not in boot or boot[key] == [], f'unsupported boot array: {key}')
    for key in ['extensions', 'environmentVariables']:
        require(key not in boot or boot[key] == {}, f'unsupported boot dictionary: {key}')
    for key in ['cacheBootResources', 'debugBuild', 'exitOnUnhandledError', 'appendElementOnExit',
                'logExitCode', 'asyncFlushOnExit', 'diagnosticTracing']:
        require(key not in boot or type(boot[key]) is bool, f'invalid boot boolean: {key}')
    for key in ['debugLevel', 'pthreadPoolInitialSize', 'pthreadPoolUnusedSize']:
        require(key not in boot or (type(boot[key]) is int and boot[key] == 0), f'unsupported boot integer: {key}')
    require(not boot.get('debugBuild') and not boot.get('diagnosticTracing'), 'debug output forbidden')
    require('applicationEnvironment' not in boot or boot['applicationEnvironment'] in ['Production', 'Development', 'Staging'],
            'unsupported application environment')
    require('globalizationMode' not in boot or boot['globalizationMode'] in ['all', 'sharded', 'invariant'],
            'unsupported globalization mode')
    config = boot.get('runtimeConfig', {})
    require(isinstance(config, dict) and not set(config) - {'runtimeOptions'}, 'unknown runtime config')
    options = config.get('runtimeOptions', {})
    require(isinstance(options, dict) and not set(options) - {'configProperties'}, 'unknown runtime options')
    props = options.get('configProperties', {})
    require(isinstance(props, dict) and all(key in RUNTIME_PROPERTIES and type(value) in {bool, int}
            and -2147483648 <= value <= 2147483647 for key, value in props.items()),
            'unsupported runtime config properties')
    require(len(json.dumps(boot)) < 1024 * 1024, 'oversized boot configuration')
    resources = boot.get('resources')
    require(isinstance(resources, dict), 'missing boot resources')
    require(not set(resources) - set(CATEGORIES) - EMPTY_CATEGORIES - {'hash'}, 'unknown boot resource category')
    if 'hash' in resources:
        require(isinstance(resources['hash'], str) and re.fullmatch(r'sha256-[A-Za-z0-9+/]{43}=', resources['hash']),
                'invalid aggregate resource hash')
    for key in EMPTY_CATEGORIES:
        require(key not in resources or resources[key] in [[], {}], f'forbidden boot resource category: {key}')
    assets = {}
    for category, extension in CATEGORIES.items():
        entries = resources.get(category, [])
        require(isinstance(entries, list), 'requires .NET 10+ non-bundler array boot resources')
        for entry in entries:
            require(isinstance(entry, dict), f'invalid boot entry: {category}')
            fields = {'name', 'moduleExports'} if extension == '.js' else {'name', 'hash', 'resolvedUrl', 'cache', 'virtualPath'}
            require(not set(entry) - fields, f'unknown asset fields: {category}')
            require('resolvedUrl' not in entry or entry['resolvedUrl'] in [None, ''], 'external/bundler asset unsupported')
            require('moduleExports' not in entry or entry['moduleExports'] in [None, ''], 'external/bundler asset unsupported')
            require('cache' not in entry or entry['cache'] in ['default', 'no-store', 'reload', 'no-cache', 'force-cache', 'only-if-cached'],
                    'unsupported runtime cache value')
            name = relative_name(entry.get('name'))
            require(name.endswith(extension), f'wrong asset extension: {category}/{name}')
            if 'virtualPath' in entry:
                relative_name(entry['virtualPath'])
            if extension == '.js':
                require(name.startswith('dotnet.'), f'non-SDK JavaScript boot asset: {name}')
            if extension == '.wasm':
                require(re.fullmatch(r'dotnet\.native(?:\.[A-Za-z0-9_-]+)?\.wasm', name), 'only interpreter runtime wasm allowed')
            if extension == '.dat':
                require(re.fullmatch(r'icudt[A-Za-z0-9_.-]*\.dat', name), 'only SDK ICU data allowed')
            output = '_framework/' + name
            require(output not in assets, f'duplicate boot asset: {output}')
            data = read(web / output)
            if extension != '.js':
                expected = 'sha256-' + base64.b64encode(hashlib.sha256(data).digest()).decode('ascii')
                require(entry.get('hash') == expected, f'boot integrity mismatch: {output}')
            assets[output] = {'path': output, 'category': category, 'sha256': sha(data), 'length': len(data)}
    for key in ['assembly', 'coreAssembly', 'wasmNative', 'jsModuleNative', 'jsModuleRuntime']:
        require(any(item['category'] == key for item in assets.values()), f'missing runtime category: {key}')
    require(sum(item['category'] == 'wasmNative' for item in assets.values()) == 1, 'requires exactly one runtime WASM')
    return assets


def source_pins(repo, allow):
    require(allow.get('schema') == 1 and set(allow.get('projectAssemblies', [])) == APP_ASSEMBLIES,
            'invalid allowlist schema/project set')
    require(len(allow.get('shaders', [])) == 4 and {x['path'] for x in allow['shaders']} == SHADERS,
            'shader source allowlist differs')
    require(len(allow.get('static', [])) == 4 and {x['path']: x['output'] for x in allow['static']} == STATIC,
            'static source allowlist differs')
    require(allow.get('syntheticHelper', {}).get('path') == HELPER, 'only SyntheticLnd helper allowed')
    records = allow['shaders'] + allow['static'] + [allow['syntheticHelper']]
    for item in records:
        require(re.fullmatch(r'[0-9a-f]{64}', item['sha256']) and sha(read(repo / item['path'])) == item['sha256'],
                f'source pin differs: {item["path"]}')
    return records


def audit_inputs(repo, inputs, build, allow):
    resource_lines = read(inputs / 'resources.txt').decode('utf-8-sig').splitlines()
    expected = {f'{repo / item["path"]}|{item["logicalName"]}' for item in allow['shaders']}
    require(len(resource_lines) == 4 and set(resource_lines) == expected, 'evaluated resources are not exactly four shaders')
    expected = {f'{repo / path}|wwwroot/{output}' if '/PathOracle/' in path else f'{repo / path}|'
                for path, output in STATIC.items()}
    content_lines = read(inputs / 'content.txt').decode('utf-8-sig').splitlines()
    require(len(content_lines) == 4 and set(content_lines) == expected, 'evaluated content differs from explicit static allowlist')
    result = []
    lines = read(inputs / 'compile.txt').decode('utf-8-sig').splitlines()
    require(lines and len(lines) == len(set(lines)), 'empty/duplicate compiled source list')
    for value in lines:
        file = no_links(value)
        require(file.suffix == '.cs', f'non-C# compiler input: {value}')
        try:
            relative = file.relative_to(repo).as_posix()
        except ValueError:
            relative = None
        if relative:
            require(relative == HELPER or any(relative.startswith('source/' + name + '/')
                    for name in ['OpenTPW', 'OpenTPW.Web', 'OpenTPW.Common', 'OpenTPW.Files']),
                    f'forbidden compiler input: {relative}')
            require(not any(part in {'bin', 'obj'} for part in Path(relative).parts), 'repository build debris compiled')
        else:
            require(file.is_relative_to(build) and (file.name.endswith('.AssemblyInfo.cs')
                    or file.name.endswith('.GlobalUsings.g.cs') or file.name.endswith('.AssemblyAttributes.cs')),
                    f'unrecognized generated compiler input: {file}')
            relative = '$BUILD/' + file.relative_to(build).as_posix()
        result.append({'path': relative, 'sha256': sha(read(file))})
    require(any(item['path'] == HELPER for item in result), 'missing synthetic helper')
    require(any(item['path'] == 'source/OpenTPW.Web/Program.PathRenderOracle.cs' for item in result), 'missing real path oracle')
    return result


def bind_pe_report(web, report, assets, allow_bytes):
    require(report.get('passed') is True and report.get('errors') == [], 'published PE audit did not pass')
    require(report.get('allowlistSha256') == sha(allow_bytes), 'PE report used different allowlist')
    records = report.get('assemblies', [])
    expected = {name for name in assets if name.endswith('.dll')}
    require(len(records) == len(expected) and {item['path'] for item in records} == expected,
            'PE report does not cover exact boot DLL closure')
    app_names = []
    for item in records:
        require(item['sha256'] == assets[item['path']]['sha256'], 'PE report not bound to published bytes')
        if item['assemblyName'] in APP_ASSEMBLIES:
            app_names.append(item['assemblyName'])
            assets[item['path']]['provenance'] = 'audited-project-assembly'
    require(len(app_names) == 3 and set(app_names) == APP_ASSEMBLIES, 'wrong project assembly set')


def normalized_loader(data, published):
    text = data.decode('utf-8')
    if published:
        require(len(JSON_MARKER.findall(text)) == 1, 'expected single inline boot loader')
        return JSON_MARKER.sub('BOOT_CONFIG', text).encode()
    require(len(TEMPLATE_MARKER.findall(text)) == 1, 'not a trusted inline boot template')
    return BUNDLER_MARKER.sub('', TEMPLATE_MARKER.sub('BOOT_CONFIG', text)).encode()


def sdk_provenance(web, assets, roots, inline):
    """Match whole bytes, not names or suffixes, to installed public SDK/package files."""
    needed = {name: item for name, item in assets.items() if 'provenance' not in item}
    sizes = {item['length'] for item in needed.values()}
    digests = {item['sha256'] for item in needed.values()}
    matches = {}
    loader_match = None
    normalized = normalized_loader(read(web / '_framework/dotnet.js'), True) if inline else None
    for index, root in enumerate(roots):
        require(root.is_dir(), f'missing trusted runtime/package root: {root}')
        # Roots are selected by the workflow, never from published output or the repository.
        require(not root.is_relative_to(web) and not web.is_relative_to(root), 'runtime root overlaps publish tree')
        for file in walk_files(root):
            if file.suffix not in {'.dll', '.wasm', '.dat', '.js'}:
                continue
            if inline and file.name == 'dotnet.js':
                try:
                    if normalized_loader(read(file), False) == normalized:
                        loader_match = {'root': index, 'path': file.relative_to(root).as_posix(), 'sha256': sha(read(file)),
                                        'transform': 'SDK boot JSON marker substitution; empty bundler imports'}
                except (ValueError, UnicodeError):
                    pass
            if file.stat().st_size not in sizes:
                continue
            digest = sha(read(file))
            if digest in digests:
                matches.setdefault(digest, {'root': index, 'path': file.relative_to(root).as_posix(), 'sha256': digest})
    for name, item in needed.items():
        if name == '_framework/dotnet.js' and inline:
            require(loader_match is not None, 'inline loader is not an exact SDK template plus parsed boot JSON')
            item['provenance'] = loader_match
        else:
            require(item['sha256'] in matches, f'no exact SDK/NuGet bytes for: {name}')
            item['provenance'] = matches[item['sha256']]


def stage(args):
    repo, web, inputs, build = [no_links(value) for value in [args.repository_root, args.web_root, args.inputs, args.build_root]]
    output = no_links(args.output)
    require(not output.exists(), 'stage destination must not exist')
    require(re.fullmatch(r'[0-9a-f]{40}', args.source_sha), 'expected exact source commit')
    git = lambda *a: subprocess.check_output(['git', '-C', str(repo), *a])
    require(git('rev-parse', 'HEAD').decode().strip() == args.source_sha, 'checkout differs from declared commit')
    require(not git('status', '--porcelain', '--untracked-files=all'), 'checkout is not clean; use external artifacts path')
    tree = git('ls-tree', '-r', '-z', '--full-tree', args.source_sha)
    source_tree = []
    for line in tree.split(b'\0'):
        if line:
            meta, path = line.decode().split('\t', 1)
            mode, kind, digest = meta.split()
            source_tree.append({'path': path, 'mode': mode, 'type': kind, 'gitSha1': digest})
    allow_file = repo / 'tools/web-path-render/artifact-allowlist.json'
    allow_bytes = read(allow_file)
    allow = parse_json(allow_bytes)
    pinned = source_pins(repo, allow)
    compiled = audit_inputs(repo, inputs, build, allow)
    boot, manifest = load_boot(web)
    assets = boot_assets(boot, web)
    pe_report = parse_json(read(args.pe_report))
    bind_pe_report(web, pe_report, assets, allow_bytes)
    loader = read(web / '_framework/dotnet.js')
    assets['_framework/dotnet.js'] = {'path': '_framework/dotnet.js', 'sha256': sha(loader), 'length': len(loader)}
    roots = [no_links(path) for path in args.runtime_root]
    require(all(not path.is_relative_to(repo) and not repo.is_relative_to(path) for path in roots), 'runtime roots overlap source checkout')
    sdk_provenance(web, assets, roots, manifest is None)
    if manifest:
        data = read(web / manifest)
        assets[manifest] = {'path': manifest, 'sha256': sha(data), 'length': len(data), 'provenance': 'parsed JSON-only SDK boot module'}
    for item in allow['static']:
        data = read(web / item['output'])
        require(sha(data) == item['sha256'], f'published static differs: {item["output"]}')
        assets[item['output']] = {'path': item['output'], 'sha256': sha(data), 'length': len(data), 'provenance': item['path']}
    omitted = []
    for file in walk_files(web):
        name = file.relative_to(web).as_posix()
        if name not in assets:
            # Source maps/symbols/compressed duplicates are not included. No unknown raw file is tolerated.
            require(name.endswith(('.br', '.gz', '.map', '.pdb', '.symbols')), f'unexpected published file: {name}')
            omitted.append(name)
    records = []
    # Only create output after *every* safety check. If copying fails, workflow never uploads.
    output.mkdir(parents=True)
    for name, item in sorted(assets.items()):
        destination = output / 'wwwroot' / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(read(web / name))
        require(sha(read(destination)) == item['sha256'], 'staged asset hash changed')
        records.append({**item, 'path': 'wwwroot/' + name})
    for name in TOOLS:
        data = read(repo / name)
        destination = output / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        records.append({'path': name, 'sha256': sha(data), 'length': len(data), 'provenance': 'source checkout'})
    evidence = {'source-tree.json': source_tree, 'compiled-sources.json': compiled, 'pe-audit.json': pe_report,
                'provenance.json': {'schema': 1, 'sourceCommit': args.source_sha, 'runtime': 'interpreter',
                 'webcil': False, 'aot': False, 'trimmed': False, 'sourcePins': pinned,
                 'runtimeRoots': [str(path) for path in roots], 'omittedPublishPaths': sorted(omitted),
                 'assetCount': len(assets), 'rendered': False, 'browserLaunched': False}}
    for name, value in evidence.items():
        data = (json.dumps(value, indent=2, sort_keys=True) + '\n').encode()
        (output / name).write_bytes(data)
        records.append({'path': name, 'sha256': sha(data), 'length': len(data), 'provenance': 'audit metadata only'})
    manifest_bytes = (json.dumps({'schema': 1, 'sourceCommit': args.source_sha, 'files': records}, indent=2) + '\n').encode()
    (output / 'artifact-manifest.json').write_bytes(manifest_bytes)
    (output / 'VERIFIED.txt').write_text('Asset isolation passed; NOT rendered/runtime coverage evidence.\n'
                                         'Manifest SHA-256: ' + sha(manifest_bytes) + '\n')
    print(f'Staged {len(records)} allowlisted files; no browser was launched.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['repository-root', 'web-root', 'inputs', 'build-root', 'pe-report', 'output', 'source-sha']:
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--runtime-root', action='append', required=True)
    args = parser.parse_args()
    try:
        stage(args)
    except (OSError, ValueError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'Artifact isolation FAILED (do not upload): {error}\n')


if __name__ == '__main__':
    main()
