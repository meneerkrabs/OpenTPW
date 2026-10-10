// The WebGL2 side of OpenTPW.Web.Veldrid (docs/WEB.md). C# owns the Veldrid objects and refers
// to the WebGL objects here by integer handle; draws arrive as one command stream per submit.

let gl = null;
// The page has one canvas; the window asks for its size before the device exists.
let canvas = document.getElementById('canvas');
let anisotropic = null;
let readFramebuffer = null;
let drawFramebuffer = null;
const objects = [null];
const freeHandles = [];

function put(object) {
	const handle = freeHandles.length ? freeHandles.pop() : objects.length;
	objects[handle] = object;
	return handle;
}

export function init(canvasId) {
	canvas = document.getElementById(canvasId) ?? canvas;
	gl = canvas?.getContext('webgl2', { antialias: false, depth: false, stencil: false, alpha: false, preserveDrawingBuffer: false });
	if (!gl)
		return false;
	anisotropic = gl.getExtension('EXT_texture_filter_anisotropic');
	gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
	gl.bindVertexArray(gl.createVertexArray());
	readFramebuffer = gl.createFramebuffer();
	drawFramebuffer = gl.createFramebuffer();
	attachInput();
	return true;
}

export function maxTextureSize() {
	return gl.getParameter(gl.MAX_TEXTURE_SIZE);
}

// Textures and multisampled renderbuffers

export function createTexture(internalFormat, width, height, mipLevels, samples) {
	if (samples > 1) {
		const renderbuffer = gl.createRenderbuffer();
		gl.bindRenderbuffer(gl.RENDERBUFFER, renderbuffer);
		gl.renderbufferStorageMultisample(gl.RENDERBUFFER, Math.min(samples, gl.getParameter(gl.MAX_SAMPLES)), internalFormat, width, height);
		return put({ kind: 'renderbuffer', renderbuffer, width, height });
	}
	const texture = gl.createTexture();
	gl.bindTexture(gl.TEXTURE_2D, texture);
	gl.texStorage2D(gl.TEXTURE_2D, mipLevels, internalFormat, width, height);
	return put({ kind: 'texture', texture, width, height });
}

export function uploadTexture(handle, level, x, y, width, height, format, type, view) {
	const bytes = view.slice();
	const pixels = type === gl.FLOAT ? new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4) : bytes;
	gl.bindTexture(gl.TEXTURE_2D, objects[handle].texture);
	gl.texSubImage2D(gl.TEXTURE_2D, level, x, y, width, height, format, type, pixels);
}

function attach(target, attachment, object) {
	if (object.kind === 'renderbuffer')
		gl.framebufferRenderbuffer(target, attachment, gl.RENDERBUFFER, object.renderbuffer);
	else
		gl.framebufferTexture2D(target, attachment, gl.TEXTURE_2D, object.texture, 0);
}

export function createFramebuffer(colorView, depthHandle) {
	const colors = colorView.slice();
	const framebuffer = gl.createFramebuffer();
	gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
	const buffers = [];
	colors.forEach((handle, index) => {
		attach(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + index, objects[handle]);
		buffers.push(gl.COLOR_ATTACHMENT0 + index);
	});
	if (depthHandle)
		attach(gl.FRAMEBUFFER, gl.DEPTH_STENCIL_ATTACHMENT, objects[depthHandle]);
	gl.drawBuffers(buffers.length ? buffers : [gl.NONE]);
	const status = gl.checkFramebufferStatus(gl.FRAMEBUFFER);
	if (status !== gl.FRAMEBUFFER_COMPLETE)
		throw new Error(`Incomplete framebuffer (0x${status.toString(16)}).`);
	return put({ kind: 'framebuffer', framebuffer });
}

// Buffers and samplers

export function createBuffer(target, size) {
	const buffer = gl.createBuffer();
	gl.bindBuffer(target, buffer);
	gl.bufferData(target, size, gl.DYNAMIC_DRAW);
	return put({ kind: 'buffer', buffer, target, size });
}

export function uploadBuffer(handle, offset, view) {
	const object = objects[handle];
	gl.bindBuffer(object.target, object.buffer);
	gl.bufferSubData(object.target, offset, view.slice());
}

