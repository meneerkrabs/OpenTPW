const notCopied = /\/data\/(movies|init)\/|\.(sf2|mpg)$/i;
const deferred = /^\/data\/levels\/[^/]+\/(?!easymode\.tpwi$)/i;
const incThemes = ['arabian', 'science', 'water'];
const worldThemes = ['jungle', 'fantasy', 'hallow', 'space'];
const knownThemes = [...incThemes, ...worldThemes];

function sourceParts(path) {
	const parts = path.replace(/\\/g, '/').split('/').filter(Boolean);
	return parts.some(part => part === '.' || part === '..') ? null : parts;
}

function sourceThemeDirectories(sources) {
	const directories = new Map();
	for (const [path] of sources) {
		const parts = sourceParts(path);
		if (!parts || parts.length < 4 || parts[0].toLowerCase() !== 'data' || parts[1].toLowerCase() !== 'levels'
			|| !knownThemes.includes(parts[2].toLowerCase())) continue;
		const directory = parts.slice(0, 3).join('/');
		if (!directories.has(directory.toLowerCase())) directories.set(directory.toLowerCase(), directory);
	}
	return [...directories.values()];
}

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
export function plan(sources, theme = "", fullSourceDirectories = null) {
	if (theme && !incThemes.includes(theme))
		throw new Error('Choose an experimental Theme Park Inc theme: arabian, science or water.');
	// Preserve the full source's native directory witnesses, even when their payloads are deferred.
	const themeDirectories = fullSourceDirectories ?? sourceThemeDirectories(sources);
	const themes = themeDirectories.map(path => path.split('/')[2].toLowerCase());
	const hasVersion = sources.some(([path]) => { const parts = sourceParts(path); return parts?.length === 2 && parts[0].toLowerCase() === 'data' && parts[1].toLowerCase() === 'ver.txt'; });
	if (!theme && hasVersion && incThemes.some(name => themes.includes(name)) && !worldThemes.some(name => themes.includes(name)))
		throw new Error('Choose an experimental Theme Park Inc theme: Arabian, Science or Water.');
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
	// A real filename can preserve an otherwise filtered directory witness in the existing manifest.
	// Its bytes are never added to the copy/keeping lists (for example an excluded movie).
	const manifestSources = [...kept];
	const represented = new Set(sourceThemeDirectories(kept).map(path => path.toLowerCase()));
	for (const directory of themeDirectories) {
		if (represented.has(directory.toLowerCase())) continue;
		const witness = sources.find(([path]) => sourceParts(path)?.slice(0, 3).join('/').toLowerCase() === directory.toLowerCase());
		if (witness) manifestSources.push(witness);
	}
	const copied = sources.filter(([relative]) => wanted(relative));
	return {
		all: kept,
		manifestSources,
		start: copied.filter(([relative]) => !deferred.test(`/${relative}`) || selected(relative)),
		levels: copied.filter(([relative]) => deferred.test(`/${relative}`) && !selected(relative)),
		theme,
		themeDirectories,
		speech,
	};
}

// Only a complete initial import publishes a loaded theme. Reset is owned runtime staging,
// never the originals store or persisted settings. The runtime refuses reset after renderer creation.
export async function importStartup(program, files, copy) {
	program.BeginImport(files.themeDirectories);
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
