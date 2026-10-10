import { dotnet } from './_framework/dotnet.js';

const log = text => { const s = document.getElementById('status'); s.textContent += '\n' + text; s.scrollTop = s.scrollHeight; };
const started = performance.now();
const { getAssemblyExports, getConfig, runMain } = await dotnet.withConsoleForwarding().create();
const exports = (await getAssemblyExports(getConfig().mainAssemblyName)).OpenTPW.Web.Program;
// runMain keeps the runtime alive after Main returns, so the exports stay callable.
await runMain();
log(`.NET runtime ready in ${Math.round(performance.now() - started)} ms.`);

// Files below Data that this spike needs, matched case-insensitively in the chosen folder.
const wanted = ['levels/jungle/terrain.wad', 'levels/jungle/rides/totem.wad'];

document.getElementById('folder').addEventListener('change', async event => {
	const files = [...event.target.files];
	const byPath = new Map(files.map(file => [file.webkitRelativePath.toLowerCase(), file]));
	const entries = {};
	for (const relative of wanted) {
		const match = [...byPath.keys()].find(key => key.endsWith('/data/' + relative));
		if (!match) { log(`Missing Data/${relative}; is this a Theme Park World folder?`); return; }
		entries[relative] = new Uint8Array(await byPath.get(match).arrayBuffer());
	}
	show(entries);
});

/** Shows the spike for game files given as { 'levels/jungle/terrain.wad': Uint8Array, ... }. */
function show(entries) {
	for (const [relative, bytes] of Object.entries(entries)) {
		exports.AddFile(relative, bytes);
		log(`Read Data/${relative} (${(bytes.length / 1024).toFixed(0)} KB).`);
	}
	exports.Mount();
	try {
		drawMap(exports.MapCells('jungle'));
		drawModel('terrain', exports.ModelTriangles('/levels/jungle/terrain.wad/base.MD2'), [0.35, 0.7, 0.3]);
		drawModel('ride', exports.ModelTriangles('/levels/jungle/rides/totem.wad/totem.MD2'), [0.85, 0.6, 0.3]);
		drawTexture('/levels/jungle/terrain.wad/textures/grd_pav1.wct');
		log('Done: map, two models and a texture decoded by the unchanged OpenTPW readers.');
	} catch (error) {
		log('Error: ' + error);
	}
}
window.openTpw = { show, wanted };

function drawMap(cells) {
	const canvas = document.getElementById('map'), context = canvas.getContext('2d');
	const image = context.createImageData(128, 128);
	for (let index = 0; index < cells.length; index++) {
		const v = cells[index];
		// blocked 0x01, water 0x02, path 0x08, entrance 0x10, fixed walkway 0x80
		const rgb = v & 0x02 ? [40, 90, 200] : v & 0x10 ? [230, 200, 60] : v & 0x80 ? [200, 120, 60] : v & 0x08 ? [180, 180, 180] : v & 0x01 ? [60, 60, 60] : [70, 150, 60];
		image.data.set([...rgb, 255], index * 4);
	}
	const scratch = new OffscreenCanvas(128, 128);
	scratch.getContext('2d').putImageData(image, 0, 0);
	context.imageSmoothingEnabled = false;
	context.drawImage(scratch, 0, 0, canvas.width, canvas.height);
	log(`Map: ${cells.length} cells.`);
}

function drawTexture(path) {
	const [width, height] = exports.TextureSize(path), info = { Width: width, Height: height };
	const pixels = exports.TexturePixels(path);
	const canvas = document.getElementById('texture'), context = canvas.getContext('2d');
	const image = new ImageData(new Uint8ClampedArray(pixels.slice(0, info.Width * info.Height * 4)), info.Width, info.Height);
	const scratch = new OffscreenCanvas(info.Width, info.Height);
	scratch.getContext('2d').putImageData(image, 0, 0);
	context.drawImage(scratch, 0, 0, canvas.width, canvas.height);
	log(`Texture: ${info.Width}x${info.Height}.`);
}

