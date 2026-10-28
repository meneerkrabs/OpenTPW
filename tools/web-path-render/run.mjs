#!/usr/bin/env node
// Opt-in real-path presented-framebuffer diagnostic. Never loads a user's game directory.
import { promises as fs } from 'node:fs';
import { createHash } from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import { mismatchesOf, names } from './assertions.mjs';
import { Cdp, launchChrome, sleep, startServer, stopChrome } from '../web-perf/lib.mjs';

const options = { root: process.argv[2], chrome: '/usr/bin/google-chrome' };
for (let i = 3; i < process.argv.length; i += 2) {
  const key = { '--chrome': 'chrome', '--report': 'report', '--source-sha': 'sourceSha', '--runtime': 'runtime' }[process.argv[i]];
  if (!key || !process.argv[i + 1]) throw new Error(`Unknown/incomplete option ${process.argv[i]}`);
  options[key] = process.argv[i + 1];
}
if (!options.root || !options.report || !/^[a-f0-9]{40}$/.test(options.sourceSha ?? '') || !['aot', 'interpreter'].includes(options.runtime))
  throw new Error('Usage: run.mjs <published wwwroot> --report <json> --source-sha <40-hex commit> --runtime <aot|interpreter> [--chrome <path>]');
const report = { contract: 'synthetic real OriginalTerrain + Level.Render; same-size A/B/A GPU pack switch and first suspended frame',
  started: new Date().toISOString(), declaredSourceCommit: options.sourceSha, declaredRuntime: options.runtime,
  provenanceNote: 'Source commit/runtime supplied by build operator; bundle hashes pin the bytes actually served. Pair with exact CI checkout/build evidence.',
  cases: [], mismatches: [], errors: [], bundle: [] };
