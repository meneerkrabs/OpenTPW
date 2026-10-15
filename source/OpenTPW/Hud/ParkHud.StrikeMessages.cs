namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>Posts the park's staff strike messages (warning, strike, called off) to the message tube.</summary>
	// [DATA:Language/*/TAG_SYSTEM.str:38-62 (staff strike messages)] the original's text of each message, delivered through the message tube (UI-025)
	private void HookStaffStrikeMessages()
	{
		if ( level.Park != null )
			level.Park.EventRaised += OnStaffStrikeEvent;
	}

	private void OnStaffStrikeEvent( ParkEvent item )
	{
		if ( item.Kind != ParkEventKind.StaffStrikeMessage )
			return;
		string[] table;
		try
		{
			table = GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" ).Entries;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
		{
			Log.Warning( $"TAG_SYSTEM.str is not available for the staff strike message: {exception.Message}" );
			return;
		}
		if ( StrikeMessageText( item, table ) is { } text )
			PostGameMessage( text );
	}

	/// <summary>The <c>TAG_SYSTEM.str</c> text of a strike message event (role in <see cref="ParkEvent.InfoId"/>, kind in <see cref="ParkEvent.Amount"/>), or null.</summary>
	internal static string? StrikeMessageText( ParkEvent item, IReadOnlyList<string> tagSystem )
	{
		if ( item.Kind != ParkEventKind.StaffStrikeMessage )
			return null;
		var index = new StrikeMessage( (StaffType)item.InfoId, (StrikeMessageKind)item.Amount ).TagSystemIndex;
		return index >= 0 && index < tagSystem.Count ? tagSystem[index] : null;
	}
}
