namespace OpenTPW.Mods;

/// <summary>
/// The id rule of the <c>opentpw.cross-edition-objects</c> extension, shared by every mod's <c>objects.json</c>
/// (<see cref="ModObjectsFile"/>). Theme Park World and Theme Park Inc number their objects from the same space (168 of the
/// 202 distinct Theme Park Inc ids are also Theme Park World ids, 145 of them naming other objects), so a mod that brings
/// Theme Park Inc attractions into Theme Park World gives each archive a new id from the range it declares as <c>idRange</c>.
/// </summary>
// [EXT:cross-edition-objects] OpenTPW's own id policy; the original catalogue only warns of a repeated Info.Id
// [BIN:TPW-EXE:0x00412E90 definition by id] the original keeps a definition whose Info.Id is taken (it stays listed) and every lookup by id returns the first in load order, so a mod object with a taken id would be unreachable (RIDES-024)
public static class CrossEditionObjects
{
	/// <summary>The lowest id a mod may reserve: above every Theme Park World (up to 4605) and Theme Park Inc (up to 4500) object.</summary>
	public const int FirstReservableId = 5000;
	/// <summary>The highest id a mod may reserve: the original park saves hold a placed object's Info.Id (<c>mId</c>) in 16 bits (docs/TPWS-PAYLOAD.md).</summary>
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
}