// Minimal WebGL2 renderer: flat-shaded triangles, orbiting camera.
function drawModel(id, triangles, colour) {
	const canvas = document.getElementById(id), gl = canvas.getContext('webgl2');
	const count = triangles.length / 3;
	const positions = new Float32Array(triangles), normals = new Float32Array(triangles.length);
	const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
	for (let i = 0; i < count; i++) for (let a = 0; a < 3; a++) { min[a] = Math.min(min[a], positions[i * 3 + a]); max[a] = Math.max(max[a], positions[i * 3 + a]); }
	for (let t = 0; t < count; t += 3) {
		const p = k => [positions[(t + k) * 3], positions[(t + k) * 3 + 1], positions[(t + k) * 3 + 2]];
		const [a, b, c] = [p(0), p(1), p(2)];
		const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
		let n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
		const l = Math.hypot(...n) || 1; n = n.map(x => x / l);
		for (let k = 0; k < 3; k++) normals.set(n, (t + k) * 3);
	}
	const centre = min.map((m, a) => (m + max[a]) / 2), radius = Math.hypot(max[0] - min[0], max[1] - min[1], max[2] - min[2]) / 2 || 1;
	const shader = (type, source) => { const s = gl.createShader(type); gl.shaderSource(s, source); gl.compileShader(s); return s; };
	const program = gl.createProgram();
	gl.attachShader(program, shader(gl.VERTEX_SHADER, `#version 300 es
		in vec3 position; in vec3 normal; uniform mat4 mvp; out vec3 n;
		void main() { n = normal; gl_Position = mvp * vec4(position, 1.0); }`));
	gl.attachShader(program, shader(gl.FRAGMENT_SHADER, `#version 300 es
		precision mediump float; in vec3 n; uniform vec3 colour; out vec4 o;
		void main() { float light = 0.35 + 0.65 * abs(dot(normalize(n), normalize(vec3(0.4, 0.8, 0.5)))); o = vec4(colour * light, 1.0); }`));
	gl.linkProgram(program); gl.useProgram(program);
	const buffer = (data, name) => { gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer()); gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW); const at = gl.getAttribLocation(program, name); gl.enableVertexAttribArray(at); gl.vertexAttribPointer(at, 3, gl.FLOAT, false, 0, 0); };
	buffer(positions, 'position'); buffer(normals, 'normal');
	gl.uniform3fv(gl.getUniformLocation(program, 'colour'), colour);
	gl.enable(gl.DEPTH_TEST);
	const frame = time => {
		const angle = time / 3000, eye = [centre[0] + Math.cos(angle) * radius * 2.2, centre[1] + radius * 1.2, centre[2] + Math.sin(angle) * radius * 2.2];
		gl.uniformMatrix4fv(gl.getUniformLocation(program, 'mvp'), false, multiply(perspective(0.9, 1, radius * 0.05, radius * 10), lookAt(eye, centre, [0, 1, 0])));
		gl.viewport(0, 0, canvas.width, canvas.height); gl.clearColor(0.05, 0.08, 0.12, 1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
		gl.drawArrays(gl.TRIANGLES, 0, count);
		requestAnimationFrame(frame);
	};
	requestAnimationFrame(frame);
	log(`${id}: ${count / 3} triangles.`);
}

function perspective(fov, aspect, near, far) { const f = 1 / Math.tan(fov / 2), r = 1 / (near - far); return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * r, -1, 0, 0, 2 * far * near * r, 0]; }
function lookAt(eye, target, up) {
	const sub = (a, b) => a.map((x, i) => x - b[i]), norm = a => { const l = Math.hypot(...a) || 1; return a.map(x => x / l); };
	const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]], dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
	const z = norm(sub(eye, target)), x = norm(cross(up, z)), y = cross(z, x);
	return [x[0], y[0], z[0], 0, x[1], y[1], z[1], 0, x[2], y[2], z[2], 0, -dot(x, eye), -dot(y, eye), -dot(z, eye), 1];
}
function multiply(a, b) { const o = new Array(16).fill(0); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) for (let k = 0; k < 4; k++) o[c * 4 + r] += a[k * 4 + r] * b[c * 4 + k]; return o; }