let temporary, server, chrome, cdp;
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
try {
  const root = path.resolve(options.root);
  async function pinFiles(directory) {
    for (const item of (await fs.readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(directory, item.name);
      if (item.isSymbolicLink()) throw new Error(`Symlink not allowed in reproducible oracle bundle: ${file}`);
      if (item.isDirectory()) await pinFiles(file);
      else if (item.isFile()) {
        const data = await fs.readFile(file);
        report.bundle.push({ path: path.relative(root, file), bytes: data.length, sha256: digest(data) });
      }
    }
  }
  await pinFiles(root);
  if (!report.bundle.some(x => x.path === 'main.js') || !report.bundle.some(x => x.path.endsWith('.wasm')))
    throw new Error('No runnable published browser bundle (main.js and WASM required)');
  await fs.mkdir(path.dirname(path.resolve(options.report)), { recursive: true });
  const images = `${path.resolve(options.report)}.frames`;
  await fs.mkdir(images, { recursive: true });
  temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'opentpw-path-oracle-'));
  await fs.mkdir(path.join(temporary, 'profile'));
  await fs.mkdir(path.join(temporary, 'empty-game'));
  server = await startServer(root, path.join(temporary, 'empty-game'));
  const launched = await launchChrome(options.chrome, path.join(temporary, 'profile'));
  chrome = launched.chrome;
  cdp = await Cdp.connect(launched.url);
  report.browser = await cdp.send('Browser.getVersion');
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const send = (method, params = {}) => cdp.send(method, params, sessionId, 65000);
  cdp.on(event => {
    if (event.sessionId === sessionId && event.method === 'Runtime.exceptionThrown')
      report.errors.push(event.params.exceptionDetails.exception?.description ?? event.params.exceptionDetails.text);
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true, timeout: 60000 });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
    return result.result.value;
  };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Page.navigate', { url: `${server.origin}/?perf=1&keep=off` });
  const deadline = Date.now() + 180000;
  for (;;) {
    if (await evaluate('typeof globalThis.opentpwPerf?.program?.PathOracleStart === "function"')) break;
    if (report.errors.length || Date.now() >= deadline) throw new Error('Path oracle export unavailable: publish with OpenTpwPathRenderOracle=true.');
    await sleep(200);
  }
  try {
    report.gl = await evaluate(`(() => {
      const canvas = document.getElementById('canvas');
      canvas.style.setProperty('display', 'block', 'important');
      canvas.style.setProperty('width', '320px', 'important');
      canvas.style.setProperty('height', '200px', 'important');
      if (!(canvas.clientWidth > 0 && canvas.clientHeight > 0)) throw new Error('Invisible oracle canvas');
      opentpwPerf.program.PathOracleStart();
      const gl = canvas.getContext('webgl2');
      if (!gl || gl.isContextLost()) throw new Error('WebGL context unavailable');
      const debug = gl.getExtension('WEBGL_debug_renderer_info');
      return { version: gl.getParameter(gl.VERSION), vendor: gl.getParameter(gl.VENDOR), renderer: gl.getParameter(gl.RENDERER),
        unmaskedVendor: debug ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL) : null,
        unmaskedRenderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null };
    })()`);
    for (const name of names) {
      // The draw, default framebuffer readback AND PNG capture are one JS task. No warm-up, retry draw or await.
      const observation = await evaluate(`(() => {
        const canvas = document.getElementById('canvas'), gl = canvas.getContext('webgl2');
        if (!gl || gl.isContextLost()) throw new Error('WebGL context unavailable');
        const initialError = gl.getError();
        if (initialError !== gl.NO_ERROR) throw new Error('Pre-render GL error ' + initialError);
        const frame = JSON.parse(opentpwPerf.program.PathOracleDraw(${JSON.stringify(name)}));
        const ratio = window.devicePixelRatio || 1;
        if (canvas.clientWidth !== 320 || canvas.clientHeight !== 200 || gl.drawingBufferWidth !== Math.round(320 * ratio)
          || gl.drawingBufferHeight !== Math.round(200 * ratio) || frame.width !== gl.drawingBufferWidth || frame.height !== gl.drawingBufferHeight)
          throw new Error('Canvas/world/output dimensions differ');
        const previous = gl.getParameter(gl.READ_FRAMEBUFFER_BINDING);
        try {
          gl.bindFramebuffer(gl.READ_FRAMEBUFFER, null);
          if (gl.checkFramebufferStatus(gl.READ_FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) throw new Error('Incomplete read framebuffer');
          for (const sample of frame.samples) {
            const x = Math.floor(sample.u * gl.drawingBufferWidth), y = Math.floor(sample.v * gl.drawingBufferHeight);
            if (x < 2 || y < 2 || x >= gl.drawingBufferWidth - 2 || y >= gl.drawingBufferHeight - 2) throw new Error('Sample outside safe framebuffer interior');
            const pixels = new Uint8Array(36);
            gl.readPixels(x - 1, y - 1, 3, 3, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
            sample.pixelX = x; sample.pixelY = y; sample.pixels = Array.from(pixels);
          }
          const corner = new Uint8Array(4);
          gl.readPixels(1, 1, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, corner);
          const error = gl.getError();
          if (error !== gl.NO_ERROR) throw new Error('Render/readback GL error ' + error);
          return { ...frame, devicePixelRatio: ratio, corner: Array.from(corner), png: canvas.toDataURL('image/png') };
        } finally {
          gl.bindFramebuffer(gl.READ_FRAMEBUFFER, previous);
          const error = gl.getError();
          if (error !== gl.NO_ERROR) throw new Error('Read binding restore GL error ' + error);
        }
      })()`);
      if (!observation.png?.startsWith('data:image/png;base64,')) throw new Error('Missing same-frame PNG');
      const bytes = Buffer.from(observation.png.slice('data:image/png;base64,'.length), 'base64');
      const filename = `${String(observation.ordinal).padStart(2, '0')}-${name}.png`;
      await fs.writeFile(path.join(images, filename), bytes);
      delete observation.png;
      observation.image = { path: `${path.basename(images)}/${filename}`, sha256: digest(bytes), bytes: bytes.length };
      report.cases.push(observation);
    }
  } finally {
    await evaluate('opentpwPerf.program.PathOracleStop()');
  }
  report.mismatches.push(...mismatchesOf(report.cases));
} catch (error) {
  report.errors.push(error.stack ?? String(error));
} finally {
  for (const cleanup of [() => cdp?.close(), () => stopChrome(chrome), () => server?.close(),
    () => temporary && fs.rm(temporary, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })]) {
    try { await cleanup(); } catch (error) { report.errors.push('Cleanup: ' + (error.stack ?? error)); }
  }
  report.finished = new Date().toISOString();
  report.passed = report.cases.length === names.length && report.errors.length === 0 && report.mismatches.length === 0;
  await fs.mkdir(path.dirname(path.resolve(options.report)), { recursive: true });
  await fs.writeFile(options.report, JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ passed: report.passed, cases: report.cases.length, mismatches: report.mismatches, errors: report.errors }, null, 2));
  if (!report.passed) process.exitCode = 1;
}
