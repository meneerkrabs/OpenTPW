namespace OpenTPW;

// Theme Park Inc's native guest states, phase P2 (docs/reverse/TPI-GUESTS.md): idle (state 6, 0x614A40..0x615411) with its spot animations (0x60FB30, the mood 0x60CDD0), its litter, the
// turn towards a show or a performing entertainer and the prank gate and switch; the wander (state 7, 0x61A179) and the walk to a litter bin (state 9, 0x616F10). The walk step, the route search and
// the wander destination stay Theme Park World's ([EXT:tpi-twp-guest-rules]), as in phase P1.
public sealed partial class GuestSimulation
{
	/// <summary>What the native guests read of the park outside the simulation: the bins, the shows, the entertainers (docs/reverse/TPI-GUESTS.md, phase P2). Null: none of them.</summary>
	public IThemeParkIncGuestWorld? ThemeParkIncWorld { get; set; }

	/// <summary>
	/// A native guest's prank report (the prank tail <c>0x614F14</c>: the message <c>0x436DD0</c> with the template <c>0x93E6B0</c>, the prank's kind and the guest's id): kind 1 a stinkbomb, 5 litter,
	/// 6 a burst balloon (states 10 and 11 post 3 and 4). The staff HQ's prank handler <c>0x6323F9</c> receives it in the original; nothing listens here until phase P6 ports the guard's chase.
	/// </summary>
	public event Action<Guest, int>? ThemeParkIncPrankPosted;

	/// <summary>The sounds of the guest bank state 6 plays (<c>0x60BE90</c> with the bank <c>[0xAE011C]</c>): vomit, a stinkbomb, the can't-decide pause, the mood animations and animation 10.</summary>
	public const uint ThemeParkIncVomitVoice = 0xCC, ThemeParkIncStinkbombVoice = 0xCF, ThemeParkIncCantDecideVoice = 0x7E, ThemeParkIncMoodVoice = 0x4D, ThemeParkIncAnimationTenVoice = 0xD5;

	/// <summary>The spot animations state 6 plays: the vomit (0xC), the can't-decide pause with the umbrella down (4) or up (3), and the mood animations 5..9 (<see cref="ThemeParkIncMoodAnimation"/>).</summary>
	public const int ThemeParkIncVomitAnimation = 0xC, ThemeParkIncCantDecideAnimation = 4, ThemeParkIncCantDecideUmbrellaAnimation = 3;

	/// <summary>The prank kinds the tail posts (<c>edi</c> at <c>0x614F14</c>): a stinkbomb (case 0), litter (case 4) and a burst balloon (case 5).</summary>
	public const int ThemeParkIncStinkbombPrank = 1, ThemeParkIncLitterPrank = 5, ThemeParkIncBalloonPrank = 6;

	/// <summary>
	/// The ESP header byte <c>0x10D</c> of each loaded guest body bank, by generation and bank (the level's loader; <see cref="SpriteAnimationFile.FlagB"/>): nonzero for a female bank. Null without
	/// loaded banks.
	/// </summary>
	internal bool[][]? TpiSpriteBankFemale { get; set; }

	/// <summary>
	/// Whether the guest's sprite bank is female (<c>0x68E7B0</c>: the bank object's byte <c>+0x206</c>, which the bank loader <c>0x68EA60</c> reads from the ESP header's byte <c>0x10D</c>): the bank of its
	/// generation its appearance draw picks, as the renderer picks it. A simulation without loaded banks (a headless rig) reads male. The answer picks the mood animation's number only, which goes into the sprite's
	/// mode (<c>+0x20</c>, <see cref="ThemeParkIncGuestRecord.Mode"/>): saved and hashed, but read by no rule.
	/// </summary>
	// [DATA:esprites.wad/Generic/{Kids,Parents,Grandparents}/SPR_xx.ESP] byte 0x10D: BD 0, GB 1, GK 1, KB 0; FA 0, MO 1; GM 1, GR 0
	private bool ThemeParkIncSpriteIsFemale( Guest guest )
	{
		if ( TpiSpriteBankFemale is not { } banks || TpiSpriteBankCounts is not { } counts )
			return false;
		var generation = TpiGeneration( guest );
		if ( (uint)generation >= (uint)banks.Length || (uint)generation >= (uint)counts.Length || counts[generation] <= 0 )
			return false;
		var bank = GuestAppearance.KidSet( guest, counts[generation] );
		return (uint)bank < (uint)banks[generation].Length && banks[generation][bank];
	}

	/// <summary>The cell a native guest stands on (its thing's bytes <c>+5</c>, <c>+7</c>), as phase P1 reads it.</summary>
	private static (int X, int Y) TpiCell( Guest guest ) => guest.Navigation.Cell;

	// ---- Spot animations ---------------------------------------------------------------------------------------------------------

