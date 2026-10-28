namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-claims] A kart or water ride's protected track in a shared park (docs/COOP.md, "Claims"; C5-3): the tiles no other player's track line or take-back may change while the
/// ride is claimed against that player. OpenTPW tags each tile with its ride's owner word (TRACK-021, <c>[EXT:track-tile-owner]</c>), but a tile of an older save has owner word 0, which every
/// ride's builder joins and every editor clears (<see cref="TrackBuilder"/>'s compatible owners, <see cref="TrackEditor"/>), so the owner word alone does not say whose a tile is. A ride's
/// protected track is the union of its tagged tiles, the tiles a flood reaches from its seeds and the tiles its own walks visit; a tile's owner word is one attribution among these, so a tile
/// is each ride's whose protected track holds it (ambiguous when that is two rides', an orphan when it is none's). It is the same on every peer, and reading it leaves the park untouched.
/// </summary>
internal static class TrackProvenance
{
	/// <summary>
	/// The origin cells of the protected track of the ride whose station is <paramref name="station"/>, read on <paramref name="scratch"/>, a throwaway copy of the park's cells, whose tiles the
	/// ride's walks write as they run (the walks' own fields: links, step numbers, pieces, overlap words, none of which a walk or the flood reads). The track holds:
	/// <list type="bullet">
	/// <item>the ride's tagged tiles (owner word its own) and its seeds: its station's tiles and the laid tiles at its entry and its exit (the open-end collector starts at the exit whether or not the
	/// station has an arm toward it);</item>
	/// <item>every tile its own walks visit, run as they are: the circuit walk from its station (<see cref="TrackCircuitWalker.Walk"/>, which also goes straight on through a tile whose mask lacks
	/// its heading) and the open-end collector from its exit (<see cref="TrackEditor.CollectorTiles"/>);</item>
	/// <item>every tile a flood from all of these reaches, over every arm of every reached tile to the tile that arm points at, whether or not that tile's own arm points back, through tiles of any
	/// owner word (an owner-zero tile joins a tagged one, and the circuit walk accepts a route of mixed owners), junctions and side branches included.</item>
	/// </list>
	/// </summary>
	public static HashSet<(int X, int Y)> ProtectedTiles( ParkCellMap scratch, TrackFamily family, IParkGrid terrain, TrackStation station )
	{
		ArgumentNullException.ThrowIfNull( scratch );
		var tracks = scratch.Tracks;
		var builder = new TrackBuilder( scratch, family, () => null, terrain );
		var owner = builder.OwnerOf( station );
		var tiles = new HashSet<(int X, int Y)>();
		var pending = new Queue<(int X, int Y)>();
		void Reach( (int X, int Y)? tile )
		{
			if ( tile is { } origin && tracks.Get( origin.X, origin.Y ).IsOrigin && tiles.Add( origin ) )
				pending.Enqueue( origin );
		}
		foreach ( var (x, y, cell) in tracks.Cells() )
		{
			if ( cell.IsOrigin && cell.Owner == owner )
				Reach( (x, y) );
		}
		foreach ( var seed in new[] { station.First, station.Second, station.Entry, station.Exit } )
		{
			if ( tracks.InBounds( seed.X, seed.Y ) )
				Reach( builder.OriginOf( seed.X, seed.Y ) );
		}
		// the walks read the arms and types alone and write fields no walk reads, so they run one after the other on the same copy
		foreach ( var step in TrackCircuitWalker.Walk( builder, station ).Steps )
			Reach( (step.X, step.Y) );
		foreach ( var tile in new TrackEditor( builder, station ).CollectorTiles() )
			Reach( tile );
		while ( pending.TryDequeue( out var at ) )
		{
			var mask = tracks.Get( at.X, at.Y ).Mask;
			foreach ( var heading in TrackHeading.All )
			{
				if ( (mask & heading) == 0 )
					continue;
				var (nx, ny) = (at.X + TrackStation.TileCells * TrackHeading.Dx( heading ), at.Y + TrackStation.TileCells * TrackHeading.Dy( heading ));
				if ( tracks.InBounds( nx, ny ) )
					Reach( builder.OriginOf( nx, ny ) );
			}
		}
		return tiles;
	}
}
