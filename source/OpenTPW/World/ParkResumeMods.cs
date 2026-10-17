using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// One extension or mod a park was saved with: its id, version and content digest (docs/MODS.md,
/// <see cref="ParkResumeSnapshot.Mods"/>). The digest is <see cref="ModSet.ContentDigest"/> of the mod's folder, or
/// <see cref="ModSet.NoFilesSha256"/> for an extension.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
internal sealed record ParkModRecord( string Id, string Version, string ContentSha256 );

/// <summary>
/// Recording and checking the mods of a park save (docs/MODS.md, docs/DETERMINISM.md). Only extensions and mods that
/// change the simulation are recorded, so a park saved without any (the default) has no <c>Mods</c> field and its save
/// is byte-identical to one from before mods existed. Loading needs the recorded mods active in the same version with
/// the same content, and no other simulation-changing mod; presentation-only mods may differ freely.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
internal static class ParkResumeMods
{
	/// <summary>Upper bound for the recorded mods of one save (defensive; a run has a handful).</summary>
	public const int MaximumRecords = 256;

	/// <summary>The records of the simulation-changing mods in <paramref name="active"/> in load order, or null when it has none.</summary>
	public static IReadOnlyList<ParkModRecord>? Capture( ModSet active )
	{
		ArgumentNullException.ThrowIfNull( active );
		if ( active.SimulationMods.Count == 0 )
			return null;
		return active.SimulationMods.Select( mod => new ParkModRecord( mod.Id, mod.Version.ToString(), mod.ContentSha256 ) ).ToArray();
	}

	/// <summary>
	/// The recorded mods' own consistency, with no active set: a bounded count, valid and distinct ids, versions and
	/// lowercase hex digests, and every <c>mod:&lt;id&gt;:</c> asset-manifest key naming a recorded mod.
	/// </summary>
	public static string? ShapeProblem( IReadOnlyList<ParkModRecord?>? mods, IReadOnlyDictionary<string, string>? assetHashes )
	{
		var recorded = new HashSet<string>( StringComparer.Ordinal );
		if ( mods != null )
		{
			if ( mods.Count == 0 )
				return "the mod list is empty (a park without mods has none)";
			if ( mods.Count > MaximumRecords )
				return $"the mod list holds {mods.Count} entries, above {MaximumRecords}";
			foreach ( var mod in mods )
			{
				if ( mod == null )
					return "the mod list holds an empty entry";
				if ( !ModManifest.IsValidId( mod.Id ) )
					return $"the mod id '{mod.Id}' is not valid";
				if ( !recorded.Add( mod.Id ) )
					return $"the mod '{mod.Id}' is listed twice";
				if ( !ModVersion.TryParse( mod.Version, out _ ) )
					return $"mod {mod.Id} has the version '{mod.Version}', not a version such as 1.0.0";
				if ( mod.ContentSha256 == null || !ParkResumeAssets.IsValidSha256( mod.ContentSha256 ) )
					return $"mod {mod.Id} has the content digest '{mod.ContentSha256}', not 64 lowercase hex characters";
			}
		}
		if ( assetHashes != null )
			foreach ( var key in assetHashes.Keys )
				if ( key != null && key.StartsWith( ContentRoot.ModPrefix, StringComparison.Ordinal ) )
				{
					if ( !ContentRoot.TryParseKey( key, out var root, out _ ) )
						return $"the asset key '{key}' does not name a mod";
					if ( !recorded.Contains( root.ModId! ) )
						return $"the asset key '{key}' belongs to mod {root.ModId}, which the save does not record";
				}
		return null;
	}

	/// <summary>
	/// Everything that keeps a save from loading under <paramref name="active"/>, one named reason per entry such as
	/// <c>needs community.tpi-rides 1.0.0</c>, or empty when the mods agree. Recorded mods that are not active, in another
	/// version or with other content are named first, then active simulation-changing mods the save does not record.
	/// </summary>
	public static IReadOnlyList<string> Unmet( IReadOnlyList<ParkModRecord>? saved, ModSet active )
	{
		ArgumentNullException.ThrowIfNull( active );
		var reasons = new List<string>();
		var current = active.SimulationMods.ToDictionary( mod => mod.Id, StringComparer.Ordinal );
		var recordedIds = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var record in saved ?? Array.Empty<ParkModRecord>() )
		{
			recordedIds.Add( record.Id );
			if ( !active.IsEnabled( record.Id ) )
				reasons.Add( $"needs {record.Id} {record.Version}"
					+ (active.Refused.Any( refusal => refusal.Id == record.Id && refusal.Kind == ModRefusalKind.SafeMode ) ? ", which is off in safe mode (script mods are off for this run; start again without --safe-mode)" : "" ) );
			else if ( !current.TryGetValue( record.Id, out var mod ) )
				// enabled, but now judged presentation-only: its files changed
				reasons.Add( $"needs {record.Id} {record.Version} with the content it was saved with" );
			else if ( mod.Version.ToString() != record.Version )
				reasons.Add( $"needs {record.Id} {record.Version}, not {mod.Version}" );
			else if ( mod.ContentSha256 != record.ContentSha256 )
				reasons.Add( $"needs {record.Id} {record.Version} with the content it was saved with (its files changed)" );
		}
		foreach ( var mod in active.SimulationMods )
			if ( !recordedIds.Contains( mod.Id ) )
				reasons.Add( $"was saved without {mod.Id} {mod.Version}, which changes the simulation; turn it off to load this park" );
		if ( reasons.Count == 0 && saved != null && !saved.Select( record => record.Id ).SequenceEqual( active.SimulationMods.Select( mod => mod.Id ), StringComparer.Ordinal ) )
			reasons.Add( $"needs its mods in the order {string.Join( ", ", saved.Select( record => record.Id ) )}" );
		return reasons;
	}

	/// <summary>Throws <see cref="InvalidDataException"/> naming the first unmet requirement; nothing when the mods agree.</summary>
	public static void Require( IReadOnlyList<ParkModRecord>? saved, ModSet active )
	{
		var unmet = Unmet( saved, active );
		if ( unmet.Count > 0 )
			throw new InvalidDataException( $"World save cannot be loaded: it {string.Join( "; ", unmet )}." );
	}

	/// <summary>
	/// The recorded mods that are not active as saved (missing, another version or other content), for a menu line such as
	/// <c>community.tpi-rides 1.0.0, community.hd-rides 2.0.0</c>; null when there are none. Unmet reasons that name no
	/// recorded mod (an extra active mod, another order) are left to the load's own message.
	/// </summary>
	public static string? MissingText( IReadOnlyList<ParkModRecord>? saved, ModSet active )
	{
		ArgumentNullException.ThrowIfNull( active );
		if ( saved == null )
			return null;
		var current = active.SimulationMods.ToDictionary( mod => mod.Id, StringComparer.Ordinal );
		var needed = saved.Where( record => !current.TryGetValue( record.Id, out var mod ) || mod.Version.ToString() != record.Version || mod.ContentSha256 != record.ContentSha256 )
			.Select( record => $"{record.Id} {record.Version}" ).ToList();
		return needed.Count == 0 ? null : string.Join( ", ", needed );
	}
}
