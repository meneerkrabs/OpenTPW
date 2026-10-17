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

/**
 * Chooses the files of a folder the mod input needs. `sources` are [path below the chosen folder, () => Promise<ArrayBuffer>, size]; the folder
 * may be the install (with its Data folder) or the Data folder itself. Returns { files, bytes } with each file as ['Data/<path>', read, size].
 * Throws an Error whose message the page shows when the folder holds no Theme Park Inc data, or holds Theme Park World's.
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
	const files = below.filter(entry => wanted.test(entry.relative))
		.map(entry => [`Data/${entry.relative}`, entry.source[1], entry.source[2] ?? null]);
	return { files, bytes: files.reduce((sum, [, , size]) => sum + (size ?? 0), 0) };
}

/** The in-memory input is replaced whole: Program.BeginInput empties it first, so two installs never mix. */
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
 * Copies a planned input into the runtime and, with a `store` (an opened kept-files store of STORE_DIRECTORY), keeps it for later visits. Keeping
 * never fails the import: a full disk only means the next visit asks for the folder again. Reports progress as text and tells the game when it is
 * done (Program.InputChanged with an empty message) or why not. Returns { count, bytes, kept }.
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
		let stored = !!store && !kept.failed;
		if (store) {
			try {
				await store.flush();
			} catch (error) {
				stored = false;
				console.warn('The list of kept Theme Park Inc files could not be written', error);
			}
		}
		program.InputChanged('');
		return { count: files.length, bytes, kept: stored };
	} catch (error) {
		program.InputChanged(error.message);
		throw error;
	}
}

/**
 * Puts the kept files back into the runtime before the game starts. Only a complete set counts: a store with a file missing or changed is cleared
 * (partial files would make a half-working mod). Returns the number of files restored, or 0.
 */
export async function restoreInput(program, store) {
	const manifest = store?.manifest;
	const entries = Object.entries(manifest?.files ?? {});
	if (entries.length === 0 || !entries.every(([relative]) => store.has(relative)))
		return 0;
	try {
		program.BeginInput(EDITION);
		for (const [relative] of entries) {
			const contents = await store.read(relative);
			if (!contents)
				throw new Error(`${relative} is no longer kept`);
			program.AddInputFile(EDITION, relative, new Uint8Array(contents));
		}
		program.InputChanged('');
		return entries.length;
	} catch (error) {
		console.warn('The kept Theme Park Inc files could not be restored; choose the folder again', error);
		await store.clear().catch(() => {});
		try {
			program.BeginInput(EDITION);
		} catch {
			// the runtime keeps what it has
		}
		return 0;
	}
}
