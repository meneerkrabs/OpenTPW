// WebAudio output for the browser build (docs/WEB.md). The game's SdlMovieAudioOutput (Browser/
// WebAudioOutput.cs) queues 16-bit stereo PCM here; an AudioWorklet plays it.
const outputs = [null];
const contexts = new Set();

// Browsers start audio only after a click or key press on the page.
function resumeAll() {
	for (const context of contexts)
		if (context.state === 'suspended')
			context.resume();
}
for (const type of ['pointerdown', 'keydown', 'touchend'])
	window.addEventListener(type, resumeAll, { capture: true });

function send(output, message, transfer) {
	if (output.node)
		output.node.port.postMessage(message, transfer ?? []);
	else
		output.pending.push([message, transfer]);
}

export function open(sampleRate) {
	let context;
	try {
		context = new AudioContext({ sampleRate, latencyHint: 'interactive' });
	} catch (error) {
		console.warn(`No audio output at ${sampleRate} Hz`, error);
		return 0;
	}
	contexts.add(context);
	const output = { context, node: null, pending: [], played: 0, queued: 0 };
	context.audioWorklet.addModule('opentpw-audio-worklet.js').then(() => {
		output.node = new AudioWorkletNode(context, 'opentpw-pcm', { numberOfInputs: 0, outputChannelCount: [2] });
		output.node.port.onmessage = ({ data }) => output.played = data;
		output.node.connect(context.destination);
		for (const [message, transfer] of output.pending)
			output.node.port.postMessage(message, transfer ?? []);
		output.pending.length = 0;
	}, error => console.warn('Audio worklet failed', error));
	outputs.push(output);
	return outputs.length - 1;
}

export function queue(id, view) {
	const output = outputs[id];
	const bytes = view.slice();
	const samples = new Int16Array(bytes.buffer, 0, bytes.length >> 1);
	const pcm = new Float32Array(samples.length);
	for (let index = 0; index < samples.length; index++)
		pcm[index] = samples[index] / 32768;
	output.queued += pcm.length >> 1;
	send(output, { pcm }, [pcm.buffer]);
}

export function play(id) {
	send(outputs[id], { play: true });
	resumeAll();
}

export function stop(id) {
	const output = outputs[id];
	send(output, { stop: true });
	output.queued = output.played;
}

export function played(id) {
	return outputs[id].played;
}

export function queued(id) {
	const output = outputs[id];
	return Math.max(0, output.queued - output.played);
}

export function latency(id) {
	const context = outputs[id].context;
	return (context.baseLatency || 0) + (context.outputLatency || 0);
}

export function close(id) {
	const output = outputs[id];
	if (!output)
		return;
	output.node?.disconnect();
	contexts.delete(output.context);
	output.context.close();
	outputs[id] = null;
}
