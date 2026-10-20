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
}
