using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The cheats window of the <c>opentpw.cheats</c> extension (docs/CHEATS.md, phase K3), opened with Alt+C in a Theme Park World park: the cheat mode, visitors arriving by a chosen vehicle, the
/// weather, a chosen golden ticket and the original's money and research cheats with their Alt keys. Built from the options pages' pieces (an <c>f_screen</c> page with <c>f_optpanel</c> rows and
/// <c>b_on2</c> buttons). Every button queues a cheat for the next park turn (<see cref="ParkEconomy.QueueCheat"/>); opening the window marks nothing.
/// </summary>
// [EXT:cheats-window] OpenTPW's own window; the original's cheats have no window, only the secret park name and the Alt keys
public sealed partial class ParkHud
{
	/// <summary>The cheats window's screen name.</summary>
	internal const string CheatsWindowName = "cheats";

	/// <summary>The golden tickets the window offers: the ten that have an original cheat, in the award order (<see cref="CheatKeys"/>).</summary>
	internal static readonly IReadOnlyList<GoldenTicketKind> CheatTickets = Enumerable.Range( (int)GoldenTicketKind.Visitors, 10 ).Select( value => (GoldenTicketKind)value ).ToArray();

	private int cheatVehicle = ArrivalVehicleKind.Bus;
	private CheatWeather cheatWeather = CheatWeather.Clear;
	private GoldenTicketKind cheatTicket = GoldenTicketKind.Visitors;
	// [EXT:cheats-ride-vehicles] the ride the window's vehicle row acts on, by attraction id (0: none chosen yet)
	private int cheatRide;
	private bool cheatsWindowKeyDown;

	/// <summary>Whether the cheats window may open: the extension is on and this is a Theme Park World park of the player's own, not a visit.</summary>
	internal bool CheatsWindowAvailable => FeatureGates.Cheats && level is { IsReadOnlyVisit: false } && FinanceEconomy is { Rules.ThemeParkInc: false };

	/// <summary>The Alt key of a ticket's original cheat, shown beside it (" (Alt+G)").</summary>
	internal static string CheatKeyHint( CheatAction action )
	{
		var key = CheatKeys.FirstOrDefault( entry => entry.Action == action ).Key;
		return action.Kind is CheatKind.Money or CheatKind.Research or CheatKind.Ticket && CheatKeys.Any( entry => entry.Action == action )
			? " (Alt+" + (key is >= Key.Keypad1 and <= Key.Keypad9 ? "Num" + (key - Key.Keypad1 + 1) : key == Key.Number1 ? "1" : key.ToString()) + ")"
			: "";
	}

	internal static OpenTpwText VehicleText( int vehicleKind ) => vehicleKind switch
	{
		ArrivalVehicleKind.Bus => OpenTpwText.CheatBus,
		ArrivalVehicleKind.Seaplane => OpenTpwText.CheatPlane,
		_ => OpenTpwText.CheatBoat
	};

	internal static OpenTpwText WeatherText( CheatWeather weather ) => weather switch
	{
		CheatWeather.Clear => OpenTpwText.CheatClear,
		CheatWeather.Rain => OpenTpwText.CheatRain,
		CheatWeather.Thunderstorm => OpenTpwText.CheatThunderstorm,
		_ => OpenTpwText.CheatSnow
	};

	internal static OpenTpwText TicketText( GoldenTicketKind kind ) => OpenTpwText.CheatTicketVisitors + ((int)kind - (int)GoldenTicketKind.Visitors);

	/// <summary>[EXT:cheats-ride-vehicles] The ride the vehicle row shows among <paramref name="rides"/>: the one chosen, or the park's first such ride.</summary>
	private OriginalObject? CheatRide( IReadOnlyList<OriginalObject> rides ) =>
		rides.FirstOrDefault( ride => ride.Runtime.AttractionId == cheatRide ) ?? rides.FirstOrDefault();

	/// <summary>Moves the vehicle row's ride <paramref name="direction"/> steps through the park's rides, wrapping.</summary>
	private void StepCheatRide( IReadOnlyList<OriginalObject> rides, int direction )
	{
		if ( rides.Count == 0 )
			return;
		var index = CheatRide( rides ) is { } current ? Math.Max( 0, IndexOf( rides, current ) ) : 0;
		cheatRide = rides[((index + direction) % rides.Count + rides.Count) % rides.Count].Runtime.AttractionId;
	}

	private static int IndexOf( IReadOnlyList<OriginalObject> rides, OriginalObject ride )
	{
		for ( var index = 0; index < rides.Count; index++ )
			if ( ReferenceEquals( rides[index], ride ) )
				return index;
		return -1;
	}

