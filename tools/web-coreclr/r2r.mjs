#!/usr/bin/env node
// Experimental ReadyToRun for the CoreCLR WebAssembly build (docs/BROWSER-PERFORMANCE.md, "CoreCLR on WebAssembly").
//
// The .NET 11 RC2 SDK cannot do this itself (PublishReadyToRun fails with NETSDK1095 for browser-wasm; the SDK integration,
// dotnet/runtime#133378, is milestone 12.0.0), but the crossgen2 package restored by the wasm-tools workload can target
// browser-wasm, and the runtime's loader accepts the images. This script runs crossgen2 over the trimmed IL of a
// `dotnet publish -p:UseMonoRuntime=false` and writes a copy of the published folder whose assemblies are the compiled
// images, with the sizes and hashes in the boot config (the object in _framework/dotnet.js) updated to match:
//
//   dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:UseMonoRuntime=false -o out/coreclr
//   node tools/web-coreclr/r2r.mjs --publish out/coreclr --linked source/OpenTPW.Web/obj/Release/net11.0/linked --out out/coreclr-r2r
//
// All assemblies are compiled as one version bubble (--inputbubble) with the generics of the bubble (--compilebubblegenerics);
// without them most hot code stays interpreted. --opt-cross-module:* (what the SDK integration uses) crashes crossgen2 RC2 on
// this project. Composite images are not accepted by the runtime. Static analysis only: the output is a build artifact, not game data.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { promises as fs, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

function readVarUint(bytes, state) {
	let result = 0, shift = 0, byte;
	do {
		byte = bytes[state.p++];
		result += (byte & 127) * 2 ** shift;
		shift += 7;
	} while (byte & 128);
	return result;
}

function skipConstExpression(bytes, state) {
	while (bytes[state.p] !== 0x0b)
		state.p++;
	state.p++;
}

/** The passive element segment's entry count (what the loader grows the function table by) and the payload size (the largest passive data segment) of a compiled image. */
export function describeImage(bytes) {
	const state = { p: 8 };
	const result = { tableSize: 0, payloadSize: 0 };
	while (state.p < bytes.length) {
		const id = bytes[state.p++];
		const length = readVarUint(bytes, state);
		const end = state.p + length;
		if (id === 9) {
			const count = readVarUint(bytes, state);
			for (let index = 0; index < count; index++) {
				const flags = readVarUint(bytes, state);
				if (flags === 0)
					skipConstExpression(bytes, state);
				else if (flags === 1 || flags === 3)
					state.p++; // the element kind
				else
					throw new Error(`Unsupported element segment flags ${flags}`);
				const entries = readVarUint(bytes, state);
				result.tableSize += entries;
				for (let entry = 0; entry < entries; entry++)
					readVarUint(bytes, state);
			}
		} else if (id === 11) {
			const count = readVarUint(bytes, state);
			for (let index = 0; index < count; index++) {
				const flags = readVarUint(bytes, state);
				if (flags === 0)
					skipConstExpression(bytes, state);
				else if (flags === 2) {
					readVarUint(bytes, state);
					skipConstExpression(bytes, state);
				}
				const size = readVarUint(bytes, state);
				if (flags === 1)
					result.payloadSize = Math.max(result.payloadSize, size);
				state.p += size;
			}
		}
		state.p = end;
	}
	return result;
}

function findCrossgen2() {
	const root = path.join(os.homedir(), '.nuget', 'packages', `microsoft.netcore.app.crossgen2.${process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch === 'arm64' ? 'arm64' : 'x64'}`);
	if (!existsSync(root))
		throw new Error(`crossgen2 package not found in ${root}; publish the project with the wasm-tools workload installed first`);
	return Promise.all([fs.readdir(root)]).then(([versions]) => path.join(root, versions.sort().pop(), 'tools', 'crossgen2'));
}

function parseArguments(argv) {
	const options = { publish: null, linked: null, out: null };
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		if (flag === '--publish') options.publish = value();
		else if (flag === '--linked') options.linked = value();
		else if (flag === '--out') options.out = value();
		else throw new Error(`Unknown option ${flag}`);
	}
	if (!options.publish || !options.linked || !options.out)
		throw new Error('Usage: node tools/web-coreclr/r2r.mjs --publish <publish folder> --linked <obj/.../linked> --out <new folder>');
	return options;
}

async function main() {
	const options = parseArguments(process.argv);
	const crossgen2 = await findCrossgen2();
	const linked = path.resolve(options.linked);
	const images = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-r2r-'));
	const assemblies = (await fs.readdir(linked)).filter(name => name.endsWith('.dll'));
	for (const file of assemblies) {
		const name = path.basename(file, '.dll');
		execFileSync(crossgen2, [path.join(linked, file), '-r', path.join(linked, '*.dll'), '--inputbubble', '--inputbubbleref', path.join(linked, '*.dll'),
			'--compilebubblegenerics', '--targetos:browser', '--targetarch:wasm', '--obj-format:wasm', '-O', '-o', path.join(images, `${name}.wasm`)], { stdio: 'pipe' });
	}
	const out = path.resolve(options.out);
	if (existsSync(out))
		throw new Error(`${out} already exists`);
	await fs.cp(path.resolve(options.publish), out, { recursive: true });
	const framework = path.join(out, 'wwwroot', '_framework');
	// The pre-compressed copies would no longer match.
	for (const entry of await fs.readdir(path.join(out, 'wwwroot'), { recursive: true }))
		if (/\.(br|gz)$/.test(entry))
			await fs.rm(path.join(out, 'wwwroot', entry));
	let boot = await fs.readFile(path.join(framework, 'dotnet.js'), 'utf8');
	let replaced = 0;
	const entries = [];
	boot = boot.replace(/\{\s*"payloadSize": \d+,\s*"virtualPath": "([^"]+)\.wasm",[^}]*?\}/gs, (block, virtualPath) => entries.push([block, virtualPath]) && block);
	for (const [block, virtualPath] of entries) {
		const image = path.join(images, `${virtualPath}.wasm`);
		const name = /"name": "([^"]+)"/.exec(block)[1];
		if (!existsSync(image) || !existsSync(path.join(framework, name)))
			continue;
		const bytes = await fs.readFile(image);
		await fs.writeFile(path.join(framework, name), bytes);
		const { tableSize, payloadSize } = describeImage(bytes);
		const hash = `sha256-${createHash('sha256').update(bytes).digest('base64')}`;
		boot = boot.replace(block, block.replace(/"payloadSize": \d+/, `"payloadSize": ${payloadSize},\n        "tableSize": ${tableSize}`).replace(/"hash": "[^"]+"/, `"hash": "${hash}"`));
		replaced++;
	}
	await fs.writeFile(path.join(framework, 'dotnet.js'), boot);
	await fs.rm(images, { recursive: true });
	console.log(`${replaced} of ${assemblies.length} assemblies replaced by ReadyToRun images in ${out}`);
}

if (import.meta.url === `file://${process.argv[1]}`)
	main().catch(error => { console.error(error.message); process.exit(1); });
