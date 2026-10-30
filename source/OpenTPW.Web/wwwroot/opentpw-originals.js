// Keeps the player's own game files in this browser (docs/WEB.md, "Game files kept in the browser"), so a later visit needs
// no folder. They go into the origin-private file system (OPFS): it stays on this device, is never uploaded and no other site
// can read it. Three modes: 'all' keeps every file of a chosen folder at once, 'lazy' keeps what the start and the parks played
// actually read, 'off' keeps nothing. A manifest lists every file of the folder (path and size, stored or not), so a later visit
// can rebuild the page's file list, and a folder chosen again is recognised as the same installation by that list.

export const MODES = ['lazy', 'all', 'off'];
const DIRECTORY = 'opentpw-originals';
const MANIFEST = 'manifest.json';
const VERSION = 1;

/** True when this browser can keep files: OPFS with writable file streams (main-thread writes). */
export function supported(storage = globalThis.navigator?.storage, fileHandle = globalThis.FileSystemFileHandle) {
	return typeof storage?.getDirectory === 'function' && typeof fileHandle?.prototype?.createWritable === 'function';
}

/**
 * Claims the right to keep files for this page: one tab at a time may (a Web Lock held until `release()`), because tabs share the
 * store and two of them could otherwise mix two installations. `acquired` is false when another tab holds it or locks are
 * unavailable; `release()` lets another tab claim it (a page that has stopped must not keep it for as long as it stays open).
 */
export async function claimKeeping(locks = globalThis.navigator?.locks, name = 'opentpw-originals') {
	let release = () => {};
	if (!locks)
		return { acquired: false, release: () => {}, unavailable: true };
	const acquired = await new Promise(resolve => {
		let request;
		try {
			request = locks.request(name, { ifAvailable: true }, lock => {
				resolve(lock !== null);
				return lock ? new Promise(done => release = done) : undefined;
			});
		} catch (error) {
			console.warn('The lock for the kept game files is not available', error);
			resolve(false);
			return;
		}
		Promise.resolve(request).catch(error => {
			console.warn('The lock for the kept game files is not available', error);
			resolve(false);
		});
	});
	return { acquired, release: () => release(), unavailable: false };
}

async function sha256(bytes) {
	const digest = await crypto.subtle.digest('SHA-256', bytes);
	return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
}

/** The directory below `directory` for a '/'-separated path's folders, and the file name; created on demand. */
async function locate(directory, relative, create) {
	const parts = relative.split('/').filter(Boolean);
	if (parts.length === 0 || parts.some(part => part === '.' || part === '..'))
		throw new Error(`Not a file path: ${relative}`);
	for (const part of parts.slice(0, -1))
		directory = await directory.getDirectoryHandle(part, { create });
	return [directory, parts.at(-1)];
}

/**
 * Opens the kept files below `root` (navigator.storage.getDirectory() in the page; any object with the same methods in tests).
 * `digest` hashes a file's bytes for the manifest. `name` is the store's own directory: the Theme Park World files use the default, and the
 * files of another original game that a mod reads (opentpw-inputs.js) a second one, so two installs never share a manifest.
 */
