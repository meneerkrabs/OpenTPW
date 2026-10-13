import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as audio from './opentpw-audio.js';
import * as storage from './opentpw-storage.js';
import { createTextureDownloads } from './opentpw-textures.js';
import * as originals from './opentpw-originals.js';

// The game in the browser (docs/WEB.md): the player's own files go into the runtime's memory, then
// the front end runs in the canvas, one Program.Frame per animation frame.
const status = document.getElementById('status');
const runtime = await dotnet.create();
const { setModuleImports, getAssemblyExports, getConfig, runMain } = runtime;
setModuleImports('opentpw-gl', webgl);
setModuleImports('opentpw-page', { requestLevel, requestLanguage, requestTexture });
setModuleImports('opentpw-audio', audio);
const program = (await getAssemblyExports(getConfig().mainAssemblyName)).OpenTPW.Program;
// runMain keeps the runtime alive after Main returns, so the exports stay callable.
await runMain();
status.textContent = 'Ready.';

// The performance harness (tools/web-perf, docs/BROWSER-PERFORMANCE.md): with ?perf=1 the page records the wall time of every
// Program.Frame and exposes the game's harness hooks; ?perfSeed=<base64 JSON {path: text}> writes the options or display file
// before the game starts (music muted, a frame cap). Only on a loopback address, where the harness serves the page: a link to the public page
// cannot use them to write into a player's saved files. Without ?perf nothing here runs.
const perfQuery = new URLSearchParams(location.search);
const perfSeedPaths = new Set(['/save/opentpw-options.json', '/config/display.json']);
const loopback = ['127.0.0.1', 'localhost', '[::1]'].includes(location.hostname);
const perf = loopback && perfQuery.get('perf') === '1' ? {
	program,
	frames: [],
	skipped: 0,
	started: false,
	wasmBytes: () => runtime.localHeapViewU8().buffer.byteLength,
	state: () => JSON.parse(program.PerfState()),
	counters: () => ({ ...JSON.parse(program.PerfCounters()), wasmBytes: runtime.localHeapViewU8().buffer.byteLength }),
	take() { const frames = this.frames; this.frames = []; return frames; },
	takeSkipped() { const skipped = this.skipped; this.skipped = 0; return skipped; },
} : null;
if (perf)
	globalThis.opentpwPerf = perf;

// Not copied: the movies and the start-up pictures (no movies in the browser yet).
const notCopied = /\/data\/(movies|init)\/|\.(sf2|mpg)$/i;
// Copied when a park first needs them: the theme folders below levels (about 45 MB each, most of it
// music). Their saved park (Easymode.TPWI) comes at once, for the Load list.
const deferred = /^\/data\/levels\/[^/]+\/(?!easymode\.tpwi$)/i;
let levelSources = [];
let speechSources = new Map();
let languageLoadingText = '';
let textureDownloads;
let gameRunning = false;
let parkLoadingText = '';
let textureLoading = false;

function updateLoading() {
	const loading = document.getElementById('loading');
	loading.hidden = !parkLoadingText && !languageLoadingText && !textureLoading;
	loading.textContent = parkLoadingText || languageLoadingText || 'Enhancing textures…';
}

// Called from TexturePack.Find. Requests are deduplicated and bounded; the original draws while
// a replacement downloads, then the next render frame refreshes only that image.
function requestTexture(path) {
	textureDownloads?.request(path);
}

async function writeTexture(path, bytes) {
	while (gameRunning && program.ClarityEnabled() && !program.TextureDownloadCapacity(bytes.length))
		await new Promise(requestAnimationFrame);
	return gameRunning && program.ClarityEnabled() && program.AddTextureFile(path, bytes);
}

async function copyFiles(sources, progress, copied) {
	let bytes = 0;
	try {
		for (const [index, [relative, read]] of sources.entries()) {
			const contents = new Uint8Array(await read());
			program.AddFile(relative, contents);
			copied?.(relative);
			bytes += contents.length;
			if (index % 20 === 0 || index === sources.length - 1)
				progress(`${index + 1} of ${sources.length} files (${(bytes / 1048576).toFixed(0)} MB)`);
		}
	} finally {
		// In the background: the list follows the files once they are written.
		keepFlush();
	}
}

