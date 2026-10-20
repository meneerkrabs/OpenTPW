using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// What loading a park save under the active extensions would switch, when the save's official extensions differ from the active ones (docs/MODS.md, "Loading a park with other extensions"):
/// <list type="bullet">
/// <item><b>rule-only extensions</b> (<see cref="RuleOnlyExtensionIds"/>), in either direction: the park continues under the active set, so a rule that is on now starts at the load
/// (<see cref="ContinueWith"/>), one that is off now stops (<see cref="ContinueWithout"/>), and one of another version continues in this build's version (<see cref="ContinueNewVersion"/>);</item>
/// <item><b>content extensions the save uses</b> that are off now (<see cref="TurnOn"/>: the pools, umbrellas, winter, cheats and evening (its torches) extensions, <see cref="ParkResumeMods.UsedOnlyExtensionIds"/>): they are switched on for
/// this run and in <c>mods.json</c>, and the park loads with them.</item>
/// </list>
/// Everything else stays strict (<see cref="Refusals"/>, the texts of <see cref="ParkResumeMods.Unmet"/>): a community mod that is missing or has another version or content, an extension switched off on the command line,
/// a content extension in another version, the cross-edition objects extension (it changes which objects a park's catalog holds) and the order of the mods. The strict load path itself is unchanged: only a player's
/// confirmed choice in the load dialog uses a switch, and a switched park is a new run from the switch point (docs/DETERMINISM.md).
/// </summary>
// [EXT:save-extension-switch] user-approved 2026-10-19 ("Ook ontbrekende extensie aanzetten"): solo play is not blocked by rule options; strictness stays for exact replays, online play and community mods
public sealed class ParkExtensionSwitch
{
	/// <summary>
	/// The official extensions that are rules only: they ship no content and a park does not need them to hold its objects or state. The rain mood, wintry Decembers, the cold mood and Always winter leave only
	/// per-guest day marks (<c>RainMoodDay</c>, <c>ColdMoodDay</c>, <c>WarmUntilDay</c>) and the weather they shaped, which every build reads with the rule on or off; the smarter guests keep their own section
	/// (<see cref="ParkResumeSnapshot.SmarterGuests"/>), which a switch starts fresh (on), drops (off) or keeps only the guests' identity of (another version), keeping the guest identity script mods share.
	/// </summary>
	public static IReadOnlySet<string> RuleOnlyExtensionIds { get; } = new HashSet<string>( StringComparer.Ordinal )
	{
		OfficialExtensions.RainMood.Id, OfficialExtensions.WinterWeather.Id, OfficialExtensions.WinterCold.Id, OfficialExtensions.WinterAlways.Id, OfficialExtensions.SmarterGuests.Id,
	};

	private ParkExtensionSwitch( IReadOnlyList<IOpenTpwExtension> continueWith, IReadOnlyList<IOpenTpwExtension> continueWithout, IReadOnlyList<(IOpenTpwExtension Extension, string SavedVersion)> continueNewVersion,
		IReadOnlyList<IOpenTpwExtension> turnOn, IReadOnlyList<string> refusals )
	{
		ContinueWith = continueWith;
		ContinueWithout = continueWithout;
		ContinueNewVersion = continueNewVersion;
		TurnOn = turnOn;
		Refusals = refusals;
	}

	/// <summary>Rule-only extensions that are on now and that the park was played without: they start at the load.</summary>
	public IReadOnlyList<IOpenTpwExtension> ContinueWith { get; }
	/// <summary>Rule-only extensions the park was played with that are off now (or not recordable in this edition): they stop at the load.</summary>
	public IReadOnlyList<IOpenTpwExtension> ContinueWithout { get; }
	/// <summary>Rule-only extensions the park was played with in another version: the park continues with this build's version.</summary>
	public IReadOnlyList<(IOpenTpwExtension Extension, string SavedVersion)> ContinueNewVersion { get; }
	/// <summary>Content extensions the park uses that are off now: loading switches them on (this run and <c>mods.json</c>).</summary>
	public IReadOnlyList<IOpenTpwExtension> TurnOn { get; }
	/// <summary>What still refuses the load, as <see cref="ParkResumeMods.Unmet"/> names it; empty when the switch can be offered.</summary>
	public IReadOnlyList<string> Refusals { get; }

