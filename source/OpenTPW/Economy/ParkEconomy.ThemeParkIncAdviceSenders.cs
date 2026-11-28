namespace OpenTPW;

/// <summary>The park state the ported score callbacks read (<see cref="ThemeParkIncAdvisor.World"/>).</summary>
internal interface IThemeParkIncAdviceWorld
{
	/// <summary>The mission's requirements are met (<c>0x55A620</c>), which the requirements-met advice's callbacks read.</summary>
	bool MissionRequirementsMet { get; }
}

/// <summary>
/// Theme Park Inc's advisor senders of phase P5 (docs/reverse/TPI-ADVISOR.md §9.3): the staff, research, bank, strike, ride and mechanic, special-ride and cutscene posts, and the advisor's
/// message handler (<c>0x719470</c>) for the message kinds it subscribes to (<c>0x7176E0</c>: 5, 0x11, 0x13, 0x17, 0xE and 8). Each post builds the record with the descriptor's score
/// (<c>0x71A930</c> with score -1) and offers it to the advisor thing's queue (<c>0x64EA00</c> -> <c>0x718820</c>).
/// </summary>
public sealed partial class ParkEconomy
{
	private sealed class ThemeParkIncAdviceWorld( ParkEconomy economy ) : IThemeParkIncAdviceWorld
	{
		public bool MissionRequirementsMet => economy.ThemeParkIncMissionMet;
	}

	/// <summary>The thing id of the object with economy instance <paramref name="instance"/> (the ride's handle, <c>0x6375C0</c>), 0 when the level has not linked one.</summary>
	private ushort ThemeParkIncThingOf( int instance ) => tpiThingIds.TryGetValue( instance, out var thing ) ? unchecked( (ushort)thing ) : ThingOf( instance );

	/// <summary>
	/// The mission status refresh (<c>0x54D660</c>): with the mission's requirements met, the advice for the career's mission counter. Its callers are the controller's message switch and daily
	/// evaluation (after the message 0x27 broadcast), the training's end and the mission window's close.
	/// </summary>
	// [BIN:TPI-EXE:0x54D660 status advice] 0x55A620 (MetRequirements) false: 0x7BAE30 (the HUD's interface, presentation) and return; true: the sound or the panel 'EHL' (presentation), then by the career's
	// counter +0x105C: 0 posts 0x20F (0x54D719), 1 posts 0x1C7 (0x54D6E2), any other 0x1E3 (0x54D6D2), each 0x71A930(message, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820
	// [EXT:tpi-capability-gates] counter 0's 0x20F is not posted: its score callback 0x727D10 reads the first-hour clock 0x650890 (0xBFCC4C / 60 below FirstHour.Minutes), which OpenTPW does not tick yet (P7)
	internal void ThemeParkIncMissionStatusAdvice()
	{
		if ( TpiAdvisor == null || TpiCareer is not { } career || !ThemeParkIncMissionMet )
			return;
		var message = StatusAdvice( career.MissionCounter );
		if ( message == FirstMissionStatusAdvice )
			Log.Trace( "Theme Park Inc advice 0x20F (mission requirements met, first mission) is not posted: its score reads the first-hour clock, which is not ported." );
		else
			PostThemeParkIncAdvice( message );
	}

	/// <summary>The requirements-met advice of the career's mission counter (<c>0x54D6B8</c>): <c>0x20F</c> at 0, <c>0x1C7</c> at 1, <c>0x1E3</c> after.</summary>
	public static int StatusAdvice( int counter ) => counter switch { 0 => FirstMissionStatusAdvice, 1 => 0x1C7, _ => ThemeParkIncMissionController.RequirementsMetAdvice };

	/// <summary>The first mission's requirements-met advice (<c>0x20F</c>), which waits for the first-hour clock (P7).</summary>
	public const int FirstMissionStatusAdvice = 0x20F;

	/// <summary>The research advice a research completion's message 0x11 asks of the advisor (<c>0x7194C7</c>), or null for none.</summary>
	/// <param name="category">The project's category (the message's <c>+0xC</c>).</param>
	/// <param name="upgrader">A mechanic has learnt Ride Upgrading (<c>0x62B7D0(1)</c> over the mechanics' list <c>+0x1EAB80</c>).</param>
	/// <param name="played18C">The history has <c>0x18C</c> played (the career's <c>+0x33E8</c>, entry <c>0x18C</c> +8).</param>
	// [BIN:TPI-EXE:0x7194C7 research advice] by the category (jump table 0x71A43C): 0 posts 0x58, 1 0x5A, 2 0x5B, 3 0x5C, 4 (0x7195F4) 0x59 when a mechanic's +0x218 is 1, else 0x176 when 0x18C was played, else
	// 0x18C; 6 posts 0x11B and then logs "unknown research category in CAdvisor::ReceiveMessage."; 5 and any other only log
	public static int? ResearchAdvice( int category, bool upgrader, bool played18C ) => category switch
	{
		0 => 0x58,
		1 => 0x5A,
		2 => 0x5B,
		3 => 0x5C,
		4 => upgrader ? 0x59 : played18C ? 0x176 : 0x18C,
		6 => 0x11B,
		_ => null
	};

