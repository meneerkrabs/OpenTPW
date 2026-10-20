import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as audio from './opentpw-audio.js';
import * as storage from './opentpw-storage.js';
import * as originals from './opentpw-originals.js';
import { plan, importStartup, startSelected } from './opentpw-startup.js';
import * as inputs from './opentpw-inputs.js';
import * as text from './opentpw-text.js';
import { parseManifest, planSprites, fetchSprites, serialRequests } from './opentpw-pool-sprites.js';

// The game in the browser (docs/WEB.md): the player's own files go into the runtime's memory, then
// the front end runs in the canvas, one Program.Frame per animation frame.
const status = document.getElementById('status');
// Harness experiment (loopback only, with ?perf=1): ?gc=<params> sets the runtime's garbage collector parameters (MONO_GC_PARAMS), to measure how they change the page's peak memory.
const gcExperiment = ['127.0.0.1', 'localhost', '[::1]'].includes(location.hostname) && new URLSearchParams(location.search).get('perf') === '1' ? new URLSearchParams(location.search).get('gc') : null;
if (gcExperiment)
	dotnet.withEnvironmentVariable('MONO_GC_PARAMS', gcExperiment);
const runtime = await dotnet.create();
const { setModuleImports, getAssemblyExports, getConfig, runMain } = runtime;
setModuleImports('opentpw-gl', webgl);
setModuleImports('opentpw-page', { requestLevel, requestLanguage, chooseInput, requestPoolSprites });
setModuleImports('opentpw-audio', audio);
setModuleImports('opentpw-text', text);
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
	// The event-hitch harness turns this on: a frame of at least markMs also carries the game's named work of that frame (FrameMarks).
	marks: false,
	markMs: 8,
	started: false,
	wasmBytes: () => runtime.localHeapViewU8().buffer.byteLength,
	state: () => JSON.parse(program.PerfState()),
	counters: () => ({ ...JSON.parse(program.PerfCounters()), wasmBytes: runtime.localHeapViewU8().buffer.byteLength }),
	take() { const frames = this.frames; this.frames = []; return frames; },
	takeSkipped() { const skipped = this.skipped; this.skipped = 0; return skipped; },
	// Copies a theme's files as a park start would (the determinism scenario's other themes); program.PerfLevelLoaded tells when they are in.
	loadLevel: level => requestLevel(level),
} : null;
if (perf)
	globalThis.opentpwPerf = perf;

let levelSources = [];
let speechSources = new Map();
let languageLoadingText = '';
let gameRunning = false;
let parkLoadingText = '';
let inputLoadingText = '';