export function createSampler(minFilter, magFilter, wrapS, wrapT, wrapR, minLod, maxLod, anisotropy) {
	const sampler = gl.createSampler();
	gl.samplerParameteri(sampler, gl.TEXTURE_MIN_FILTER, minFilter);
	gl.samplerParameteri(sampler, gl.TEXTURE_MAG_FILTER, magFilter);
	gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_S, wrapS);
	gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_T, wrapT);
	gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_R, wrapR);
	gl.samplerParameterf(sampler, gl.TEXTURE_MIN_LOD, minLod);
	gl.samplerParameterf(sampler, gl.TEXTURE_MAX_LOD, maxLod);
	if (anisotropic && anisotropy > 1)
		gl.samplerParameterf(sampler, anisotropic.TEXTURE_MAX_ANISOTROPY_EXT, Math.min(anisotropy, gl.getParameter(anisotropic.MAX_TEXTURE_MAX_ANISOTROPY_EXT)));
	return put({ kind: 'sampler', sampler });
}

// Programs and pipelines

function compile(type, source) {
	const shader = gl.createShader(type);
	gl.shaderSource(shader, source);
	gl.compileShader(shader);
	if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
		throw new Error(`Shader compilation failed: ${gl.getShaderInfoLog(shader)}`);
	return shader;
}

export function createProgram(vertexSource, fragmentSource) {
	const program = gl.createProgram();
	gl.attachShader(program, compile(gl.VERTEX_SHADER, vertexSource));
	gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragmentSource));
	gl.linkProgram(program);
	if (!gl.getProgramParameter(program, gl.LINK_STATUS))
		throw new Error(`Shader link failed: ${gl.getProgramInfoLog(program)}`);
	return put({ kind: 'program', program });
}

export function bindProgramResource(handle, name, uniformBlock, slot) {
	const program = objects[handle].program;
	if (uniformBlock) {
		const index = gl.getUniformBlockIndex(program, name);
		if (index === gl.INVALID_INDEX)
			return false;
		gl.uniformBlockBinding(program, index, slot);
		return true;
	}
	const location = gl.getUniformLocation(program, name);
	if (!location)
		return false;
	gl.useProgram(program);
	gl.uniform1i(location, slot);
	return true;
}

// Layout written by Pipeline (Resources.cs).
export function createPipeline(view) {
	const data = view.slice();
	const floats = new Float32Array(data.buffer, data.byteOffset, data.length);
	let at = 0;
	const next = () => data[at++];
	const pipeline = {
		kind: 'pipeline',
		program: objects[next()].program,
		topology: next(),
		cull: next(),
		frontFace: next(),
		depthTest: next() === 1,
		depthWrite: next() === 1,
		depthFunction: next(),
		scissor: next() === 1,
		blend: next() === 1,
		blendSourceColor: next(), blendDestinationColor: next(), blendColor: next(),
		blendSourceAlpha: next(), blendDestinationAlpha: next(), blendAlpha: next(),
		writeMask: next(),
		blendFactor: [floats[at++], floats[at++], floats[at++], floats[at++]],
		layouts: [],
		sets: [],
	};
	for (let layout = next(); layout > 0; layout--) {
		const stride = next(), stepRate = next(), elements = [];
		for (let count = next(); count > 0; count--)
			elements.push({ location: next(), size: next(), type: next(), normalized: next() === 1, integer: next() === 1, offset: next() });
		pipeline.layouts.push({ stride, stepRate, elements });
	}
	for (let set = next(); set > 0; set--) {
		const elements = [];
		for (let count = next(); count > 0; count--)
			elements.push({ kind: next(), slot: next() });
		pipeline.sets.push(elements);
	}
	return put(pipeline);
}

export function createResourceSet(view) {
	const data = view.slice();
	const entries = [];
	for (let index = 0; index < data[0]; index++) {
		const kind = data[1 + index * 3], object = objects[data[2 + index * 3]], size = data[3 + index * 3];
		entries.push({ kind, object, size });
	}
	return put({ kind: 'resourceSet', entries });
}

export function destroy(handle) {
	const object = objects[handle];
	if (!object)
		return;
	switch (object.kind) {
		case 'texture': gl.deleteTexture(object.texture); break;
		case 'renderbuffer': gl.deleteRenderbuffer(object.renderbuffer); break;
		case 'framebuffer': gl.deleteFramebuffer(object.framebuffer); break;
		case 'buffer': gl.deleteBuffer(object.buffer); break;
		case 'sampler': gl.deleteSampler(object.sampler); break;
		case 'program': gl.deleteProgram(object.program); break;
	}
	objects[handle] = null;
	freeHandles.push(handle);
}

// Command stream (opcodes in Gl.cs)

let pipeline = null;
let pipelineApplied = false;
let framebuffer = null;
let indexBuffer = null;
const vertexBuffers = [];
const resourceSets = [];
const enabledAttributes = new Set();

