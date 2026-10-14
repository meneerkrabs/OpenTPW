namespace OpenTPW;

/// <summary>Explicit local-file static inspection, independent of installations and live worlds.</summary>
internal static class TpiMissionInspection
{
	public static void Run( string path )
	{
		if ( !Path.GetExtension( path ).Equals( ".sam", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "--inspect-tpi-mission requires a local .sam file (docs/TPI-MISSION-INSPECTION.md)." );
		var text = File.ReadAllText( path );
		var document = SamDocument.Parse( text, path );
		var results = TpiMissionSamValidation.ValidatePresentEntries( document );
		Console.WriteLine( "TPI mission static inspection: modified-NoCD schema bounds only; not retail or gameplay validation." );
		Console.WriteLine( "Evidence binary SHA-256: 77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a" );
		foreach ( var result in results )
		{
			var status = result.Error != null ? "ERROR" : result.Checked ? "bounded" : "unchecked";
			Console.WriteLine( $"{result.Entry.Source}:{result.Entry.Line}: {result.Entry.Key} [{status}] retained tokens: {System.Text.Json.JsonSerializer.Serialize( result.Entry.Values )}" );
			if ( result.Error != null )
				Console.WriteLine( result.Error );
		}
		var retainedLines = document.Entries.Select( entry => entry.Line ).ToHashSet();
		var lines = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		var inBlock = false;
		var omitted = 0;
		var skippedShapeLines = 0;
		for ( var index = 0; index < lines.Length; index++ )
		{
			var line = lines[index].Trim();
			if ( line.StartsWith( "---", StringComparison.Ordinal ) ) { inBlock = !inBlock; skippedShapeLines++; continue; }
			if ( inBlock ) { skippedShapeLines++; continue; }
			if ( line.Length == 0 || line[0] == '#' || line.StartsWith( "//", StringComparison.Ordinal ) || retainedLines.Contains( index + 1 ) )
				continue;
			omitted++;
			Console.WriteLine( $"{path}:{index + 1}: omitted/unrecognized lexical record: {line}" );
		}
		Console.WriteLine( $"{results.Count} retained entries; {results.Count( result => result.Checked )} bounds checked; {omitted} omitted records; {skippedShapeLines} skipped shape lines." );
		Console.WriteLine( "Case-sensitive known keys only. Indexed member values, unknown keys, quoted-token syntax, trailing text, omitted records and shape blocks are not schema-validated. Only the first retained scalar value is checked; no arity, missing/default/reset, whole-text validity or runtime eligibility claim." );
		if ( results.Any( result => result.Error != null ) )
			throw new InvalidDataException( "TPI mission inspection rejected retained entries; see source/line diagnostics above." );
	}
}
