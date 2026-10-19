using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// One extension or mod a park was saved with: its id, version and content digest (docs/MODS.md,
/// <see cref="ParkResumeSnapshot.Mods"/>). The digest is <see cref="ModSet.ContentDigest"/> of the mod's folder, or
/// <see cref="ModSet.NoFilesSha256"/> for an extension.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
internal sealed record ParkModRecord( string Id, string Version, string ContentSha256 );

/// <summary>What keeps a recorded or active mod from agreeing with a save (<see cref="ParkResumeMods.UnmetItems"/>).</summary>
internal enum UnmetKind
{
	/// <summary>The save records the mod and it is not enabled.</summary>
	Missing,
	/// <summary>The mod is enabled but not recorded in this run (a mod now judged presentation-only, or an extension this edition does not record).</summary>
	NotRecordable,
	/// <summary>The mod is enabled in another version than the save's.</summary>
	OtherVersion,
	/// <summary>The mod is enabled with other content than the save's.</summary>
	OtherContent,
	/// <summary>The mod is enabled and changes the simulation, but the save does not record it.</summary>
	Extra,
	/// <summary>Everything agrees but the order of the mods.</summary>
	Order,
}

/// <summary>One reason of <see cref="ParkResumeMods.Unmet"/>: the mod it names (null for the order), its kind, its text and the save's record of the mod (null when the save has none).</summary>
internal sealed record UnmetMod( string? Id, UnmetKind Kind, string Reason, ParkModRecord? Record );

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

	/// <summary>
	/// [EXT:pools-extension] The extensions a save records only while the park actually uses them: <c>opentpw.pools</c>, recorded when the park has pool cells or a changing room, and
	/// [EXT:umbrellas-save] <c>opentpw.umbrellas</c>, recorded when the park has a brolly shop or a guest with an umbrella, and [EXT:winter-save] <c>opentpw.winter</c>, recorded when the park has a winter kiosk or a glühwein stall or a guest
	/// with a warm item, and [EXT:cheats-save] <c>opentpw.cheats</c>, recorded once the park has used one of the extension's own cheats (<see cref="ParkCheats.UsesExtension"/>; a park cheated only through the original's cheats
	/// does not record it), and [EXT:evening-torches-save] <c>opentpw.evening</c>, recorded when the park holds a torch. A park that does not use one saves byte-identically whether it is on or off, and loads whether it is on or off
	/// (docs/POOLS.md, docs/UMBRELLAS.md, docs/WINTER.md, docs/CHEATS.md, docs/EVENING.md, docs/MODS.md).
	/// </summary>
	public static IReadOnlySet<string> UsedOnlyExtensionIds { get; } = new HashSet<string>( StringComparer.Ordinal ) { OfficialExtensions.Pools.Id, OfficialExtensions.Umbrellas.Id, OfficialExtensions.Winter.Id, OfficialExtensions.Cheats.Id, OfficialExtensions.Evening.Id };

	/// <summary>
	/// The simulation-changing mods of <paramref name="active"/> that a save or an export counts: all of them, except that <c>opentpw.rain-mood</c>, <c>opentpw.winter-weather</c>, <c>opentpw.winter-cold</c> and <c>opentpw.winter-always</c> are left out in a Theme Park Inc run, where their
	/// edition gates are off (<see cref="FeatureGates.RainMood"/>, <see cref="FeatureGates.WinterWeather"/>, <see cref="FeatureGates.WinterCold"/>, <see cref="FeatureGates.WinterAlways"/>) and they change nothing, so recording them would only refuse a load or an export for no reason.
	/// </summary>
	// [EXT:rain-mood-extension] the extension is not offered in a Theme Park Inc run, so an enabled flag there must not leave a trace in what is recorded; [EXT:winter-weather] likewise (a Theme Park Inc park has no weather of this kind)
	public static IEnumerable<SimulationMod> RecordableMods( ModSet active ) =>
		active.SimulationMods.Where( mod => (mod.Id != OfficialExtensions.RainMood.Id && mod.Id != OfficialExtensions.WinterWeather.Id && mod.Id != OfficialExtensions.WinterCold.Id && mod.Id != OfficialExtensions.WinterAlways.Id) || !GameEditions.IsThemeParkInc );

	/// <summary>
	/// The records of the simulation-changing mods in <paramref name="active"/> in load order, or null when it has none. A used-only extension
	/// (<see cref="UsedOnlyExtensionIds"/>) is left out unless its id is in <paramref name="inUse"/>.
	/// </summary>
	public static IReadOnlyList<ParkModRecord>? Capture( ModSet active, IReadOnlySet<string>? inUse = null )
	{
		ArgumentNullException.ThrowIfNull( active );
		var recorded = RecordableMods( active ).Where( mod => !UsedOnlyExtensionIds.Contains( mod.Id ) || inUse?.Contains( mod.Id ) == true ).ToArray();
		if ( recorded.Length == 0 )
			return null;
		return recorded.Select( mod => new ParkModRecord( mod.Id, mod.Version.ToString(), mod.ContentSha256 ) ).ToArray();
	}

	/// <summary>
	/// [EXT:pools-extension] The used-only extensions a park uses: <c>opentpw.pools</c> when <paramref name="cells"/> holds a pool cell, or when the park holds one of the extension's own objects
	/// (<paramref name="poolsObjects"/>: a changing room, whose asset keys name the extension and whose swimmers the save holds). [EXT:umbrellas-save] <c>opentpw.umbrellas</c> when
	/// <paramref name="umbrellas"/>: the park holds one of the extension's own objects (a brolly shop) or a guest with an umbrella (<see cref="UmbrellasInUse"/>). [EXT:winter-save] <c>opentpw.winter</c> when <paramref name="winter"/>:
	/// the park holds a winter kiosk or a glühwein stall or a guest with a warm item (<see cref="WinterInUse"/>).
	/// </summary>
	/// <param name="cheats">[EXT:cheats-save] The park has used one of the cheats extension's own cheats or has one waiting (<see cref="ParkCheats.UsesExtension"/>).</param>
	/// <param name="evening">[EXT:evening-torches-save] The park holds a torch (<see cref="EveningInUse"/>).</param>
	public static IReadOnlySet<string> InUse( ParkCellMap? cells, bool poolsObjects = false, bool umbrellas = false, bool winter = false, bool cheats = false, bool evening = false )
	{
		var used = new HashSet<string>( StringComparer.Ordinal );
		if ( poolsObjects || (cells != null && cells.CountOf( ParkCellType.PoolWater ) > 0) )
			used.Add( OfficialExtensions.Pools.Id );
		if ( umbrellas )
			used.Add( OfficialExtensions.Umbrellas.Id );
		if ( winter )
			used.Add( OfficialExtensions.Winter.Id );
		if ( cheats )
			used.Add( OfficialExtensions.Cheats.Id );
		if ( evening )
			used.Add( OfficialExtensions.Evening.Id );
		return used;
	}

	/// <summary>
	/// [EXT:umbrellas-save] Whether a park uses the umbrellas extension (the pools rule): it holds a brolly shop (<paramref name="placed"/>, the entries of its objects) or a guest with an umbrella
	/// (<paramref name="guests"/>). A park with neither is the same bytes with the extension on or off.
	/// </summary>
	public static bool UmbrellasInUse( IEnumerable<ObjectCatalogEntry> placed, GuestSimulation? guests ) =>
		placed.Any( IsUmbrellasObject ) || guests?.Guests.Any( guest => guest.UmbrellaState != 0 ) == true;

	/// <summary>
	/// [EXT:winter-save] Whether a park uses the winter extension (the umbrellas rule): it holds a winter kiosk or a glühwein stall (<paramref name="placed"/>, the entries of its objects) or a guest with a warm item
	/// (<paramref name="guests"/>). A park with neither is the same bytes with the extension on or off.
	/// </summary>
	public static bool WinterInUse( IEnumerable<ObjectCatalogEntry> placed, GuestSimulation? guests ) =>
		placed.Any( IsWinterObject ) || guests?.Guests.Any( guest => guest.HoldsWinter ) == true;

	/// <summary>
	/// [EXT:evening-torches-save] user-approved 2026-10-19 (used-only, like the umbrellas): whether a park uses the evening extension's simulation side, which is only its torches (<paramref name="placed"/>, the entries of its objects;
	/// a torch changes no guest, so there is no guest state to look at). A park without a torch is the same bytes with the extension on or off.
	/// </summary>
	public static bool EveningInUse( IEnumerable<ObjectCatalogEntry> placed ) => placed.Any( IsEveningObject );

	/// <summary>
	/// [EXT:pools-extension] The asset manifest as a save holds it: the catalog lists every object of a used-only extension (<c>mod:opentpw.pools:</c>, <c>mod:opentpw.umbrellas:</c>, <c>mod:opentpw.winter:</c> and <c>mod:opentpw.evening:</c> keys) whenever the extension is on, but a save holds, and a load requires,
	/// only the keys of the extension objects the park has PLACED (<paramref name="placedPoolsObjects"/>: the changing rooms, brolly shops, winter kiosks, glühwein stalls and torches; none for a park of pool cells only). A park without any is the same bytes with the
	/// extension on or off and loads with it on or off; an extension object that is not placed is ignored in both directions, so adding objects to the extension (more changing rooms, extras, slides) never
	/// invalidates an older save. A placed object keeps the exact-hash check: changing its files changes the saves that place it.
	/// </summary>
	public static IReadOnlyDictionary<string, string> AssetsForUse( IReadOnlyDictionary<string, string> assets, IEnumerable<ObjectCatalogEntry> placedPoolsObjects )
	{
		ArgumentNullException.ThrowIfNull( assets );
		ArgumentNullException.ThrowIfNull( placedPoolsObjects );
		var prefixes = new[] { ContentRoot.Mod( OfficialExtensions.Pools.Id ).KeyPrefix, ContentRoot.Mod( OfficialExtensions.Umbrellas.Id ).KeyPrefix, ContentRoot.Mod( OfficialExtensions.Winter.Id ).KeyPrefix, ContentRoot.Mod( OfficialExtensions.Evening.Id ).KeyPrefix };
		if ( !assets.Keys.Any( key => prefixes.Any( prefix => key.StartsWith( prefix, StringComparison.Ordinal ) ) ) )
			return assets;
		var placed = placedPoolsObjects.Where( entry => IsPoolsObject( entry ) || IsUmbrellasObject( entry ) || IsWinterObject( entry ) || IsEveningObject( entry ) ).Select( entry => entry.Root.KeyPrefix + entry.ArchivePath.TrimEnd( '/' ) + "/" ).Distinct( StringComparer.Ordinal ).ToArray();
		return assets.Where( pair => !prefixes.Any( prefix => pair.Key.StartsWith( prefix, StringComparison.Ordinal ) ) || placed.Any( archive => pair.Key.StartsWith( archive, StringComparison.Ordinal ) ) )
			.ToDictionary( pair => pair.Key, pair => pair.Value, StringComparer.Ordinal );
	}

	/// <summary>[EXT:pools-extension] Whether a placed object is one of the pools extension's own (a changing room).</summary>
	public static bool IsPoolsObject( ObjectCatalogEntry entry ) => entry.ModId == OfficialExtensions.Pools.Id;

	/// <summary>[EXT:umbrellas-save] Whether a placed object is one of the umbrellas extension's own (a brolly shop).</summary>
	public static bool IsUmbrellasObject( ObjectCatalogEntry entry ) => entry.ModId == OfficialExtensions.Umbrellas.Id;

	/// <summary>[EXT:winter-save] Whether a placed object is one of the winter extension's own (a winter kiosk or a glühwein stall).</summary>
	public static bool IsWinterObject( ObjectCatalogEntry entry ) => entry.ModId == OfficialExtensions.Winter.Id;

	/// <summary>[EXT:evening-torches-save] Whether a placed object is one of the evening extension's own (a torch).</summary>
	public static bool IsEveningObject( ObjectCatalogEntry entry ) => entry.ModId == OfficialExtensions.Evening.Id;

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
	public static IReadOnlyList<string> Unmet( IReadOnlyList<ParkModRecord>? saved, ModSet active ) => UnmetItems( saved, active ).Select( item => item.Reason ).ToArray();

	/// <summary>
	/// <see cref="Unmet"/> with the kind of each reason and the mod it names, so a load can tell an extension switch it may offer
	/// (<see cref="ParkExtensionSwitch"/>) from a requirement that stays strict. The reasons and their order are <see cref="Unmet"/>'s.
	/// </summary>
	internal static IReadOnlyList<UnmetMod> UnmetItems( IReadOnlyList<ParkModRecord>? saved, ModSet active )
	{
		ArgumentNullException.ThrowIfNull( active );
		var reasons = new List<UnmetMod>();
		var current = RecordableMods( active ).ToDictionary( mod => mod.Id, StringComparer.Ordinal );
		var recordedIds = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var record in saved ?? Array.Empty<ParkModRecord>() )
		{
			recordedIds.Add( record.Id );
			if ( !active.IsEnabled( record.Id ) )
				reasons.Add( new( record.Id, UnmetKind.Missing, $"needs {record.Id} {record.Version}"
					+ (active.Refused.Any( refusal => refusal.Id == record.Id && refusal.Kind == ModRefusalKind.SafeMode ) ? ", which is off in safe mode (script mods are off for this run; start again without --safe-mode)" : "" ), record ) );
			else if ( !current.TryGetValue( record.Id, out var mod ) )
				// enabled, but now judged presentation-only: its files changed (or, for an extension, it is not recordable in this edition)
				reasons.Add( new( record.Id, UnmetKind.NotRecordable, $"needs {record.Id} {record.Version} with the content it was saved with", record ) );
			else if ( mod.Version.ToString() != record.Version )
				reasons.Add( new( record.Id, UnmetKind.OtherVersion, $"needs {record.Id} {record.Version}, not {mod.Version}", record ) );
			else if ( mod.ContentSha256 != record.ContentSha256 )
				reasons.Add( new( record.Id, UnmetKind.OtherContent, $"needs {record.Id} {record.Version} with the content it was saved with (its files changed)", record ) );
		}
		// a used-only extension that is on but not recorded is fine: the save has no use of it (a save that does use it must record it: ParkResumeSaveFile.ValidateShape refuses pool cells without the record)
		foreach ( var mod in RecordableMods( active ) )
			if ( !recordedIds.Contains( mod.Id ) && !UsedOnlyExtensionIds.Contains( mod.Id ) )
				reasons.Add( new( mod.Id, UnmetKind.Extra, $"was saved without {mod.Id} {mod.Version}, which changes the simulation; turn it off to load this park", null ) );
		var expectedOrder = RecordableMods( active ).Where( mod => !UsedOnlyExtensionIds.Contains( mod.Id ) || recordedIds.Contains( mod.Id ) ).Select( mod => mod.Id );
		if ( reasons.Count == 0 && saved != null && !saved.Select( record => record.Id ).SequenceEqual( expectedOrder, StringComparer.Ordinal ) )
			reasons.Add( new( null, UnmetKind.Order, $"needs its mods in the order {string.Join( ", ", saved.Select( record => record.Id ) )}", null ) );
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
		var current = RecordableMods( active ).ToDictionary( mod => mod.Id, StringComparer.Ordinal );
		var needed = saved.Where( record => !current.TryGetValue( record.Id, out var mod ) || mod.Version.ToString() != record.Version || mod.ContentSha256 != record.ContentSha256 )
			.Select( record => $"{record.Id} {record.Version}" ).ToList();
		return needed.Count == 0 ? null : string.Join( ", ", needed );
	}
}
