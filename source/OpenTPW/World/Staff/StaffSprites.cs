namespace OpenTPW;

/// <summary>One staff animation: the original script number (<see cref="StaffScripts"/>) and the ESP slot it shows; the frames and their order come from the script.</summary>
/// <param name="Intro">An entertainer's performance program register 0x11 (the entry's third byte): frames 0 to Intro play once when the performance starts.</param>
/// <param name="Hold">The program's register 0x12 (the fourth byte): after the frames of each pass frame 0 holds Hold + 1 steps.</param>
public readonly record struct StaffClip( int Script, int Slot, int Intro = 0, int Hold = 0 );

/// <summary>The sprite banks of one role in a theme: a folder of <c>esprites.wad</c> and the banks the variant picks from.</summary>
public sealed record StaffBankSet( string Folder, IReadOnlyList<string> Banks );

/// <summary>
/// Which original sprites draw a staff member and which clip plays (tp.exe category table 0x763f88, body sprite 0x4d4140, scripts 0x75a418; docs/STAFF-PLACEMENT.md).
/// Pure rules: no assets, no GPU.
/// </summary>
public static class StaffSpriteRules
{
	// [BIN:TPW-EXE:0x4d4190 script table 0x75a418] each script is a list of (ESP slot, frames); the numbers are the table's indices
	public static readonly StaffClip Stand = new( 3, 0 );
	public static readonly StaffClip Walk = new( 1, 1 );
	public static readonly StaffClip Run = new( 2, 2 );
	/// <summary>Script 9: the walk at half rate (patrol, to a strike, a handyman to litter or a toilet).</summary>
	public static readonly StaffClip WalkHalf = new( 9, 1 );
	/// <summary>Script 10: researching, 4 frames then four steps of the first, looping.</summary>
	public static readonly StaffClip Research = new( 10, 4 );
	/// <summary>Script 0x11: sweeping and cleaning, 4 frames at half rate.</summary>
	public static readonly StaffClip Sweep = new( 0x11, 4 );
	/// <summary>Script 0x12: a mechanic repairing, an 8-frame loop.</summary>
	public static readonly StaffClip Repair = new( 0x12, 4 );
	/// <summary>Script 0x13: striking, 4 frames then six steps of the first, looping.</summary>
	public static readonly StaffClip Strike = new( 0x13, 5 );
	/// <summary>Script 0x14: state 6, a single frame of slot 3.</summary>
	// [BIN:TPW-EXE:0x5054D0 staff state 6] state 6 has no StaffJob, so no clip plays it in OpenTPW: SetState 0x5054D0 case 6 stamps the idle clock (+0x200) and plays script 0x14 (slot 3, one frame), and ModelState 0x5056E0 returns the member to state 0 after 3 x the role's rate (table 0x785330)
	// [BIN:TPW-EXE:0x5054D0 staff state 6] nothing in tp.exe writes 6: the SetState call sites pass 0, 1, 2, 3, 4, 5 or 7 or a role setter's parameter (0x4D46D0, 0x4D65D0, 0x4D7330, 0x4DA370, 0x502C20), whose callers pass 0, 1, 8-0xD, 0xF, 0x10, 0x12 or 0x13; the other stores of the state word +0x19C are those setters', the constructor's 0 (0x5185D8) and the entertainer's 0, 1 and 0xE; only the staff serializer 0x504DE0 can restore a 6 from a save, so the clip stays for a loaded save
	public static readonly StaffClip State6 = new( 0x14, 3 );
	/// <summary>Script 0x15: the dangling person of the hire and carry tools, slot 6 frames 0-3 looping (the TPC the game draws has the art; the FPC holds 2 x 2 pixel placeholders there).</summary>
	public static readonly StaffClip Dangle = new( 0x15, 6 );
	/// <summary>Script 0x16: a guard catching (slot 7 once) and then carrying the kid (slot 8 looping).</summary>
	public static readonly StaffClip CatchAndCarry = new( 0x16, 7 );

