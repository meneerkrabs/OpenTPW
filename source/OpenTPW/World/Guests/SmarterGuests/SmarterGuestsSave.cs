using System.Text.Json.Serialization;
using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>One guest's birth id, keyed by the guest's thing id (the stable identity a restore rebuilds guests by; <see cref="GuestSimulationSnapshot"/>).</summary>
internal sealed record SmarterGuestBirth( int GuestId, ulong BirthId );

/// <summary>
/// The park-resume section of the <c>opentpw.smarter-guests</c> extension (<see cref="ParkResumeSnapshot.SmarterGuests"/>): the park seed, the birth-id counter, the first-tick flag and each
/// guest's birth id. Later phases add their own state here (docs/SMARTER-GUESTS.md).
/// </summary>
internal sealed record SmarterGuestsSaveData
{
	public required ulong ParkSeed { get; init; }
	public required ulong NextBirthId { get; init; }
	public required bool Bootstrapped { get; init; }
	/// <summary>One entry per guest that has an id, in guest-id order.</summary>
	public required IReadOnlyList<SmarterGuestBirth> Births { get; init; }
	/// <summary>
	/// The value-for-money rule's runs of price-emptied decisions (<see cref="PriceStalls"/>), in birth-id order; absent from the file while there are none, which keeps the section of a park without any
	/// (and every script-mod-only park) as it was.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<PriceStall>? Stalls { get; init; }
	/// <summary>
	/// The spreading rules' throughput estimates (<see cref="RideThroughputs"/>), in attraction-id order; absent from the file while there are none, which keeps the section of a park without any (an older version, a
	/// script-mod-only park, or an extension park whose attractions have not boarded anybody) as it was.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<RideThroughput>? Throughputs { get; init; }
	/// <summary>
	/// The planning rules' meal times (<see cref="MealTimes"/>, phase 5), in birth-id order; absent from the file while there are none, which keeps the section of a park without any (an older version, a
	/// script-mod-only park, or an extension park whose guests have not eaten) as it was.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<MealTime>? Meals { get; init; }
	/// <summary>
	/// The path rules' complaint times (<see cref="SmarterGuestsState.Complaints"/>, phase 6; the entries read "complained on turn"), in birth-id order; absent from the file while there are none, which keeps the section of
	/// a park without any (an older version, a script-mod-only park, or an extension park whose guests have not complained) as it was.
	/// </summary>
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<MealTime>? Complaints { get; init; }
}

/// <summary>
/// Capture, validation and restore of <see cref="SmarterGuestsSaveData"/>, apart from the guest snapshot, which this extension never touches. A park saved with the extension off has no
/// section, so its save is byte-identical to one from before the extension existed; builds from before it refuse a save that has one (the codec rejects unknown fields), as intended.
/// </summary>
// [EXT:smarter-guests] OpenTPW's own extension; the original game has no such state
internal static class SmarterGuestsSave
{
	/// <summary>The extension's id as the mod records name it.</summary>
	public static string ExtensionId => OfficialExtensions.SmarterGuests.Id;