// Game files kept in this browser (opentpw-originals.js, docs/WEB.md): the mode the player chose ('lazy' keeps what the start
// and the parks read, 'all' keeps a chosen folder at once, 'off' keeps nothing) and the store, when the browser can keep files.
const keepSupported = originals.supported();
// ?keep=<mode> sets it for the performance harness, on a loopback address only (like ?perf).
let keepMode = keepSupported ? ((loopback ? perfQuery.get('keep') : null) ?? localStorage.getItem('opentpw-keep') ?? 'lazy') : 'off';
if (!originals.MODES.includes(keepMode))
	keepMode = 'lazy';
let store = null;
let keepNote = '';
// One tab at a time keeps files: tabs share the store, and two of them could otherwise mix two installations. The lock is held
// for the page's life; a second tab plays without kept files.
const keepLock = keepSupported && navigator.locks
	? await new Promise(resolve => navigator.locks.request('opentpw-originals', { ifAvailable: true }, lock => {
		resolve(lock !== null);
		return lock ? new Promise(() => {}) : undefined;
	}).catch(error => {
		console.warn('The lock for the kept game files is not available', error);
		resolve(false);
	}))
	: false;
if (keepSupported && !keepLock)
	keepNote = navigator.locks ? 'Another OpenTPW tab is using the game files kept in this browser.' : '';
if (keepLock) {
	try {
		store = await originals.openOriginalStore(await navigator.storage.getDirectory());
	} catch (error) {
		console.warn('This browser cannot keep the game files', error);
		keepNote = 'This browser could not open its storage for the game files.';
	}
}
if (!store)
	keepMode = 'off';
let keepFailed = false;
// Files are kept in the background, one after another, so copying into the game never waits for the disk; at most this many
// bytes wait to be written (the 'all' mode reads a whole folder, faster than it can be written).
const keepPendingLimit = 64 * 1048576;
// Writing waits until the game has started, so keeping never slows the first start: the start's files wait in memory (they are
// copied into the game anyway) and go to the disk while the lobby runs. The 'all' mode opens it before it reads the rest.
let openKeeping;
let keepingOpen = false;
let keepChain = new Promise(resolve => openKeeping = () => { keepingOpen = true; resolve(); });
let keepPending = 0;
let keptSinceFlush = 0;
// Each start attempt has its own generation: writes queued by an earlier, failed attempt (possibly another folder) are dropped.
let keepGeneration = 0;
const keepQueued = new Map();

// Queues one file the page has read for keeping; a full disk stops keeping for this visit, the game goes on. `generation` is the
// attempt that started reading it: a read that finishes after a newer attempt began is dropped, whichever folder it came from.
async function keepFile(relative, bytes, generation = keepGeneration) {
	if (generation !== keepGeneration)
		return;
	if (keepFailed || keepMode === 'off' || !store?.manifest?.files[relative] || store.has(relative) || keepQueued.get(relative) === generation)
		return;
	while (keepingOpen && keepPending > 0 && keepPending + bytes.byteLength > keepPendingLimit)
		await keepChain;
	keepQueued.set(relative, generation);
	keepPending += bytes.byteLength;
	keepChain = keepChain.then(async () => {
		if (keepFailed || generation !== keepGeneration)
			return;
		try {
			await store.write(relative, bytes);
			// A checkpoint now and then, so a visit that ends early still leaves what it kept usable.
			if (++keptSinceFlush >= 25) {
				keptSinceFlush = 0;
				await store.flush();
			}
		} catch (error) {
			keepFailed = true;
			console.warn(`Keeping ${relative} failed; the rest of this visit keeps nothing more`, error);
			if (error?.name === 'QuotaExceededError')
				status.textContent = 'This browser has no room to keep more game files; the game goes on without keeping them.';
		}
	}).finally(() => {
		keepPending -= bytes.byteLength;
		if (keepQueued.get(relative) === generation)
			keepQueued.delete(relative);
		// The list follows as soon as the queue is empty, so a short visit leaves a usable checkpoint.
		if (keepPending === 0)
			store?.flush().catch(error => console.warn('The list of kept game files could not be written', error));
	});
}

// Waits for the files queued so far, then writes the list of kept files.
async function keepFlush() {
	await keepChain;
	await store?.flush().catch(error => console.warn('The list of kept game files could not be written', error));
}

