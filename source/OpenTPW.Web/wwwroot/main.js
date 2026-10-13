import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as audio from './opentpw-audio.js';
import * as storage from './opentpw-storage.js';
import { createTextureDownloads } from './opentpw-textures.js';

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
// Program.Frame and exposes the game's harness hooks; ?perfSeed=<base64 JSON {path: text}> writes the options file before the
// game starts (music muted, say). Only on a loopback address, where the harness serves the page: a link to the public page
// cannot use them to write into a player's saved files. Without ?perf nothing here runs.
const perfQuery = new URLSearchParams(location.search);
const perfSeedPaths = new Set(['/save/opentpw-options.json']);
const loopback = ['127.0.0.1', 'localhost', '[::1]'].includes(location.hostname);
const perf = loopback && perfQuery.get('perf') === '1' ? {
	program,
	frames: [],
	started: false,
	wasmBytes: () => runtime.localHeapViewU8().buffer.byteLength,
	state: () => JSON.parse(program.PerfState()),
	counters: () => ({ ...JSON.parse(program.PerfCounters()), wasmBytes: runtime.localHeapViewU8().buffer.byteLength }),
	take() { const frames = this.frames; this.frames = []; return frames; },
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
	for (const [index, [relative, read]] of sources.entries()) {
		const contents = new Uint8Array(await read());
		program.AddFile(relative, contents);
		copied?.(relative);
		bytes += contents.length;
		if (index % 20 === 0 || index === sources.length - 1)
			progress(`${index + 1} of ${sources.length} files (${(bytes / 1048576).toFixed(0)} MB)`);
	}
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

// Each source is [path below the game folder, () => Promise<ArrayBuffer>].
async function copy(sources) {
	const languages = languageFolders(sources);
	const wanted = relative => {
		if (/^data\//i.test(relative))
			return !notCopied.test(`/${relative}`);
		const match = /^([^/]+)\/(data\/language|meshes)\//i.exec(relative);
		return match != null && languages.has(match[1].toLowerCase());
	};
	speechSources = new Map();
	for (const source of sources) {
		const match = /^([^/]+)\/data\/(global\/speech\/(speechHD\.SDT|lips\.wad)|levels\/[^/]+\/speech\/.+)$/i.exec(source[0]);
		if (match && languages.has(match[1].toLowerCase()))
			speechSources.set(match[1].toLowerCase(), [...(speechSources.get(match[1].toLowerCase()) ?? []), source]);
	}
	sources = sources.filter(([relative]) => wanted(relative));
	if (!sources.some(([relative]) => /^data\//i.test(relative))) {
		status.textContent = 'No Data folder in that folder.';
		return;
	}
	levelSources = sources.filter(([relative]) => deferred.test(`/${relative}`));
	await copyFiles(sources.filter(([relative]) => !deferred.test(`/${relative}`)), text => status.textContent = `Copying ${text}…`);
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

// Drop the chosen folder's own name: "theme park/Data/ui.wad" becomes "Data/ui.wad".
document.getElementById('folder').addEventListener('change', event => copy([...event.target.files].map(file =>
	[file.webkitRelativePath.split('/').slice(1).join('/'), () => file.arrayBuffer()])));

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
		// Keep saves and settings: every few seconds, and when the page is hidden or closed.
		const keep = () => storage.sync(program).catch(error => console.warn('Saving to this browser failed', error));
		setInterval(keep, 5000);
		document.addEventListener('visibilitychange', () => { if (document.hidden) keep(); });
		window.addEventListener('pagehide', keep);
		document.getElementById('canvas').focus();
		const frame = () => {
			let running;
			try {
				if (perf) {
					const begin = performance.now();
					running = program.Frame();
					perf.frames.push([begin, performance.now() - begin]);
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
