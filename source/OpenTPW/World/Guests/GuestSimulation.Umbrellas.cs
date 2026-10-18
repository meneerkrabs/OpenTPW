namespace OpenTPW;

// The host side of the umbrellas extension (docs/UMBRELLAS.md, phase U2): the brolly shop's score, a purchase, the umbrella states and the balloon trade. Every rule starts from a brolly shop or a guest
// who owns an umbrella, so a park without either runs exactly as it did without the extension (nothing here draws from a random stream or changes a hash then).
public sealed partial class GuestSimulation
{
	/// <summary>
	/// [EXT:umbrellas-weather-map] The weather as Theme Park Inc's state for the umbrella rules (<see cref="UmbrellaRules.WeatherStateOf"/>: 2 rain, 3 storm, 4 snow, anything else, or null, none). The level reads it from
	/// <see cref="ParkWeather"/>; nothing but the umbrella rules reads it.
	/// </summary>
	public Func<int>? UmbrellaWeatherState { get; set; }

	/// <summary>The brolly shops registered (maintained by <see cref="Register"/> and <see cref="Unregister"/>): the park uses the umbrellas extension while there is one.</summary>
	private int umbrellaShops;

	/// <summary>
	/// What the host does to the policy's score of <paramref name="attraction"/> for <paramref name="guest"/> (the decision interface is untouched): for a brolly shop, 0 for a guest who has an umbrella or in weather that is not
	/// rain, storm or snow, else the weather percentage, the held balloon's cut and the generation percentage in that order; for a balloon shop in a park that uses umbrellas, 0 for a guest with a balloon or an open or opening umbrella.
	/// Every other attraction keeps its score.
	/// </summary>
	// [BIN:TPI-EXE:0x6101e0 attraction score] the extension's objects only: type +0x21C == 3 gates on the umbrella and the weather, the weather word +0xF0..+0xFC scales the score when nonzero (0x6109d0), the balloon factor cuts an umbrella shop (0x610af1), the generation word +0x110 scales last (0x610b59)
	// [EXT:umbrellas-balloon-gate] a balloon shop scores 0 for a balloon holder only in a park that uses umbrellas (a brolly shop, or a guest with an umbrella): tp.exe's own score reads no appearance word, so every other park keeps its balloon shops as they were
	internal uint HostScore( Guest guest, IRideVisitorBridge attraction, uint score )
	{
		if ( attraction.UmbrellaTerms is { } terms )
		{
			var weather = UmbrellaWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
			var shop = attraction.AppearanceEffect == UmbrellaRules.ShopAppearanceEffect;
			if ( shop && UmbrellaRules.ShopScoresZero( weather, guest.UmbrellaState ) )
				return 0;
			score = UmbrellaRules.ApplyWeather( score, weather, terms );
			if ( shop )
				score = UmbrellaRules.ApplyBalloonFactor( score, guest.HasBalloon, guest.BalloonLife );
			return UmbrellaRules.Percent( score, terms.KidScorePercentage );
		}
		if ( attraction.AppearanceEffect == GuestAppearance.Balloon && (umbrellaShops > 0 || guest.UmbrellaState != UmbrellaRules.StateNone)
			&& UmbrellaRules.BalloonShopScoresZero( guest.HasBalloon, guest.BalloonLife, guest.UmbrellaState ) )
			return 0;
		return score;
	}

	/// <summary>
	/// A purchase at a brolly shop: the umbrella opens at once, whatever the weather, in a colour from one draw of the world generator. A guest who already has one (it was bought in the queue's time, the score gate only
	/// keeps it from choosing a second shop) gets a new colour and an open umbrella again.
	/// </summary>
	// [BIN:TPI-EXE:0x612ea9 umbrella purchase] AppearanceEffect 3 in the use routine: state 3, the colour from 0x690370 (one world draw), the sprite created if there is none, state 4; the balloon is not touched
	private void GiveUmbrella( Guest guest )
	{
		guest.UmbrellaType = UmbrellaRules.Colour( random.NextWord() );
		guest.UmbrellaState = UmbrellaRules.StateOpen;
		guest.UmbrellaDueTurn = 0;
	}

	/// <summary>
	/// A held balloon is let go: the balloon goes, the guest loses the medium happiness step and the life is cleared. Both an umbrella that opens and a guest who enters a brolly shop with a balloon do it.
	/// </summary>
	// [BIN:TPI-EXE:0x613d00 release balloon] the model (+0x294) is destroyed with the float-away effect, happiness falls by MediumHappinessChange (0xA01A7C, 15) through the clamped adder and the life +0x298 is cleared; the callers test "model or life above 0"
	private void ReleaseBalloon( Guest guest )
	{
		if ( !guest.HasBalloon && guest.BalloonLife <= 0 )
			return;
		guest.HasBalloon = false;
		guest.Happiness = MeterMath.Add( guest.Happiness, -(float)(Settings.MediumHappinessChange & 0xFF) );
		guest.BalloonLife = 0;
	}

	/// <summary>
	/// The guest enters an attraction: a balloon holder entering a brolly shop lets the balloon go, and an umbrella that has a sprite (open, or open until a due close) is closed (<see cref="UmbrellaRules.StateAfterEntering"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x61a7b5 enter attraction] with a balloon: an umbrella shop (type +0x21C == 3) calls 0x613d00, any other attraction puts the model away; then a nonzero umbrella state calls 0x6142e0, which closes it only while its sprite exists
	private void EnterAttraction( Guest guest, IRideVisitorBridge ride )
	{
		if ( ride.IsUmbrellaShop )
			ReleaseBalloon( guest );
		var state = UmbrellaRules.StateAfterEntering( guest.UmbrellaState );
		if ( state != guest.UmbrellaState )
		{
			guest.UmbrellaState = state;
			guest.UmbrellaDueTurn = 0;
		}
	}

