namespace OpenTPW;

// [EXT:pools-slides] The slide activity's two seams into the simulation (docs/POOLS.md, "Water slides"): the park's slides, and the water of the swimmer's own changing room.
public sealed partial class GuestSimulation
{
	/// <summary>[EXT:pools-slides] The park's water slides, set by the level while the pools extension is on; null in a simulation without them.</summary>
	public PoolSlideWorld? PoolSlides { get; set; }

	/// <summary>[EXT:pools-slides] The water the guest's changing room shares (<see cref="SwimRegionOf"/>), or null when it has none or the guest names no changing room.</summary>
	internal PoolSwimRegion? SwimRegionOfGuest( Guest guest ) => FindAttraction( guest.AttractionId ) is { } ride ? SwimRegionOf( ride ) : null;
}
