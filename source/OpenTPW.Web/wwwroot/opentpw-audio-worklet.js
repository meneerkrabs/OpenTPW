// Plays the 16-bit stereo PCM that OpenTPW queues (opentpw-audio.js) and reports how many frames it
// has taken from the queue, like SDL's queued audio. An empty queue plays silence.
class PcmPlayer extends AudioWorkletProcessor {
	constructor() {
		super();
		this.chunks = [];
		this.offset = 0;
		this.played = 0;
		this.reported = 0;
		this.playing = false;
		this.port.onmessage = ({ data }) => {
			if (data.pcm)
				this.chunks.push(data.pcm);
			else if (data.play)
				this.playing = true;
			else if (data.stop) {
				this.playing = false;
				this.chunks = [];
				this.offset = 0;
			}
		};
	}

	process(inputs, outputs) {
		const [left, right] = outputs[0];
		let frame = 0;
		while (this.playing && frame < left.length && this.chunks.length) {
			const chunk = this.chunks[0];
			while (frame < left.length && this.offset < chunk.length) {
				left[frame] = chunk[this.offset];
				right[frame] = chunk[this.offset + 1];
				this.offset += 2;
				frame++;
			}
			if (this.offset >= chunk.length) {
				this.chunks.shift();
				this.offset = 0;
			}
		}
		this.played += frame;
		left.fill(0, frame);
		right.fill(0, frame);
		if (this.played - this.reported >= 256 || (frame === 0 && this.played !== this.reported)) {
			this.reported = this.played;
			this.port.postMessage(this.played);
		}
		return true;
	}
}

registerProcessor('opentpw-pcm', PcmPlayer);