function applyPipeline() {
	const p = pipeline;
	gl.useProgram(p.program);
	if (p.cull) {
		gl.enable(gl.CULL_FACE);
		gl.cullFace(p.cull === 1 ? gl.BACK : gl.FRONT);
	} else
		gl.disable(gl.CULL_FACE);
	gl.frontFace(p.frontFace);
	if (p.depthTest) {
		gl.enable(gl.DEPTH_TEST);
		gl.depthFunc(p.depthFunction);
	} else
		gl.disable(gl.DEPTH_TEST);
	gl.depthMask(p.depthWrite);
	if (p.scissor)
		gl.enable(gl.SCISSOR_TEST);
	else
		gl.disable(gl.SCISSOR_TEST);
	if (p.blend) {
		gl.enable(gl.BLEND);
		gl.blendFuncSeparate(p.blendSourceColor, p.blendDestinationColor, p.blendSourceAlpha, p.blendDestinationAlpha);
		gl.blendEquationSeparate(p.blendColor, p.blendAlpha);
		gl.blendColor(...p.blendFactor);
	} else
		gl.disable(gl.BLEND);
	gl.colorMask((p.writeMask & 1) !== 0, (p.writeMask & 2) !== 0, (p.writeMask & 4) !== 0, (p.writeMask & 8) !== 0);
	pipelineApplied = true;
}

function applyVertexBuffers(baseVertex) {
	const used = new Set();
	pipeline.layouts.forEach((layout, index) => {
		const source = vertexBuffers[index];
		if (!source)
			return;
		gl.bindBuffer(gl.ARRAY_BUFFER, source.buffer);
		const start = source.offset + baseVertex * layout.stride;
		for (const element of layout.elements) {
			used.add(element.location);
			if (!enabledAttributes.has(element.location)) {
				gl.enableVertexAttribArray(element.location);
				enabledAttributes.add(element.location);
			}
			if (element.integer)
				gl.vertexAttribIPointer(element.location, element.size, element.type, layout.stride, start + element.offset);
			else
				gl.vertexAttribPointer(element.location, element.size, element.type, element.normalized, layout.stride, start + element.offset);
			gl.vertexAttribDivisor(element.location, layout.stepRate);
		}
	});
	for (const location of [...enabledAttributes]) {
		if (!used.has(location)) {
			gl.disableVertexAttribArray(location);
			enabledAttributes.delete(location);
		}
	}
}

function applyResourceSets() {
	pipeline.sets.forEach((elements, set) => {
		const resources = resourceSets[set];
		if (!resources)
			return;
		elements.forEach((element, index) => {
			const entry = resources.entries[index];
			if (!entry?.object)
				return;
			if (element.kind === 0)
				gl.bindBufferRange(gl.UNIFORM_BUFFER, element.slot, entry.object.buffer, 0, entry.size);
			else if (element.kind === 1) {
				gl.activeTexture(gl.TEXTURE0 + element.slot);
				gl.bindTexture(gl.TEXTURE_2D, entry.object.texture);
			} else if (element.kind === 2) {
				// The set's sampler applies to all of the set's textures.
				for (const texture of elements)
					if (texture.kind === 1)
						gl.bindSampler(texture.slot, entry.object.sampler);
			}
		});
	});
}

function prepareDraw(baseVertex) {
	if (!pipelineApplied)
		applyPipeline();
	applyVertexBuffers(baseVertex);
	applyResourceSets();
}