	/// <summary>
	/// Steps per second of a sprite that keeps the interval it was created with, 62 ms (the dangling cursor sprite and the hire window's figure). A walking member's interval comes from
	/// <see cref="SpritePlayer"/>.
	/// </summary>
	// [APPROX:GUEST-016] SpritePlayer ticks once per renderer call, one step past due, where tp.exe's scheduler (0x475360) runs on even 31 ms substeps; the walk step's own interval (0x4FA2A0) and the sub-turn point (0x4F9F00) are ported
	// [BIN:TPW-EXE:0x004FA2A0 walk step interval] the sprite's interval is the step's own length times 0x007006E8, doubled unless the thing is a guest or a guard with a speed bonus, as clamp(trunc(62 / v), 0, 250) (0x004D41D0)
	public const float FramesPerSecond = 1000f / SpritePlayer.DefaultInterval;

	/// <summary>The slot an entertainer performs when the bank lists no performance (every shipped entertainer bank lists slot 4).</summary>
	public const int DefaultPerformanceSlot = 4;

	/// <summary>The key a person's sprite bank derives from: the same for the candidate and the member it becomes.</summary>
	// [BIN:TPW-EXE:0x541f70 sprite variant draw] tp.exe draws a candidate's variant first in its constructor (0x507600 → 0x541f70: (world draw >> 2) mod the category's loaded bank count; candidate +9, member +0x20) and draws bank = the category's first bank + variant (0x542010): OpenTPW keeps the drawn variant on the candidate and its member (KeyOf)
	// [BIN:TPW-EXE:0x4f8b10 saved sprite variant] a member of an original save takes the saved mSpriteID modulo the role's banks (the serializer saves it and reduces it by the category's bank count on load); the name-and-id key below is only for a candidate or member of an OpenTPW park save from before the variant was kept (the loaded banks are GUEST-020)
	public static int VariantKey( int nameIndex, int id ) => (nameIndex + id) & int.MaxValue;

	/// <summary>The key a candidate's sprite bank derives from: its variant (the drawn or saved one), else the name-and-id key of a pool from before the variant was kept.</summary>
	public static int KeyOf( StaffCandidate candidate ) => candidate.Variant >= 0 ? candidate.Variant : VariantKey( candidate.NameIndex, candidate.Id );

	/// <summary>The key a member's sprite bank derives from: the variant it was hired with, else the name-and-id key (a member of an OpenTPW park save from before the variant was kept).</summary>
	public static int KeyOf( StaffMember member ) => member.Variant >= 0 ? member.Variant : VariantKey( member.NameIndex, member.Id );

	/// <summary>Index of a variant's bank among <paramref name="count"/> banks.</summary>
	public static int BankIndex( int variantKey, int count ) => count <= 0 ? -1 : variantKey % count;

	/// <summary>The <c>esprites.wad</c> theme folder of a level (<c>jungle</c>, <c>fantasy</c>, <c>hallow</c>, <c>space</c>), null for another level.</summary>
	public static string? ThemeFolder( string level ) => level.ToLowerInvariant() switch
	{
		"jungle" => "Jungle",
		"fantasy" => "Fantasy",
		"hallow" => "Hallow",
		"space" => "Space",
		_ => null
	};

	// [BIN:TPW-EXE:0x541310 staff bank load] the banks of a folder are sorted by file name (0x541210: a bubble sort over the name compare 0x424CC0, the case-insensitive _stricmp 0x67DF40, which GuestSpriteAtlas.LoadSets does) and the folders handymen, mechanics, guards and researchers (0x7640C8..0x7640A8) stop at the staff limit 0x41AA40; the entertainers' theme folders are not flagged and load whole; the load chain 0x54E360 -> 0x540900 -> 0x541D90 -> 0x541310 is reached from the program's start only (0x45A960, 0x5503F0), so one run keeps its banks (StaffSpriteSet.Load takes the limit once, when a level builds its staff)
	// [APPROX:GUEST-020] the staff bank limit follows the preset's GameOptions.NUMKIDS (the original's detail word 0x7858D0, read only by 0x41A9D0 and 0x41AA40) and not the hardware-class path of 0x41AA40 (the flags word 0x79D86C a configuration file sets, class 0x4000: 1 for the memory word 0x20, 2 for 8 or 0x10, 0 for any other), which has no counterpart here; the original's stored variant is drawn modulo the loaded count (0x541F70 divides by the entry +8 that 0x541E7D fills from 0x541310's result), OpenTPW's modulo the static folder count with the reduction at drawing only, so the simulation stays independent of graphics; and a park started after a settings change loads the limit its level start reads, where the original reads it at the program's start
	// [BIN:TPW-EXE:0x763f88 sprite categories] by thing type: handymen Generic\Handymen SPR_HA; mechanics Generic\Mechanics SPR_FA, SPR_OM; guards Generic\Guards SPR_GU; researchers Generic\Researchers SPR_RE; entertainers per theme: Jungle DI, EX, NA; Fantasy FL, GN, LA; Hallow DR, FK, HU; Space MO, SM, SP
	/// <summary>
	/// How many banks of a flagged staff folder (handymen, mechanics, guards, researchers) the game loads for a detail word <paramref name="numKids"/> (the preset's <c>GameOptions.NUMKIDS</c>): 1 for 0, else 2.
	/// </summary>
	// [BIN:TPW-EXE:0x41AA40 staff bank limit] the jump table 0x41AA84 holds 1 for the word 0 and 2 for 1, 2, 3 and anything above (low.sam 0, med.sam and high.sam 2); the kid sets' own limit 0x41A9D0 takes 2, 4, 6 and 8 from the same word
	public static int BankLimit( int numKids ) => numKids == 0 ? 1 : 2;

