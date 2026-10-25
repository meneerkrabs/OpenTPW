namespace OpenTPW;

/// <summary>
/// The cells that take a procedural texture (docs/LND.md): an inclusive rectangle of heightfield cells. A cell outside it keeps its static ground texture.
/// </summary>
/// <param name="X0">First column.</param>
/// <param name="Y0">First row.</param>
/// <param name="X1">Last column, inclusive.</param>
/// <param name="Y1">Last row, inclusive.</param>
// [BIN:TPW-EXE:0x00457CF5 rectangle] x1 = x0 + max(0, ThemeEngine.ProceduralTexturingXOffset) + width and y1 = y0 + max(0, ProceduralTexturingYOffset) + height, handed to 0x56DF70; the four values are the level's MapInfo.HeightfieldXStart, YStart, Width and Height (read through the getters 0x519410..0x519440); the cell tests of 0x56F090 and 0x56F380 are inclusive on both ends
public readonly record struct ProceduralTerrainRectangle( int X0, int Y0, int X1, int Y1 )
{
	public bool Contains( int x, int y ) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

	/// <summary>The rectangle of a level's settings (<c>levels/Standard.sam</c> then <c>levels/&lt;theme&gt;/Standard.sam</c>), or null when the level has none.</summary>
	public static ProceduralTerrainRectangle? From( SamSettings settings )
	{
		if ( !settings.Contains( "MapInfo.HeightfieldXStart" ) || !settings.Contains( "MapInfo.HeightfieldYStart" )
			|| !settings.Contains( "MapInfo.HeightfieldWidth" ) || !settings.Contains( "MapInfo.HeightfieldHeight" ) )
			return null;
		var offsetX = Math.Max( 0, settings.GetInt( "ThemeEngine.ProceduralTexturingXOffset", 0, optional: true ) );
		var offsetY = Math.Max( 0, settings.GetInt( "ThemeEngine.ProceduralTexturingYOffset", 0, optional: true ) );
		var x0 = settings.GetInt( "MapInfo.HeightfieldXStart" );
		var y0 = settings.GetInt( "MapInfo.HeightfieldYStart" );
		return new ProceduralTerrainRectangle( x0, y0, x0 + offsetX + settings.GetInt( "MapInfo.HeightfieldWidth" ), y0 + offsetY + settings.GetInt( "MapInfo.HeightfieldHeight" ) );
	}

	/// <summary>The settings layers the original reads for a level: the shared file, then the theme's.</summary>
	public static SamSettings? ReadLevelSettings( string level )
	{
		var layers = new List<SamDocument>();
		foreach ( var path in new[] { "/levels/Standard.sam", $"/levels/{level}/Standard.sam" } )
			if ( FileSystem.FileExists( path ) )
				layers.Add( SamDocument.Parse( FileSystem.ReadAllText( path ), path ) );
		return layers.Count == 0 ? null : new SamSettings( layers );
	}
}
