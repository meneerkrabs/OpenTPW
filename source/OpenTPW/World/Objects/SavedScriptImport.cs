namespace OpenTPW;

/// <summary>
/// Gives the imported objects' scripts the ids, list order, next id and pass counter of an original save's RSSE block (<see cref="RideScriptWorld.ImportSaved"/>), so the one slice
/// per eight passes ((id xor pass) &amp; 7, 0x5516E1) falls on the passes the saved park ran it on. A saved script belongs to the object whose thing id it names (+0xAC); a script
/// whose object is not in the park or whose folder does not match is reported and left out on the legacy root-only path. Ordinary-child graphs use a separate
/// bounded all-or-nothing importer; sound graphs and the original animation/table residuals are not restored. Nothing changes when a graph is refused.
/// </summary>
// [BIN:TPW-EXE:0x5597A0 RSSE loader] the header's pass counter, next id and the records' ids are the saved ones; the loader links every record at the list's head, so the walk runs the saved scripts in reverse of the file's order (RideScriptWorld.ImportSaved)
public static class SavedScriptImport
{
	/// <summary>A script the park holds for an imported object: its thing id, its catalogue entry, the script and the object's rider bridge (null for a bare script, which keeps no rider tables).</summary>
	public readonly record struct Candidate( int ThingId, ObjectCatalogEntry Entry, RideVM Script, RideVisitorBridge? Visitors = null );

