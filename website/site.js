// Points the main download button at the visitor's platform and marks that platform's card. Nothing is sent
// anywhere for that.
const button = document.getElementById('primary-download');
if (button) {
	const platform = (navigator.userAgentData?.platform || navigator.platform || '').toLowerCase();
	const agent = navigator.userAgent.toLowerCase();
	const base = 'https://github.com/meneerkrabs/OpenTPW/releases/latest/download/';
	const choices = [
		[/win/.test(platform) || /windows/.test(agent), 'OpenTPW-win-x64.zip', 'Windows', 'windows'],
		[/mac/.test(platform) && !/iphone|ipad/.test(agent), 'OpenTPW-osx-arm64.tar.gz', 'macOS', 'mac'],
		[/linux/.test(platform) && !/android/.test(agent), 'OpenTPW-linux-x64.tar.gz', 'Linux', 'linux'],
	];
	const match = choices.find(([test]) => test);
	if (match) {
		button.href = base + match[1];
		button.textContent = `Download for ${match[2]}`;
		document.querySelector(`.platform[data-os="${match[3]}"]`)?.classList.add('mine');
	}
}

// The gameplay clip plays only while it is on screen, and not at all when reduced motion is on (the system
// setting, or the "Reduce motion" reading option in a11y.js); then it shows its controls and poster instead.
const clip = document.getElementById('clip');
if (clip) {
	const reduced = () => document.documentElement.dataset.motion === 'reduce' || window.matchMedia('(prefers-reduced-motion: reduce)').matches && document.documentElement.dataset.motion !== 'full';
	let onScreen = false;
	const update = () => {
		if (reduced()) {
			clip.pause();
			clip.controls = true;
		} else if (onScreen) {
			clip.controls = false;
			clip.play().catch(() => { clip.controls = true; });
		} else
			clip.pause();
	};
	if (!('IntersectionObserver' in window))
		clip.controls = true;
	else {
		new IntersectionObserver(entries => {
			for (const entry of entries)
				onScreen = entry.isIntersecting;
			update();
		}, { threshold: 0.4 }).observe(clip);
		window.addEventListener('opentpw:motion', update);
	}
	if (reduced())
		clip.controls = true;
}

// The live status figures, generated from the repository when the site is built (tools/site_status.py).
// If docs/status.json is missing, the fallback text and link in the page stay.
const live = document.getElementById('live-status');
if (live) {
	fetch('docs/status.json', { credentials: 'omit', cache: 'no-cache' }).then(response => {
		if (!response.ok)
			throw new Error(String(response.status));
		return response.json();
	}).then(status => {
		const tile = (value, label, note) => {
			const box = element('div', 'stat');
			box.append(element('span', 'stat-num', String(value)), element('span', 'stat-label', label), element('span', 'stat-note', note));
			return box;
		};
		const { fidelity, oracle, formats, recent, commit } = status;
		const tiles = element('div', 'stats');
		tiles.append(
			tile(fidelity.total, 'open fidelity items', `${fidelity.original} in the original areas, ${fidelity.online} online`),
			tile(oracle.f_count + oracle.x_count, 'executed oracle findings', `C# agrees with the original in ${oracle.csharp_agrees} of ${oracle.target_count} executed targets`),
			tile(formats.decoded, 'formats fully decoded', `${formats.partial} more partly`),
			tile(recent.items.length ? recent.items[0].date : '-', 'latest merged change', recent.items.length ? recent.items[0].title : ''));
		const list = element('ul', 'doc-list');
		for (const item of recent.items.slice(0, 5)) {
			const entry = element('li');
			const link = element('a', '', item.title);
			if (/^https:\/\/github\.com\//.test(item.url))
				link.href = item.url;
			entry.append(link, element('span', 'doc-file', `${item.number ? '#' + item.number + ' · ' : ''}${item.date}`));
			list.append(entry);
		}
		const more = element('p', 'fine');
		more.append('Generated from commit ', element('code', '', commit.short), ` on ${commit.date.slice(0, 10)}. `);
		const all = element('a', '', 'Full status page');
		all.href = 'docs/status.html';
		more.append(all);
		live.replaceChildren(element('h3', '', 'Live from the repository'), tiles, element('h4', '', 'Recent progress'), list, more);
	}).catch(() => {});
}

// News and the website park list from the official server. Only public data is read, without cookies or
// referrer; when the server cannot be reached or has nothing to show, the section stays hidden.
const server = 'https://play.opentpw.io/api/v1/';
const community = document.getElementById('community');

async function serverJson(path) {
	const response = await fetch(server + path, { credentials: 'omit', referrerPolicy: 'no-referrer', cache: 'no-cache' });
	if (!response.ok)
		throw new Error(`${path}: ${response.status}`);
	return response.json();
}

function element(tag, className, text) {
	const node = document.createElement(tag);
	if (className)
		node.className = className;
	if (text !== undefined)
		node.textContent = text;
	return node;
}

// Plain server text to paragraphs and lists: blank lines separate paragraphs, lines starting with "- " or
// "* " become list items. Only text nodes are created, never HTML from the server.
function renderNews(target, text) {
	target.replaceChildren();
	let list = null;
	let paragraph = null;
	for (const raw of text.replace(/\r\n?/g, '\n').split('\n')) {
		const line = raw.trim();
		const item = /^[-*•]\s+(.*)$/.exec(line);
		if (item) {
			paragraph = null;
			list ??= target.appendChild(element('ul'));
			list.append(element('li', '', item[1]));
		} else if (!line) {
			list = paragraph = null;
		} else {
			list = null;
			if (paragraph)
				paragraph.append(document.createTextNode(' ' + line));
			else
				paragraph = target.appendChild(element('p', '', line));
		}
	}
}

async function showNews() {
	const news = await serverJson('news');
	let shown = false;
	for (const [key, box] of [['game', 'news-game-box'], ['system', 'news-system-box']]) {
		const text = (news[key] || '').trim();
		if (!text)
			continue;
		renderNews(document.getElementById(`news-${key}`), text);
		document.getElementById(box).hidden = false;
		shown = true;
	}
	return shown;
}

async function showParks() {
	const { parks } = await serverJson('parks/website');
	if (!parks.length)
		return false;
	const list = document.getElementById('park-list');
	for (const park of parks) {
		const item = element('li');
		if (park.hasThumbnail) {
			const picture = element('img');
			picture.src = `${server}parks/website/${encodeURIComponent(park.id)}/thumbnail`;
			picture.alt = '';
			picture.loading = 'lazy';
			picture.width = 96;
			picture.height = 72;
			item.append(picture);
		} else
			item.append(element('span', 'no-picture'));
		const text = element('div');
		text.append(element('strong', '', park.name));
		const votes = `${park.votes} vote${park.votes === 1 ? '' : 's'}, ${park.visits} visit${park.visits === 1 ? '' : 's'}`;
		text.append(element('span', 'by', `by ${park.author} · ${votes}`));
		if (park.description)
			text.append(element('p', 'about', park.description));
		item.append(text);
		list.append(item);
	}
	document.getElementById('top-parks').hidden = false;
	return true;
}

if (community) {
	Promise.allSettled([showNews(), showParks()]).then(results => {
		if (results.some(result => result.status === 'fulfilled' && result.value))
			community.hidden = false;
	});
}
