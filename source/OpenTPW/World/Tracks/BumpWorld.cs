namespace OpenTPW;

/// <summary>
/// The bump-ride module state of one park (the original's module globals): the cosmetic generator and the controllers in creation order. Owned by
/// the park's <see cref="RideScriptWorld"/>, so every ride runtime reaches it; it holds nothing for a park without a kart or water ride.
/// </summary>
public sealed class BumpWorld
{
	private readonly List<BumpController> controllers = new();

	public IReadOnlyList<BumpController> Controllers => controllers;

	/// <summary>The cosmetic LCG state (DAT 0x877B90): only the spray, smoke, TOOT and CRUNCH gates read it; gameplay draws use the world generator.</summary>
	// [BIN:TPW-EXE:0x00547F50 module lcg] s = s × 214013 + 2531011 (0x343FD, 0x269EC3), gating the water boats' spray (one in 8 every 32nd tick), the kart smoke and the horn and crash cues; the start value of the global is not traced and starts at 0 here
	public uint CosmeticState { get; internal set; }

	/// <summary>Draws the next cosmetic value: the LCG state as a signed 32-bit word.</summary>
	public int NextCosmetic()
	{
		CosmeticState = unchecked( CosmeticState * 214013u + 2531011u );
		return unchecked((int)CosmeticState);
	}

	internal void Add( BumpController controller )
	{
		if ( !controllers.Contains( controller ) )
			controllers.Add( controller );
	}

	internal void Remove( BumpController controller ) => controllers.Remove( controller );

	/// <summary>Controller part of <see cref="WorldStateHash"/>; called only while a controller exists, so every park without a kart or water ride keeps its pins.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( (long)CosmeticState );
		hash.Add( controllers.Count );
		foreach ( var controller in controllers )
			controller.AddCanonicalState( hash );
	}
}
