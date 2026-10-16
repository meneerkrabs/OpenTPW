namespace OpenTPW.Mods;

/// <summary>
/// An inclusive range of <c>Info.Id</c>s that one mod reserves for the objects it brings from another original game
/// (<c>infoIds</c> in <c>opentpw-mod.json</c>, docs/MODS.md).
/// </summary>
public readonly record struct InfoIdRange( int First, int Last )
{
	public bool Contains( int id ) => id >= First && id <= Last;

	public bool Overlaps( InfoIdRange other ) => First <= other.Last && other.First <= Last;

	public override string ToString() => $"{First}..{Last}";
}

/// <summary>An object definition a mod could not claim an <c>Info.Id</c> for, and why.</summary>
public sealed record InfoIdRefusal( string Archive, int Id, string Reason );

/// <summary>
/// The id rules of the <c>opentpw.cross-edition-objects</c> extension. Theme Park World and Theme Park Inc number their objects
/// from the same space (168 of the 202 distinct Theme Park Inc ids are also Theme Park World ids, 145 of them naming other objects), so a mod that brings
/// Theme Park Inc attractions into Theme Park World gives each archive a new id from a range it declares, and the engine
/// refuses anything else instead of letting the later definition lose silently.
/// </summary>
// [EXT:cross-edition-objects] OpenTPW's own id policy; the original catalogue only warns of a repeated Info.Id
// [BIN:TPW-EXE:0x00412E90 definition by id] the original keeps a definition whose Info.Id is taken (it stays listed) and every lookup by id returns the first in load order, so a mod object with a taken id would be unreachable (RIDES-024)
public static class CrossEditionObjects
{
	/// <summary>The lowest id a mod may reserve: above every Theme Park World (up to 4605) and Theme Park Inc (up to 4500) object.</summary>
	public const int FirstReservableId = 5000;
	/// <summary>The highest id a mod may reserve: the original park saves hold an attraction's id in 16 bits (docs/TPWS-PAYLOAD.md, <c>mId</c>).</summary>
	public const int LastReservableId = 65535;
	/// <summary>The most ids one mod may reserve.</summary>
	public const int MaximumRangeSize = 4096;

	/// <summary>The reason a range cannot be reserved, or null.</summary>
	public static string? RangeProblem( int first, int last )
	{
		if ( first < FirstReservableId || last > LastReservableId )
			return $"must lie within {FirstReservableId}..{LastReservableId}";
		if ( last < first )
			return "must not end before it starts";
		if ( (long)last - first + 1 > MaximumRangeSize )
			return $"may hold at most {MaximumRangeSize} ids";
		return null;
	}

	/// <summary>
	/// Judges the ids a mod claims for its object archives: each must lie in the mod's range, be unique within the mod and
	/// not be taken by an object already in the catalogue (the game's own, or an earlier mod's). The mod's other objects are
	/// unaffected by a refused one.
	/// </summary>
	/// <param name="taken">The ids of every object that is already loaded.</param>
	public static IReadOnlyList<InfoIdRefusal> CheckClaims( InfoIdRange? range, IEnumerable<(string Archive, int Id)> claims, ISet<int> taken )
	{
		var refusals = new List<InfoIdRefusal>();
		var seen = new Dictionary<int, string>();
		foreach ( var (archive, id) in claims )
		{
			if ( range is not { } declared )
				refusals.Add( new InfoIdRefusal( archive, id, "the mod declares no 'infoIds' range" ) );
			else if ( !declared.Contains( id ) )
				refusals.Add( new InfoIdRefusal( archive, id, $"Info.Id {id} is outside the mod's range {declared}" ) );
			else if ( taken.Contains( id ) )
				refusals.Add( new InfoIdRefusal( archive, id, $"Info.Id {id} is already used by another object" ) );
			else if ( seen.TryGetValue( id, out var other ) )
				refusals.Add( new InfoIdRefusal( archive, id, $"Info.Id {id} is also claimed by {other}" ) );
			else
				seen.Add( id, archive );
		}
		return refusals;
	}
}
