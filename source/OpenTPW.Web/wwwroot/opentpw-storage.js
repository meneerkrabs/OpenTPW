// Keeps the game's saves, options, online files and settings between visits (docs/WEB.md): the
// folders Program.PersistentFiles lists are mirrored into IndexedDB and put back before the game
// starts. Only files the game writes are kept; the player's game files are not.
const database = new Promise((resolve, reject) => {
	const request = indexedDB.open('opentpw', 1);
	request.onupgradeneeded = () => request.result.createObjectStore('files');
	request.onsuccess = () => resolve(request.result);
	request.onerror = () => reject(request.error);
});

async function transaction(mode, work) {
	const db = await database;
	return new Promise((resolve, reject) => {
		const tx = db.transaction('files', mode);
		const result = work(tx.objectStore('files'));
		tx.oncomplete = () => resolve(result?.result ?? result);
		tx.onerror = () => reject(tx.error);
	});
}

let stamps = new Map();

/** Puts every kept file back into the runtime; call before Program.Start. */
export async function restore(program) {
	const records = await transaction('readonly', store => store.getAll());
	const keys = await transaction('readonly', store => store.getAllKeys());
	keys.forEach((path, index) => {
		program.RestoreFile(path, records[index].bytes);
		stamps.set(path, records[index].stamp);
	});
	// Ask the browser not to clear this storage under pressure (granted silently or not at all).
	navigator.storage?.persist?.();
	return keys.length;
}

/** Writes changed and removes deleted files. */
export async function sync(program) {
	const current = new Map(program.PersistentFiles().map(line => line.split('\t')));
	const changed = [...current].filter(([path, stamp]) => stamps.get(path) !== stamp);
	const removed = [...stamps.keys()].filter(path => !current.has(path));
	if (!changed.length && !removed.length)
		return;
	const contents = changed.map(([path, stamp]) => [path, stamp, program.ReadFile(path)]);
	await transaction('readwrite', store => {
		for (const [path, stamp, bytes] of contents)
			store.put({ stamp, bytes }, path);
		for (const path of removed)
			store.delete(path);
	});
	for (const [path, stamp] of changed)
		stamps.set(path, stamp);
	for (const path of removed)
		stamps.delete(path);
}