	/// <summary>
	/// A spot animation (<c>0x60FB30</c>): the animation's sound for one thing id in three (4: 0x7E, 5..9: 0x4D, 10: 0xD5), the animation on the sprite, the state saved into <c>+0x2C4</c>, the turn
	/// stamped and state 8, which restores the saved state ten turns later.
	/// </summary>
	// [BIN:TPI-EXE:0x60FB30 spot animation] the id (0x6375C0) % 3 == 0 gives the flag; anim − 4 below 7 picks through the table 0x60FBDC: 4 sound 0x7E, 5..9 0x4D, 10 0xD5, each only with the flag
	// (0x60BE90 with [0xAE011C], presentation); 0x5D0F90(anim) on the sprite (+0x20), +0x2C4 = +0x2C0, +0x230 = the world turn, setter(8)
	private void StartThemeParkIncSpotAnimation( Guest guest, ThemeParkIncGuestRecord tpi, int animation )
	{
		if ( (ushort)guest.Id % 3 == 0 )
		{
			uint? voice = animation switch
			{
				4 => ThemeParkIncCantDecideVoice,
				>= 5 and <= 9 => ThemeParkIncMoodVoice,
				10 => ThemeParkIncAnimationTenVoice,
				_ => null
			};
			if ( voice is { } sound )
				Voiced?.Invoke( guest, sound );
		}
		tpi.Mode = animation;
		tpi.Saved = tpi.State;
		tpi.SpotTurn = TpiTurn;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.SpotAnimation );
	}

	/// <summary>
	/// The mood animation (<c>0x60CDD0</c>): a kid's 5; a parent's 6 (female) or 7 (male); a grandparent's 8 (female) or 9 (male); any other generation 0.
	/// </summary>
	// [BIN:TPI-EXE:0x60CDD0 mood animation] the sprite bank [0xB88AEC + 4 × ([0x9D7004 + 12 × +0x34] + +0x30)] and its byte +0x206 (0x68E7B0: nonzero female, the ESP header's byte 0x10D, read by the
	// bank loader 0x68EA60); eax = 2 for female, 1 for male; +0x2A4 = 0 gives 5, 1 gives 6 + (eax != 2), 2 gives 8 + (eax != 2), any other 0
	internal static int ThemeParkIncMoodAnimation( int generation, bool female ) => generation switch
	{
		0 => 5,
		1 => female ? 6 : 7,
		2 => female ? 8 : 9,
		_ => 0
	};

	// ---- State 6 ---------------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 6 (<c>0x614A40</c>): one world draw A, then in order the mood animation, the vomit, the litter urge, the leave gate (which falls through), the turn towards a show and the turn towards a
	/// performing entertainer, the prank gate with its switch and, last, the four-way decision on <c>A &amp; 3</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x614A70 draw A] the first world draw of the handler, before anything is tested; A % 3, A % 100, A % 5 and A & 3 are its only uses; +0x1B8 = 0 (0x614A86)
	internal void UpdateTpiIdle( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		var turn = TpiTurn;
		var a = random.NextWord();
		tpi.Word1B8 = 0;
		// [BIN:TPI-EXE:0x614A75 mood] turn − +0x230 above 100 (unsigned), the happiness byte above 80 and the umbrella state 0 or 2: the mood animation 0x60CDD0 through 0x60FB30, and return
		if ( unchecked( turn - (uint)tpi.SpotTurn ) > 100 && MeterMath.Byte( guest.Happiness ) > 80 && guest.UmbrellaState is UmbrellaRules.StateNone or UmbrellaRules.StateClosed )
		{
			StartThemeParkIncSpotAnimation( guest, tpi, ThemeParkIncMoodAnimation( tpi.Generation, ThemeParkIncSpriteIsFemale( guest ) ) );
			return;
		}
		// [BIN:TPI-EXE:0x614ACE vomit] the illness byte (fild) above 80.0 and equal to 100.0, and A % 3 == 0: the spot animation 0xC, the vomit 0x5D8C80(the cell's record, 7), the sound 0xCC for every
		// guest, illness +0x1D4 = 0, and return
		if ( MeterMath.Byte( guest.Nausea ) == 100 && a % 3 == 0 )
		{
			StartThemeParkIncSpotAnimation( guest, tpi, ThemeParkIncVomitAnimation );
			DropThemeParkIncLitter( guest, LitterType.Vomit );
			Voiced?.Invoke( guest, ThemeParkIncVomitVoice );
			guest.Nausea = 0;
			return;
		}
		// [BIN:TPI-EXE:0x614B71 litter urge] the litter byte at least 90 (jb): draw B, B & 3 nonzero leaves the block; the bin search 0x618D10(radius 3): a bin sets +0x204 and state 9 and returns;
		// else draw C, type C % ([0x93E2BC] − 1) + 1 (0x93E2BC holds 6), and only on a cell whose record's +0x30 is 0 the drop 0x5D8C80 and the urge +0x1D8 = 0
		if ( MeterMath.Byte( guest.Litter ) >= ParkLitter.MeterThreshold && (random.NextWord() & 3) == 0 )
		{
			var bin = ThemeParkIncBinSearch( guest, 3 );
			if ( bin != 0 )
			{
				tpi.Target = bin;
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToBin );
				return;
			}
			var type = (LitterType)(random.NextWord() % ParkLitter.OrdinaryKinds + 1);
			var (x, y) = TpiCell( guest );
			if ( Litter is not { } litter || litter.TypeAt( x, y ) == LitterType.None )
			{
				DropThemeParkIncLitter( guest, type );
				guest.Litter = 0;
			}
		}
		// [BIN:TPI-EXE:0x614C2B leave gate] the happiness byte 0, +0x1E0 at most 0 (signed) or 0x64FA30 nonzero: 0x614460(2), the bus stop walk with the mask retry and setter(0x16); the handler then
		// falls through to its later steps (no return), which can overwrite state 22 in the same update (bug-compatible)
		if ( MeterMath.Byte( guest.Happiness ) == 0 || guest.ExitLevel <= 0 || Payments?.ParkClosed == true )
		{
			guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 2 ) );
			WalkToBusStopWithRetry( guest, settings );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.LeavingPark );
		}
		if ( TurnToThemeParkIncShow( guest, tpi ) || TurnToThemeParkIncPerformer( guest, tpi ) )
			return;
		if ( ThemeParkIncPrank( guest, tpi, settings, a ) )
			return;
		ThemeParkIncIdleDecision( guest, tpi, settings, a, turn );
	}

	/// <summary>
	/// The turn towards a show (<c>0x614C7B</c>): the nearest show within two cells (<c>0x6193B0</c>) whose offset parts do not sum to 0 sets the heading, and the update ends.
	/// </summary>
	// [BIN:TPI-EXE:0x614C7B show turn] 0x6193B0's id nonzero: dx = the show's cell x − the guest's, dy likewise, dx + dy == 0 (test; jbe, so only equality) goes on; else +0x2C = 0x91E074(−dx, dy) & 0xFFFF
	// and return
	// [BIN:TPI-EXE:0x6193B0 show finder] the object list (world +0x1EAB88, next +0x14); a thing with +0x42 bit 0x80 (UsageInfo.IsFireworks, type +0x1CC, 0x5DB75B) whose script variable 0 is 0; the squared
	// cell distance (|dx|² + |dy|²) at most 4 and below the best so far (unsigned, from 0xFFFFFFFF: a tie keeps the first); the id of the best, 0 for none. Theme Park World's 0x00501020 is the same walk
	private bool TurnToThemeParkIncShow( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( ThemeParkIncWorld?.Shows() is not { Count: > 0 } shows )
			return false;
		var (gx, gy) = TpiCell( guest );
		var best = uint.MaxValue;
		(int X, int Y)? found = null;
		for ( var index = 0; index < shows.Count; index++ )
		{
			var show = shows[index];
			if ( show.Variable( 0 ) != 0 )
				continue;
			var dx = show.Cell.X - gx;
			var dy = show.Cell.Y - gy;
			var distance = (uint)(dx * dx + dy * dy);
			if ( distance > 4 || distance >= best )
				continue;
			best = distance;
			found = show.Cell;
		}
		return found is { } cell && TurnThemeParkIncGuest( tpi, cell.X - gx, cell.Y - gy );
	}

	/// <summary>
	/// The reaction to a performing entertainer (<c>0x614D41</c>): with an entertainer in the 3 × 3 cells around the guest (<c>0x5B1B20</c>, kind 6, radius 1), the nearest entertainer of the list
	/// (<c>0x619100</c>) must be performing (state 0x17); an offset whose parts do not sum to 0 then sets the heading and <c>+0x1B8 = 1</c>, and the update ends. Otherwise <c>+0x1B8 = 0</c> and the
	/// prank gate follows. Nothing else changes: no happiness, urge or illness.
	/// </summary>
	// [BIN:TPI-EXE:0x614D41 entertainer reaction] 0x64EAC0 (the analyser) → 0x5B1B20(6, the guest's cell word, 1) zero goes to the prank gate; 0x619100's id, its record's +0x1B4 != 0x17 goes to the prank
	// gate; dx + dy == 0 (jbe after test) sets +0x1B8 = 0 (0x614E4E) and goes on; else +0x2C = 0x91E074(−dx, dy) & 0xFFFF, +0x1B8 = 1 and return
	// [BIN:TPI-EXE:0x5B19C0 kind finder] the square [cell ± radius] clipped to 0..0x7F, the things of each cell's chain by the kind byte +2: a placed entertainer (kind 6) on one of the nine cells
	// [BIN:TPI-EXE:0x619100 entertainer finder] the entertainers' list (world +0x1EAB82, next +0x5D0), the squared cell distance (|dx|² + |dy|², no cap) below the best so far (unsigned, from 0xFFFFFFFF:
	// a tie keeps the first); 0 for an empty list. Theme Park World's 0x00500F00 walks every staff member instead (TurnToShowOrPerformer)
	private bool TurnToThemeParkIncPerformer( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( ThemeParkIncWorld?.Entertainers() is not { Count: > 0 } entertainers )
			return false;
		var (gx, gy) = TpiCell( guest );
		var near = false;
		for ( var index = 0; index < entertainers.Count && !near; index++ )
			near = Math.Abs( entertainers[index].Cell.X - gx ) <= 1 && Math.Abs( entertainers[index].Cell.Y - gy ) <= 1;
		if ( !near )
			return false;
		var best = uint.MaxValue;
		ThemeParkIncEntertainerView? nearest = null;
		for ( var index = 0; index < entertainers.Count; index++ )
		{
			var dx = entertainers[index].Cell.X - gx;
			var dy = entertainers[index].Cell.Y - gy;
			var distance = (uint)(dx * dx + dy * dy);
			if ( distance < best )
				(best, nearest) = (distance, entertainers[index]);
		}
		if ( nearest is not { Performing: true } performer )
			return false;
		if ( !TurnThemeParkIncGuest( tpi, performer.Cell.X - gx, performer.Cell.Y - gy ) )
		{
			tpi.Word1B8 = 0;
			return false;
		}
		tpi.Word1B8 = 1;
		return true;
	}

	/// <summary>Turns the guest towards the offset (<c>0x91E074(−dx, dy)</c>, the same routine as Theme Park World's <c>0x006E7074</c>) unless its two parts sum to 0; true when it turned.</summary>
	// [BIN:TPI-EXE:0x91E074 heading angle] by quadrant of (first, second) the smaller magnitude over the larger indexes the table 0x91E1B4 (257 words, entry i = floor(atan(i / 256) × 1024 / π), checked
	// against all 257), added to or subtracted from 0, 0x200, 0x400, 0x600 or 0x800, masked with 0x7FF; both 0 answers 0: HeadingAngle.Of
	private static bool TurnThemeParkIncGuest( ThemeParkIncGuestRecord tpi, int dx, int dy )
	{
		if ( dx + dy == 0 )
			return false;
		tpi.Heading = HeadingAngle.Of( -dx, dy );
		return true;
	}

	/// <summary>
	/// The prank gate (<c>0x614E56</c>) and the prank switch (<c>0x615414</c>) on the guest's prank code (<c>+0x1E4</c>, fixed at birth). True when the update ends here (a bin found for a prank,
	/// rumours or whinging); false to go on to the idle decision, which also follows a stinkbomb, litter or a burst balloon.
	/// </summary>
	// [BIN:TPI-EXE:0x614E56 prank gate] the happiness byte at least PrankHappinessThreshold (0xA01B04, signed jge), or +0x29C nonzero, or A % 100 at least PrankLikelihood (0xA01B08, unsigned jb) with
	// +0x2B1 zero go to the decision 0x61523F; else +0x2B1 = 0 (0x614EA9), S = +0x1E4 − 1 above 7 (unsigned) goes to the decision, else the table 0x615414
	// [BIN:TPI-EXE:0x615414 prank switch] S 0 0x614EC0 (stinkbomb), 1 0x61508B (toilet), 2 0x6150AC (a bin, state 10), 3 0x61511D (a plant, state 11), 4 0x614F8E (litter), 5 0x614FE2 (a balloon
	// to burst), 6 0x61519B (rumours, state 25), 7 0x61515B (whinging, state 24)
	private bool ThemeParkIncPrank( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, uint a )
	{
		if ( MeterMath.Byte( guest.Happiness ) >= settings.PrankHappinessThreshold || tpi.Word29C != 0 )
			return false;
		var hundred = a % 100;
		if ( hundred >= unchecked( (uint)settings.PrankLikelihood ) && tpi.Byte2B1 == 0 )
			return false;
		tpi.Byte2B1 = 0;
		int kind;
		switch ( unchecked( (uint)((guest.TpiPrankCode ?? 0) - 1) ) )
		{
			case 0:
				// [BIN:TPI-EXE:0x614EC0 stinkbomb] A % 100 at least StinkbombLikelihood (0xA01B00, unsigned jae) goes to the decision; else the stinkbomb 0x5D8C80(the cell's record, 8), the sound 0xCF
				// for every guest (0x60BE90) and the tail with kind 1
				if ( hundred >= unchecked( (uint)settings.StinkbombLikelihood ) )
					return false;
				DropThemeParkIncLitter( guest, LitterType.Stinkbomb );
				Voiced?.Invoke( guest, ThemeParkIncStinkbombVoice );
				kind = ThemeParkIncStinkbombPrank;
				break;
			case 1:
				// [BIN:TPI-EXE:0x61508B toilet prank] +0x1D0 (the toilet meter) = 80.0f and +0x29C = 1, which shuts the prank gate for good; then the decision
				guest.Toilet = 80f;
				tpi.Word29C = 1;
				return false;
			case 4:
				// [BIN:TPI-EXE:0x614F8E litter prank] type A % 5 + 1 (a literal 5), the drop 0x5D8C80(the cell's record, type), +0x1E4 = 0, setter(7) and the tail with kind 5; no prank code of the
				// table 0x9D0624 is 5, so only the dead +0x2B1 commands could reach it
				DropThemeParkIncLitter( guest, (LitterType)(a % 5 + 1) );
				guest.TpiPrankCode = 0;
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Wandering );
				kind = ThemeParkIncLitterPrank;
				break;
			case 2:
				// [BIN:TPI-EXE:0x6150AC bin prank] the bin search 0x618D10 with radius 6 (its route requests included); a bin whose VAR_PRANKED (10, read again through 0x6B0740/0x6B0C90) is 0: +0x204 =
				// the bin, setter(10) and return (0x61510A); no bin, or the variable set, goes to the decision
				var bin = ThemeParkIncBinSearch( guest, 6 );
				if ( bin == 0 || ThemeParkIncWorld?.Thing( bin ) is not { } found || found.Variable( 10 ) != 0 )
					return false;
				tpi.Target = bin;
				// [EXT:tpi-capability-gates] state 10 (0x617140, the walk to the bin for the prank: VARs 2, 4, 10 and 12, the deposit and the prank tail with kind 3) is phase P4's: the entry is not made,
				// the guest stays idle with its target and its walk to the bin, and the update ends as the original's does
				return true;
			case 6:
			case 7:
				// [BIN:TPI-EXE:0x61519B rumours] case 6: +0x1E8 = WhingeDuration (0xA01B0C), +0x2B0 = 0, setter(0x19), 0x60B6E0 (the stand animation 3 on the sprite), thought 0x12 and return (0x6151C8);
				// [BIN:TPI-EXE:0x61515B whinging] case 7 the same with setter(0x18), returning at 0x615188; neither draws, and neither reaches the decision
				// [EXT:tpi-capability-gates] states 24 and 25 (0x6161C0, 0x616100: whinging to the staff and spreading rumours) are phase P5's: their entry is not made, and the words +0x1E8 and +0x2B0,
				// which only those two states read, are not held; the guest stays idle. The thought 0x12 is not shown (Theme Park World's bubbles). The stand animation is made and the update ends
				tpi.Mode = 3;
				return true;
			default:
				// [EXT:tpi-capability-gates] case 3 (the plant search 0x618EF0 with radius 6 and its fire test 0x618F30; a plant found sets +0x204 and state 11 and returns at 0x615148) is phase P4's: it is
				// read as finding no plant, so a prankster with code 4 within six cells of a plant goes on to the decision where the original's update ends. Case 5 (the first guest on the cell holding
				// a balloon or a balloon countdown: 0x613D00(victim, 1) and the tail with kind 6) finds no victim, which is exact while no native guest holds a balloon (none can before phase P3: a
				// restore refuses a block that holds one); neither draws
				return false;
		}
		// [BIN:TPI-EXE:0x614F14 prank tail] the thought object takes thought 0x15 (0x638960(0x15, 0)), the message 0x436DD0 with the template 0x93E6B0 (the kind, the guest's id; the buffer 0xA09728
		// is allocated on first use), happiness += 1.0f clamped to 0..100 (0x6151F3..0x615239), then the decision 0x61523F (no return)
		// [EXT:tpi-capability-gates] thought 0x15 ("I pranked", held 120 turns by 0x6388F0 and 0x638960) is not shown: the bubbles are Theme Park World's (Think), which has no thought 0x15; it draws nothing
		ThemeParkIncPrankPosted?.Invoke( guest, kind );
		guest.Happiness = MeterMath.Add( guest.Happiness, 1f );
		return false;
	}

	/// <summary>
	/// The idle decision on <c>A &amp; 3</c> (<c>0x61523F</c>): 0 and 2 the major decision after a 30-turn rest, else the can't-decide pause; 1 the wander; 3 the two CRT draws towards an infodesk or
	/// another guest.
	/// </summary>
	// [BIN:TPI-EXE:0x61523F idle decision] the table 0x615434: 0 and 2 0x615256, 1 0x6152D4, 3 0x615318
	private void ThemeParkIncIdleDecision( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, uint a, uint turn )
	{
		switch ( a & 3 )
		{
			case 1:
				// [BIN:TPI-EXE:0x6152D4 wander] 0x609F70 nonzero: setter(7) and return; zero: +0x224 = the turn
				if ( ThemeParkIncWander( guest, tpi ) )
					SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Wandering );
				else
					tpi.Word224 = unchecked( (int)turn );
				return;
			case 3:
				// [BIN:TPI-EXE:0x615318 CRT draws] rand() (0x870D38) at least 0x3FFF (signed jge) returns; a second rand() at least 0x3FFF walks to another guest (0x6153B1: 0x619240's guest, its cell
				// through 0x60B7F0, no state change); both below: the infodesk 0x618B60 (+0x204, the record 0x5E8FE0, 0x60B7F0 to its +0x46, setter(0xC))
				// [EXT:tpi-staff-crt-rand] user-approved 2026-11-22, the reduction decided 2026-11-23: each rand() < 0x3FFF is world.Next(0x8000) < 0x3FFF, a world draw over rand's full range 0..0x7FFF
				if ( random.Next( 0x8000 ) >= 0x3FFF )
					return;
				// [EXT:tpi-capability-gates] the walk to another guest (0x619240, the guest finder) and the infodesk (0x618B60, state 12) are phase P5's: both are read as finding nothing; neither draws
				_ = random.Next( 0x8000 ) >= 0x3FFF;
				return;
			default:
				// [BIN:TPI-EXE:0x615256 major decision] the turn at most +0x224 + 30 (unsigned jbe) returns; else 0x610080, +0x204 nonzero sets 13 and returns; zero: the spot animation 4 with the umbrella
				// state 0 or 2, else 3 (0x60FB30), 0x614460(0) (SmallHappinessChange), +0x224 = the turn
				if ( turn <= unchecked( (uint)tpi.Word224 + 30 ) )
					return;
				// [BIN:TPI-EXE:0x615272 ride chosen] 0x610080 (ThemeParkIncMajorDecision, phase P3a), then +0x204 nonzero sets 13 and returns
				ThemeParkIncMajorDecision( guest, tpi, settings );
				if ( tpi.Target != 0 )
				{
					SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WalkingToMajor );
					return;
				}
				StartThemeParkIncSpotAnimation( guest, tpi,
					guest.UmbrellaState is UmbrellaRules.StateNone or UmbrellaRules.StateClosed ? ThemeParkIncCantDecideAnimation : ThemeParkIncCantDecideUmbrellaAnimation );
				guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 0 ) );
				tpi.Word224 = unchecked( (int)turn );
				return;
		}
	}

	/// <summary>
	/// The wander destination <c>0x609F70</c>: Theme Park World's wander (<c>[EXT:tpi-twp-guest-rules]</c>, tp.exe <c>0x004F9490</c>, as the staff's); a walk it sets raises the wander flag
	/// <c>+0xE8</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x609F70 wander] the stranded hold ("Confused", thought 0x11), the draws 0x60A010, 0x60A34C, 0x60A3DA, 0x60A3EA, 0x60A7A5 and 0x60A7BE (tp.exe 0x004F9490's), and +0xE8 = 1 after a
	// SetDest that succeeds (0x60A5D2, 0x60A8C1)
	private bool ThemeParkIncWander( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( !Wander( guest ) )
			return false;
		tpi.Wandered = true;
		return true;
	}

	/// <summary>The route request <c>0x60B8E0</c> to the target words <c>+0x28</c>, <c>+0x2A</c>: the walk's own target point, which every route request of a native guest writes.</summary>
	// [BIN:TPI-EXE:0x60B8E0 target words] +0x28 and +0x2A take the words before SetDest (0x60B92A, 0x60B94C), as the wander does before its own (0x60A597, 0x60A73A); the walk's target is the
	// words << 8 (GuestNavigation.TargetX, TargetY)
	private bool RouteToThemeParkIncTarget( Guest guest )
	{
		var navigation = guest.Navigation;
		return RouteToTpiPoint( guest, (navigation.TargetX >> 16, navigation.TargetY >> 16), (navigation.TargetX >> 8) & 0xFF, (navigation.TargetY >> 8) & 0xFF );
	}

	/// <summary>The litter drop <c>0x5D8C80</c> on the guest's cell record (the drop reseeds and draws twice, <see cref="ParkLitter.Drop"/>); a cell that holds litter keeps it.</summary>
	// [BIN:TPI-EXE:0x5D8C80 littering cell] nothing with [0xC65B80] == 1 or the record's +0x30 nonzero; the descriptor of type − 1 below 12 (the table 0x5D8EFC; 7 adds RegionFX row 2, 8 row 5 through
	// 0x5D74A0), the sprite 0x4E4810, the reseed 0x649430(cell word) and two draws, +0x34 = 0, +0x30 = type, and for 8 the particle 0x65BD10(0x32)
	private void DropThemeParkIncLitter( Guest guest, LitterType type )
	{
		var (x, y) = TpiCell( guest );
		if ( Litter?.Drop( x, y, type ) == true )
			Observer?.LitterDropped( guest );
	}

	// ---- The removal message ------------------------------------------------------------------------------------------------------

	/// <summary>
	/// An attraction thing is destroyed (message 0xA, the guest's handler <c>0x60D120</c>): every native guest whose target <c>+0x204</c> it is loses <c>SmallHappinessChange</c> and its target and
	/// becomes idle, whatever its state; every one forgets it in <c>+0x206</c> and in its histories.
	/// </summary>
	// [BIN:TPI-EXE:0x60D120 removal message] message 0xA with the object's id (+8): +0x204 equal: state 0x14 gets its model back and the sound 0x80, else a guest in the queue states (0x61AC40) leaves
	// the queue (0x619700(0)); then happiness += −byte [0xA01A78] (0x60D0D0), +0x204 = 0 and setter(6); then +0x206 equal is cleared, and each of the four words +0x20A..+0x210 equal is cleared with
	// the word 8 bytes on (+0x212..+0x218, the temporary history)
	// [EXT:tpi-twp-guest-rules] a guest of the bridged boarding (state 16 until phase P3b) leaves the removed ride by Theme Park World's removal (DropForRemovedAttraction: on the ride, the model back
	// and the sound 0x80 as for 0x14; on the way to the stand point, the medium step; then its own small step), and state 6 as below
	internal void ThemeParkIncObjectRemoved( int thingId )
	{
		if ( NativeTpi is not { } settings || thingId == 0 )
			return;
		var ride = TpiAttraction( thingId )?.Ride;
		foreach ( var guest in guests )
		{
			if ( guest.Tpi is not { } tpi )
				continue;
			if ( tpi.Target == thingId )
			{
				if ( tpi.State == ThemeParkIncGuestStates.BeingLoaded && ride != null )
					// Theme Park World's removal takes its own small step
					DropForRemovedAttraction( guest, ride );
				else
				{
					if ( ThemeParkIncGuestStates.InQueueOrLoading( tpi.State, tpi.Saved ) )
					{
						// [BIN:TPI-EXE:0x60D17F queued guest] 0x619700(0); the destroyed ride's list goes with it (the bridge lets the guest go here so that its teardown does not send it away twice)
						if ( ride != null )
							TpiRemoveFromQueue( ride, guest );
						TpiDismissFromQueue( guest, tpi, quietly: false );
					}
					guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( settings, 0 ) );
				}
				tpi.Target = 0;
				ClearTpiQueueMirror( guest );
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			}
			if ( tpi.Word206 == thingId )
				tpi.Word206 = 0;
			for ( var slot = 0; slot < Guest.HistorySlots; slot++ )
			{
				if ( guest.UsedHistory[slot] != thingId )
					continue;
				guest.UsedHistory[slot] = 0;
				guest.TemporaryHistory[slot] = 0;
			}
		}
	}

	// ---- The bin search ----------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The bin search <c>0x618D10</c>: over the object list's bins, the nearest (squared cells below the radius squared and below the best so far) whose <c>VAR_PRANKED</c> (10) and <c>VAR_ONFIRE</c>
	/// (5) are 0 and that the route request to its stand point reaches. Every closer bin makes its own route request, so a failed request after a found bin leaves the walk on the failed one. The bin's
	/// thing id, 0 for none.
	/// </summary>
	// [BIN:TPI-EXE:0x618D10 bin search] the list (world +0x1EAB88, next +0x14); +0x42 bit 0x40; |dx|² + |dy|² below radius² (unsigned jae) and below the best (from 0xFFFFFFFF); the stand point 0x5E1300
	// (before the variable tests); VAR 10 nonzero or VAR 5 nonzero skips; 0x60B8E0 to the point, success keeps the bin as the best
	private ushort ThemeParkIncBinSearch( Guest guest, int radius )
	{
		if ( ThemeParkIncWorld?.Bins() is not { Count: > 0 } bins )
			return 0;
		var (gx, gy) = TpiCell( guest );
		var limit = unchecked( (uint)(radius * radius) );
		var best = uint.MaxValue;
		ushort found = 0;
		for ( var index = 0; index < bins.Count; index++ )
		{
			var bin = bins[index];
			var dx = bin.Cell.X - gx;
			var dy = bin.Cell.Y - gy;
			var distance = (uint)(dx * dx + dy * dy);
			if ( distance >= limit || distance >= best )
				continue;
			var (byteX, byteY) = ThemeParkIncStandBytes( bin.StandX, bin.StandY, bin.Angle );
			if ( bin.Variable( 10 ) != 0 || bin.Variable( 5 ) != 0 )
				continue;
			if ( !RouteToTpiPoint( guest, ThemeParkIncGuestSettings.CellOf( bin.EntranceWord ), byteX, byteY ) )
				continue;
			best = distance;
			found = bin.ThingId;
		}
		return found;
	}

	/// <summary>
	/// The stand point's bytes in an object's entrance cell (<c>0x5E1300</c>): <c>UsageInfo.EntryCellStandPosX/Y</c> turned with the object (90: (y, 1 − x), 180: (1 − x, 1 − y), 270: (1 − y, x)), each
	/// times 255.0f at the game thread's 24-bit precision and truncated to a byte.
	/// </summary>
	// [BIN:TPI-EXE:0x5E1300 stand point] the type record (0x43C700 on +0x16): +0x184 and +0x188; the angle +0x18 (0, 0x5A, 0xB4, 0x10E; any other angle reads a stack word, which no placement gives);
	// 1.0f − a term by fsubs; fmuls 255.0f (0x93AF80) and _ftol, the low byte; the cell bytes ((+0x46 − 1) & 0x7F, (+0x46 − 1) >> 7) above them
	// [EXT:hardware-d3d-path] the game thread runs at 24-bit x87 precision: each subtraction and product rounds to a single, as C#'s float arithmetic does
	internal static (int X, int Y) ThemeParkIncStandBytes( float standX, float standY, int angle )
	{
		var (fx, fy) = angle switch
		{
			90 => (standY, 1f - standX),
			180 => (1f - standX, 1f - standY),
			270 => (1f - standY, standX),
			_ => (standX, standY)
		};
		return ((int)(fx * 255f) & 0xFF, (int)(fy * 255f) & 0xFF);
	}

	// ---- States 7 and 9 -------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 7 (<c>0x61A179</c>): the walk; arrived or stuck, one world draw whose low two bits must be 0 and a new wander destination keep the guest wandering (state 7 again), anything else makes it
	/// idle.
	/// </summary>
	// [BIN:TPI-EXE:0x61A179 state 7] 0x60B440: 0 or 2 go on, anything else returns; a draw with G & 3 nonzero, or 0x609F70 zero, go to 0x61A307 (setter(6)); else setter(7). Theme Park World's state 7
	// (0x00501ABB) is the same skeleton
	internal void UpdateTpiWandering( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiWalk( guest ) == WalkResult.Walking )
			return;
		if ( (random.NextWord() & 3) != 0 || !ThemeParkIncWander( guest, tpi ) )
		{
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Wandering );
	}

	/// <summary>
	/// State 9 (<c>0x616F10</c>): the walk to the bin. Arrived at a bin that is neither full (<c>VAR_CONTENTS</c> below <c>VAR_CAPACITY</c>) nor burning: the bin counts a customer and its
	/// <c>VAR_USE</c> becomes 1. Arrived anywhere else (no bin, a full or burning bin): one draw and litter on the cell. Stuck: the same draw and litter. Each end empties the urge and makes the guest
	/// idle.
	/// </summary>
	// [BIN:TPI-EXE:0x616F10 state 9] 0x60B440 nonzero: 2 goes to 0x61706E (draw E, type E % 5 + 1, 0x5D8C80 on the cell's record, +0x1D8 = 0, setter(6)), else return; arrived with +0x204 zero: the
	// drop 0x617017 (draw D, the same type and drop); with a bin: 0x5E5DA0 (the customer), VAR 3 (capacity), 4 (contents), 5 (on fire): contents at least capacity (signed jge) or on fire drop as
	// above; else VAR 0 = 1 (0x6B0780), 0x5E3990 (the bin's dirt test) and, when it holds, 0x5D74D0(8) and 0x5D74A0(9) on the bin's cell; +0x1D8 = 0 and setter(6) (0x617057)
	internal void UpdateTpiWalkingToBin( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var walk = TpiWalk( guest );
		if ( walk == WalkResult.Walking )
			return;
		if ( walk == WalkResult.Arrived && tpi.Target != 0 )
		{
			// a bin's destroy sends message 0xA first, and its handler 0x60D120 (ThemeParkIncObjectRemoved) clears every walker's target, so this branch is defensive: in the original a slot another
			// thing took since is a defined read of that thing, and an empty slot a null dereference
			// [EXT:original-undefined-reads] a target that no litter bin holds is read as a thing without a script and without a history: its variables read 0, so it counts as full
			var bin = ThemeParkIncWorld?.Thing( unchecked( (ushort)tpi.Target ) );
			if ( bin is { } found )
				ThemeParkIncWorld!.RecordCustomer( found.ThingId );
			var capacity = bin?.Variable( 3 ) ?? 0;
			var contents = bin?.Variable( 4 ) ?? 0;
			var onFire = bin?.Variable( 5 ) ?? 0;
			if ( bin is { } usable && contents < capacity && onFire == 0 )
			{
				usable.SetVariable( 0, 1 );
				// [BIN:TPI-EXE:0x5E3990 dirt test] a bin (+0x42 bit 0x40): VAR 4 above VAR 11 (signed jg) or VAR 5 nonzero
				// [EXT:tpi-no-maintenance-wear] the bins' fill and their RegionFX rows 8 (clean, from the constructor 0x5DB6DD) and 9 (dirty) are unported: a dirty bin's row change (0x5D74D0(8), 0x5D74A0(9))
				// is not made, and the constructor puts no row 8 down either
				guest.Litter = 0;
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
				return;
			}
		}
		// [BIN:TPI-EXE:0x617017 bin drop] draw D (or E for a stuck walk), type D % ([0x93E2BC] − 1) + 1
		DropThemeParkIncLitter( guest, (LitterType)(random.NextWord() % ParkLitter.OrdinaryKinds + 1) );
		guest.Litter = 0;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}
}
