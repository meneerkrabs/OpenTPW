#!/usr/bin/env node
// Compares two builds measured with run.mjs (docs/BROWSER-PERFORMANCE.md, "Measuring"): the median of each build's reports per
// measure, the change, and whether their digests (texture decoding, the fixed park run) agree. Example, with runs taken alternately:
//
//   node tools/web-perf/compare.mjs --a interp-1.json,interp-2.json,interp-3.json --b aot-1.json,aot-2.json,aot-3.json
import { readFile } from 'node:fs/promises';
import { keyMetrics, median } from './lib.mjs';

function parse(argv) {
	const options = { a: [], b: [], names: ['A', 'B'] };
	for (let index = 2; index < argv.length; index++) {
		const [flag, inline] = argv[index].split(/=(.*)/s);
		const value = () => inline ?? argv[++index];
		if (flag === '--a') options.a = value().split(',').filter(Boolean);
		else if (flag === '--b') options.b = value().split(',').filter(Boolean);
		else if (flag === '--names') options.names = value().split(',');
		else throw new Error(`Unknown option ${flag}; use --a <reports> --b <reports> [--names A,B]`);
	}
	if (!options.a.length || !options.b.length)
		throw new Error('Pass the reports of both builds: --a <a.json,...> --b <b.json,...>');
	return options;
}

const options = parse(process.argv);
const load = async files => Promise.all(files.map(async file => JSON.parse(await readFile(file, 'utf8'))));
const [reportsA, reportsB] = [await load(options.a), await load(options.b)];
// A report of several runs keeps each run; those count as runs here.
const runs = reports => reports.flatMap(report => report.runs ?? [report]).map(report => report.results);
const [runsA, runsB] = [runs(reportsA), runs(reportsB)];
// Per measure: its values over the runs that have it. A row from fewer runs than were taken says so.
function collect(results) {
	const values = new Map();
	for (const run of results)
		for (const [name, value, unit] of keyMetrics(run))
			values.set(name, { unit, list: [...(values.get(name)?.list ?? []), value] });
	return values;
}
const [valuesA, valuesB] = [collect(runsA), collect(runsB)];
const [nameA, nameB] = options.names;
const cell = (entry, total) => {
	if (!entry)
		return 'n/a';
	const value = `${Math.round(median(entry.list) * 10) / 10} ${entry.unit}`;
	return entry.list.length < total ? `${value} (${entry.list.length} of ${total} runs)` : value;
};
console.log(`| Measure | ${nameA} (median of ${runsA.length}) | ${nameB} (median of ${runsB.length}) | Change |`);
console.log('| --- | ---: | ---: | ---: |');
for (const name of new Set([...valuesA.keys(), ...valuesB.keys()])) {
	const [a, b] = [valuesA.get(name), valuesB.get(name)];
	const [ma, mb] = [a && median(a.list), b && median(b.list)];
	const change = a && b && ma ? `${mb >= ma ? '+' : ''}${Math.round((mb - ma) / ma * 100)} %` : '';
	console.log(`| ${name} | ${cell(a, runsA.length)} | ${cell(b, runsB.length)} | ${change} |`);
}
// The digests agree only when every run of both builds has both hashes, no digest error, and all of them are the same.
const digestOf = results => results.digest && !results.digest.error && results.digest.textureHash && results.digest.simulationHash
	? `${results.digest.textureHash}/${results.digest.simulationHash}` : null;
const [digestsA, digestsB] = [runsA.map(digestOf), runsB.map(digestOf)];
const all = [...digestsA, ...digestsB];
const same = all.every(digest => digest !== null) && new Set(all).size === 1;
const describe = digests => digests.map(digest => digest ?? 'missing').join(', ');
console.log(`\nDigest: ${nameA} ${describe(digestsA)}; ${nameB} ${describe(digestsB)} — ${same ? 'identical in every run' : 'DIFFERENT or missing'}`);
process.exitCode = same ? 0 : 1;
