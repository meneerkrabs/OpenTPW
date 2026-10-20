#!/usr/bin/env node
// Compares the cross-runtime digests (docs/DETERMINISM.md, "Browser and native") of two runtimes, value by value: a desktop report from
// `OpenTPW --determinism-digest --out native.json` and a browser report from tools/web-perf's `determinism` scenario (or two of either).
// Prints one line per theme and part; for the M4 scenario's hash list it names the first differing state. Exits 1 on any difference.
//   node tools/web-determinism/compare-digest.mjs native.json browser.json
import { promises as fs } from 'node:fs';

/** The per-theme digests of a report: the desktop's { themes } or the browser report's results.determinism.themes. */
export function themesOf(report) {
	const themes = report.themes ?? report.results?.determinism?.themes;
	if (!Array.isArray(themes))
		throw new Error('Not a determinism digest report (no themes)');
	return new Map(themes.map(theme => [theme.theme, theme]));
}

/** The differences between two digests of one theme: [{ part, left, right, firstDifference? }]. */
export function compareTheme(left, right) {
	const differences = [];
	for (const part of ['fixedRun', 'fixedRunWithStaff', 'weather'])
		if (left[part] !== right[part])
			differences.push({ part, left: left[part], right: right[part] });
	if (Boolean(left.m4) !== Boolean(right.m4))
		differences.push({ part: 'm4', left: left.m4 ? `${left.m4.digest} (${left.m4.count})` : 'missing', right: right.m4 ? `${right.m4.digest} (${right.m4.count})` : 'missing' });
	else if (left.m4 && right.m4 && (left.m4.digest !== right.m4.digest || left.m4.count !== right.m4.count)) {
		const a = left.m4.hashes ?? [], b = right.m4.hashes ?? [];
		const index = a.findIndex((entry, i) => !b[i] || entry[0] !== b[i][0] || entry[1] !== b[i][1]);
		differences.push({ part: 'm4', left: `${left.m4.digest} (${left.m4.count})`, right: `${right.m4.digest} (${right.m4.count})`,
			firstDifference: index >= 0 ? { left: a[index], right: b[index] ?? null } : a.length !== b.length ? 'count' : null });
	}
	return differences;
}

async function main() {
	const [leftPath, rightPath] = process.argv.slice(2);
	if (!leftPath || !rightPath)
		throw new Error('Usage: node tools/web-determinism/compare-digest.mjs <left.json> <right.json>');
	const left = themesOf(JSON.parse(await fs.readFile(leftPath, 'utf8')));
	const right = themesOf(JSON.parse(await fs.readFile(rightPath, 'utf8')));
	let different = 0;
	for (const [theme, digest] of left) {
		const other = right.get(theme);
		if (!other) {
			console.log(`${theme}: missing on the right`);
			different++;
			continue;
		}
		const differences = compareTheme(digest, other);
		console.log(`${theme}: ${differences.length === 0 ? 'identical' : differences.map(item => `${item.part} ${item.left} != ${item.right}${item.firstDifference ? ` (first: ${JSON.stringify(item.firstDifference)})` : ''}`).join('; ')}`);
		different += differences.length;
	}
	for (const theme of right.keys())
		if (!left.has(theme)) {
			console.log(`${theme}: missing on the left`);
			different++;
		}
	process.exitCode = different ? 1 : 0;
}

if (import.meta.url === `file://${process.argv[1]}`)
	main().catch(error => { console.error(error.stack ?? error); process.exitCode = 1; });
