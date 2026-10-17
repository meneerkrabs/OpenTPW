// Mod inputs (docs/WEB.md, "Mods in the browser"): the player's own files of another original game, for data mods such as the
// Theme Park Inc rides mod. A desktop mod input is a folder; the browser copies the files a mod needs into the runtime's memory
// (Program.AddInputFile, below /inputs/<edition>/Data) beside the Theme Park World files, and keeps them in a second store of the
// origin-private file system (opentpw-originals.js, its own directory and manifest), so the two games' files never mix and a later
// visit restores them before the game starts. Nothing is uploaded and nothing of the game ships with the page.

/** The only edition a browser input can hold. */
export const EDITION = 'theme-park-inc';
/** The directory of the kept files in the origin-private file system, apart from the Theme Park World files. */
export const STORE_DIRECTORY = 'opentpw-input-theme-park-inc';

const inc = ['arabian', 'science', 'water'];
const world = ['jungle', 'fantasy', 'hallow', 'space'];
// What the Theme Park Inc rides mod reads: the object archives of each world (rides, shops, sideshow, features, upgrades and the
// facilities folder), the shared textures, the sound banks of the world and the global ones, and the files that identify the install.
// Everything else (music, terrain, movies, speech, the user interface) stays out: about 77 MB of the install's 400 MB.
const wanted = new RegExp(String.raw`^(ver\.txt|levels/standard\.sam|levels/(${inc.join('|')})/((rides|shops|sideshow|features|upgrades|facilities)/.+|(sharetex|ssharete)\.wad|sound/.+)|global/sound/.+)$`, 'i');
const worldFolder = new RegExp(String.raw`^levels/(${world.join('|')})/`, 'i');
const incFolder = new RegExp(String.raw`^levels/(${inc.join('|')})/`, 'i');

function parts(path) {
	const result = path.replace(/\\/g, '/').split('/').filter(Boolean);
	return result.some(part => part === '.' || part === '..') ? null : result;
}

/** The first thing a Theme Park Inc folder must hold for the mod that the chosen files lack, or null. */
function missingMinimum(files) {
	const relative = files.map(([path]) => path.slice('Data/'.length).toLowerCase());
	if (!relative.includes('ver.txt'))
		return 'Ver.txt';
	for (const name of inc) {
		if (!relative.some(path => path.startsWith(`levels/${name}/rides/`)))
			return `the ${name} ride archives`;
		if (!relative.includes(`levels/${name}/sharetex.wad`))
			return `${name}'s shared textures`;
	}
	return relative.some(path => path.startsWith('global/sound/')) ? null : 'the global sounds';
}

/**
 * Chooses the files of a folder the mod input needs. `sources` are [path below the chosen folder, () => Promise<ArrayBuffer>, size]; the folder
 * may be the install (with its Data folder) or the Data folder itself. Returns { files, bytes } with each file as ['Data/<path>', read, size].
 * Throws an Error whose message the page shows when the folder holds no Theme Park Inc data, holds Theme Park World's, or lacks what the mod
 * needs (Ver.txt, per world a ride archive and the shared textures, the global sounds): the game checks the copy again before it is used.
 */
export function planInput(sources) {
	const listed = sources.map(source => ({ source, parts: parts(source[0]) })).filter(entry => entry.parts);
	const insideData = listed.some(entry => entry.parts[0].toLowerCase() === 'data');
	const below = listed.map(entry => ({ ...entry, relative: (insideData ? entry.parts.slice(1) : entry.parts).join('/') }))
		.filter(entry => insideData ? entry.parts[0].toLowerCase() === 'data' && entry.parts.length > 1 : true);
	if (below.some(entry => worldFolder.test(entry.relative)) && !below.some(entry => incFolder.test(entry.relative)))
		throw new Error('This folder holds Theme Park World data, not Theme Park Inc.');
	if (!below.some(entry => incFolder.test(entry.relative)))
		throw new Error('No Theme Park Inc data in that folder: choose the folder that holds Data (or Data itself).');
	if (below.some(entry => worldFolder.test(entry.relative)))
		throw new Error('This folder mixes Theme Park World and Theme Park Inc data; choose a Theme Park Inc install.');
	const files = below.filter(entry => wanted.test(entry.relative))
		.map(entry => [`Data/${entry.relative}`, entry.source[1], entry.source[2] ?? null]);
	const missing = missingMinimum(files);
	if (missing)
		throw new Error(`That is not a complete Theme Park Inc install: ${missing} is missing.`);
	return { files, bytes: files.reduce((sum, [, , size]) => sum + (size ?? 0), 0) };
}

