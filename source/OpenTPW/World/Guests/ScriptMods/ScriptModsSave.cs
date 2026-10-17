using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW;

/// <summary>One script mod's saved state: the canonical <c>state</c> text, the failure counters, the disabled flag and the versions it ran under.</summary>
internal sealed record ScriptModSaveData
{
	/// <summary><see cref="ScriptLimits.BehaviourVersion"/> when the park was saved: a park of another version is refused, never resumed under other rules.</summary>
	public required int BehaviourVersion { get; init; }
	/// <summary><see cref="ScriptLimits.ScriptApiVersion"/> when the park was saved.</summary>
	public required string ApiVersion { get; init; }
	/// <summary><see cref="ScriptState.Serialize"/> of the mod's <c>state</c>: canonical text, checked byte for byte on load.</summary>
	public required string State { get; init; }
	public required long RuntimeErrors { get; init; }
	public required long BudgetFailures { get; init; }
	public required long InvalidResults { get; init; }
	public required bool Disabled { get; init; }
}

/// <summary>The park-resume section of the script mods (<see cref="ParkResumeSnapshot.ScriptMods"/>), keyed by mod id.</summary>
internal sealed record ScriptModsSaveData
{
	public required IReadOnlyDictionary<string, ScriptModSaveData> Mods { get; init; }
}

/// <summary>
/// Capture, validation and restore of <see cref="ScriptModsSaveData"/> (docs/SCRIPT-MODS.md, "Saves and versioning"). A park saved with no script mod has no section, so its save is byte-identical to one from
/// before script mods existed; a build from before it refuses a save that has one (the codec rejects unknown fields), as intended. The section sits beside the smarter-guests section, whose guest identity
/// state (birth ids and the park seed) script mods need and share.
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no such state
internal static class ScriptModsSave
{
	/// <summary>Upper bound for the mods of one section (the save's mod list has the same bound).</summary>
	public const int MaximumMods = ParkResumeMods.MaximumRecords;

	/// <summary>The section for the guests' script mods, or null when no script mod is enabled.</summary>
	public static ScriptModsSaveData? Capture( GuestSimulation guests )
	{
		ArgumentNullException.ThrowIfNull( guests );
		if ( guests.ScriptMods is not { } runtime )
			return null;
		var mods = new SortedDictionary<string, ScriptModSaveData>( StringComparer.Ordinal );
		foreach ( var mod in runtime.Mods )
			mods.Add( mod.Id, new ScriptModSaveData
			{
				BehaviourVersion = ScriptLimits.BehaviourVersion,
				ApiVersion = ScriptLimits.ScriptApiVersion,
				State = ScriptState.Serialize( mod.State ),
				RuntimeErrors = mod.RuntimeErrors,
				BudgetFailures = mod.BudgetFailures,
				InvalidResults = mod.InvalidResults,
				Disabled = mod.Disabled
			} );
		return new ScriptModsSaveData { Mods = mods };
	}

	/// <summary>
	/// The section's own consistency against the mods the save records, or null when it holds: every key names a recorded mod, there is at least one, and each entry is valid (<see cref="EntryProblem"/>).
	/// That a section exists exactly when a script mod is recorded needs the mod set (a record does not say whether a mod has scripts); <see cref="Restore"/> checks it against the active one.
	/// </summary>
	public static string? ShapeProblem( ScriptModsSaveData? section, IReadOnlyList<ParkModRecord?>? mods )
	{
		if ( section == null )
			return null;
		if ( section.Mods == null || section.Mods.Count == 0 )
			return "the ScriptMods section holds no mod";
		if ( section.Mods.Count > MaximumMods )
			return $"the ScriptMods section holds {section.Mods.Count} mods, above {MaximumMods}";
		var recorded = new HashSet<string>( mods?.Where( mod => mod != null ).Select( mod => mod!.Id ) ?? Enumerable.Empty<string>(), StringComparer.Ordinal );
		foreach ( var (id, entry) in section.Mods )
		{
			if ( !recorded.Contains( id ) )
				return $"the ScriptMods section holds state for {id}, which the save does not record";
			if ( EntryProblem( id, entry ) is { } problem )
				return problem;
		}
		return null;
	}

