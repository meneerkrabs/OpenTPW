namespace OpenTPW;

/// <summary>
/// The referenced Theme Park Inc analyser's arrival-group ring, decoded from dynamic named fields in its exact thing body.
/// This retains data for the host's five-entry ring; it neither restores the native clock nor enables the TPI advisor.
/// </summary>
public static class SaveThemeParkIncArrivalRing
{
	public const int Capacity = 5;

	/// <summary>Returns owned, read-only signed group sizes, oldest first. Null means no analyser reference; an empty list is authoritative.</summary>
	// [BIN:TPI-EXE:0x64EAC0 analyser lookup] the World reference names model 13; an unrelated analyser is not a substitute for a zero reference
	// [BIN:TPI-EXE:0x5BB870 ring serializer] ring5 follows the variable-count recent-stays ring: head, count, raw wrapped byte, scratch word, then count signed words
	// [BIN:TPI-EXE:0x5BCB81 arrival append] the group size is stored unchanged after advancing/wrapping the index; 0x5BCE92/0x5BCEE4 can produce a negative remainder
	// [BIN:TPI-EXE:0x720CA0 arrival consumers] hype, big-group (0x721130) and exactly-one-group (0x722DF0) read held entries, not mTemp or admission totals
	public static IReadOnlyList<int>? Read( ThemeParkIncSaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var analyserId = world.Variables.ParkAnalyser;
		if ( analyserId == 0 )
			return null;
		if ( world.Find( analyserId ) is not { Model: ThemeParkIncThingModel.ParkAnalyser } thing )
			throw new InvalidDataException( "The referenced Theme Park Inc park analyser is missing or has the wrong model." );
		var body = new ThemeParkIncBody( thing.Model, world.BodyOf( thing ) );
		var capacity = body.U32( "ring5.mNumEntries" );
		var head = unchecked( (int)body.U32( "ring5.mCurrentEntry" ) );
		// The typed Ring5.WrappedAround reader collapses every nonzero byte to true; validate the original byte instead.
		var wrapped = body.U8( "ring5.mWrappedAround" );
		if ( head < -1 || head >= (long)capacity || wrapped > 1 || head == -1 && wrapped != 0 )
			throw new InvalidDataException( "Theme Park Inc analyser arrival ring has an invalid head or wrapped flag." );
		// [EXT:tpi-arrival-host-profile] the bounded layout supports native capacities 0..5; smaller retention cannot be replayed into a fixed five-entry host ring without changing continuation
		if ( capacity != Capacity )
			throw new InvalidDataException( $"Theme Park Inc analyser arrival capacity {capacity} is structurally supported but unsupported by the fixed host history profile (requires {Capacity})." );
		int Word( int slot ) => unchecked( (int)body.U32( $"ring5.mData[{slot}]" ) );
		var sizes = new List<int>( Capacity );
		if ( wrapped != 0 )
			for ( var slot = head + 1; slot < Capacity; slot++ )
				sizes.Add( Word( slot ) );
		for ( var slot = 0; slot <= head; slot++ )
			sizes.Add( Word( slot ) );
		return sizes.AsReadOnly();
	}
}