	/// <summary>The limit <paramref name="settings"/> give, or null for no limit: no settings, or a preset whose original file is missing (the data this limit belongs to is then missing too).</summary>
	public static int? BankLimitOf( IGraphicsSettings? settings ) =>
		settings != null && settings.GetPresetValues( settings.Current.Preset ) is { } detail ? BankLimit( detail.NumKids ) : null;

	/// <summary>Whether tp.exe flags a role's folder for the limit (all but the entertainers' theme folders, 0x541310).</summary>
	public static bool IsBankLimited( StaffType type ) => type != StaffType.Entertainer;

	/// <summary>The sprite folder and banks of a role; entertainers belong to the level's theme (null for an unknown level).</summary>
	public static StaffBankSet? Banks( StaffType type, string level )
	{
		switch ( type )
		{
			case StaffType.Handyman:
				return new StaffBankSet( "/esprites/Generic/Handymen", new[] { "SPR_HA" } );
			case StaffType.Mechanic:
				return new StaffBankSet( "/esprites/Generic/Mechanics", new[] { "SPR_FA", "SPR_OM" } );
			case StaffType.Guard:
				return new StaffBankSet( "/esprites/Generic/Guards", new[] { "SPR_GU" } );
			case StaffType.Researcher:
				return new StaffBankSet( "/esprites/Generic/Researchers", new[] { "SPR_RE" } );
			default:
				var theme = ThemeFolder( level );
				if ( theme == null )
					return null;
				return new StaffBankSet( $"/esprites/{theme}/Entertainers", theme switch
				{
					"Jungle" => new[] { "SPR_DI", "SPR_EX", "SPR_NA" },
					"Fantasy" => new[] { "SPR_FL", "SPR_GN", "SPR_LA" },
					"Hallow" => new[] { "SPR_DR", "SPR_FK", "SPR_HU" },
					_ => new[] { "SPR_MO", "SPR_SM", "SPR_SP" }
				} );
		}
	}

	/// <summary>
	/// The clip of a placed member (null: not drawn). Resting members have no sprite (state 3) and a carried one is the dangling sprite instead.
	/// <paramref name="moving"/> is whether the member is being moved now; <paramref name="performances"/> (the bank's performance entries,
	/// <see cref="SpriteAnimationFile.Performances"/>) give an entertainer's performance.
	/// </summary>
	// [BIN:TPW-EXE:0x004D46D0 performance start] the clip of a performing member is the script its search queued: 13 + (r mod (n − 1)) from the search's second world draw r (StaffMember.PerformanceDraw), 13 when the bank's n present entries (0x541FA0) is 1; scripts 13..16 of the table 0x75A418 hold the entry indices 0..3, and 0x475B80 runs the entry's program on its slot (StaffScripts.PerformanceFrame plays program 0 with the entry's intro and hold). An entertainer of an original save in state 14 has no saved draw: it plays entry 0 until its next performance (GUEST-015 retired)
	public static StaffClip? SelectClip( StaffMember member, bool moving, IReadOnlyList<SpritePerformance>? performances = null, int performanceCount = 0 )
	{
		// a resting member is inside the Staff Room (0x5054d0 state 3 removes the model); a carried one is the dangling sprite instead
		if ( !HasBodySprite( member ) )
			return null;
		if ( member.Job == StaffJob.ToStrike )
			return moving ? WalkHalf : Stand;
		if ( member.State == StaffState.OnStrike || member.Job == StaffJob.Striking )
			return Strike;
		switch ( member.Type )
		{
			case StaffType.Handyman:
				return member.Job switch
				{
					StaffJob.Sweeping or StaffJob.Cleaning => Sweep,
					StaffJob.Patrol or StaffJob.ToLitter or StaffJob.ToToilet => moving ? WalkHalf : Stand,
					_ => moving ? Walk : Stand
				};
			case StaffType.Mechanic:
				return member.BusyTicks > 0 && !member.AwaitingJobArrival && !moving ? Repair : moving ? Walk : Stand;
			case StaffType.Researcher:
				return member.Job == StaffJob.Researching && !moving ? Research : moving ? Walk : Stand;
			case StaffType.Guard:
				return member.Job == StaffJob.Escorting ? CatchAndCarry : moving ? (member.Job == StaffJob.Chasing ? Run : Walk) : Stand;
			default:
				return member.Job == StaffJob.Performing && !moving ? Perform( member.PerformanceDraw, performances, performanceCount ) : moving ? Walk : Stand;
		}
	}

