using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// Feature switches that combine the edition's capabilities with the active official extensions (docs/MODS.md). Read
/// them instead of the underlying capability, so a disabled extension hides its features everywhere.
/// </summary>
// [EXT:mods] OpenTPW's own extension switches; the original game had none
public static class FeatureGates
{
	public const string OnlineDisabledMessage = "OpenTPW online extension is disabled";

	/// <summary>The <c>opentpw.online</c> extension is on for this run.</summary>
	public static bool Online => OpenTpwExtensions.IsEnabled( OfficialExtensions.Online );

	/// <summary>Exporting, importing, visiting and postcards: the edition supports sharing and the online extension is on.</summary>
	public static bool Sharing => GameEditions.Capabilities.Sharing && Online;

	/// <summary>
	/// Refuses the online command-line options while the online extension is disabled: <c>--export-park</c>,
	/// <c>--import-park</c>, <c>--visit-park</c> and <c>--online-dir</c>.
	/// </summary>
	public static void RefuseOnlineCommandLine( string[] args )
	{
		if ( Online )
			return;
		var option = new[] { "--export-park", "--import-park", "--visit-park", "--online-dir" }.FirstOrDefault( args.Contains );
		if ( option != null )
			throw new ArgumentException( $"{OnlineDisabledMessage}: {option} is unavailable (enable opentpw.online in mods.json, or leave out --disable-extension opentpw.online; docs/MODS.md)." );
	}
}