/** Removes the staged and live copy in the runtime; never throws (the game may refuse while a mod reads it). */
function abort(program) {
	try {
		program.AbortInput(EDITION);
	} catch (error) {
		console.warn('The partial Theme Park Inc copy could not be removed', error);
	}
}

/** True when the store lists files and every one is kept: only such a store is a usable copy. */
export function keptComplete(store) {
	const entries = Object.values(store?.manifest?.files ?? {});
	return entries.length > 0 && entries.every(entry => entry.stored === true && !!entry.sha256);
}

/** Plans and imports a folder, reporting exactly one result to the game (Program.InputChanged): '' for success, else why not. */
export async function importFolder(program, sources, options) {
	let planned;
	try {
		planned = planInput(sources);
	} catch (error) {
		program.InputChanged(error.message);
		throw error;
	}
	return importInput(program, planned, options);
}

async function keepFile(kept, relative, bytes) {
	if (kept.failed)
		return;
	try {
		await kept.store.write(relative, bytes);
	} catch (error) {
		kept.failed = true;
		console.warn(`Keeping ${relative} failed; the rest of this import is not kept`, error);
	}
}

/**
 * Copies a planned input into the runtime's staging and, with a `store` (an opened kept-files store of STORE_DIRECTORY), keeps it for later visits. The
 * copy becomes the live one only when it is complete and recognised (Program.PublishInput); on any failure the staged and live copies and the kept
 * files are removed, so a half-copied install is never read. Keeping alone never fails the import: a full disk means the next visit asks again.
 * Reports progress as text and exactly one result to the game (Program.InputChanged: empty on success, else the reason). Returns { count, bytes, kept }.
 */
export async function importInput(program, planned, { store = null, progress = () => {} } = {}) {
	const { files } = planned;
	try {
		program.BeginInput(EDITION);
		const kept = { store, failed: false };
		if (store) {
			try {
				await store.begin('all', files.map(([relative, , size]) => [relative, size ?? null]));
			} catch (error) {
				kept.failed = true;
				console.warn('Keeping the Theme Park Inc files could not start', error);
			}
		}
		let bytes = 0;
		for (const [index, [relative, read]] of files.entries()) {
			const contents = new Uint8Array(await read());
			program.AddInputFile(EDITION, relative, contents);
			if (store)
				await keepFile(kept, relative, contents);
			bytes += contents.length;
			if (index % 20 === 0 || index === files.length - 1)
				progress(`${index + 1} of ${files.length} files (${(bytes / 1048576).toFixed(0)} MB)`);
		}
		program.PublishInput(EDITION, files.length);
		let stored = !!store && !kept.failed;
		if (store && kept.failed)
			await store.clear().catch(() => {});
		else if (store) {
			try {
				await store.flush();
			} catch (error) {
				stored = false;
				console.warn('The list of kept Theme Park Inc files could not be written', error);
				await store.clear().catch(() => {});
			}
		}
		program.InputChanged('');
		return { count: files.length, bytes, kept: stored };
	} catch (error) {
		abort(program);
		await store?.clear().catch(() => {});
		program.InputChanged(error.message);
		throw error;
	}
}

/**
 * Puts the kept files back into the runtime before the game starts. Only a complete set counts: every file must be listed as kept, and every file
 * must still match its stored SHA-256 before anything is copied. A store that is incomplete, has an invalid manifest, or fails any check is cleared
 * (partial files would make a half-working mod). Returns the number of files restored, or 0.
 */
export async function restoreInput(program, store) {
	if (!store)
		return 0;
	const entries = Object.entries(store.manifest?.files ?? {});
	if (entries.length === 0) {
		// nothing kept, or an unreadable manifest: leave no stray files behind
		await store.clear().catch(() => {});
		return 0;
	}
	try {
		if (!keptComplete(store))
			throw new Error('the kept list is incomplete');
		for (const [relative] of entries) {
			const contents = await store.read(relative);
			if (!contents || !await store.matches(relative, contents))
				throw new Error(`${relative} is missing or changed`);
		}
		program.BeginInput(EDITION);
		for (const [relative] of entries) {
			const contents = await store.read(relative);
			if (!contents || !await store.matches(relative, contents))
				throw new Error(`${relative} changed while restoring`);
			program.AddInputFile(EDITION, relative, new Uint8Array(contents));
		}
		program.PublishInput(EDITION, entries.length);
		program.InputChanged('');
		return entries.length;
	} catch (error) {
		console.warn('The kept Theme Park Inc files could not be restored; choose the folder again', error);
		abort(program);
		await store.clear().catch(() => {});
		return 0;
	}
}