export async function openOriginalStore(root, { digest = sha256, name = DIRECTORY } = {}) {
	const directory = await root.getDirectoryHandle(name, { create: true });
	let manifest = null;
	try {
		const file = await (await directory.getFileHandle(MANIFEST)).getFile();
		const parsed = JSON.parse(await file.text());
		if (parsed?.version === VERSION && parsed.files && typeof parsed.files === 'object')
			manifest = parsed;
	} catch {
		// nothing kept yet, or an unreadable manifest: start over
	}
	// Every change bumps the revision; a flush clears `dirty` only when nothing changed while it wrote, and flushes run one at a
	// time, so a write that finishes during a flush is never forgotten.
	let revision = 0;
	let flushed = 0;
	let flushing = Promise.resolve();
	const fileWrites = new Map();
	const changed = () => revision++;
	const dirty = () => revision !== flushed;

	const store = {
		get manifest() { return manifest; },

		/** True when `relative` is kept. */
		has(relative) { return manifest?.files[relative]?.stored === true; },

		/** Bytes of the kept files and of every file the manifest lists (sizes unknown count as 0). */
		get bytes() {
			const files = Object.values(manifest?.files ?? {});
			return {
				stored: files.filter(file => file.stored).reduce((sum, file) => sum + (file.size ?? 0), 0),
				total: files.reduce((sum, file) => sum + (file.size ?? 0), 0),
			};
		},

		/**
		 * Starts keeping a folder in `mode`: `entries` are [path, size or null] of every file the page may copy. The same list
		 * (paths and known sizes) as the kept one keeps what is stored; any other folder clears the store first.
		 */
		async begin(mode, entries) {
			if (!MODES.includes(mode) || mode === 'off')
				throw new Error(`Cannot keep files in mode ${mode}`);
			const same = manifest && entries.length === Object.keys(manifest.files).length && entries.every(([relative, size]) => {
				const kept = manifest.files[relative];
				return kept && (size == null || kept.size == null || kept.size === size);
			});
			if (!same) {
				await store.clear();
				manifest = { version: VERSION, files: Object.fromEntries(entries.map(([relative, size]) => [relative, { size: size ?? null, stored: false }])) };
			}
			manifest.mode = mode;
			manifest.savedAt = new Date().toISOString();
			changed();
			await store.flush();
		},

		/** Sets (or, with null, removes) a note on the manifest, such as where the files came from; written by the next flush(). */
		annotate(key, value) {
			if (!manifest)
				return;
			if (value == null)
				delete manifest[key];
			else
				manifest[key] = value;
			changed();
		},

		/** Keeps one file's bytes (a path the manifest lists); the manifest is written by flush(). */
		async write(relative, bytes) {
			const entry = manifest?.files[relative];
			if (!entry)
				throw new Error(`${relative} is not in the kept folder's list`);
			// Hash first: a rejected digest must not replace bytes still advertised by an older manifest entry.
			const sha256 = await digest(bytes);
			// A cleared/replaced folder no longer owns this write's entry.
			if (manifest?.files[relative] !== entry)
				return;
			// Mutations share a queue for each listed path; a pending digest does not block clearing or unrelated files.
			const mutation = (fileWrites.get(relative) ?? Promise.resolve()).catch(() => {}).then(async () => {
				if (manifest?.files[relative] !== entry)
					return;
				// Before replacing a previously kept file, persist that it is missing. If close or the later manifest flush fails,
				// another visit must not read new bytes under the old hash. Retaining that hash makes a failed invalidation retryable.
				if (entry.stored || entry.sha256 != null) {
					entry.stored = false;
					changed();
					await store.flush();
					if (manifest?.files[relative] !== entry)
						return;
				}
				const [folder, name] = await locate(directory, relative, true);
				const writable = await (await folder.getFileHandle(name, { create: true })).createWritable();
				await writable.write(bytes);
				await writable.close();
				Object.assign(entry, { size: bytes.byteLength, stored: true, sha256 });
				changed();
			});
			fileWrites.set(relative, mutation);
			try {
				await mutation;
			} finally {
				if (fileWrites.get(relative) === mutation)
					fileWrites.delete(relative);
			}
		},

		/** True when `bytes` are what is kept for `relative` (its SHA-256 matches); false for a different file or one not kept. */
		async matches(relative, bytes) {
			const entry = manifest?.files[relative];
			return !!entry?.stored && entry.sha256 === await digest(bytes);
		},

		/** The kept bytes of `relative`, or null when it is not kept or its size no longer matches (then it counts as not kept). */
		async read(relative) {
			const entry = manifest?.files[relative];
			if (!entry?.stored)
				return null;
			try {
				const [folder, name] = await locate(directory, relative, false);
				const file = await (await folder.getFileHandle(name)).getFile();
				if (entry.size != null && file.size !== entry.size)
					throw new Error('size changed');
				return await file.arrayBuffer();
			} catch {
				entry.stored = false;
				changed();
				return null;
			}
		},

		/** Writes the manifest when something changed (one flush at a time). */
		flush() {
			// A failed flush leaves the manifest dirty for the next one instead of blocking every later flush.
			flushing = flushing.catch(() => {}).then(async () => {
				if (!dirty() || !manifest)
					return;
				const writing = revision;
				const writable = await (await directory.getFileHandle(MANIFEST, { create: true })).createWritable();
				await writable.write(JSON.stringify(manifest));
				await writable.close();
				flushed = writing;
			});
			return flushing;
		},

		/** Removes every kept file and the manifest. */
		async clear() {
			await flushing.catch(() => {});
			for await (const name of directory.keys())
				await directory.removeEntry(name, { recursive: true });
			manifest = null;
			flushed = revision;
		},
	};
	return store;
}
