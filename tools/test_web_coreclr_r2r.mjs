import assert from 'node:assert/strict';
import test from 'node:test';
import { describeImage } from './web-coreclr/r2r.mjs';

const header = [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];
const section = (id, body) => [id, body.length, ...body];

test('a compiled image reports its function table entries and its payload size', () => {
	const bytes = Uint8Array.from([
		...header,
		// one passive element segment (flags 1, kind 0) with three function indices
		...section(9, [1, 1, 0, 3, 0, 1, 2]),
		// two passive data segments: a 2-byte one and the 5-byte payload
		...section(11, [2, 1, 2, 0xaa, 0xbb, 1, 5, 1, 2, 3, 4, 5]),
	]);
	assert.deepEqual(describeImage(bytes), { tableSize: 3, payloadSize: 5 });
});

test('an image without segments reports zeros', () => {
	assert.deepEqual(describeImage(Uint8Array.from(header)), { tableSize: 0, payloadSize: 0 });
});
