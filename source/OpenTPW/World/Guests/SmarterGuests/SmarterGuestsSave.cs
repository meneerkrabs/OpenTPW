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
			Births = guests.Guests.Where( guest => guest.BirthId != 0 ).OrderBy( guest => guest.Id ).Select( guest => new SmarterGuestBirth( guest.Id, guest.BirthId ) ).ToArray()
		};
	}

	/// <summary>
	/// The section's own consistency against the guests it names and the mods the save records, or null when it holds: a section exists exactly when <c>opentpw.smarter-guests</c> is recorded; the
	/// counter starts at <see cref="SmarterGuestsState.FirstBirthId"/>; every id is non-zero, below the counter and given once, to a guest the save holds, once; and after the first tick
	/// every guest has an id.
	/// </summary>
	public static string? ShapeProblem( SmarterGuestsSaveData? section, IEnumerable<int>? guestIds, IReadOnlyList<ParkModRecord?>? mods )
	{
		var recorded = mods?.Any( mod => mod != null && mod.Id == ExtensionId ) == true;
		if ( section == null )
			return recorded ? $"the save records {ExtensionId} but has no SmarterGuests section" : null;
		if ( !recorded )
			return $"the save has a SmarterGuests section but does not record {ExtensionId}";
		return SectionProblem( section, guestIds );
	}

	/// <summary>The section's consistency with the guests it names, apart from the mod record (<see cref="ShapeProblem"/>).</summary>
	public static string? SectionProblem( SmarterGuestsSaveData section, IEnumerable<int>? guestIds )
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
			throw new InvalidDataException( $"The saved park has {ExtensionId} state, but the extension is not active." );
		if ( SectionProblem( section, guests.Guests.Select( guest => guest.Id ) ) is { } problem )
			throw new InvalidDataException( $"The saved smarter-guests state is inconsistent: {problem}." );
		foreach ( var guest in guests.Guests )
			guest.BirthId = 0;
		var byId = guests.Guests.ToDictionary( guest => guest.Id );
		foreach ( var birth in section.Births )
			byId[birth.GuestId].BirthId = birth.BirthId;
		state.Restore( section.ParkSeed, section.NextBirthId, section.Bootstrapped );
	}
}