	/// <summary>The ride's name, numbered when the park has more than one of the same kind ("Jurassic Tours 2").</summary>
	private static string CheatRideName( IReadOnlyList<OriginalObject> rides, OriginalObject ride )
	{
		var same = rides.Where( other => other.Entry.InfoId == ride.Entry.InfoId ).ToList();
		var name = ride.Entry.DisplayName ?? ride.Entry.ArchiveName;
		return same.Count > 1 ? $"{name} {same.IndexOf( ride ) + 1}" : name;
	}

	/// <summary>Alt+C opens or closes the cheats window (on the key's press, Alt alone).</summary>
	private void HandleCheatsWindowKey( bool allowed )
	{
		var down = Input.IsFrameKeyDown( Key.C );
		var pressed = down && !cheatsWindowKeyDown;
		cheatsWindowKeyDown = down;
		if ( !pressed || Input.ModifierMaskDown != CheatModifierMask )
			return;
		if ( Stack.Top?.Name == CheatsWindowName )
			Stack.Pop();
		else if ( allowed && CheatsWindowAvailable )
			Stack.Push( CreateCheatsWindow() );
	}

	/// <summary>Builds the cheats window; its rows read the live park each frame.</summary>
	internal UiScreen CreateCheatsWindow()
	{
		var screen = new UiScreen( CheatsWindowName );
		var page = new OptionsScreen.PageBuilder( screen );
		page.Frame( () => strings.Extra( OpenTpwText.CheatsTitle ) );
		ParkEconomy? Economy() => FinanceEconomy;
		// the cheat mode is on, or waits for the next turn: the other buttons act from then on
		bool Allowed() => Economy() is { } economy && (economy.Cheats.Mode || economy.Cheats.Pending.Any( action => action.Kind == CheatKind.Mode ));
		void Queue( CheatAction action )
		{
			if ( Economy() != null && Allowed() )
				level.Commands.Issue( new QueueCheatCommand( action.Kind, action.Argument ) );
		}
		var buttons = new List<(UiButton Button, Func<bool> Enabled)>();
		var rowIndex = 0;
		void Row( string id, Func<string> text, Action clicked, Func<bool> enabled, Action<int>? adjusted = null )
		{
			// seven rows on the left, the rest (four) on the right, at the OpenTPW page's panel size and pitch
			var column = rowIndex < 7 ? 0 : 1;
			var left = column == 0 ? 57f : 1331f;
			var top = 167f + (column == 0 ? rowIndex : rowIndex - 7) * 156f;
			rowIndex++;
			page.Panel( id, "f_optpanel", OptionsScreen.PageBuilder.Rect( left, top, left + 633, top + 149 ) );
			page.Label( id, OptionsScreen.PageBuilder.Rect( left + 70, top + 50, left + 470, top + 95 ), text );
			buttons.Add( (page.Toggle( id, "b_on2", OptionsScreen.PageBuilder.Rect( left + 476, top + 23, left + 579, top + 125 ), () => true, clicked, true, adjusted ), enabled) );
		}
		string Extra( OpenTpwText key ) => strings.Extra( key );
		Row( "cheatMode", () => Extra( OpenTpwText.CheatModeLabel ) + strings[Allowed() ? UIStrings.On : UIStrings.Off],
			() => { if ( !Allowed() && Economy() != null ) level.Commands.Issue( new QueueCheatCommand( CheatKind.Mode ) ); }, () => !Allowed() );
		Row( "vehicle", () => Extra( OpenTpwText.CheatArrivalLabel ) + Extra( VehicleText( cheatVehicle ) ),
			() => cheatVehicle = (cheatVehicle + 1) % 3, () => true, direction => cheatVehicle = ((cheatVehicle + direction) % 3 + 3) % 3 );
		Row( "arrival", () => Extra( OpenTpwText.CheatSendVisitors ),
			() => Queue( new CheatAction( CheatKind.Arrival, cheatVehicle ) ), () => Allowed() && Economy()?.Cheats.ArrivalPending == false );
		Row( "weatherKind", () => Extra( OpenTpwText.CheatWeatherLabel ) + Extra( WeatherText( cheatWeather ) ),
			() => cheatWeather = OptionsScreen.CycleWrap( Enum.GetValues<CheatWeather>(), cheatWeather, 1 ), () => true,
			direction => cheatWeather = OptionsScreen.CycleWrap( Enum.GetValues<CheatWeather>(), cheatWeather, direction ) );
		Row( "weather", () => Extra( OpenTpwText.CheatChangeWeather ),
			() => Queue( new CheatAction( CheatKind.Weather, (int)cheatWeather ) ), Allowed );
		Row( "ticketKind", () => Extra( OpenTpwText.CheatTicketLabel ) + Extra( TicketText( cheatTicket ) ),
			() => cheatTicket = OptionsScreen.CycleWrap( CheatTickets, cheatTicket, 1 ), () => true,
			direction => cheatTicket = OptionsScreen.CycleWrap( CheatTickets, cheatTicket, direction ) );
		Row( "ticket", () => Extra( OpenTpwText.CheatAwardTicket ) + CheatKeyHint( new CheatAction( CheatKind.Ticket, (int)cheatTicket ) ),
			() => Queue( new CheatAction( CheatKind.Ticket, (int)cheatTicket ) ), Allowed );
		Row( "money", () => Extra( OpenTpwText.CheatAddMoney ) + CheatKeyHint( new CheatAction( CheatKind.Money ) ),
			() => Queue( new CheatAction( CheatKind.Money ) ), Allowed );
		Row( "research", () => Extra( OpenTpwText.CheatBuildEverything ) + CheatKeyHint( new CheatAction( CheatKind.Research ) ),
			() => Queue( new CheatAction( CheatKind.Research ) ), Allowed );
		// [EXT:cheats-ride-vehicles] a ride of the park (bumper cars, karts or a tour) and its vehicle limit; the park's rides are listed once a frame (Updating below)
		var rides = level.CheatRides;
		OriginalObject? Ride() => CheatRide( rides );
		Row( "ride", () => Extra( OpenTpwText.CheatRideLabel ) + (Ride() is { } ride ? " " + CheatRideName( rides, ride ) : Extra( OpenTpwText.CheatRideNone )),
			() => StepCheatRide( rides, 1 ), () => rides.Count > 1, direction => StepCheatRide( rides, direction ) );
		Row( "rideVehicles", () => Ride() is { } ride
				? string.Format( Extra( ride.Runtime.VehiclesRaised ? OpenTpwText.CheatVehiclesRaised : OpenTpwText.CheatRaiseVehicles ), ride.Runtime.RaisedVehicleCount )
				: string.Format( Extra( OpenTpwText.CheatRaiseVehicles ), BumpController.RaisedVehicleLimit ),
			() => { if ( Ride() is { } ride ) Queue( new CheatAction( CheatKind.RideVehicles, ride.Runtime.AttractionId ) ); },
			() => Allowed() && Ride() is { Runtime: { CanRaiseVehicles: true, VehiclesRaised: false } } ride
				&& Economy()?.Cheats.Pending.Contains( new CheatAction( CheatKind.RideVehicles, ride.Runtime.AttractionId ) ) == false );
		// the park's state below the right column: marked or not, and what waits for the next turn
		screen.Add( new UiLabel
		{
			Id = "cheatsStatus",
			Text = () =>
			{
				var economy = Economy();
				var line = Extra( economy?.Cheats.Cheated == true ? OpenTpwText.CheatsMarkedNote : OpenTpwText.CheatsWillMarkNote );
				if ( economy?.Cheats.Pending.Count > 0 )
					line += "\n" + string.Format( Extra( OpenTpwText.CheatsWaiting ), economy.Cheats.Pending.Count );
				return line;
			},
			Font = fonts => fonts.Label,
			Color = UiColors.OptionText,
			Shadow = false,
			Wrap = true,
			Bounds = OptionsScreen.PageBuilder.Rect( 1331, 167 + 4 * 156 + 20, 1964, 1300 ),
			Anchor = UiAnchor.Center
		} );
		screen.Add( new UiModelImage { Id = "buttonsPanel", Model = "!f_plain", Bounds = OptionsScreen.PageBuilder.Rect( 1835, 1321, 1947, 1443 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "ok", Model = "b_okay", Clicked = () => { if ( Stack.Top == screen ) Stack.Pop(); }, Bounds = OptionsScreen.PageBuilder.Rect( 1853, 1340, 1936, 1424 ), Anchor = UiAnchor.Center } );
		screen.Updating += _ =>
		{
			rides = level.CheatRides;
			foreach ( var (button, enabled) in buttons )
				button.Enabled = enabled();
			// the park may stop being one the window works in (a visit replaced it): the window closes
			if ( !CheatsWindowAvailable && Stack.Top == screen )
				Stack.Pop();
		};
		screen.Back = () => { if ( Stack.Top == screen ) Stack.Pop(); };
		return screen;
	}
}