// The folder chosen in this visit (path below it → File), for files the browser has not kept; askForFolder shows the folder
// button again and waits for a choice.
let chosenFolder = null;
let folderWaiter = null;
function folderMap(files) {
	return new Map(files.map(file => [file.webkitRelativePath.split('/').slice(1).join('/'), file]));
}
function findIn(folder, relative) {
	return folder.get(relative) ?? [...folder].find(([path]) => path.toLowerCase() === relative.toLowerCase())?.[1];
}
// Shows the folder button and waits for a folder that has `relative` (with `size` when known); another folder is refused with
// a message and the button stays, so a wrong choice can be corrected.
function askForFolder(reason, relative, size) {
	document.getElementById('setup').hidden = false;
	// In the middle of the game only the folder button matters.
	if (gameRunning)
		for (const id of ['kept', 'keepRow'])
			document.getElementById(id).hidden = true;
	status.textContent = reason;
	return new Promise(resolve => folderWaiter = async folder => {
		const file = findIn(folder, relative);
		let same = false;
		try {
			same = !!file && (size == null || file.size === size) && await sameInstallation(folder);
		} catch (error) {
			console.warn('The chosen folder could not be read', error);
		}
		if (!same) {
			status.textContent = `That folder is not the installation whose files are kept here (${relative} ${file ? 'differs' : 'is missing'}): choose the same Theme Park World folder, or remove the kept files first.`;
			return false;
		}
		resolve(folder);
		return true;
	});
}
// A folder asked for again joins the kept files only when up to three of them (the smallest kept ones) have the same bytes in it.
async function sameInstallation(folder) {
	const kept = Object.entries(store?.manifest?.files ?? {}).filter(([, entry]) => entry.stored && entry.sha256)
		.sort(([, a], [, b]) => a.size - b.size).slice(0, 3);
	for (const [relative] of kept) {
		const file = findIn(folder, relative);
		if (!file || !await store.matches(relative, await file.arrayBuffer()))
			return false;
	}
	return true;
}
async function readFromFolder(relative, size) {
	let file = chosenFolder && findIn(chosenFolder, relative);
	if (!file || size != null && file.size !== size) {
		chosenFolder = await askForFolder('Some game files are not kept in this browser yet: choose your Theme Park World folder again to load them.', relative, size);
		file = findIn(chosenFolder, relative);
	}
	return file.arrayBuffer();
}

// Every source reads the kept copy when there is one, else its own way (a served file, the folder asked for again), and keeps
// what it read. With `fromFolder` (the player chose a folder) the folder is read instead and every kept file it meets is compared
// by its SHA-256: another installation with the same file list clears the store, which then keeps this folder.
// The chosen folder's files for this start attempt: every file the page may copy (with the folder's sizes) and the start's files,
// so a replaced store can keep them again.
let attemptFiles = null;
let replacing = null;
// Another installation (a kept file differs from the chosen folder's): nothing kept stays, whenever it shows, so old and new
// files never mix, even when the start then fails. The start's files of this folder are kept again in the background.
function replaceStore(relative) {
	replacing ??= (async () => {
		console.warn(`${relative} differs from the kept copy: the chosen folder is another installation, the kept files are replaced`);
		keepGeneration++;
		try {
			await store.clear();
			await store.begin(keepMode, attemptFiles.all.map(([path, , size]) => [path, size ?? null]));
		} catch (error) {
			keepFailed = true;
			console.warn('Replacing the kept game files failed; this visit keeps nothing more', error);
			return;
		}
		const generation = keepGeneration;
		const start = attemptFiles.start;
		(async () => {
			for (const [path, read] of start) {
				if (keepFailed || generation !== keepGeneration)
					return;
				if (!store.has(path))
					await keepFile(path, await read(), generation);
			}
		})().catch(error => console.warn('Keeping the start files again failed', error));
	})();
	return replacing;
}
function keeping(sources, fromFolder) {
	if (!store?.manifest || keepMode === 'off' || keepFailed)
		return sources;
	return sources.map(([relative, read, size]) => [relative, async () => {
		if (keepFailed)
			return read();
		const generation = keepGeneration;
		if (!fromFolder) {
			const kept = store.has(relative) ? await store.read(relative) : null;
			if (kept)
				return kept;
		}
		const bytes = await read();
		if (generation !== keepGeneration)
			return bytes;
		if (fromFolder && store.manifest && store.has(relative) && !await store.matches(relative, bytes))
			await replaceStore(relative);
		await keepFile(relative, bytes, replacing ? keepGeneration : generation);
		return bytes;
	}, size]);
}