	/// <summary>The body-existence exclusions shared by clip selection and read-only employee output.</summary>
	internal static bool HasBodySprite( StaffMember member ) =>
		member.State is not (StaffState.Resting or StaffState.PickedUp) && member.Job is not (StaffJob.Resting or StaffJob.PickedUp);

	/// <summary>
	/// An entertainer's performance clip: entry 0 when the bank has one (or none: <see cref="DefaultPerformanceSlot"/>), else entry
	/// <paramref name="draw"/> mod (count − 1), the original's script choice (the search's second draw, <see cref="StaffMember.PerformanceDraw"/>), its slot looping.
	/// </summary>
	// [BIN:TPW-EXE:0x004D47D8 performance script] n = the bank's count of present entries (0x541FA0): 13 when n is 1, else 13 + (draw mod (n - 1)), queued on the member's sprite (0x4217F0); a full bank of four never plays its fourth entry
	public static StaffClip Perform( uint draw, IReadOnlyList<SpritePerformance>? performances, int count )
	{
		if ( performances == null || count <= 0 || performances.Count == 0 )
			return new StaffClip( 13, DefaultPerformanceSlot );
		var entry = count == 1 ? 0 : (int)(draw % (uint)(count - 1));
		var slot = performances[Math.Min( entry, performances.Count - 1 )].Slot;
		return new StaffClip( 13 + entry, slot is >= 0 and < SpriteAnimationFile.SlotCount ? slot : DefaultPerformanceSlot, performances[Math.Min( entry, performances.Count - 1 )].Intro, performances[Math.Min( entry, performances.Count - 1 )].Hold );
	}

	/// <summary>The hire window's figure: the walk slot (ESP slot 1), frames 0..7 in turn, looping.</summary>
	public const int PortraitSlot = 1;
	/// <summary>The hire window's figure faces script direction 5 (of eight).</summary>
	public const int PortraitDirection = 5;
	/// <summary>The frames the hire window's script steps through.</summary>
	public const int PortraitFrames = 8;

	/// <summary>
	/// The hire window's figure <paramref name="elapsed"/> seconds after it was created (tp.exe <c>0x0049BA00</c> creates it through <c>0x00475A10</c>
	/// with the script <c>0x0074F758</c>): the walk slot, the frame of the script's step, and the stored direction and mirroring of script direction 5
	/// for a bank of <paramref name="directions"/> directions.
	/// </summary>
	// [BIN:TPW-EXE:0x0074F758 hire figure script] register 0x10 (the flags +0xC4) = 0x51000 (0x40000: a screen-space sprite, no camera turn), slot +0xB4 = 1 (0x004768B0), register 0xF (the direction +0xC0) = 5 (0x004766D0), then the frames 0..7 (0x00476940, one per script step) and back to the start (0x00476020)
	// [BIN:TPW-EXE:0x00542010 sprite draw] the direction is (+0xC0 + 0, a screen sprite taking no camera turn) & 7; above the bank's direction count − 1 it is mirrored and becomes 8 − direction
	public static (int Slot, int Direction, bool Mirror, int Step) Portrait( float elapsed, int directions ) => PortraitAtStep( (int)MathF.Max( 0, elapsed * FramesPerSecond ), directions );

