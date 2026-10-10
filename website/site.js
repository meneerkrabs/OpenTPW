// Points the main download button at the visitor's platform. Nothing is sent anywhere.
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
