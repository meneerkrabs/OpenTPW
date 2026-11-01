namespace OpenTPW;

internal sealed partial class GameFlow
{
	/// <summary>Samples application-owned progress without attaching or creating a ledger in the lobby.</summary>
	internal static int? ReadLobbyPlayerKeys( string path )
	{
		try
		{
			var ledger = PlayerTicketFile.ReadIfPresent( path )?.Ledger;
			return ledger?.Mode == ParkGameMode.FullSimulation ? ledger.Keys : null;
		}
		catch ( Exception error ) when ( ProgressFailure( error ) )
		{
			// A corrupt/unreadable history must not prevent reaching the lobby or be replaced with starter keys.
			// Entry retains its existing explicit progress failure dialog and admission validation.
			Log.Warning( $"OpenTPW lobby progress unavailable: {error.Message}" );
			return null;
		}
	}

	/// <summary>
	/// The keys a theme costs to enter, the field tp.exe's <c>0x5B11A0</c> returns from the theme's loaded <c>global.sam</c> (<c>Keys.CostToEnter</c>); null when the theme has no
	/// readable cost (no such file or an invalid one), where <c>0x5B09C0</c> finds no usable record.
	/// </summary>
	internal static int? ReadLobbyThemeEntryCost( string theme )
	{
		try
		{
			// without a file system (a front end built before the game files are known) no theme has a record
			return (FileSystem as BaseFileSystem)?.FileExists( $"/levels/{theme}/global.sam" ) == true ? TrustedThemeEntryCost( theme ) : null;
		}
		catch ( Exception error ) when ( ProgressFailure( error ) )
		{
			Log.Warning( $"OpenTPW lobby entry cost unavailable for {theme}: {error.Message}" );
			return null;
		}
	}
}