function updateLoading() {
	const loading = document.getElementById('loading');
	loading.hidden = !parkLoadingText && !languageLoadingText && !inputLoadingText;
	loading.textContent = parkLoadingText || languageLoadingText || inputLoadingText;
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
// until the game stops or the page closes (releaseKeeping); a second tab plays without kept files.
const keepClaim = keepSupported ? await originals.claimKeeping() : { acquired: false, release() {}, unavailable: true };
const keepLock = keepClaim.acquired;
if (keepSupported && !keepLock)
	keepNote = keepClaim.unavailable ? '' : 'Another OpenTPW tab is using the game files kept in this browser.';
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
// The files of another original game that a mod reads (opentpw-inputs.js, docs/WEB.md "Mods in the browser") are kept in a second store, so the
// Theme Park World files and the Theme Park Inc files never share a manifest. Opened with the same lock as the first.
let inputStore = null;
if (keepLock) {
	try {
		inputStore = await originals.openOriginalStore(await navigator.storage.getDirectory(), { name: inputs.STORE_DIRECTORY });
	} catch (error) {
		console.warn('This browser cannot keep the files of mod inputs', error);
	}
}
// The input's files are in the runtime (copied this visit or restored); mods read them from the next start on.
let inputLoaded = false;
let inputBusy = false;
// The import in progress: the game never starts (and never resolves its mods) before it has finished, so it cannot read a half-copied install.
let inputImport = Promise.resolve();
let keepFailed = false;
// Set once a stopped game has given the files and the lock back (releaseKeeping): this page no longer keeps or offers them.
let keepReleased = false;
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

// A game that has stopped (an uncaught error in a frame) leaves its page open on the error. That page must not hold the kept files
// for as long as it stays open: a restart in a new tab would find them locked, play without them and copy the whole folder again,
// and keep nothing. So it drops what is still queued, writes the list of what was kept and lets another tab claim the lock.
async function releaseKeeping() {
	if (keepReleased || !keepLock)
		return;
	keepReleased = true;
	keepFailed = true;
	try {
		await keepChain;
		await store?.flush();
	} catch (error) {
		console.warn('The list of kept game files could not be written', error);
	}
	keepClaim.release();
	updateSetup();
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
// A folder asked for again joins the kept files only when every kept file has the same size in it and up to eight of them (the
// smallest kept ones) have the same bytes.
async function sameInstallation(folder) {
	const stored = Object.entries(store?.manifest?.files ?? {}).filter(([, entry]) => entry.stored && entry.sha256);
	if (stored.some(([relative, entry]) => findIn(folder, relative)?.size !== entry.size))
		return false;
	const kept = stored.sort(([, a], [, b]) => a.size - b.size).slice(0, 8);
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
		chosenFolder = await askForFolder('Some game files are not kept in this browser yet: choose your game folder again to load them.', relative, size);
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
			await store.begin(keepMode, attemptFiles.manifestSources.map(([path, , size]) => [path, size ?? null]));
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

let starting = false;
async function copy(sources, fromFolder = false) {
	if (starting)
		return;
	let files;
	try {
		files = plan(sources, document.getElementById('edition').value);
	} catch (error) {
		status.textContent = error.message;
		return;
	}
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
	await importStartup(program, files, async () => {
		if (store && keepMode !== 'off' && !keepFailed) {
			try {
				await store.begin(keepMode, files.manifestSources.map(([relative, , size]) => [relative, size ?? null]));
			} catch (error) {
				keepFailed = true;
				console.warn('Keeping the game files could not start', error);
			}
		}
		const kept = plan(keeping(files.all, fromFolder), files.theme, files.themeDirectories);
		speechSources = kept.speech;
		levelSources = kept.levels;
		copiedSpeech.clear();
		languageLoadingText = '';
		parkLoadingText = '';
		updateLoading();
		await copyFiles(kept.start, text => status.textContent = `Copying ${text}…`);
		// 'all': the rest of the folder is kept now, before the game starts (the theme folders and every language's speech).
		if (keepMode === 'all' && store?.manifest && !keepFailed) {
			openKeeping();
			// From a chosen folder every file is read, the kept ones too, so each is compared with its kept copy (keeping()).
			const missing = kept.all.filter(([relative]) => (fromFolder || !store.has(relative)) && keepQueued.get(relative) !== keepGeneration);
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
	});
	await start(files.theme);
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
	if (!store?.manifest || keepReleased)
		return false;
	try {
		const files = plan(Object.entries(store.manifest.files).map(([relative]) => [relative]), document.getElementById('edition').value);
		return files.start.length > 0 && files.start.every(([relative]) => store.has(relative));
	} catch {
		return false;
	}
}
function updateSetup() {
	const select = document.getElementById('keep');
	select.value = keepMode;
	document.getElementById('keepRow').hidden = !store || keepReleased || perf !== null && !!perfQuery.get('data');
	document.getElementById('forget').disabled = starting;
	document.getElementById('edition').disabled = starting;
	if (keepNote && !starting && !gameRunning)
		status.textContent = keepNote;
	const ready = keepMode !== 'off' && startKept();
	document.getElementById('kept').hidden = !ready;
	document.getElementById('folderText').textContent = ready ? 'Choose another folder' : 'Choose folder';
	document.getElementById('forget').hidden = !store?.manifest || keepReleased;
	if (store?.manifest)
		document.getElementById('keptSize').textContent = `${(store.bytes.stored / 1048576).toFixed(0)} MB`;
}
document.getElementById('edition').addEventListener('change', updateSetup);
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

// ---- Mod inputs: the player's Theme Park Inc files for the Theme Park Inc rides mod (opentpw-inputs.js) ----
const inputFolder = document.getElementById('inputFolder');
const inputStatus = document.getElementById('inputStatus');
function updateInput() {
	const kept = inputs.keptComplete(inputStore) && keepMode !== 'off';
	document.getElementById('inputRow').hidden = gameRunning;
	document.getElementById('inputChoose').disabled = inputBusy;
	document.getElementById('inputRemove').hidden = !(inputLoaded || kept);
	document.getElementById('inputRemove').disabled = inputBusy;
	if (inputBusy)
		return;
	const size = inputStore?.bytes ? ` (${(inputStore.bytes.stored / 1048576).toFixed(0)} MB)` : '';
	inputStatus.textContent = inputLoaded ? `Theme Park Inc files loaded for mods${kept ? `, kept in this browser${size}` : ''}.`
		: kept ? `Theme Park Inc files kept in this browser${size}; they load when the game starts.` : 'None chosen.';
}
// Called by the game (the Extensions and mods screen) and by the setup button: opens the folder dialog.
function chooseInput() {
	if (inputBusy)
		program.InputChanged('The Theme Park Inc files are still being copied.');
	else
		inputFolder.click();
}
document.getElementById('inputChoose').addEventListener('click', chooseInput);
// A dialog closed without a choice must not leave the game's screen waiting.
inputFolder.addEventListener('cancel', () => program.InputChanged(''));
inputFolder.addEventListener('change', async event => {
	const files = [...event.target.files];
	event.target.value = '';
	// Every outcome tells the game exactly once (importFolder does for a chosen folder), so its screen never waits for ever.
	if (files.length === 0)
		return program.InputChanged('');
	if (inputBusy)
		return program.InputChanged('The Theme Park Inc files are still being copied.');
	inputBusy = true;
	let failure = '';
	const report = text => {
		inputStatus.textContent = `Copying ${text}…`;
		inputLoadingText = gameRunning ? `Copying the Theme Park Inc files for mods: ${text}` : '';
		updateLoading();
	};
	updateInput();
	const sources = files.map(file => [file.webkitRelativePath.split('/').slice(1).join('/'), () => file.arrayBuffer(), file.size]);
	let finished;
	inputImport = new Promise(resolve => finished = resolve);
	try {
		const result = await inputs.importFolder(program, sources, { store: keepMode !== 'off' && !keepFailed ? inputStore : null, progress: report });
		inputLoaded = true;
		console.info(`Theme Park Inc files for mods: ${result.count} files, ${(result.bytes / 1048576).toFixed(0)} MB, ${result.kept ? 'kept' : 'not kept'}`);
	} catch (error) {
		console.warn('The Theme Park Inc files could not be used', error);
		failure = error.message;
		// a failed copy removed the earlier one too (before the game runs; a running game keeps what its mods read)
		if (!gameRunning)
			inputLoaded = false;
	} finally {
		inputBusy = false;
		inputLoadingText = '';
		updateLoading();
		updateInput();
		if (failure)
			inputStatus.textContent = failure;
		finished();
	}
});
document.getElementById('inputRemove').addEventListener('click', async () => {
	if (!confirm('Remove the Theme Park Inc files kept in this browser for mods?'))
		return;
	await inputStore?.clear();
	try {
		program.AbortInput(inputs.EDITION);
		inputLoaded = false;
	} catch (error) {
		console.warn(error);
	}
	updateInput();
});
updateInput();

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

// The pools extension's sprite sets (docs/POOLS.md, "Browser delivery"): fetched in the background once the game runs and its extensions are known,
// never blocking the menu. A park that loads first keeps the fallbacks; the swimmers pick their sets up mid-level.
async function fetchPoolSprites() {
	// Whatever ends the fetch without every file, the game is told once, so it stops waiting for the sets (Program.PoolSpritesFailed).
	let complete = false;
	try {
		if (!program.PoolsEnabled()) return;
		const manifest = program.PoolSpriteManifest();
		const planned = manifest ? planSprites(parseManifest(manifest), { pools: true, guestArt: program.PoolGuestArtEnabled() }) : [];
		// A fetch started again after an extension switch skips the files it already stored.
		const files = planned.filter(file => !program.PoolSpriteStored(file.path));
		complete = planned.length > 0 && (files.length === 0 || await fetchSprites({ files, active: () => gameRunning, store: (path, bytes) => gameRunning && program.AddPoolSpriteFile(path, bytes) }));
	} catch (error) {
		console.warn('Pools sprites unavailable; swimmers and land guests keep their fallbacks:', error?.message ?? error);
	}
	if (!complete && gameRunning) {
		try { program.PoolSpritesFailed(); } catch { /* the game is gone */ }
	}
}

// One fetch at a time; a request while one runs (the game switched the pools extension or its guest art on after the start: Page.RequestPoolSprites) is followed by one more.
const poolSpriteRequests = serialRequests(() => fetchPoolSprites(), () => gameRunning);
function requestPoolSprites() {
	poolSpriteRequests();
}

async function start(theme) {
	status.textContent = 'Starting the game…';
	try {
		await storage.restore(program);
	} catch (error) {
		console.warn('Saved games could not be read from this browser', error);
	}
	await loadSavedLanguage();
	await inputImport;
	// The kept Theme Park Inc files (when the player chose a folder in an earlier visit) are back in memory before the mods are resolved.
	if (!inputLoaded && inputStore && keepMode !== 'off') {
		status.textContent = 'Restoring the Theme Park Inc files for mods…';
		inputLoaded = await inputs.restoreInput(program, inputStore) > 0;
		updateInput();
	}
	if (perf && perfQuery.get('perfSeed')) {
		const seed = JSON.parse(atob(perfQuery.get('perfSeed')));
		for (const [path, text] of Object.entries(seed))
			if (perfSeedPaths.has(path) && typeof text === 'string')
				program.RestoreFile(path, new TextEncoder().encode(text));
	}
	await new Promise(requestAnimationFrame);
	try {
		document.getElementById('setup').hidden = true;
		gameRunning = true;
		startSelected(program, location.origin, theme);
		requestPoolSprites();
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
						perf.frames.push(perf.marks && end - begin >= perf.markMs ? [begin, end - begin, JSON.parse(program.PerfTakeMarks())] : [begin, end - begin]);
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
				releaseKeeping();
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