	/// <summary>The section against the active mod set: present exactly when a script mod is enabled, with one entry for each, or null when they agree.</summary>
	public static string? ActiveProblem( ScriptModsSaveData? section, ModSet active )
	{
		var expected = active.Mods.Where( mod => mod.Scripts != null ).Select( mod => mod.Id ).Order( StringComparer.Ordinal ).ToArray();
		if ( section?.Mods == null )
			return expected.Length == 0 ? null : $"the save has no ScriptMods section, but the script mod{(expected.Length == 1 ? "" : "s")} {string.Join( ", ", expected )} {(expected.Length == 1 ? "is" : "are")} active";
		var held = section.Mods.Keys.Order( StringComparer.Ordinal ).ToArray();
		return expected.SequenceEqual( held ) ? null : $"the ScriptMods section is for {string.Join( ", ", held )}, but the active script mods are {(expected.Length == 0 ? "none" : string.Join( ", ", expected ))}";
	}

	/// <summary>One mod's entry: the versions this build runs, canonical state text that parses, counters of at most the disable limit and a flag that agrees with them.</summary>
	public static string? EntryProblem( string id, ScriptModSaveData? entry )
	{
		if ( entry == null )
			return $"the ScriptMods section has an empty entry for {id}";
		if ( entry.BehaviourVersion != ScriptLimits.BehaviourVersion )
			return $"{id} was saved under script behaviour version {entry.BehaviourVersion}, but this build runs version {ScriptLimits.BehaviourVersion}";
		if ( entry.ApiVersion != ScriptLimits.ScriptApiVersion )
			return $"{id} was saved under script API {entry.ApiVersion}, but this build runs {ScriptLimits.ScriptApiVersion}";
		if ( entry.RuntimeErrors < 0 || entry.BudgetFailures < 0 || entry.InvalidResults < 0 )
			return $"{id} has a negative failure counter";
		var total = (decimal)entry.RuntimeErrors + entry.BudgetFailures + entry.InvalidResults;
		// a mod is disabled by the call that reaches the limit, so it is disabled exactly when the counters reach it, and no counter passes it
		if ( total > ScriptLimits.DisableAfterFailures )
			return $"{id} counts {total} failures, above the limit of {ScriptLimits.DisableAfterFailures} that disables it";
		if ( entry.Disabled != (total >= ScriptLimits.DisableAfterFailures) )
			return entry.Disabled ? $"{id} is disabled with only {total} failures" : $"{id} counts {total} failures but is not disabled";
		if ( entry.State == null )
			return $"{id} has no state";
		try
		{
			var parsed = ScriptState.Parse( entry.State );
			if ( ScriptState.Serialize( parsed ) != entry.State )
				return $"the state of {id} is not in canonical form";
		}
		catch ( ScriptStateException exception )
		{
			return $"the state of {id} is invalid: {exception.Message}";
		}
		return null;
	}

	/// <summary>
	/// Puts the section's state, counters and flags into <paramref name="guests"/>' script mods. The section must name exactly the active script mods: no section for a park with script mods, or one
	/// for a park without, is refused, as is any entry of another version.
	/// </summary>
	public static void Restore( GuestSimulation guests, ScriptModsSaveData? section )
	{
		ArgumentNullException.ThrowIfNull( guests );
		var runtime = guests.ScriptMods;
		if ( runtime == null )
		{
			if ( section != null )
				throw new InvalidDataException( "The saved park has script mod state, but no script mod is active." );
			return;
		}
		if ( section?.Mods == null )
			throw new InvalidDataException( $"The saved park has no script mod state, but {string.Join( ", ", runtime.Mods.Select( mod => mod.Id ) )} {(runtime.Mods.Count == 1 ? "is" : "are")} active." );
		var expected = runtime.Mods.Select( mod => mod.Id ).Order( StringComparer.Ordinal ).ToArray();
		var held = section.Mods.Keys.Order( StringComparer.Ordinal ).ToArray();
		if ( !expected.SequenceEqual( held ) )
			throw new InvalidDataException( $"The saved script mod state is for {string.Join( ", ", held )}, but {string.Join( ", ", expected )} {(expected.Length == 1 ? "is" : "are")} active." );
		// validate everything before touching anything
		foreach ( var mod in runtime.Mods )
			if ( EntryProblem( mod.Id, section.Mods[mod.Id] ) is { } problem )
				throw new InvalidDataException( $"The saved script mod state is inconsistent: {problem}." );
		foreach ( var mod in runtime.Mods )
		{
			var entry = section.Mods[mod.Id];
			mod.Restore( ScriptState.Parse( entry.State ), entry.RuntimeErrors, entry.BudgetFailures, entry.InvalidResults, entry.Disabled );
		}
	}
}