export function execute(commandView, dataView) {
	const commands = commandView.slice();
	const floats = new Float32Array(commands.buffer, commands.byteOffset, commands.length);
	const data = dataView.length ? dataView.slice() : null;
	let at = 0;
	while (at < commands.length) {
		const op = commands[at++];
		switch (op) {
			case 1: { // SetFramebuffer
				framebuffer = objects[commands[at]];
				gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer.framebuffer);
				at += 3;
				break;
			}
			case 2: // Viewport
				gl.viewport(Math.round(floats[at]), Math.round(floats[at + 1]), Math.round(floats[at + 2]), Math.round(floats[at + 3]));
				gl.depthRange(floats[at + 4], floats[at + 5]);
				at += 6;
				break;
			case 3: // Scissor
				gl.scissor(commands[at], commands[at + 1], commands[at + 2], commands[at + 3]);
				at += 4;
				break;
			case 4: // ClearColor: clears ignore scissor and write masks, as in Veldrid
				gl.disable(gl.SCISSOR_TEST);
				gl.colorMask(true, true, true, true);
				gl.clearColor(floats[at], floats[at + 1], floats[at + 2], floats[at + 3]);
				gl.clear(gl.COLOR_BUFFER_BIT);
				pipelineApplied = false;
				at += 4;
				break;
			case 5: // ClearDepth
				gl.disable(gl.SCISSOR_TEST);
				gl.depthMask(true);
				gl.stencilMask(0xff);
				gl.clearDepth(floats[at]);
				gl.clearStencil(commands[at + 1]);
				gl.clear(gl.DEPTH_BUFFER_BIT | gl.STENCIL_BUFFER_BIT);
				pipelineApplied = false;
				at += 2;
				break;
			case 6: // SetPipeline
				pipeline = objects[commands[at++]];
				pipelineApplied = false;
				break;
			case 7: // SetVertexBuffer
				vertexBuffers[commands[at]] = { buffer: objects[commands[at + 1]].buffer, offset: commands[at + 2] };
				at += 3;
				break;
			case 8: // SetIndexBuffer
				indexBuffer = { buffer: objects[commands[at]].buffer, type: commands[at + 1], offset: commands[at + 2] };
				at += 3;
				break;
			case 9: // SetResourceSet
				resourceSets[commands[at]] = objects[commands[at + 1]];
				at += 2;
				break;
			case 10: { // Draw
				const [count, instances, start] = [commands[at], commands[at + 1], commands[at + 2]];
				at += 4;
				prepareDraw(0);
				if (instances > 1)
					gl.drawArraysInstanced(pipeline.topology, start, count, instances);
				else
					gl.drawArrays(pipeline.topology, start, count);
				break;
			}
			case 11: { // DrawIndexed
				const [count, instances, start, baseVertex] = [commands[at], commands[at + 1], commands[at + 2], commands[at + 3]];
				at += 5;
				// WebGL2 has no base vertex; offset the attribute pointers instead.
				prepareDraw(baseVertex);
				gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer.buffer);
				const offset = indexBuffer.offset + start * (indexBuffer.type === gl.UNSIGNED_INT ? 4 : 2);
				if (instances > 1)
					gl.drawElementsInstanced(pipeline.topology, count, indexBuffer.type, offset, instances);
				else
					gl.drawElements(pipeline.topology, count, indexBuffer.type, offset);
				break;
			}
			case 12: { // UpdateBuffer
				const object = objects[commands[at]];
				gl.bindBuffer(object.target, object.buffer);
				gl.bufferSubData(object.target, commands[at + 1], data, commands[at + 2], commands[at + 3]);
				at += 4;
				break;
			}
			case 13: { // Resolve
				const source = objects[commands[at]], target = objects[commands[at + 1]];
				const width = commands[at + 2], height = commands[at + 3];
				at += 4;
				gl.bindFramebuffer(gl.READ_FRAMEBUFFER, readFramebuffer);
				attach(gl.READ_FRAMEBUFFER, gl.COLOR_ATTACHMENT0, source);
				gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, drawFramebuffer);
				attach(gl.DRAW_FRAMEBUFFER, gl.COLOR_ATTACHMENT0, target);
				gl.disable(gl.SCISSOR_TEST);
				gl.blitFramebuffer(0, 0, width, height, 0, 0, width, height, gl.COLOR_BUFFER_BIT, gl.NEAREST);
				gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer ? framebuffer.framebuffer : null);
				pipelineApplied = false;
				break;
			}
			case 14: // GenerateMipmaps
				gl.bindTexture(gl.TEXTURE_2D, objects[commands[at++]].texture);
				gl.generateMipmap(gl.TEXTURE_2D);
				break;
			default:
				throw new Error(`Unknown render command ${op}.`);
		}
	}
}

// Canvas

export function resizeCanvas(width, height) {
	canvas.width = width;
	canvas.height = height;
}

// Rendering keeps row 0 at the top (docs/WEB.md); the canvas has it at the bottom, so flip once here.
export function present(handle, width, height) {
	gl.bindFramebuffer(gl.READ_FRAMEBUFFER, objects[handle].framebuffer);
	gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
	gl.disable(gl.SCISSOR_TEST);
	gl.blitFramebuffer(0, 0, width, height, 0, height, width, 0, gl.COLOR_BUFFER_BIT, gl.NEAREST);
	gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer ? framebuffer.framebuffer : null);
	pipelineApplied = false;
}

export function metrics() {
	return [canvas.clientWidth, canvas.clientHeight, window.devicePixelRatio || 1, screen.width, screen.height];
}

export function setCursorVisible(visible) {
	canvas.style.cursor = visible ? '' : 'none';
}

// Input, queued as lines for Sdl2Window.PumpEvents (Input.cs)

const events = [];