	/// <summary>Applies the save's RSSE block to <paramref name="world"/>; returns the report lines.</summary>
	public static IReadOnlyList<string> Apply( RideScriptWorld world, SaveRsseBlock rsse, IEnumerable<Candidate> candidates, uint? clockWord = null )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( rsse );
		ArgumentNullException.ThrowIfNull( candidates );
		// Existing no-ordinary-child imports retain their established compatibility path. A rejected graph never falls through.
		if ( rsse.Records.Any( record => record.Raw.Length != SaveRsseRecord.Bytes || record.ChildScriptId != 0 || record.ParentScriptId != 0 ) )
			return SavedScriptGraphImport.Apply( world, rsse, candidates, clockWord );
		var pool = candidates.Where( candidate => candidate.ThingId != 0 && candidate.Script.Parent == null ).ToList();
		var matched = new List<(RideVM Vm, int SavedId)>();
		var unmatched = new List<string>();
		var usedRecords = new List<SaveRsseRecord>();
		foreach ( var record in rsse.Records )
		{
			var owners = pool.Where( candidate => candidate.ThingId == record.ThingId ).ToList();
			if ( record.ParentScriptId != 0 || owners.Count != 1 || !FolderMatches( owners[0].Entry, record.Folder ) || matched.Any( pair => ReferenceEquals( pair.Vm, owners[0].Script ) ) )
			{
				unmatched.Add( $"script {record.ScriptId} ({FolderName( record.Folder )}{(record.ParentScriptId != 0 ? $", child of {record.ParentScriptId}" : $", thing {record.ThingId}")})" );
				continue;
			}
			matched.Add( (owners[0].Script, checked( (int)record.ScriptId )) );
			usedRecords.Add( record );
		}
		var ids = matched.Select( pair => pair.SavedId ).ToList();
		if ( matched.Count == 0 || ids.Count != ids.Distinct().Count() || ids.Any( id => id <= 0 ) || rsse.NextScriptId == 0 || rsse.NextScriptId > int.MaxValue )
			return new[] { $"[APPROX:DET-013] The save's {rsse.Records.Count} RSSE scripts were not imported: {(matched.Count == 0 ? "none belongs to an imported object" : "their ids are not a unique positive set")}; the scripts keep OpenTPW's ids and the pass counter starts at 0." };
		world.ImportSaved( matched, usedRecords.Select( record => (int)record.ScriptId ).ToList(), (int)rsse.NextScriptId, rsse.TickCounter );
		var bridges = matched.Select( pair => pool.First( candidate => ReferenceEquals( candidate.Script, pair.Vm ) ).Visitors ).ToList();
		var runState = clockWord is { } clock ? ApplyRunState( world, matched, usedRecords, bridges, clock ) : Array.Empty<string>();
		return new[] { $"RSSE scripts imported: {matched.Count} of {rsse.Records.Count} took their saved ids ({string.Join( "/", ids )}), the pass counter {rsse.TickCounter} and next id {Math.Max( (int)rsse.NextScriptId, ids.Max() + 1 )}; the list walks in reverse of the saved order, as the original's loader leaves it{(unmatched.Count == 0 ? "" : $"; left out: {string.Join( ", ", unmatched )}")}." }.Concat( runState ).ToList();
	}

	/// <summary>
	/// Gives each matched script its saved run state (<see cref="RideVM.ImportSavedRunState"/>) on the saved game clock, which every absolute deadline of the records counts from. A script whose record does
	/// not fit this install's script keeps its fresh state and is named in the report.
	/// </summary>
	private static IReadOnlyList<string> ApplyRunState( RideScriptWorld world, IReadOnlyList<(RideVM Vm, int SavedId)> matched, IReadOnlyList<SaveRsseRecord> records, IReadOnlyList<RideVisitorBridge?> bridges, uint clockWord )
	{
		if ( !world.ImportClock( clockWord ) )
			return new[] { $"[APPROX:DET-013] The saved game clock {clockWord} was not applied: a script of this world had already run; the scripts restart from their first instruction." };
		var restored = new List<string>();
		var refused = new List<string>();
		var tables = new List<string>();
		for ( var index = 0; index < matched.Count; index++ )
		{
			var record = records[index];
			var reason = matched[index].Vm.ImportSavedRunState( record, clockWord );
			if ( reason == null )
			{
				restored.Add( $"{matched[index].SavedId}@{record.ProgramCounter}{(record.WaitDeadline != 0 ? $" (waiting until {record.WaitDeadline})" : "")}" );
				StageRiderTables( matched[index].Vm, record, bridges[index], clockWord, tables );
			}
			else
				refused.Add( $"script {matched[index].SavedId}: {reason}" );
		}
		var lines = new List<string> { $"RSSE run state imported on the saved game clock {clockWord}: {restored.Count} of {matched.Count} scripts resume at their saved program counter with their stack, variables, limbo table, result register, waits and timers ({string.Join( ", ", restored )}){(refused.Count == 0 ? "" : $"; kept fresh: {string.Join( "; ", refused )}")}." };
		lines.AddRange( tables );
		return lines;
	}

	/// <summary>
	/// A script whose run state was taken keeps its walk, bounce and head tables for the riders (<see cref="SavedRideTables"/>, staged on its rider bridge until the guest import has the riders) and marks
	/// its bridge as holding the saved state. A script with a child or sound child keeps no tables: the bridge has one set of them, the root's.
	/// </summary>
	internal static void StageRiderTables( RideVM vm, SaveRsseRecord record, RideVisitorBridge? bridge, uint clockWord, List<string> notes )
	{
		if ( bridge == null )
			return;
		bridge.SavedRunStateImported = true;
		var headSlots = bridge.Heads?.Count ?? 0;
		var occupied = record.WalkEntries.Any( entry => !entry.IsFree ) || record.BounceEntries.Any( entry => !entry.IsFree ) || record.HeadSlots.Any( slot => slot != 0 );
		if ( !occupied )
			return;
		if ( record.ChildScriptId != 0 || record.ParentScriptId != 0 )
		{
			notes.Add( $"[APPROX:DET-013] script {record.ScriptId} holds rider tables but has a child or parent script: its walk, bounce and head tables are not kept." );
			return;
		}
		var tables = SavedRideTables.Read( record, vm.Script.WalkSize, vm.Script.BounceSize, headSlots, clockWord, out var refusal );
		if ( tables == null )
		{
			notes.Add( $"[APPROX:DET-013] script {record.ScriptId}: the walk, bounce and head tables are not kept: {refusal}." );
			return;
		}
		bridge.StageSavedTables( tables );
	}

	private static string FolderName( string folder ) => folder.TrimEnd( '\\', '/' ).Split( '\\', '/' ).LastOrDefault() ?? folder;

	/// <summary>True when the saved script folder (<c>data\levels\jungle\Features\bus\</c>) names the object's own script directory.</summary>
	private static bool FolderMatches( ObjectCatalogEntry entry, string folder )
	{
		if ( entry.ScriptPath == null )
			return false;
		var directory = Path.GetDirectoryName( entry.ScriptPath.Replace( '\\', '/' ).TrimEnd( '/' ) )?.Replace( '\\', '/' ).Split( '/' ).LastOrDefault();
		return directory != null && string.Equals( directory, FolderName( folder ), StringComparison.OrdinalIgnoreCase );
	}
}