// The CD keeps the other languages beside Data: <Lang>/data/Language/<Lang> (text, fonts) and <Lang>/Meshes/<Lang>
// (banners), about 2 to 3 MB per language, copied at once so the language can be listed and switched. The advisor's
// speech of a language (<Lang>/data/global/speech/speechHD.SDT, about 27 MB, and lips.WAD, plus each level's speech folder, about 0.6 MB) is copied when that language
// is chosen (requestLanguage), both files together. A folder counts as a language by that structure, not by name, so
// every CD folder that holds its own text is offered.
function languageFolders(sources) {
	const found = new Set();
	for (const [relative] of sources) {
		const match = /^([^/]+)\/data\/language\/([^/]+)\//i.exec(relative);
		if (match && match[1].toLowerCase() === match[2].toLowerCase() && match[1].toLowerCase() !== 'data')
			found.add(match[1].toLowerCase());
	}
	return found;
}

// Which of a folder's files the page copies: the files it may copy at all (Data without movies, the other languages' text,
// banners and speech), those it copies at start, the theme folders it copies when a park needs them, and each language's speech.
// Each source is [path below the game folder, () => Promise<ArrayBuffer>, size or null].
function plan(sources) {
	const languages = languageFolders(sources);
	const wanted = relative => {
		if (/^data\//i.test(relative))
			return !notCopied.test(`/${relative}`);
		const match = /^([^/]+)\/(data\/language|meshes)\//i.exec(relative);
		return match != null && languages.has(match[1].toLowerCase());
	};
	const speech = new Map();
	for (const source of sources) {
		const match = /^([^/]+)\/data\/(global\/speech\/(speechHD\.SDT|lips\.wad)|levels\/[^/]+\/speech\/.+)$/i.exec(source[0]);
		if (match && languages.has(match[1].toLowerCase()))
			speech.set(match[1].toLowerCase(), [...(speech.get(match[1].toLowerCase()) ?? []), source]);
	}
	const kept = sources.filter(([relative]) => wanted(relative) || [...speech.values()].some(files => files.some(([path]) => path === relative)));
	const copied = sources.filter(([relative]) => wanted(relative));
	return {
		all: kept,
		start: copied.filter(([relative]) => !deferred.test(`/${relative}`)),
		levels: copied.filter(([relative]) => deferred.test(`/${relative}`)),
		speech,
	};
}

let starting = false;
async function copy(sources, fromFolder = false) {
	if (starting)
		return;
	const files = plan(sources);
	if (!files.start.some(([relative]) => /^data\//i.test(relative))) {
		status.textContent = 'No Data folder in that folder.';
		return;
	}
	starting = true;
	keepGeneration++;
	replacing = null;
	attemptFiles = files;
	updateSetup();
	try {
		await copyAndStart(files, fromFolder);
	} catch (error) {
		// A failed start can be tried again: Play, or another folder.
		console.error(error);
		starting = false;
		status.textContent = `Could not start: ${error.message}`;
		updateSetup();
	}
}

async function copyAndStart(files, fromFolder) {
	if (store && keepMode !== 'off' && !keepFailed) {
		try {
			await store.begin(keepMode, files.all.map(([relative, , size]) => [relative, size ?? null]));
		} catch (error) {
			keepFailed = true;
			console.warn('Keeping the game files could not start', error);
		}
	}
	const kept = plan(keeping(files.all, fromFolder));
	speechSources = kept.speech;
	levelSources = kept.levels;
	await copyFiles(kept.start, text => status.textContent = `Copying ${text}…`);
	// 'all': the rest of the folder is kept now, before the game starts (the theme folders and every language's speech).
	if (keepMode === 'all' && store?.manifest && !keepFailed) {
		openKeeping();
		const missing = kept.all.filter(([relative]) => !store.has(relative) && keepQueued.get(relative) !== keepGeneration);
		let bytes = 0;
		for (const [index, [, read]] of missing.entries()) {
			bytes += (await read()).byteLength;
			if (index % 20 === 0 || index === missing.length - 1)
				status.textContent = `Keeping the game files in this browser: ${index + 1} of ${missing.length} (${(bytes / 1048576).toFixed(0)} MB)…`;
			if (keepFailed)
				break;
		}
		await keepFlush();
	}
	await start();
}

// Called by the game (GameFlow.LevelDataReady) when a park in a theme not copied yet is started.
function requestLevel(level) {
	const prefix = `data/levels/${level.toLowerCase()}/`;
	const files = levelSources.filter(([relative]) => relative.toLowerCase().startsWith(prefix));
	parkLoadingText = 'Loading the park…';
	updateLoading();
	copyFiles(files, text => { parkLoadingText = `Loading the park: ${text}`; updateLoading(); })
		.catch(error => console.error(`Copying ${level} failed`, error))
		.finally(() => {
			parkLoadingText = '';
			updateLoading();
			program.LevelLoaded(level);
		});
}

// Copies a language's speech files, bank and lip marks together. A file counts as copied only once it is in memory, so a
// retry after a failure copies just what is missing; the language is reported loaded only when every file is in.
const copiedSpeech = new Set();
async function copySpeech(language, progress) {
	const files = (speechSources.get(language.toLowerCase()) ?? []).filter(([relative]) => !copiedSpeech.has(relative));
	await copyFiles(files, progress, relative => copiedSpeech.add(relative));
}

// Called by the game (GameFlow.LanguageDataReady) when a language is chosen. The game switches the text and the advisor's voice
// only when this reports the language loaded; on a failure it keeps the current language (and says so), and choosing the
// language again calls this again.
function requestLanguage(language) {
	languageLoadingText = 'Loading the language…';
	updateLoading();
	copySpeech(language, text => { languageLoadingText = `Loading the language: ${text}`; updateLoading(); })
		.then(() => {
			languageLoadingText = '';
			updateLoading();
			program.LanguageLoaded(language);
		}, error => {
			console.error(`Copying the ${language} speech failed`, error);
			program.LanguageFailed(language);
			languageLoadingText = `Could not load the ${language} speech (${error.message}); keeping the current language. Choose it again to retry.`;
			updateLoading();
			setTimeout(() => { if (languageLoadingText.startsWith('Could not')) { languageLoadingText = ''; updateLoading(); } }, 10000);
		});
}

// The saved language (language.json, restored from the browser's storage) is copied before the game starts, so the front end
// never runs in a language whose speech is missing; when that fails the game starts in the default language.
async function loadSavedLanguage() {
	let language;
	try {
		language = JSON.parse(new TextDecoder().decode(program.ReadFile('/config/language.json'))).Language;
	} catch {
		return;
	}
	if (!language)
		return;
	try {
		await copySpeech(language, text => status.textContent = `Loading the ${language} speech: ${text}…`);
		program.LanguageLoaded(language);
	} catch (error) {
		console.error(`Copying the ${language} speech failed`, error);
		program.LanguageFailed(language);
		status.textContent = `Could not load the ${language} speech (${error.message}); starting in the default language.`;
		await new Promise(resolve => setTimeout(resolve, 1500));
	}
}

// Drop the chosen folder's own name: "theme park/Data/ui.wad" becomes "Data/ui.wad". A folder asked for while the game runs (files
// the browser has not kept) goes to that request; otherwise the choice starts the game.
document.getElementById('folder').addEventListener('change', event => {
	const files = [...event.target.files];
	event.target.value = '';
	if (folderWaiter) {
		const waiter = folderWaiter;
		status.textContent = 'Checking the folder…';
		waiter(folderMap(files)).catch(() => false).then(accepted => {
			if (!accepted || folderWaiter !== waiter)
				return;
			folderWaiter = null;
			if (gameRunning)
				document.getElementById('setup').hidden = true;
			status.textContent = 'Loading…';
		});
		return;
	}
	chosenFolder = folderMap(files);
	copy(files.map(file => [file.webkitRelativePath.split('/').slice(1).join('/'), () => file.arrayBuffer(), file.size]), true);
});

// The setup screen's choices for kept files: Play starts from them, the mode applies to the next folder chosen and to what the
// game reads from now on, and Remove deletes them.
function startKept() {
	return !!store?.manifest && plan(Object.entries(store.manifest.files).map(([relative]) => [relative])).start.every(([relative]) => store.has(relative));
}
function updateSetup() {
	const select = document.getElementById('keep');
	select.value = keepMode;
	document.getElementById('keepRow').hidden = !store || perf !== null && !!perfQuery.get('data');
	document.getElementById('forget').disabled = starting;
	if (keepNote && !starting && !gameRunning)
		status.textContent = keepNote;
	const ready = keepMode !== 'off' && startKept();
	document.getElementById('kept').hidden = !ready;
	document.getElementById('folderText').textContent = ready ? 'Choose another folder' : 'Choose folder';
	document.getElementById('forget').hidden = !store?.manifest;
	if (store?.manifest)
		document.getElementById('keptSize').textContent = `${(store.bytes.stored / 1048576).toFixed(0)} MB`;
}
document.getElementById('keep').addEventListener('change', event => {
	keepMode = event.target.value;
	localStorage.setItem('opentpw-keep', keepMode);
	updateSetup();
});
document.getElementById('forget').addEventListener('click', async () => {
	if (!confirm('Remove the game files kept in this browser? Your saves stay.'))
		return;
	await store.clear();
	updateSetup();
});
document.getElementById('play').addEventListener('click', () => {
	status.textContent = 'Starting with the game files kept in this browser…';
	copy(Object.entries(store.manifest.files).map(([relative, entry]) => [relative, () => readFromFolder(relative, entry.size), entry.size]));
});
updateSetup();

// For local testing: ?data=<url of a game folder served next to the page, with a files.txt listing its
// files>. A line may name the URL to fetch after a tab (for servers that only serve known file types).
const served = new URLSearchParams(location.search).get('data');
if (served) {
	const base = served.endsWith('/') ? served : `${served}/`;
	const list = (await (await fetch(`${base}files.txt`)).text()).split('\n').filter(Boolean).map(line => line.split('\t'));
	copy(list.map(([relative, url]) => [relative, async () => {
		const response = await fetch(url ?? base + relative.split('/').map(encodeURIComponent).join('/'));
		if (!response.ok)
			throw new Error(`${relative}: HTTP ${response.status}`);
		return response.arrayBuffer();
	}]));
}

async function start() {
	status.textContent = 'Starting the game…';
	try {
		await storage.restore(program);
	} catch (error) {
		console.warn('Saved games could not be read from this browser', error);
	}
	await loadSavedLanguage();
	if (perf && perfQuery.get('perfSeed')) {
		const seed = JSON.parse(atob(perfQuery.get('perfSeed')));
		for (const [path, text] of Object.entries(seed))
			if (perfSeedPaths.has(path) && typeof text === 'string')
				program.RestoreFile(path, new TextEncoder().encode(text));
	}
	try {
		const distribution = JSON.parse(program.ClarityDistribution());
		textureDownloads = createTextureDownloads({
			distribution, writeFile: writeTexture, enabled: () => gameRunning && program.ClarityEnabled(),
			changed: state => { textureLoading = state.active + state.queued > 0; updateLoading(); },
		});
		program.InstallClarity(JSON.stringify(distribution.packManifest));
	} catch (error) {
		console.warn('Clarity could not be prepared; the game will use its original world textures', error);
	}
	await new Promise(requestAnimationFrame);
	try {
		document.getElementById('setup').hidden = true;
		gameRunning = true;
		program.Start(location.origin);
		openKeeping();
		// Keep saves and settings: every few seconds, and when the page is hidden or closed.
		const keep = () => storage.sync(program).catch(error => console.warn('Saving to this browser failed', error));
		setInterval(keep, 5000);
		document.addEventListener('visibilitychange', () => { if (document.hidden) keep(); });
		window.addEventListener('pagehide', keep);
		window.addEventListener('pagehide', () => keepFlush());
		document.getElementById('canvas').focus();
		const frame = () => {
			let running;
			try {
				if (perf) {
					// Only drawn frames are recorded; a call the frame cap skips is counted apart.
					const drawn = program.PerfFramesDrawn();
					const begin = performance.now();
					running = program.Frame();
					const end = performance.now();
					if (program.PerfFramesDrawn() > drawn)
						perf.frames.push([begin, end - begin]);
					else
						perf.skipped++;
					// Bounded when nobody drains it (about half an hour at 60 fps).
					if (perf.frames.length > 100000)
						perf.frames.splice(0, 50000);
					perf.started = true;
				} else
					running = program.Frame();
			} catch (error) {
				gameRunning = false;
				// Stop instead of freezing silently, and say why.
				document.getElementById('setup').hidden = false;
				status.textContent = `The game stopped: ${error.message}`;
				console.error(error);
				return;
			}
			if (running)
				requestAnimationFrame(frame);
			else
				gameRunning = false;
		};
		requestAnimationFrame(frame);
	} catch (error) {
		gameRunning = false;
		document.getElementById('setup').hidden = false;
		status.textContent = `Could not start: ${error.message}`;
		throw error;
	}
}
