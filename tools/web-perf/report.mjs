// Formats a browser performance report without running a browser or requiring game files.
export function markdown(report) {
	const lines = [`## ${report.label} — ${report.date} (${report.chrome}, ${report.renderer ?? 'renderer unknown'})`, ''];
	const results = report.results;
	if (results.start && !results.start.error)
		lines.push(`- Start: first frame ${results.start.firstFrameMs} ms, lobby ${results.start.lobbyMs} ms`);
	for (const [name, steady] of [['Lobby', results.lobby], ['Jungle', results.jungle?.steady], ['Jungle at zoom ' + results.jungle?.zoomed?.zoom, results.jungle?.zoomed]])
		if (steady && !steady.error)
			lines.push(`- ${name} steady: main thread busy ${steady.mainThreadBusyPercent}%, ${steady.fps} fps, frame p95 ${steady.p95Ms} ms, largest gap ${steady.largestGapMs} ms, long tasks ${steady.longTasks}`);
	if (results.jungle?.cold) {
		lines.push(`- Jungle cold: ready in ${results.jungle.cold.readyMs} ms, largest frame ${results.jungle.cold.maxFrameMs} ms`
			+ (results.jungle.cold.longFramesMs?.length ? `; frames over 50 ms: ${results.jungle.cold.longFramesMs.join(', ')}` : ''));
		if (results.jungle.cold.buildFramesMs?.length)
			lines.push(`- Jungle built over ${results.jungle.cold.buildFramesMs.length} frames, construction ms per frame (the last enters the park): ${results.jungle.cold.buildFramesMs.join(', ')}`);
	}
	const describe = profile => Object.entries(profile).map(([name, entry]) => `${name} ${entry.ms} (${entry.count})`).join(', ');
	if (results.jungle?.cold?.startProfile)
		lines.push(`- Jungle load profile until ready (ms, count; nested sections overlap): ${describe(results.jungle.cold.startProfile)}`);
	if (results.jungle?.cold?.profile)
		lines.push(`- Jungle load profile including 5 s after: ${describe(results.jungle.cold.profile)}`);
	if (results.buy && !results.buy.error) {
		lines.push('', '| First action | Largest frame (ms) | Frames ≥ 50 ms |', '| --- | ---: | ---: |');
		for (const [name, entry] of Object.entries(results.buy))
			lines.push(entry.missing ? `| ${name} | not listed | |` : `| ${name === 'openMenu' ? `Open buy menu / ${entry.selected ?? ''}` : name} | ${entry.maxFrameMs} | ${entry.over50Ms} |`);
	}
	if (results.revisit && !results.revisit.error)
		lines.push(`- Revisit from kept files (${results.revisit.kept}): Play shown after ${results.revisit.playShownMs} ms, first frame ${results.revisit.firstFrameMs} ms and lobby ${results.revisit.lobbyMs} ms after the click, Jungle ready in ${results.revisit.jungleReadyMs} ms`);
	if (results.digest?.simulationHash)
		lines.push('', `- Digest: ${results.digest.textures} textures in ${results.digest.textureMs} ms (hash ${results.digest.textureHash}); fixed park run in ${results.digest.simulationMs} ms (hash ${results.digest.simulationHash})`);
	if (results.switch?.steps)
		lines.push('', '| Step | Wasm MB | Live managed MB | Levels alive | Entities | Textures held | Archive files read MB | Archive members MB | Decoded sound MB | JS heap MB | GPU textures MB (live / count) | Renderer RSS MB |', '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |',
			...results.switch.steps.map(step => `| ${step.step} | ${(step.wasmBytes / 1048576).toFixed(0)} | ${(step.managedBytes / 1048576).toFixed(0)} | ${step.levelsAlive} | ${step.entities} | ${step.texturesHeld} | ${(step.archiveBytes / 1048576).toFixed(0)} | ${(step.archiveMemberBytes / 1048576).toFixed(0)} | ${(step.decodedSampleBytes / 1048576).toFixed(0)} | ${(step.jsHeapBytes / 1048576).toFixed(0)} | ${((step.gpuTextureBytes ?? 0) / 1048576).toFixed(0)} / ${step.gpuTextures ?? 0} | ${step.rssBytes ? (step.rssBytes / 1048576).toFixed(0) : ''} |`));
	if (results.memory && !results.memory.error)
		lines.push('', `- Memory over ${results.memory.minutes} min: wasm +${(results.memory.wasmGrowthBytes / 1048576).toFixed(1)} MB (peak ${(results.memory.peakWasmBytes / 1048576).toFixed(0)} MB), managed +${(results.memory.managedGrowthBytes / 1048576).toFixed(1)} MB`);
	for (const [name, entry] of Object.entries(results))
		if (entry?.error)
			lines.push(`- ${name}: FAILED — ${entry.error}`);
	return lines.join('\n');
}
