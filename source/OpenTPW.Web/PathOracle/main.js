// No user game-data import, persistent storage, online client, asset download, animation loop or game Start.
import { dotnet } from './_framework/dotnet.js';
import * as webgl from './opentpw-gl.js';
import * as text from './opentpw-text.js';

if (!['localhost', '127.0.0.1', '[::1]'].includes(location.hostname))
  throw new Error('The synthetic path diagnostic is loopback-only.');
const unavailable = () => { throw new Error('Game/media/audio access is not part of the synthetic path diagnostic.'); };
const runtime = await dotnet.create();
runtime.setModuleImports('opentpw-gl', webgl);
runtime.setModuleImports('opentpw-text', text);
runtime.setModuleImports('opentpw-page', { requestLevel: unavailable, requestLanguage: unavailable, chooseInput: unavailable, requestPoolSprites: unavailable });
runtime.setModuleImports('opentpw-audio', Object.fromEntries(['open', 'queue', 'play', 'stop', 'played', 'queued', 'latency', 'close'].map(name => [name, unavailable])));
const program = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).OpenTPW.Program;
await runtime.runMain();
if (typeof program.PathOracleStart !== 'function') throw new Error('Missing isolated path diagnostic exports.');
globalThis.opentpwPerf = Object.freeze({ program }); // Existing readback runner contract only; no PerfMode or game loop.
document.getElementById('status').textContent = 'Ready: generated terrain only.';
