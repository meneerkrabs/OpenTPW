import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as audio from './opentpw-audio.js';
import * as storage from './opentpw-storage.js';
import { createTextureDownloads } from './opentpw-textures.js';

// The game in the browser (docs/WEB.md): the player's own files go into the runtime's memory, then
// the front end runs in the canvas, one Program.Frame per animation frame.
const status = document.getElementById('status');
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('opentpw-gl', webgl);
setModuleImports('opentpw-page', { requestLevel, requestLanguage, requestTexture });
setModuleImports('opentpw-audio', audio);
const program = (await getAssemblyExports(getConfig().mainAssemblyName)).OpenTPW.Program;
// runMain keeps the runtime alive after Main returns, so the exports stay callable.
await runMain();
status.textContent = 'Ready.';

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

async function copyFiles(sources, progress) {
	let bytes = 0;
	for (const [index, [relative, read]] of sources.entries()) {
		const contents = new Uint8Array(await read());
		program.AddFile(relative, contents);
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

// Called by the game (GameFlow.LanguageDataReady) when a language is chosen: copies its speech bank and lip marks
// together, then tells the game, which switches the advisor's voice only once both are in.
function requestLanguage(language) {
	const files = speechSources.get(language.toLowerCase()) ?? [];
	languageLoadingText = 'Loading the language…';
	updateLoading();
	copyFiles(files, text => { languageLoadingText = `Loading the language: ${text}`; updateLoading(); })
		.catch(error => console.error(`Copying the ${language} speech failed`, error))
		.finally(() => {
			languageLoadingText = '';
			updateLoading();
			program.LanguageLoaded(language);
		});
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
	copy(list.map(([relative, url]) => [relative, async () => (await fetch(url ?? base + relative.split('/').map(encodeURIComponent).join('/'))).arrayBuffer()]));
}

async function start() {
	status.textContent = 'Starting the game…';
	try {
		await storage.restore(program);
	} catch (error) {
		console.warn('Saved games could not be read from this browser', error);
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
