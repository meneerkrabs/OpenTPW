using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>The original's cheat keys and the HUD's note of an applied cheat (docs/CHEATS.md).</summary>
public sealed partial class ParkHud
{
	/// <summary>
	/// The original's cheat key table (<c>0x7484D0</c>, twelve entries, each Alt alone): R research, 1 money, G and the keypad's 1..9 the ten non-secret golden tickets. The keys work while the park's cheat mode
	/// is on, whether the cheats extension is on or not.
	/// </summary>
	// [BIN:TPW-EXE:0x7484D0 cheat key table] VK 0x52 (R) 0x40C430, 0x31 (the top row's 1) 0x40C450, 0x47 (G) 0x40C6C0 and VK_NUMPAD1..9 (0x61..0x69) 0x40C6E0..0x40C7E0, every entry with the modifier mask 0x30 (Alt); the table is not rebindable (it is built by 0x40CB80 from fixed data, unlike the shortcuts of 0x40CAA0)
	internal static readonly IReadOnlyList<(Key Key, CheatAction Action)> CheatKeys = new (Key, CheatAction)[]
	{
		(Key.R, new CheatAction( CheatKind.Research )),
		(Key.Number1, new CheatAction( CheatKind.Money )),
		(Key.G, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors )),
		(Key.Keypad1, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.PeopleInPark )),
		(Key.Keypad2, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Happiness )),
		(Key.Keypad3, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.AllResearchedAndBuilt )),
		(Key.Keypad4, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.ProfitYear )),
		(Key.Keypad5, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.RecentVisitors )),
		(Key.Keypad6, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.CoasterHeight )),
		(Key.Keypad7, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.GokartExcitement )),
		(Key.Keypad8, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.WaterLength )),
		(Key.Keypad9, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.BigPark )),
	};

	/// <summary>The modifier mask of every cheat key: Alt alone (<see cref="Input.ModifierMask"/>).</summary>
	internal const int CheatModifierMask = 0x30;

	private HashSet<Key>? cheatKeysDown;
	private int cheatModifierMaskBefore;

	/// <summary>
	/// The cheats the keys released this frame ask for: the table's keys among <paramref name="released"/> while the modifier mask is exactly Alt at the release, in table order; none in a park without the
	/// cheat mode (<c>0x405560</c> gates the context).
	/// </summary>
	// [BIN:TPW-EXE:0x488991 cheat context] after the camera, game and shortcuts contexts the key goes to the cheat context 0x78733C only while 0x405560 (the mode byte +0xA) is set; [BIN:TPW-EXE:0x40C990 key release] the cheat handlers sit in the entries' +0xC slot, which the release message 0x1000B calls (the press, 0x1000A through 0x40C900, only sets the entry's pressed flag at 0x40C974), with the modifier mask read at that moment
	internal static IReadOnlyList<CheatAction> CheatKeyActions( bool cheatMode, int modifierMask, IReadOnlyCollection<Key> released ) =>
		!cheatMode || modifierMask != CheatModifierMask ? Array.Empty<CheatAction>()
			: CheatKeys.Where( entry => released.Contains( entry.Key ) ).Select( entry => entry.Action ).ToArray();

	/// <summary>Queues the cheats of the keys released this frame while Alt alone is held, once per release (an auto-repeat does not fire); nothing in a visit or while the park keys are not allowed.</summary>
	private void HandleCheatKeys( bool allowed )
	{
		var down = CheatKeys.Select( entry => entry.Key ).Where( Input.IsFrameKeyDown ).ToHashSet();
		var previous = cheatKeysDown ??= new();
		var released = previous.Where( key => !down.Contains( key ) ).ToArray();
		previous.Clear();
		previous.UnionWith( down );
		// [EXT:cheats-key-release] the original reads the mask when the key's release message arrives, while Alt is usually still held; a frame that sees the key and Alt released together
		// cannot tell their order, so it takes the previous frame's mask (a quick release with Alt still counts)
		var mask = Input.ModifierMaskDown;
		var maskAtRelease = mask == 0 ? cheatModifierMaskBefore : mask;
		cheatModifierMaskBefore = mask;
		if ( !allowed || level is not { IsReadOnlyVisit: false } || FinanceEconomy is not { } economy )
			return;
		foreach ( var action in CheatKeyActions( economy.Cheats.Mode, maskAtRelease, released ) )
			level.Commands.Issue( new QueueCheatCommand( action.Kind, action.Argument ) );
	}

	/// <summary>
	/// The short note an applied cheat leaves on the screen, or null for one that changed nothing (a ticket the park held already): the ticket cheats play no advisor line, as in the original, so this
	/// note is all the player sees.
	/// </summary>
	// [EXT:cheats-ticket-provenance] the original's cheats are silent; OpenTPW writes one short line per applied cheat (the user's decision: no advisor line, only a short note)
	internal static string? CheatNote( UiStringTable strings, ParkEvent applied ) => (CheatKind)applied.InfoId switch
	{
		CheatKind.Mode => strings.Extra( OpenTpwText.CheatModeOn ),
		CheatKind.Money => strings.Extra( OpenTpwText.CheatMoney ),
		CheatKind.Research => strings.Extra( OpenTpwText.CheatResearch ),
		CheatKind.Weather => strings.Extra( OpenTpwText.CheatWeatherNote ),
		CheatKind.Arrival => strings.Extra( OpenTpwText.CheatArrivalNote ),
		CheatKind.Ticket when applied.Detail is nameof( GlobalTicketOutcome.Awarded ) or nameof( GlobalTicketOutcome.Moved ) => strings.Extra( OpenTpwText.CheatTicket ),
		_ => null
	};

	private void PostCheatNotes()
	{
		if ( level == null )
			return;
		foreach ( var applied in level.TakeCheatNotes() )
			if ( CheatNote( strings, applied ) is { } note )
				PostMessage( note );
	}
}
