using OpenTPW.Mods;
using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>
/// What a package's official extensions ask of the visitor (docs/MODS.md, "Parks that use extensions"): a content extension the park uses that is off here is either switched on with the visitor's
/// confirmation (<see cref="TurnOn"/>, as a park load does, <see cref="ParkExtensionSwitch"/>) or the visit is refused (<see cref="Blocked"/>); a visit never runs without a content extension its park uses.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public sealed class ParkVisitExtensions
{
	/// <summary>
	/// The extensions a visit needs when a park uses them: the content extensions (<see cref="ParkResumeMods.UsedOnlyExtensionIds"/>) whose objects and art a visit draws. The cheats extension and the rule-only
	/// extensions (<see cref="ParkExtensionSwitch.RuleOnlyExtensionIds"/>) change how a park plays, and a visit does not play it, so a visitor does not need them.
	/// </summary>
	public static IReadOnlySet<string> RequiredIds { get; } = new HashSet<string>( ParkResumeMods.UsedOnlyExtensionIds.Where( id => id != OfficialExtensions.Cheats.Id ), StringComparer.Ordinal );

	private ParkVisitExtensions( IReadOnlyList<IOpenTpwExtension> turnOn, IReadOnlyList<string> blocked )
	{
		TurnOn = turnOn;
		Blocked = blocked;
	}

	/// <summary>Content extensions the park uses that are off here and may be switched on for the visit.</summary>
	public IReadOnlyList<IOpenTpwExtension> TurnOn { get; }
	/// <summary>The ids of content extensions the park uses that cannot be used here: switched off on the command line, not in this edition, not offered to players, or unknown to this build.</summary>
	public IReadOnlyList<string> Blocked { get; }

	/// <summary>The dialog's plan for <see cref="TurnOn"/> (<see cref="FrontEnd.ExtensionSwitchDialog"/>).</summary>
	internal ParkExtensionSwitch ToSwitch() => ParkExtensionSwitch.ForTurnOn( TurnOn );

	/// <summary>The choices that switch <see cref="TurnOn"/> on (<see cref="ModStartup.SaveExtensionChoices"/>).</summary>
	public IReadOnlyDictionary<string, bool> Changes => TurnOn.ToDictionary( extension => extension.Id, _ => true, StringComparer.Ordinal );

	/// <summary>
	/// What visiting a park that records <paramref name="used"/> asks under <paramref name="active"/>; null when it asks nothing (the package records none, or every content extension it uses is on).
	/// An extension of another version is not a difference for a visit (the park is drawn, not played). <paramref name="disabledForRun"/> are the extensions switched off on the command line.
	/// </summary>
	public static ParkVisitExtensions? Plan( IReadOnlyList<ExtensionUse>? used, ModSet active, IReadOnlyCollection<string>? disabledForRun = null )
	{
		ArgumentNullException.ThrowIfNull( active );
		if ( used == null || used.Count == 0 )
			return null;
		disabledForRun ??= ModStartup.DisabledForRun;
		var turnOn = new List<IOpenTpwExtension>();
		var blocked = new List<string>();
		foreach ( var use in used )
		{
			var extension = OfficialExtensions.Find( use.Id );
			// an extension this build does not know may be anything: a visit does not run without it
			if ( extension == null )
				blocked.Add( use.Id );
			else if ( !RequiredIds.Contains( extension.Id ) || active.IsEnabled( extension.Id ) )
				continue;
			else if ( extension.PlayerSelectable && !GameEditions.IsThemeParkInc && !disabledForRun.Contains( extension.Id, StringComparer.Ordinal ) )
				turnOn.Add( extension );
			else
				blocked.Add( extension.Id );
		}
		return turnOn.Count == 0 && blocked.Count == 0 ? null : new ParkVisitExtensions( turnOn, blocked );
	}

	/// <summary>The refusal text of <see cref="Blocked"/> in the interface language: the names of the extensions that cannot be used (<paramref name="name"/> names an id).</summary>
	public string BlockedText( Func<string, string> name ) =>
		OnlineStrings.Get( OnlineLabel.VisitNeedsExtensions ).Replace( "{0}", string.Join( ", ", Blocked.Select( name ) ) );

	/// <summary>
	/// The startup visit (<c>--visit-park</c>), which has no dialog: throws <see cref="InvalidDataException"/> naming the content extensions the park uses that are off, and how to switch them on for the run.
	/// </summary>
	public static void RequireOn( ParkPackage package, ModSet active )
	{
		ArgumentNullException.ThrowIfNull( package );
		if ( Plan( package.Manifest.Extensions, active ) is not { } plan )
			return;
		if ( plan.Blocked.Count > 0 )
			throw new InvalidDataException( plan.BlockedText( id => id ) );
		throw new InvalidDataException( OnlineStrings.Get( OnlineLabel.VisitEnableOnCommandLine ).Replace( "{0}", string.Join( ", ", plan.TurnOn.Select( extension => extension.Id ) ) )
			.Replace( "{1}", string.Join( " ", plan.TurnOn.Select( extension => $"{ModStartup.EnableModOption} {extension.Id}" ) ) ) );
	}
}