	/// <summary>Whether the load can be offered with this switch: nothing strict is left and something is switched.</summary>
	public bool CanSwitch => Refusals.Count == 0 && (ContinueWith.Count + ContinueWithout.Count + ContinueNewVersion.Count + TurnOn.Count) > 0;

	/// <summary>Whether <paramref name="other"/> switches the same extensions the same way (a confirmed switch is used only while the plan has not changed).</summary>
	public bool SameAs( ParkExtensionSwitch? other ) => other != null
		&& Ids( ContinueWith ).SequenceEqual( Ids( other.ContinueWith ) ) && Ids( ContinueWithout ).SequenceEqual( Ids( other.ContinueWithout ) )
		&& ContinueNewVersion.Select( item => item.Extension.Id + " " + item.SavedVersion ).SequenceEqual( other.ContinueNewVersion.Select( item => item.Extension.Id + " " + item.SavedVersion ) )
		&& Ids( TurnOn ).SequenceEqual( Ids( other.TurnOn ) ) && Refusals.SequenceEqual( other.Refusals );

	private static IEnumerable<string> Ids( IEnumerable<IOpenTpwExtension> extensions ) => extensions.Select( extension => extension.Id );

	/// <summary>
	/// The switch that loading a save with the recorded <paramref name="saved"/> under <paramref name="active"/> needs, or null when the mods agree (the load needs no switch). <paramref name="disabledForRun"/> are the
	/// extensions switched off on the command line (<see cref="ModStartup.DisabledForRun"/> by default): such an extension is never switched on.
	/// </summary>
	internal static ParkExtensionSwitch? Plan( IReadOnlyList<ParkModRecord>? saved, ModSet active, IReadOnlyCollection<string>? disabledForRun = null )
	{
		ArgumentNullException.ThrowIfNull( active );
		var unmet = ParkResumeMods.UnmetItems( saved, active );
		if ( unmet.Count == 0 )
			return null;
		disabledForRun ??= ModStartup.DisabledForRun;
		var with = new List<IOpenTpwExtension>();
		var without = new List<IOpenTpwExtension>();
		var newVersion = new List<(IOpenTpwExtension, string)>();
		var turnOn = new List<IOpenTpwExtension>();
		var refusals = new List<string>();
		foreach ( var item in unmet )
		{
			var extension = item.Id == null ? null : OfficialExtensions.Find( item.Id );
			var ruleOnly = extension != null && RuleOnlyExtensionIds.Contains( extension.Id ) && (item.Record == null || item.Record.ContentSha256 == ModSet.NoFilesSha256);
			switch ( item.Kind )
			{
				case UnmetKind.Missing or UnmetKind.NotRecordable when ruleOnly:
					without.Add( extension! );
					break;
				case UnmetKind.Missing when extension != null && CanTurnOn( extension, item.Record!, disabledForRun ):
					turnOn.Add( extension );
					break;
				case UnmetKind.OtherVersion when ruleOnly:
					newVersion.Add( (extension!, item.Record!.Version) );
					break;
				case UnmetKind.Extra when ruleOnly:
					with.Add( extension! );
					break;
				default:
					refusals.Add( item.Reason );
					break;
			}
		}
		var plan = new ParkExtensionSwitch( with, without, newVersion, turnOn, refusals );
		if ( refusals.Count > 0 )
			return plan;
		// what loads must agree with the switched set exactly; the order of the mods both have stays strict
		var after = active.WithExtensions( turnOn.ToDictionary( extension => extension.Id, _ => true ), ModStartup.Choices.Order );
		var records = plan.Records( saved, after );
		var kept = new HashSet<string>( records.Select( record => record.Id ), StringComparer.Ordinal );
		var savedIds = (saved ?? Array.Empty<ParkModRecord>()).Select( record => record.Id ).ToArray();
		if ( !savedIds.Where( kept.Contains ).SequenceEqual( records.Select( record => record.Id ).Where( savedIds.Contains ), StringComparer.Ordinal ) )
			refusals.Add( $"needs its mods in the order {string.Join( ", ", savedIds )}" );
		else
			refusals.AddRange( ParkResumeMods.Unmet( records.Count == 0 ? null : records, after ) );
		return plan;
	}

