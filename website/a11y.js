// Reading options: font, colours and motion. The choice stays in this browser (localStorage, key
// "opentpw-a11y") and is never sent anywhere. It is applied as data attributes on <html> before the page is
// painted; style.css does the rest. Without a stored motion choice, the system's "reduce motion" setting applies.
(() => {
	const KEY = 'opentpw-a11y';
	const root = document.documentElement;
	const FONTS = [['default', 'Standard (Fredoka)'], ['atkinson', 'Atkinson Hyperlegible'], ['dyslexic', 'OpenDyslexic']];
	const THEMES = [['default', 'Standard'], ['contrast', 'High contrast'], ['calm', 'Calm']];
	const system = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;

	let saved = {};
	try { saved = JSON.parse(localStorage.getItem(KEY)) || {}; } catch { /* storage blocked or damaged: use the defaults */ }
	const pick = (list, value) => list.some(([key]) => key === value) ? value : 'default';
	const state = { font: pick(FONTS, saved.font), theme: pick(THEMES, saved.theme), motion: saved.motion === 'reduce' || saved.motion === 'full' ? saved.motion : null };

	function apply() {
		root.dataset.font = state.font;
		root.dataset.theme = state.theme;
		const reduce = state.motion ? state.motion === 'reduce' : !!system?.matches;
		root.dataset.motion = reduce ? 'reduce' : 'full';
		window.dispatchEvent(new CustomEvent('opentpw:motion', { detail: { reduce } }));
	}
	function save() {
		try { localStorage.setItem(KEY, JSON.stringify(state)); } catch { /* the choice then lasts for this page only */ }
	}
	apply();
	system?.addEventListener?.('change', () => { if (!state.motion) apply(); });

	function make(tag, className, text) {
		const node = document.createElement(tag);
		if (className) node.className = className;
		if (text !== undefined) node.textContent = text;
		return node;
	}
	function choices(legend, name, list, current, onChange) {
		const set = make('fieldset', 'a11y-set');
		set.append(make('legend', '', legend));
		for (const [value, label] of list) {
			const row = make('label', 'a11y-row');
			const input = make('input');
			input.type = 'radio';
			input.name = name;
			input.value = value;
			input.checked = value === current;
			input.addEventListener('change', () => onChange(value));
			row.append(input, make('span', '', label));
			set.append(row);
		}
		return set;
	}

	function build() {
		const bar = document.querySelector('.top .wrap');
		if (!bar || document.getElementById('a11y-panel')) return;
		const button = make('button', 'a11y-toggle', 'Reading options');
		button.type = 'button';
		button.setAttribute('aria-expanded', 'false');
		button.setAttribute('aria-controls', 'a11y-panel');
		const panel = make('div', 'a11y-panel');
		panel.id = 'a11y-panel';
		panel.hidden = true;
		panel.setAttribute('role', 'group');
		panel.setAttribute('aria-label', 'Reading options');

		panel.append(choices('Font', 'a11y-font', FONTS, state.font, value => { state.font = value; apply(); save(); }));
		panel.append(choices('Colours', 'a11y-theme', THEMES, state.theme, value => { state.theme = value; apply(); save(); }));

		const motionSet = make('fieldset', 'a11y-set');
		motionSet.append(make('legend', '', 'Motion'));
		const row = make('label', 'a11y-row');
		const motion = make('input');
		motion.type = 'checkbox';
		motion.checked = root.dataset.motion === 'reduce';
		motion.addEventListener('change', () => { state.motion = motion.checked ? 'reduce' : 'full'; apply(); save(); });
		row.append(motion, make('span', '', 'Reduce motion (no tilting, sliding or auto-playing video)'));
		motionSet.append(row);
		panel.append(motionSet);

		const reset = make('button', 'a11y-reset', 'Reset to defaults');
		reset.type = 'button';
		reset.addEventListener('click', () => {
			state.font = state.theme = 'default';
			state.motion = null;
			try { localStorage.removeItem(KEY); } catch { /* nothing stored */ }
			apply();
			panel.querySelectorAll('input[type=radio]').forEach(input => { input.checked = input.value === 'default'; });
			motion.checked = root.dataset.motion === 'reduce';
		});
		panel.append(reset, make('p', 'a11y-note', 'Saved in this browser only; nothing is sent anywhere.'));

		const setOpen = open => {
			panel.hidden = !open;
			button.setAttribute('aria-expanded', String(open));
		};
		button.addEventListener('click', () => {
			setOpen(panel.hidden);
			if (!panel.hidden) panel.querySelector('input:checked, input')?.focus();
		});
		document.addEventListener('keydown', event => {
			if (event.key === 'Escape' && !panel.hidden) {
				setOpen(false);
				button.focus();
			}
		});
		document.addEventListener('click', event => {
			if (!panel.hidden && !panel.contains(event.target) && event.target !== button) setOpen(false);
		});
		panel.addEventListener('focusout', event => {
			if (!panel.hidden && event.relatedTarget && !panel.contains(event.relatedTarget) && event.relatedTarget !== button) setOpen(false);
		});
		bar.append(button, panel);
	}

	if (document.readyState === 'loading')
		document.addEventListener('DOMContentLoaded', build);
	else
		build();
})();