export function takeEvents() {
	const text = events.join('\n');
	events.length = 0;
	return text;
}

// KeyboardEvent.code to Veldrid's Key names.
const keys = {
	ShiftLeft: 'ShiftLeft', ShiftRight: 'ShiftRight', ControlLeft: 'ControlLeft', ControlRight: 'ControlRight',
	AltLeft: 'AltLeft', AltRight: 'AltRight', MetaLeft: 'WinLeft', MetaRight: 'WinRight', ContextMenu: 'Menu',
	ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
	Enter: 'Enter', Escape: 'Escape', Space: 'Space', Tab: 'Tab', Backspace: 'BackSpace',
	Insert: 'Insert', Delete: 'Delete', PageUp: 'PageUp', PageDown: 'PageDown', Home: 'Home', End: 'End',
	CapsLock: 'CapsLock', ScrollLock: 'ScrollLock', PrintScreen: 'PrintScreen', Pause: 'Pause', NumLock: 'NumLock',
	NumpadDivide: 'KeypadDivide', NumpadMultiply: 'KeypadMultiply', NumpadSubtract: 'KeypadSubtract',
	NumpadAdd: 'KeypadAdd', NumpadDecimal: 'KeypadDecimal', NumpadEnter: 'KeypadEnter',
	Backquote: 'Grave', Minus: 'Minus', Equal: 'Plus', BracketLeft: 'BracketLeft', BracketRight: 'BracketRight',
	Semicolon: 'Semicolon', Quote: 'Quote', Comma: 'Comma', Period: 'Period', Slash: 'Slash',
	Backslash: 'BackSlash', IntlBackslash: 'NonUSBackSlash',
};

function keyName(code) {
	if (keys[code])
		return keys[code];
	let match;
	if ((match = /^Key([A-Z])$/.exec(code)))
		return match[1];
	if ((match = /^Digit([0-9])$/.exec(code)))
		return `Number${match[1]}`;
	if ((match = /^Numpad([0-9])$/.exec(code)))
		return `Keypad${match[1]}`;
	if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code))
		return code;
	return null;
}

function modifiers(event) {
	return (event.altKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) | (event.shiftKey ? 4 : 0) | (event.metaKey ? 8 : 0);
}

const heldKeys = new Set();
const heldButtons = new Set();

// Releases everything held when the canvas loses the keyboard or the page is hidden, because
// the matching key-up or mouse-up then never reaches the canvas.
function releaseAll() {
	for (const name of heldKeys)
		events.push(`k 0 ${name} 0 0`);
	for (const button of heldButtons)
		events.push(`b 0 ${button}`);
	heldKeys.clear();
	heldButtons.clear();
}

function attachInput() {
	canvas.tabIndex = 0;
	const key = down => event => {
		const name = keyName(event.code);
		if (name) {
			if (!down && !heldKeys.has(name))
				return;
			if (down)
				heldKeys.add(name);
			else
				heldKeys.delete(name);
			events.push(`k ${down ? 1 : 0} ${name} ${modifiers(event)} ${event.repeat ? 1 : 0}`);
		}
		if (down && event.key.length === 1 && !event.ctrlKey && !event.metaKey)
			events.push(`c ${event.key.codePointAt(0)}`);
		// Keep Tab, Space, arrows and Backspace in the game instead of scrolling or navigating.
		if (name && !(event.metaKey || event.ctrlKey))
			event.preventDefault();
	};
	canvas.addEventListener('keydown', key(true));
	canvas.addEventListener('keyup', key(false));
	const position = event => events.push(`m ${event.offsetX} ${event.offsetY}`);
	const button = event => event.button === 0 ? 0 : event.button === 1 ? 1 : event.button === 2 ? 2 : 3 + event.button - 3;
	canvas.addEventListener('mousemove', position);
	canvas.addEventListener('mousedown', event => {
		canvas.focus();
		position(event);
		heldButtons.add(button(event));
		events.push(`b 1 ${button(event)}`);
	});
	window.addEventListener('mouseup', event => {
		if (heldButtons.delete(button(event)))
			events.push(`b 0 ${button(event)}`);
	});
	canvas.addEventListener('blur', releaseAll);
	window.addEventListener('blur', releaseAll);
	document.addEventListener('visibilitychange', () => {
		if (document.hidden)
			releaseAll();
	});
	canvas.addEventListener('contextmenu', event => event.preventDefault());
	canvas.addEventListener('wheel', event => {
		events.push(`w ${-Math.sign(event.deltaY)}`);
		event.preventDefault();
	}, { passive: false });
	new ResizeObserver(() => events.push('r')).observe(canvas);
}