	/// <summary>
	/// A content extension the save uses can be switched on when it is a used-only extension (<see cref="ParkResumeMods.UsedOnlyExtensionIds"/>) a player may select, in the save's version, with the content of an
	/// extension, outside a Theme Park Inc run (which has none of them) and not switched off for this run on the command line.
	/// </summary>
	private static bool CanTurnOn( IOpenTpwExtension extension, ParkModRecord record, IReadOnlyCollection<string> disabledForRun ) =>
		ParkResumeMods.UsedOnlyExtensionIds.Contains( extension.Id ) && extension.PlayerSelectable && !GameEditions.IsThemeParkInc && !disabledForRun.Contains( extension.Id )
		&& record.Version == extension.Version.ToString() && record.ContentSha256 == ModSet.NoFilesSha256;

	/// <summary>
	/// The menu line of a save's needs (<see cref="ParkResumeMods.MissingText"/>), or null when it has none or when every difference can be switched at the load (the load then asks), so a park that only played with another
	/// rule set is not listed as needing a mod.
	/// </summary>
	internal static string? RequiresText( IReadOnlyList<ParkModRecord>? saved, ModSet active ) =>
		Plan( saved, active ) is { CanSwitch: true } ? null : ParkResumeMods.MissingText( saved, active );

	/// <summary>The active set the switched park loads under: <paramref name="active"/> with <see cref="TurnOn"/> switched on (<see cref="ModStartup.SwitchExtensions"/>).</summary>
	internal ModSet ActiveAfter( ModSet active ) =>
		TurnOn.Count == 0 ? active : ModStartup.SwitchExtensions( active, TurnOn.ToDictionary( extension => extension.Id, _ => true ) );

	/// <summary>
	/// The records a park saved now under <paramref name="after"/> holds for the save's mods: every recordable mod of the set, a used-only extension only when the save records it (it uses it), in the set's order.
	/// </summary>
	private List<ParkModRecord> Records( IReadOnlyList<ParkModRecord>? saved, ModSet after )
	{
		var savedIds = new HashSet<string>( (saved ?? Array.Empty<ParkModRecord>()).Select( record => record.Id ), StringComparer.Ordinal );
		return ParkResumeMods.RecordableMods( after ).Where( mod => !ParkResumeMods.UsedOnlyExtensionIds.Contains( mod.Id ) || savedIds.Contains( mod.Id ) )
			.Select( mod => new ParkModRecord( mod.Id, mod.Version.ToString(), mod.ContentSha256 ) ).ToList();
	}

	/// <summary>
	/// The save as the switched park continues it under <paramref name="after"/> (<see cref="ActiveAfter"/>): its mod records are those of the switched set, and the smarter guests' section starts fresh when the
	/// extension comes on (the park seed, no birth ids yet, as a park that starts with the extension has), and loses the extension's own state when it goes off (the guest identity script mods share stays) or
	/// continues in another version (the guests keep their identity and traits; the rules' stalls, throughputs, meals and complaints start fresh).
	/// Everything else is the save's: the strict validation then checks the result like any other save.
	/// </summary>
	internal ParkResumeSnapshot Apply( ParkResumeSnapshot snapshot, ModSet after )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( after );
		if ( !CanSwitch )
			throw new InvalidOperationException( "This park's extensions cannot be switched: " + string.Join( "; ", Refusals ) + "." );
		var records = Records( snapshot.Mods, after );
		var smarter = snapshot.SmarterGuests;
		var smarterId = OfficialExtensions.SmarterGuests.Id;
		if ( ContinueWithout.Any( extension => extension.Id == smarterId ) && smarter != null )
			smarter = snapshot.ScriptMods == null ? null : smarter with { Stalls = null, Throughputs = null, Meals = null, Complaints = null };
		// another version's rules wrote that state with their own meaning: the extension's own state starts fresh in this version, the guests' identity (park seed, birth ids) and so their traits stay
		if ( ContinueNewVersion.Any( item => item.Extension.Id == smarterId ) && smarter != null )
			smarter = smarter with { Stalls = null, Throughputs = null, Meals = null, Complaints = null };
		if ( ContinueWith.Any( extension => extension.Id == smarterId ) )
			smarter ??= new SmarterGuestsSaveData { ParkSeed = snapshot.Seed, NextBirthId = SmarterGuestsState.FirstBirthId, Bootstrapped = false, Births = Array.Empty<SmarterGuestBirth>() };
		return snapshot with { Mods = records.Count == 0 ? null : records, SmarterGuests = smarter };
	}
}
