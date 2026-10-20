namespace OpenTPW;

/// <summary>Bounded TPW occupied blueprint offsets; unknown surfaces remain the legacy unraised preview.</summary>
internal static class BlueprintHeights
{
	// [BIN:TPW-EXE:0x5330C6–0x533189] truncate H, add 9, signed divide by 10, multiply by 10.
	// 0x67A830 temporarily sets x87 RC to truncate; this is not Math.Ceiling, particularly for negative H.
	internal static float? Quantize( float height )
	{
		if ( !float.IsFinite( height ) || height < int.MinValue || height >= 2147483520f ) return null;
		return ((long)height + 9) / 10 * 10f;
	}

	/// <summary>No secondary instances are claimed here, including fence posts. Only ordinary base cells and proven 4/9/10 owners.</summary>
	internal static float? Sample( ParkCellMap cells, int x, int y, Func<int, int, float?> ownerHeight )
	{
		if ( !cells.InBounds( x, y ) || cells.Tracks.Get( x, y ).Type != 0 || cells.Land?.IsPost( x, y ) == true ) return null;
		var type = cells.RawTypeAt( x, y );
		if ( type is 0 or 1 or 3 ) return 0;
		if ( !ParkCellTypes.IsObjectCell( type ) || ownerHeight( x, y ) is not { } height ) return null;
		return Quantize( height );
	}
}
