import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as audio from './opentpw-audio.js';

// The game in the browser (docs/WEB.md): the player's own files go into the runtime's memory, then
// the front end runs in the canvas, one Program.Frame per animation frame.
const status = document.getElementById('status');
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('opentpw-gl', webgl);
setModuleImports('opentpw-page', { requestLevel });
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

// Each source is [path below the game folder, () => Promise<ArrayBuffer>].
async function copy(sources) {
	sources = sources.filter(([relative]) => /^data\//i.test(relative) && !notCopied.test(`/${relative}`));
	if (!sources.length) {
		status.textContent = 'No Data folder in that folder.';
		return;
	}
	levelSources = sources.filter(([relative]) => deferred.test(`/${relative}`));
	await copyFiles(sources.filter(([relative]) => !deferred.test(`/${relative}`)), text => status.textContent = `Copying ${text}…`);
	await start();
}

// Called by the game (GameFlow.LevelDataReady) when a park in a theme not copied yet is started.
function requestLevel(level) {
	const loading = document.getElementById('loading');
	const prefix = `data/levels/${level.toLowerCase()}/`;
	const files = levelSources.filter(([relative]) => relative.toLowerCase().startsWith(prefix));
	loading.hidden = false;
	loading.textContent = 'Loading the park…';
	copyFiles(files, text => loading.textContent = `Loading the park: ${text}`)
		.catch(error => console.error(`Copying ${level} failed`, error))
		.finally(() => {
			loading.hidden = true;
			program.LevelLoaded(level);
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
	await new Promise(requestAnimationFrame);
	try {
		document.getElementById('setup').hidden = true;
		program.Start(location.origin);
		document.getElementById('canvas').focus();
		const frame = () => {
			let running;
			try {
				running = program.Frame();
			} catch (error) {
				// Stop instead of freezing silently, and say why.
				document.getElementById('setup').hidden = false;
				status.textContent = `The game stopped: ${error.message}`;
				console.error(error);
				return;
			}
			if (running)
				requestAnimationFrame(frame);
		};
		requestAnimationFrame(frame);
	} catch (error) {
		document.getElementById('setup').hidden = false;
		status.textContent = `Could not start: ${error.message}`;
		throw error;
	}
}
