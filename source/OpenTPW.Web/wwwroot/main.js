import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';

// The game in the browser (docs/WEB.md): the player's own files go into the runtime's memory, then
// the front end runs in the canvas, one Program.Frame per animation frame.
const status = document.getElementById('status');
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
setModuleImports('opentpw-gl', webgl);
const program = (await getAssemblyExports(getConfig().mainAssemblyName)).OpenTPW.Program;
// runMain keeps the runtime alive after Main returns, so the exports stay callable.
await runMain();
status.textContent = 'Ready.';

// The front end needs neither the movies, the parks, the start-up pictures nor the sound banks yet.
const skipped = /\/data\/(movies|levels|init)\/|\.(sdt|sf2|mpg)$/i;

// Each source is [path below the game folder, () => Promise<ArrayBuffer>].
async function copy(sources) {
	sources = sources.filter(([relative]) => /^data\//i.test(relative) && !skipped.test(`/${relative}`));
	if (!sources.length) {
		status.textContent = 'No Data folder in that folder.';
		return;
	}
	let bytes = 0;
	for (const [index, [relative, read]] of sources.entries()) {
		const contents = new Uint8Array(await read());
		program.AddFile(relative, contents);
		bytes += contents.length;
		if (index % 20 === 0)
			status.textContent = `Copying ${index + 1} of ${sources.length} files (${(bytes / 1048576).toFixed(0)} MB)…`;
	}
	await start();
}

// Drop the chosen folder's own name: "theme park/Data/ui.wad" becomes "Data/ui.wad".
document.getElementById('folder').addEventListener('change', event => copy([...event.target.files].map(file =>
	[file.webkitRelativePath.split('/').slice(1).join('/'), () => file.arrayBuffer()])));

// For local testing: ?data=<url of a game folder served next to the page, with a files.txt listing its files>.
const served = new URLSearchParams(location.search).get('data');
if (served) {
	const base = served.endsWith('/') ? served : `${served}/`;
	const list = (await (await fetch(`${base}files.txt`)).text()).split('\n').filter(Boolean);
	copy(list.map(relative => [relative, async () => (await fetch(base + relative.split('/').map(encodeURIComponent).join('/'))).arrayBuffer()]));
}

async function start() {
	status.textContent = 'Starting the game…';
	await new Promise(requestAnimationFrame);
	try {
		document.getElementById('setup').hidden = true;
		program.Start();
		document.getElementById('canvas').focus();
		const frame = () => {
			if (program.Frame())
				requestAnimationFrame(frame);
		};
		requestAnimationFrame(frame);
	} catch (error) {
		document.getElementById('setup').hidden = false;
		status.textContent = `Could not start: ${error.message}`;
		throw error;
	}
}