	/// <summary>
	/// The guest is inside an attraction (or its changing room's water): the reaction is not scheduled or carried out then.
	/// </summary>
	// [APPROX:UMB-003] Theme Park Inc's "inside" words (+0x38, +0x3C: set while the guest is in the attraction's own handling) are not known state for OpenTPW's guests: the in-attraction states stand in for them, the ride or shop in use and the pool states of a changing room
	private static bool IsInsideAttraction( Guest guest ) =>
		guest.State is GuestState.Using or GuestState.Swimming or GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.PoolActivity;

	/// <summary>
	/// The guest's age in park turns for the day gate: the turns since its creation, from the time it has spent in the park (the sum of its steps, read as the visitor roster reads it). A guest without a known time (an
	/// imported original guest) counts as new, which opens the gate.
	/// </summary>
	// [BIN:TPW-EXE:0x4FD950 turns in park] the original subtracts two world turns (the stamp +0x1D4 never rewritten); OpenTPW keeps the seconds (APPROX:GUEST-920) and the turn count is their floor over the turn's 248 ms
	private static long AgeTurnsOf( Guest guest ) =>
		guest.TimeInParkSeconds is { } seconds ? (long)Math.Floor( seconds * 1000.0 / ParkCalendar.TurnMilliseconds ) : 0;

	/// <summary>
	/// The weather step of a guest who owns an umbrella, on the guest's own update (every fourth park turn, before its needs): on a qualifying day (<see cref="UmbrellaRules.DayGateOpen"/>) the weather opens or closes the umbrella
	/// through the states of <see cref="UmbrellaRules.WeatherAction"/>. The mood part of Theme Park Inc's rule (happiness, thirst and hunger in bad weather) is the <c>opentpw.rain-mood</c> extension's, not this one's.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 weather rule] called from the guest update 0x619B50 at the top of the needs phase (turn & 3 == id & 3) for every guest; the view condition of the scheduling (0x67F890 / 0x685680) is dropped
	// [APPROX:UMB-002] the scheduling and the carrying out test a view condition (the guest "not identified": two thing calls the trace could not name); OpenTPW has none, so a guest is never held back by it
	private void UpdateUmbrella( Guest guest, long turn )
	{
		if ( !UmbrellaRules.DayGateOpen( AgeTurnsOf( guest ) ) )
			return;
		var weather = UmbrellaWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		switch ( UmbrellaRules.WeatherAction( guest.UmbrellaState, UmbrellaRules.IsBadWeather( weather ) ) )
		{
			case UmbrellaAction.CancelClose:
				guest.UmbrellaState = UmbrellaRules.StateOpen;
				guest.UmbrellaDueTurn = 0;
				break;
			case UmbrellaAction.CancelOpen:
				guest.UmbrellaState = UmbrellaRules.StateClosed;
				guest.UmbrellaDueTurn = 0;
				break;
			case UmbrellaAction.ScheduleClose:
				Schedule( guest, turn, UmbrellaRules.StateClosing );
				break;
			case UmbrellaAction.ScheduleOpen:
				Schedule( guest, turn, UmbrellaRules.StateOpening );
				break;
			case UmbrellaAction.CarryOut:
				CarryOut( guest, turn );
				break;
		}
	}

	/// <summary>A change is scheduled (state 1 or 3): not while the guest is inside an attraction; due after the reaction delay.</summary>
	// [BIN:TPI-EXE:0x613ea0 schedule] +0x38 and +0x3C zero (not inside), then the delay from rand() (UMB-001), the balloon's lengthening, the due word +0x23C = (clock >> 2) + delay and the state written
	private void Schedule( Guest guest, long turn, int state )
	{
		if ( IsInsideAttraction( guest ) )
			return;
		var delay = UmbrellaRules.Delay( guest.Id, turn, guest.HasBalloon, guest.BalloonLife );
		guest.UmbrellaDueTurn = UmbrellaRules.DueTurn( turn, delay );
		guest.UmbrellaState = state;
	}

	/// <summary>
	/// A pending change is carried out when it is due and the guest is not inside an attraction: a pending close (1) closes the umbrella (2); a pending open (3) lets a held balloon go (the happiness step) and opens it (4).
	/// </summary>
	// [BIN:TPI-EXE:0x614010 carry out] not inside, the due word reached (clock >> 2 against +0x23C); state 1 sets 2 and destroys the sprite; otherwise the balloon is released through 0x613d00's body and the sprite is made, state 4
	private void CarryOut( Guest guest, long turn )
	{
		if ( IsInsideAttraction( guest ) || !UmbrellaRules.IsDue( turn, guest.UmbrellaDueTurn ) )
			return;
		if ( guest.UmbrellaState == UmbrellaRules.StateClosing )
		{
			guest.UmbrellaState = UmbrellaRules.StateClosed;
		}
		else
		{
			ReleaseBalloon( guest );
			guest.UmbrellaState = UmbrellaRules.StateOpen;
		}
		guest.UmbrellaDueTurn = 0;
	}
}
