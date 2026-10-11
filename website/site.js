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

// The gameplay clip plays only while it is on screen, and not at all when the visitor prefers reduced motion
// (then it shows its controls and poster instead).
const clip = document.getElementById('clip');
if (clip) {
	const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
	if (reduced.matches || !('IntersectionObserver' in window))
		clip.controls = true;
	else {
		new IntersectionObserver(entries => {
			for (const entry of entries) {
				if (entry.isIntersecting)
					clip.play().catch(() => { clip.controls = true; });
				else
					clip.pause();
			}
		}, { threshold: 0.4 }).observe(clip);
	}
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
