using System.Globalization;

namespace OpenTPW;

/// <summary>Opt-in bounds for retained mission SAM entries; no defaults or gameplay eligibility.</summary>
public static class TpiMissionSamValidation
{
	public sealed record EntryResult( SamEntry Entry, bool Checked, string? Error );

	/// <summary>
	/// Validates every retained occurrence, including duplicates. Unknown fields remain unchecked.
	/// First-value integer syntax follows the host SAM reader; native lexical equivalence is unknown.
	/// </summary>
	public static IReadOnlyList<EntryResult> ValidatePresentEntries( SamDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		return document.Entries.Select( Validate ).ToArray();
	}

	private static EntryResult Validate( SamEntry entry )
	{
		var upper = entry.Key switch
		{
			"Info.NoResearch" or "Completion.ThemeOpened" or "Completion.EndOfGame" or "Completion.LoadNewHoardings" => 2,
			"TechLevel.Theme" => 3,
			_ => 0
		};
		if ( upper != 0 )
		{
			if ( entry.Values.Count == 0 || !long.TryParse( entry.Values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value ) )
				return new( entry, true, "First retained value must be an integer." );
			return new( entry, true, value >= 0 && value < upper ? null : $"Value must be in [0,{upper}); no clamping was applied." );
		}
		foreach ( var (group, capacity) in new[] { ("BoundaryClicks", 64), ("ExitStatistic", 5) } )
		{
			if ( !entry.Key.StartsWith( group + "[", StringComparison.Ordinal ) )
				continue;
			var close = entry.Key.IndexOf( ']', group.Length + 1 );
			if ( close < 0 || close + 2 >= entry.Key.Length || entry.Key[close + 1] != '.' ||
				!int.TryParse( entry.Key.AsSpan( group.Length + 1, close - group.Length - 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var index ) )
				return new( entry, true, $"Unsupported host index syntax for {group}; expected nonnegative decimal [n].member." );
			return new( entry, true, index < capacity ? null : $"{group} index must be below {capacity}; no clamping was applied. Member value remains unchecked." );
		}
		return new( entry, false, null );
	}
}