	/// <summary>
	/// A research completion's message 0x11 (the object researched and its project's category) at the advisor, its first listener (thing 3 in the prebuilt park, before the analyser and the lab):
	/// the category's advice, with the object type as its locate dword.
	/// </summary>
	// [BIN:TPI-EXE:0x7194DA research advice post] 0x71A930(message, locate 0, the message's word +8 (the object), variant -1, bypass 0, score -1) -> 0x718820 on the advisor; the dispatcher 0x436DD0 calls the
	// kind's listeners in ascending thing handle order (the tree 0x437EF0), and the advisor (thing 3) comes before the analyser (5), the lab (6, 0x61DC20) and the challenge system (11)
	internal void ThemeParkIncResearchAdvice( int type, int category )
	{
		if ( TpiAdvisor == null || TpiCareer is not { } career )
			return;
		var upgrader = TpiStaff?.OfRole( ThemeParkIncStaffRole.Mechanic ).Any( member => member.Ability == 1 ) ?? false;
		if ( ResearchAdvice( category, upgrader, career.AdviceHistory( 0x18C ).Played ) is { } message )
			PostThemeParkIncAdvice( message, 0, unchecked( (ushort)type ) );
		if ( category is not (>= 0 and <= 4) )
			Log.Trace( "unknown research category in CAdvisor::ReceiveMessage." );
	}

	/// <summary>The advice a strike message (kind 0x17) asks of the advisor (<c>0x719270</c>), or null for none.</summary>
	// [BIN:TPI-EXE:0x719270 strike advice] the reason (+0x10) 2 gives 0x22, 3 gives 0x1DD and 4 gives 0x1D1; any other reason goes by the kind (+0xC): 2 gives 0x28, 3 gives 0x2E and 5 gives 0x34, another nothing;
	// the message is that plus the role's (+8) word of 0x940784 (handyman 1, mechanic 0, entertainer 3, guard 2, researcher 4, gardener 5)
	public static int? StrikeAdvice( ThemeParkIncStaff.StrikeMessage message )
	{
		int? offset = message.Reason switch
		{
			2 => 0x22,
			3 => 0x1DD,
			4 => 0x1D1,
			_ => message.Kind switch { 2 => 0x28, 3 => 0x2E, 5 => 0x34, _ => null }
		};
		var role = message.Role switch
		{
			ThemeParkIncStaffRole.Handyman => 1,
			ThemeParkIncStaffRole.Mechanic => 0,
			ThemeParkIncStaffRole.Entertainer => 3,
			ThemeParkIncStaffRole.Guard => 2,
			ThemeParkIncStaffRole.Researcher => 4,
			ThemeParkIncStaffRole.Gardener => 5,
			_ => throw new ArgumentOutOfRangeException( nameof( message ), $"Theme Park Inc staff role {message.Role} has no row of the strike table 0x940784." )
		};
		return offset + role;
	}

	/// <summary>A strike message (kind 0x17, <c>0x435CA0</c>) at its only listener, the advisor: its advice.</summary>
	// [BIN:TPI-EXE:0x7192C1 strike advice post] 0x71A930(message, locate 0, 0, variant -1, bypass 0, score -1) -> 0x718820; the dispatcher 0x436DD0 reaches the advisor at once, inside the HQ's month (0x6330B0)
	// or the strike's end (0x6342B0)
	internal void ThemeParkIncStrikeMessage( ThemeParkIncStaff.StrikeMessage message )
	{
		if ( StrikeAdvice( message ) is { } advice )
			PostThemeParkIncAdvice( advice );
	}

	/// <summary>
	/// A Theme Park Inc ride's entry into state 1 (broken down: message kind 7) or 4 (condemned: message kind 8), which <c>0x5E5160</c> posts with the ride's thing: the mechanics' HQ answers a
	/// breakdown with <c>0x41</c> or <c>0x42</c>, the advisor a condemned ride with <c>0xDB</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x5E53E5 broken message] SetState(1): the head step 0x5E3CC0, "Closing...", the closed model call, then message kind 7 (0x435690, the ride's handle) to the dispatcher 0xA09728;
	// [BIN:TPI-EXE:0x5E519B condemned message] SetState(4): "Ride has become CONDEMNED!!!", the head step, the close, then message kind 8 (0x435710, the handle)
	internal void ThemeParkIncRideStateAdvice( ParkObjectState item, int state )
	{
		if ( TpiAdvisor == null )
			return;
		var thing = ThemeParkIncThingOf( item.Id );
		if ( state == RideObjectStates.BrokenDown )
			ThemeParkIncMechanicHqBreakdown( item, thing );
		else if ( state == RideObjectStates.Condemned )
			// [BIN:TPI-EXE:0x71A3AE condemned advice] kind 8 at the advisor (thing 3, before the interface's receiver 10): 0x71A930(0xDB, locate = the message's word +8 (the ride), 0, variant -1, bypass 0,
			// score -1) -> 0x718820
			PostThemeParkIncAdvice( 0xDB, thing );
	}

