namespace OpenTPW;

/// <summary>
/// Gives the imported objects' scripts the ids, list order, next id and pass counter of an original save's RSSE block (<see cref="RideScriptWorld.ImportSaved"/>), so the one slice
/// per eight passes ((id xor pass) &amp; 7, 0x5516E1) falls on the passes the saved park ran it on. A saved script belongs to the object whose thing id it names (+0xAC); a script
/// whose object is not in the park, a saved child script (a parent's script creates its children as it runs) and a script of a folder other than the object's own are reported and left
/// out. Nothing changes when the save holds no usable record.
/// </summary>
// [BIN:TPW-EXE:0x5597A0 RSSE loader] the header's pass counter, next id and the records' ids are the saved ones; the loader links every record at the list's head, so the walk runs the saved scripts in reverse of the file's order (RideScriptWorld.ImportSaved)
public static class SavedScriptImport
{
	/// <summary>A script the park holds for an imported object: its thing id, its catalogue entry and the script.</summary>
	public readonly record struct Candidate( int ThingId, ObjectCatalogEntry Entry, RideVM Script );

	/// <summary>Applies the save's RSSE block to <paramref name="world"/>; returns the report lines.</summary>
	public static IReadOnlyList<string> Apply( RideScriptWorld world, SaveRsseBlock rsse, IEnumerable<Candidate> candidates )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( rsse );
		ArgumentNullException.ThrowIfNull( candidates );
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
		return new[] { $"RSSE scripts imported: {matched.Count} of {rsse.Records.Count} took their saved ids ({string.Join( "/", ids )}), the pass counter {rsse.TickCounter} and next id {Math.Max( (int)rsse.NextScriptId, ids.Max() + 1 )}; the list walks in reverse of the saved order, as the original's loader leaves it{(unmatched.Count == 0 ? "" : $"; left out: {string.Join( ", ", unmatched )}")}." };
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
