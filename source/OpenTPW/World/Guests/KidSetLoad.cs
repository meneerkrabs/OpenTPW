namespace OpenTPW;

/// <summary>
/// Which kid and kid-head sets the original loads and in what order: tp.exe's folder loader (0x00541310) stops the <c>kids</c> and <c>kidsheads</c> folders at the limit of 0x0041A9D0 and loads four named
/// sets first, so the bank a guest's draw selects is its place in that load order, not in the alphabetical one.
/// </summary>
public static class KidSetLoad
{
	// [BIN:TPW-EXE:0x00764030 kid sets loaded first] the loader's first pass (0x005419C0, "Loading Avatar Sprites") walks the table {SPR_BI.ESP, SPR_KI.ESP, SPR_TA.ESP, SPR_SU.ESP} and loads the folder's entry of each name (the case-insensitive equality 0x00424CF0), stopping at the limit; its second pass loads the remaining entries in the folder's name order (0x00541210)
	/// <summary>The sets the kid folders load first, in the order the original loads them.</summary>
	public static readonly IReadOnlyList<string> PriorityNames = new[] { "SPR_BI", "SPR_KI", "SPR_TA", "SPR_SU" };

	/// <summary>
	/// How many sets the <c>kids</c> and <c>kidsheads</c> folders load for a detail word <paramref name="numKids"/> (the preset's <c>GameOptions.NUMKIDS</c>): 2, 4, 6 and 8 for 0, 1, 2 and 3, and 8 for anything above (the word is unsigned).
	/// </summary>
	// [BIN:TPW-EXE:0x0041A9D0 kid set limit] the jump table 0x0041AA24 holds 2 (0x0041AA09), 4, 6 and 8 for the word at 0x007858D0 below 4 and 8 above it; the hardware-class path in front of it (the flags word 0x0079D86C, class 0x4000) gives 8 for the memory word 8, 6 for 0x10, 4 for 0x20 and 0 otherwise
	public static int Limit( int numKids ) => (uint)numKids switch
	{
		0 => 2,
		1 => 4,
		2 => 6,
		_ => 8
	};

	/// <summary>The limit <paramref name="settings"/> give, or null for no limit: no settings, or a preset whose original file is missing (the data this limit belongs to is then missing too).</summary>
	public static int? LimitOf( IGraphicsSettings? settings ) =>
		// the resolved detail (Custom's simulation options are reset to High's unless the enhanced-game-options fix is on), as the particle density reads it
		settings != null && settings.GetPresetValues( settings.Current.Preset ) != null ? Limit( settings.Detail.NumKids ) : null;

	/// <summary>
	/// The sets the original keeps, in its load order, from <paramref name="sorted"/> (the folder's loadable sets in name order): the priority names present first, then the rest in name order, each pass
	/// stopping once <paramref name="limit"/> sets are loaded (the test follows a successful load, so a limit of 0 or 1 still loads one set). A null limit keeps them all.
	/// </summary>
	// [BIN:TPW-EXE:0x00541ACE kid set limit test] after each loaded entry the count is compared with the limit (0x00541ACE in the first pass, 0x00541BC2 in the second), so the stop is "count >= limit" after a load
	public static List<T> Order<T>( IReadOnlyList<T> sorted, Func<T, string> name, int? limit )
	{
		var kept = new List<T>();
		bool Full() => limit is { } max && kept.Count >= max;
		foreach ( var priority in PriorityNames )
		{
			foreach ( var set in sorted )
			{
				if ( kept.Contains( set ) || !string.Equals( name( set ), priority, StringComparison.OrdinalIgnoreCase ) )
					continue;
				kept.Add( set );
				if ( Full() )
					return kept;
			}
		}
		foreach ( var set in sorted )
		{
			if ( kept.Contains( set ) )
				continue;
			kept.Add( set );
			if ( Full() )
				return kept;
		}
		return kept;
	}
}