	/// <summary>The section of <paramref name="guests"/>' extension state, or null when the extension is off for the simulation.</summary>
	public static SmarterGuestsSaveData? Capture( GuestSimulation guests )
	{
		ArgumentNullException.ThrowIfNull( guests );
		if ( guests.SmarterGuests is not { } state )
			return null;
		return new SmarterGuestsSaveData
		{
			ParkSeed = state.ParkSeed,
			NextBirthId = state.NextBirthId,
			Bootstrapped = state.Bootstrapped,
			Births = guests.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.Id ).Select( guest => new SmarterGuestBirth( guest.Id, guest.BirthId ) ).ToArray(),
			Stalls = state.Stalls.Count == 0 ? null : state.Stalls.Entries.ToArray(),
			Throughputs = state.Throughputs.Count == 0 ? null : state.Throughputs.Entries.ToArray(),
			Meals = state.Meals.Count == 0 ? null : state.Meals.Entries.ToArray(),
			Complaints = state.Complaints.Count == 0 ? null : state.Complaints.Entries.ToArray()
		};
	}

	/// <summary>
	/// The section's own consistency against the guests it names and the mods the save records, or null when it holds: a section exists exactly when <c>opentpw.smarter-guests</c> is recorded; the
	/// counter starts at <see cref="SmarterGuestsState.FirstBirthId"/>; every id is non-zero, below the counter and given once, to a guest the save holds, once; and after the first tick
	/// every guest has an id. Script mods share the section (their identity state): with <paramref name="scriptMods"/> (the save has a ScriptMods section) the section is required and the extension's
	/// record is not.
	/// </summary>
	public static string? ShapeProblem( SmarterGuestsSaveData? section, IEnumerable<int>? guestIds, IReadOnlyList<ParkModRecord?>? mods, bool scriptMods = false, IEnumerable<int>? attractionIds = null )
	{
		var recorded = mods?.Any( mod => mod != null && mod.Id == ExtensionId ) == true;
		if ( section == null )
			return recorded ? $"the save records {ExtensionId} but has no SmarterGuests section" : scriptMods ? "the save has a ScriptMods section but no SmarterGuests section" : null;
		if ( !recorded && !scriptMods )
			return $"the save has a SmarterGuests section but does not record {ExtensionId}";
		// the extension's own state belongs to a save that records the extension: a script-mod-only park carries the identity half of the section and nothing else
		if ( !recorded && (section.Stalls != null || section.Throughputs != null || section.Meals != null || section.Complaints != null) )
			return $"the SmarterGuests section holds extension state (stalls, throughputs, meals or complaints) but the save does not record {ExtensionId}";
		return SectionProblem( section, guestIds, attractionIds );
	}

	/// <summary>The section's consistency with the guests it names, apart from the mod record (<see cref="ShapeProblem"/>).</summary>
	public static string? SectionProblem( SmarterGuestsSaveData section, IEnumerable<int>? guestIds, IEnumerable<int>? attractionIds = null )
	{
		if ( section.Births == null )
			return "the SmarterGuests section has no birth list";
		if ( section.NextBirthId < SmarterGuestsState.FirstBirthId || section.NextBirthId == ulong.MaxValue )
			return $"the birth id counter {section.NextBirthId} is outside {SmarterGuestsState.FirstBirthId}..{ulong.MaxValue - 1}";
		var holders = new HashSet<int>( guestIds ?? Array.Empty<int>() );
		var seenGuests = new HashSet<int>();
		var seenIds = new HashSet<ulong>();
		foreach ( var birth in section.Births )
		{
			if ( birth == null )
				return "the SmarterGuests section holds an empty birth entry";
			if ( !holders.Contains( birth.GuestId ) )
				return $"the SmarterGuests section names guest {birth.GuestId}, whom the save does not hold";
			if ( !seenGuests.Add( birth.GuestId ) )
				return $"the SmarterGuests section gives guest {birth.GuestId} two birth ids";
			if ( birth.BirthId == 0 || birth.BirthId >= section.NextBirthId )
				return $"guest {birth.GuestId} has birth id {birth.BirthId}, outside 1..{section.NextBirthId - 1}";
			if ( !seenIds.Add( birth.BirthId ) )
				return $"birth id {birth.BirthId} is given to two guests";
		}
		if ( section.Bootstrapped && seenGuests.Count != holders.Count )
			return $"the extension has run, but only {seenGuests.Count} of {holders.Count} guests have a birth id";
		return StallsProblem( section.Stalls, seenIds ) ?? ThroughputsProblem( section.Throughputs, attractionIds ) ?? TurnsProblem( section.Meals, seenIds, "meal", "a meal" ) ?? TurnsProblem( section.Complaints, seenIds, "complaint", "a complaint" );
	}

	/// <summary>
	/// The throughput estimates' consistency: attractions the save holds (when <paramref name="attractionIds"/> is given), strictly ascending by id, each with a capacity of at least 1, an interval and a sample
	/// count inside the limits of <see cref="RideThroughputs"/>, a last boarding turn of −1 or more, and a demand flag only after a boarding. A boarding turn later than the saved park's turn is refused at restore.
	/// </summary>
	private static string? ThroughputsProblem( IReadOnlyList<RideThroughput>? entries, IEnumerable<int>? attractionIds )
	{
		if ( entries == null )
			return null;
		if ( entries.Count == 0 )
			return "the SmarterGuests section holds an empty throughput list (it is left out when there is none)";
		var known = attractionIds == null ? null : new HashSet<int>( attractionIds );
		int? previous = null;
		foreach ( var entry in entries )
		{
			if ( entry.AttractionId <= 0 )
				return $"the throughput estimates name attraction {entry.AttractionId}, outside 1..{int.MaxValue}";
			if ( known != null && !known.Contains( entry.AttractionId ) )
				return $"the throughput estimates name attraction {entry.AttractionId}, which the save does not hold";
			if ( previous is { } last && entry.AttractionId <= last )
				return "the throughput estimates are not in strictly ascending attraction id order";
			previous = entry.AttractionId;
			if ( entry.Capacity < 1 )
				return $"attraction {entry.AttractionId} has a throughput capacity of {entry.Capacity}, below 1";
			if ( entry.Interval256 < RideThroughputs.MinimumInterval256 || entry.Interval256 > RideThroughputs.MaximumInterval256 )
				return $"attraction {entry.AttractionId} has a throughput interval of {entry.Interval256}, outside {RideThroughputs.MinimumInterval256}..{RideThroughputs.MaximumInterval256}";
			if ( entry.Samples < 0 || entry.Samples > RideThroughputs.MaximumSamples )
				return $"attraction {entry.AttractionId} has {entry.Samples} throughput samples, outside 0..{RideThroughputs.MaximumSamples}";
			if ( entry.LastBoardTurn < -1 )
				return $"attraction {entry.AttractionId} has a last boarding turn of {entry.LastBoardTurn}, below -1";
			if ( entry.ClockTurn < entry.LastBoardTurn )
				return $"attraction {entry.AttractionId} has a silence clock at turn {entry.ClockTurn}, before its last boarding {entry.LastBoardTurn}";
			if ( entry.Demand && entry.ClockTurn < 0 )
				return $"attraction {entry.AttractionId} has demand without a silence clock";
		}
		return null;
	}

	/// <summary>
	/// The meal times' consistency: guests the section gives a birth id (<paramref name="births"/>), strictly ascending by birth id (so each guest once), each with a turn of 0 or more. A meal later than the saved
	/// park's turn is refused at restore.
	/// </summary>
	private static string? TurnsProblem( IReadOnlyList<MealTime>? meals, HashSet<ulong> births, string noun, string article )
	{
		if ( meals == null )
			return null;
		if ( meals.Count == 0 )
			return $"the SmarterGuests section holds an empty {noun} list (it is left out when there is none)";
		ulong? previous = null;
		foreach ( var entry in meals )
		{
			if ( !births.Contains( entry.BirthId ) )
				return $"the {noun} times name birth id {entry.BirthId}, whom the section does not hold";
			if ( entry.Turn < 0 )
				return $"birth id {entry.BirthId} has {article} on turn {entry.Turn}, below 0";
			if ( previous is { } last && entry.BirthId <= last )
				return $"the {noun} times are not in strictly ascending birth id order";
			previous = entry.BirthId;
		}
		return null;
	}

	/// <summary>
	/// The price stalls' consistency: runs of guests the section gives a birth id (<paramref name="births"/>), strictly ascending by birth id (so each guest once), each below
	/// from 1 to <see cref="PriceStalls.MaximumRun"/>.
	/// </summary>
	private static string? StallsProblem( IReadOnlyList<PriceStall>? stalls, HashSet<ulong> births )
	{
		if ( stalls == null )
			return null;
		ulong? previous = null;
		foreach ( var entry in stalls )
		{
			if ( !births.Contains( entry.BirthId ) )
				return $"the price stalls name birth id {entry.BirthId}, whom the section does not hold";
			if ( entry.Count < 1 || entry.Count > PriceStalls.MaximumRun )
				return $"birth id {entry.BirthId} has a run of {entry.Count} price-emptied decisions, outside 1..{PriceStalls.MaximumRun}";
			if ( previous is { } last && entry.BirthId <= last )
				return "the price stalls are not in strictly ascending birth id order";
			previous = entry.BirthId;
		}
		return null;
	}

	/// <summary>
	/// Puts the section's state and ids into <paramref name="guests"/>, whose guests were restored from the same save. A null section leaves a fresh state: its first tick gives every guest its id.
	/// A section for a simulation without the extension is refused.
	/// </summary>
	public static void Restore( GuestSimulation guests, SmarterGuestsSaveData? section )
	{
		ArgumentNullException.ThrowIfNull( guests );
		if ( section == null )
			return;
		if ( guests.SmarterGuests is not { } state )
			throw new InvalidDataException( $"The saved park has guest identity state ({ExtensionId} or script mods), but neither is active." );
		if ( SectionProblem( section, guests.Guests.Select( guest => guest.Id ), guests.Attractions.Select( attraction => attraction.AttractionId ) ) is { } problem )
			throw new InvalidDataException( $"The saved smarter-guests state is inconsistent: {problem}." );
		// the stamps are the guests' park turns, which the turns' phase may put one past the restored tick count's turn: the phase is taken again on the next running tick
		var latest = GuestSimulation.LatestParkTurn( guests.TickCount );
		foreach ( var entry in section.Throughputs ?? [] )
			if ( Math.Max( entry.LastBoardTurn, entry.ClockTurn ) > latest )
				throw new InvalidDataException( $"The saved smarter-guests state is inconsistent: attraction {entry.AttractionId} has a turn {Math.Max( entry.LastBoardTurn, entry.ClockTurn )}, after the park's turn {latest}." );
		foreach ( var entry in section.Meals ?? [] )
			if ( entry.Turn > latest )
				throw new InvalidDataException( $"The saved smarter-guests state is inconsistent: birth id {entry.BirthId} has a meal on turn {entry.Turn}, after the park's turn {latest}." );
		foreach ( var entry in section.Complaints ?? [] )
			if ( entry.Turn > latest )
				throw new InvalidDataException( $"The saved smarter-guests state is inconsistent: birth id {entry.BirthId} has a complaint on turn {entry.Turn}, after the park's turn {latest}." );
		foreach ( var guest in guests.Guests )
			guest.BirthId = 0;
		var byId = guests.Guests.ToDictionary( guest => guest.Id );
		foreach ( var birth in section.Births )
			byId[birth.GuestId].BirthId = birth.BirthId;
		state.Restore( section.ParkSeed, section.NextBirthId, section.Bootstrapped );
		if ( section.Stalls is { Count: > 0 } stalls )
			state.Stalls.Load( stalls );
		if ( section.Throughputs is { Count: > 0 } throughputs )
			state.Throughputs.Load( throughputs );
		if ( section.Meals is { Count: > 0 } meals )
			state.Meals.Load( meals );
		if ( section.Complaints is { Count: > 0 } complaints )
			state.Complaints.Load( complaints );
	}
}
