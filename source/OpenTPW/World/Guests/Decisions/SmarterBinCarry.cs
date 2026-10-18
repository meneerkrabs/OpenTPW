namespace OpenTPW;

/// <summary>
/// The pure rule of the litter bins (docs/SMARTER-GUESTS.md, phase 6b): a guest whose litter meter is full carries the litter to a bin up to <see cref="SearchRadius"/> cells away instead of dropping it, where the
/// original looks only <see cref="GuestSimulation.BinRadius"/> cells around the guest. The host keeps the whole mechanism (the bin list, the nearest-bin choice, the route request, the walk, the throw and the drop
/// with its draw when there is no bin in reach); the policy only answers how far to look. Nothing is drawn here.
/// </summary>
// [EXT:smarter-guests-litter] OpenTPW's own rule; the original's search looks at the bins within 3 cells only (tp.exe 0x00500DC0)
public static class SmarterBinCarry
{
	/// <summary>
	/// How far, in cells, a guest looks for a bin: eight, a little more than two and a half times the original's three. The sweep of docs/SMARTER-GUESTS.md (phase 6b: 5, 6, 8 and 12) gave a weak effect at 5 and 6 and the whole gain at 8, which a larger radius (12) does not improve in the scenario's layout (four bins
	/// along the street, 4 to 15 cells apart). The walk is the original's bin
	/// walk, so a guest never goes further than eight cells out of its way, a few park turns.
	/// </summary>
	public const int SearchRadius = 8;

	/// <summary>The radius within which this guest looks for a bin.</summary>
	public static int Radius( in GuestView guest ) => SearchRadius;
}