	/// <summary>
	/// The mechanics' HQ hears a ride break down (<c>0x5DAEB0</c>, message kind 7): without mechanics it posts <c>0x41</c>; a mechanic already going to the ride posts <c>0x43</c> unless the ride has asked
	/// for maintenance; a free mechanic (no ride, neither resting nor on strike) posts nothing; else <c>0x42</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x5DAEB0 breakdown advice] the HQ thing (class 10, 0x6379EE) takes kind 7 only; the mechanics' list (world +0x1EAB80) empty: 0x71A930(0x41, locate = the ride's handle, the ride's type +0x16,
	// variant -1, bypass 0, score -1) -> 0x718820; else each mechanic in list order (+0x5D0): his ride +0x5D8 is the ride: [BIN:TPI-EXE:0x5DB000 breakdown claimed] the ride's request +0x78 (0x5E3B70) unset
	// posts 0x43 the same way (0x5DB06F), set nothing; his ride 0 and his display state (0x62B010) neither 3 nor 4: nothing; the list's end: 0x42 the same way. The HQ (thing 2) hears it before the
	// advisor (3), which does not take kind 7
	private void ThemeParkIncMechanicHqBreakdown( ParkObjectState item, ushort thing )
	{
		var mechanics = TpiStaff?.OfRole( ThemeParkIncStaffRole.Mechanic ).ToList() ?? new List<ThemeParkIncStaffMember>();
		if ( mechanics.Count == 0 )
		{
			PostThemeParkIncAdvice( 0x41, thing, unchecked( (ushort)item.InfoId ) );
			return;
		}
		foreach ( var mechanic in mechanics )
		{
			if ( mechanic.RideId == item.Id )
			{
				if ( !item.MaintenanceRequested )
					PostThemeParkIncAdvice( 0x43, thing, unchecked( (ushort)item.InfoId ) );
				return;
			}
			if ( mechanic.RideId == 0 && ThemeParkIncStaffWorld.DisplayCategory( mechanic ) is not (3 or 4) )
				return;
		}
		PostThemeParkIncAdvice( 0x42, thing, unchecked( (ushort)item.InfoId ) );
	}

	/// <summary>
	/// The wear tick's warning (<c>0x5E2791</c>): the state of repair byte has just fallen below the type's <c>RideBreakDown.Imminent</c> line (it is below it now, and was not before the wear), so
	/// advice <c>0x40</c> names the ride.
	/// </summary>
	// [BIN:TPI-EXE:0x5E2784 imminent advice] b = (u8)ftol(+0x54) after the wear; fild b, fcom Imminent (+0x3C8): not below (C0 clear) returns; fadd the wear (a single at esp+0xC), fcomp Imminent: below
	// (C0 set, an unordered compare too) returns; else 0x71A930(0x40, locate = the ride's handle, the ride's type +0x16, variant -1, bypass 0, score -1) -> 0x718820. At the 24-bit control word the sum
	// rounds to a single
	internal static bool CrossedImminentLine( int repairByte, float wear, float imminent ) =>
		!((float)repairByte >= imminent) && (float)(repairByte + wear) >= imminent;

	/// <summary>The wear tick's warning <c>0x40</c> for a ride whose repair byte crossed its imminent line (<see cref="CrossedImminentLine"/>).</summary>
	private void ThemeParkIncImminentBreakdownAdvice( ParkObjectState item ) =>
		PostThemeParkIncAdvice( 0x40, ThemeParkIncThingOf( item.Id ), unchecked( (ushort)item.InfoId ) );

	/// <summary>
	/// A mechanic claims a ride that is neither breaking down nor being upgraded (<c>0x5DA4AD</c>): advice <c>0x43</c> names it.
	/// </summary>
	// [BIN:TPI-EXE:0x5DA4AD mechanic claim advice] after the claim 0x5E3780: 0x5E3B70 and 0x5E3B90 both 0: 0x71A930(0x43, locate = the mechanic's ride +0x5D8, the ride's type +0x16, variant -1, bypass = 0 (the
	// test's eax), score -1) -> 0x718820; its descriptor's score is the constant 0, so it never plays
	internal void ThemeParkIncMechanicClaimAdvice( ParkObjectState ride ) =>
		PostThemeParkIncAdvice( 0x43, ThemeParkIncThingOf( ride.Id ), unchecked( (ushort)ride.InfoId ) );

	/// <summary>The ride's notices a mechanic who starts its repair clears (<c>0x5DA6E8</c>): the tag system's rows of tags 6 to 9 keyed by the ride's thing.</summary>
	internal void DeleteThemeParkIncRideNotices( ParkObjectState ride ) => TpiTagTube?.DeleteTagged( RideNoticeTags, ThemeParkIncThingOf( ride.Id ) );

	/// <summary>The tags the repair's start clears (<c>0x5DA6FD</c>..<c>0x5DA715</c>).</summary>
	public static readonly IReadOnlyList<int> RideNoticeTags = new[] { 6, 7, 8, 9 };
}
