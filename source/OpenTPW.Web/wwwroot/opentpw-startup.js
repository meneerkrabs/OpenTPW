const notCopied = /\/data\/(movies|init)\/|\.(sf2|mpg)$/i;
const deferred = /^\/data\/levels\/[^/]+\/(?!easymode\.tpwi$)/i;

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
export function plan(sources, theme = "") {
	if (theme && !['arabian', 'science', 'water'].includes(theme))
		throw new Error('Choose an experimental Theme Park Inc theme: arabian, science or water.');
	const selected = relative => theme && relative.toLowerCase().startsWith(`data/levels/${theme}/`);
	if (theme && !sources.some(([relative]) => selected(relative)))
		throw new Error(`No ${theme} theme files in that folder.`);
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
		start: copied.filter(([relative]) => !deferred.test(`/${relative}`) || selected(relative)),
		levels: copied.filter(([relative]) => deferred.test(`/${relative}`) && !selected(relative)),
		theme,
		speech,
	};
}

// Only a complete initial import publishes a loaded theme. Reset is owned runtime staging,
// never the originals store or persisted settings. The runtime refuses reset after renderer creation.
export async function importStartup(program, files, copy) {
	program.BeginImport();
	await copy(files.start);
	if (files.theme)
		program.LevelLoaded(files.theme);
}

export function startSelected(program, origin, theme) {
	if (theme)
		program.StartExperimental(origin, theme);
	else
		program.Start(origin);
}