	/// <summary>The slot, direction, mirroring and frame of the hire figure's <paramref name="steps"/>-th script frame (frame <c>steps</c> mod 8; the first FRAME of the script is step 0).</summary>
	public static (int Slot, int Direction, bool Mirror, int Step) PortraitAtStep( int steps, int directions )
	{
		var step = Math.Max( 0, steps ) % PortraitFrames;
		var direction = PortraitDirection;
		var mirror = direction > directions - 1;
		return (PortraitSlot, mirror ? 8 - direction : direction, mirror, step);
	}

	/// <summary>The slot and frame step of a clip <paramref name="elapsed"/> seconds after it started, at the interval a sprite is created with.</summary>
	public static (int Slot, int Step) Resolve( StaffClip clip, float elapsed, IReadOnlyList<SpriteAnimation> animations ) =>
		ResolveSteps( clip, (int)MathF.Max( 0, elapsed * FramesPerSecond ), animations );

	/// <summary>The slot and frame the clip shows at its <paramref name="steps"/>-th script step (the entertainers' performance scripts, which follow the bank, loop all frames of their slot).</summary>
	public static (int Slot, int Step) ResolveSteps( StaffClip clip, int steps, IReadOnlyList<SpriteAnimation> animations )
	{
		if ( StaffScripts.At( clip.Script, steps ) is { } shown )
			return shown;
		var count = clip.Slot >= 0 && clip.Slot < animations.Count && !animations[clip.Slot].IsEmpty ? animations[clip.Slot].FramesPerDirection : 1;
		return (clip.Slot, StaffScripts.PerformanceFrame( clip.Intro, clip.Hold, count, steps ));
	}
}

/// <summary>
/// The decoded sprite banks of the staff roles of one level, in the order their atlas textures are bound: handymen, mechanics, guards, researchers, the theme's
/// entertainers. Loaded through the same <see cref="GuestSpriteAtlas"/> path as the guests' kids.
/// </summary>
public sealed class StaffSpriteSet
{
	private const int RoleCount = 5;
	private readonly (int First, int Count)[] ranges = new (int, int)[RoleCount];

	/// <summary>The decoded banks; their atlas index is the texture slot the renderer binds.</summary>
	public IReadOnlyList<GuestSpriteAtlas> Atlases { get; }

	private StaffSpriteSet( IReadOnlyList<GuestSpriteAtlas> atlases, (int First, int Count)[] ranges )
	{
		Atlases = atlases;
		this.ranges = ranges;
	}

	public static StaffSpriteSet Empty { get; } = new( Array.Empty<GuestSpriteAtlas>(), new (int, int)[RoleCount] );

	/// <summary>Loads the banks of every role for a level; a role whose folder is missing from the game data has none.</summary>
	/// <param name="bankLimit">The banks a flagged folder loads (<see cref="StaffSpriteRules.BankLimit"/>); null loads them all.</param>
	public static StaffSpriteSet Load( string level, int? bankLimit = null )
	{
		var atlases = new List<GuestSpriteAtlas>();
		var ranges = new (int First, int Count)[RoleCount];
		foreach ( var type in new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Researcher, StaffType.Entertainer } )
		{
			if ( StaffSpriteRules.Banks( type, level ) is not { } set )
				continue;
			// [BIN:TPW-EXE:0x0046C730 staff tool enter] the hire and carry tools' sprite is script 0x15 (0x0074ED98, slot 6 frames 0-3 looping) of the role's sprite type (0x00475A10: mechanics 6, handymen 5, entertainers 4, guards 7, researchers 8; table 0x00763F88) with the candidate's variant (+0x11): the person's own bank
			var banks = GuestSpriteAtlas.LoadSets( set.Folder, $"staff-{type.ToString().ToLowerInvariant()}" );
			if ( bankLimit is { } limit && StaffSpriteRules.IsBankLimited( type ) && banks.Count > limit )
				banks.RemoveRange( limit, banks.Count - limit );
			ranges[(int)type] = (atlases.Count, banks.Count);
			atlases.AddRange( banks );
		}
		return new StaffSpriteSet( atlases, ranges );
	}

	/// <summary>The atlas index drawing a person of a role and variant, -1 when the role has no bank.</summary>
	public int AtlasFor( StaffType type, int variantKey )
	{
		var (first, count) = ranges[(int)type];
		return count == 0 ? -1 : first + StaffSpriteRules.BankIndex( variantKey, count );
	}
}
