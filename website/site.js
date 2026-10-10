// Points the main download button at the visitor's platform. Nothing is sent anywhere for that.
const button = document.getElementById('primary-download');
if (button) {
	const platform = (navigator.userAgentData?.platform || navigator.platform || '').toLowerCase();
	const agent = navigator.userAgent.toLowerCase();
	const base = 'https://github.com/meneerkrabs/OpenTPW/releases/latest/download/';
	const choices = [
		[/win/.test(platform) || /windows/.test(agent), 'OpenTPW-win-x64.zip', 'Windows'],
		[/mac/.test(platform) && !/iphone|ipad/.test(agent), 'OpenTPW-osx-arm64.tar.gz', 'macOS'],
		[/linux/.test(platform) && !/android/.test(agent), 'OpenTPW-linux-x64.tar.gz', 'Linux'],
	];
	const match = choices.find(([test]) => test);
	if (match) {
		button.href = base + match[1];
		button.textContent = `⬇ Download for ${match[2]}`;
	}
}

// News and the website park list from the official server. Only public data is read, without cookies or
// referrer; when the server cannot be reached the section simply stays hidden.
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

async function showNews() {
	const news = await serverJson('news');
	if (!news.game && !news.system)
		return false;
	document.getElementById('news-game').textContent = news.game;
	document.getElementById('news-system').textContent = news.system;
	document.getElementById('news').hidden = false;
	return true;
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
