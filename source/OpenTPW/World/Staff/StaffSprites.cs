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
	/// Steps per second of a sprite that keeps the interval it was created with, 62 ms: the hire window's figure, whose passes run on the host's renderer-call clock (UI-082 qualifies that). The staff members'
	/// and the tools' sprites are sources stepped by the sprite scheduler (<see cref="StaffSpriteState"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004758F0 sprite interval] a sprite is created with the interval 62 ms (+0x80 = 0x3E) and the first due time 62 ms later; a walk step's own interval (0x004FA2A0, 0x004D41D0) is the clamp(trunc(62 / v), 0, 250) of its length times 0x007006E8
	public const float FramesPerSecond = 1000f / SpritePlayer.DefaultInterval;

	/// <summary>The slot an entertainer performs when the bank lists no performance (every shipped entertainer bank lists slot 4).</summary>
	public const int DefaultPerformanceSlot = 4;

	/// <summary>The key a person's sprite bank derives from: the same for the candidate and the member it becomes.</summary>
	// [BIN:TPW-EXE:0x541f70 sprite variant draw] tp.exe draws a candidate's variant first in its constructor (0x507600 → 0x541f70: (world draw >> 2) mod the category's loaded bank count; candidate +9, member +0x20) and draws bank = the category's first bank + variant (0x542010): OpenTPW keeps the drawn variant on the candidate and its member (KeyOf)
	// [BIN:TPW-EXE:0x4f8b10 saved sprite variant] a member of an original save takes the saved mSpriteID modulo the role's banks (the serializer saves it and reduces it by the category's bank count on load); the name-and-id key below is only for a candidate or member of an OpenTPW park save from before the variant was kept (the stored draw is reduced by the loaded count, StaffMember.SpriteDraw)
	public static int VariantKey( int nameIndex, int id ) => (nameIndex + id) & int.MaxValue;

	/// <summary>The key a candidate's sprite bank derives from: its variant (the drawn or saved one), else the name-and-id key of a pool from before the variant was kept.</summary>
	public static int KeyOf( StaffCandidate candidate ) => candidate.SpriteDraw is >= 0 and var draw ? draw : candidate.Variant >= 0 ? candidate.Variant : VariantKey( candidate.NameIndex, candidate.Id );

	/// <summary>The key a member's sprite bank derives from: the variant it was hired with, else the name-and-id key (a member of an OpenTPW park save from before the variant was kept).</summary>
	public static int KeyOf( StaffMember member ) => member.SpriteDraw >= 0 ? member.SpriteDraw : member.Variant >= 0 ? member.Variant : VariantKey( member.NameIndex, member.Id );

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
	// [APPROX:GUEST-020] the original loads the banks inside the sprite system's start (0x540900: the main loop 0x54E360 calls it at 0x54E688 and 0x54ED2C), so the limit is the detail word 0x7858D0 of that moment; OpenTPW's StaffSpriteSet.Load reads the preset when a level builds its staff, so a park started after a settings change takes the new limit
	// [BIN:TPW-EXE:0x541F70 stored variant] the stored variant is (R >> 2) mod the category's LOADED count (0x541F70 divides by the entry +8 that 0x541E7D fills from 0x541310's result) and is reduced by the loaded count again at every load of a member (the person serializer 0x4F8B10 at 0x4F93AA; the candidates are copied raw, 0x4D6BED to 0x4D6C17): StaffMember.SpriteDraw and StaffCandidate.SpriteDraw, kept out of the hash like the guests' kid-set draw so peers with other presets hash alike
	// [EXT:no-launcher-machine-class] 0x41AA40 first tests the hardware class (0x44A590: the flags word 0x79D86C, bit 0x4000) and then the memory word (0x44A5D0, which only ever returns 8, 0x10 or 0x20: the staff limit is 1 for 0x20 and 2 for 8 or 0x10; the kid sets' limit 0x41A9D0 has the same path: 4 for 0x20, 6 for 0x10, 8 for 8); no code sets those bits (the writers 0x44D93E to 0x44E322 set 0x200, 0x400, 0x80, 0x40 and 1, 2, 4 only): the word is the launcher file dialog.tcf's, copied whole from the blob's +0x7C (0x449E78, from 0x78D954); bit 0x4000 only skips the class-preset load 0x44DC60 (0x44E1BF), and the software renderer path inside 0x44DD50 is chosen by bit 0x40 (0x44DDC0), not by 0x4000; OpenTPW always follows the graphics preset, where the original's launcher machine class (bit 0x4000 with memory sub-classes) depends on the player's PC (8, 6, 4 or 0 banks through 0x41AA40)
	// [BIN:TPW-EXE:0x763f88 sprite categories] by thing type: handymen Generic\Handymen SPR_HA; mechanics Generic\Mechanics SPR_FA, SPR_OM; guards Generic\Guards SPR_GU; researchers Generic\Researchers SPR_RE; entertainers per theme: Jungle DI, EX, NA; Fantasy FL, GN, LA; Hallow DR, FK, HU; Space MO, SM, SP
	/// <summary>
	/// How many banks of a flagged staff folder (handymen, mechanics, guards, researchers) the game loads for a detail word <paramref name="numKids"/> (the preset's <c>GameOptions.NUMKIDS</c>): 1 for 0, else 2.
	/// </summary>
	// [BIN:TPW-EXE:0x41AA40 staff bank limit] the jump table 0x41AA84 holds 1 for the word 0 and 2 for 1, 2, 3 and anything above (low.sam 0, med.sam and high.sam 2); the kid sets' own limit 0x41A9D0 takes 2, 4, 6 and 8 from the same word
	public static int BankLimit( int numKids ) => numKids == 0 ? 1 : 2;

	/// <summary>The limit <paramref name="settings"/> give, or null for no limit: no settings, or a preset whose original file is missing (the data this limit belongs to is then missing too).</summary>
	public static int? BankLimitOf( IGraphicsSettings? settings ) =>
		settings != null && settings.GetPresetValues( settings.Current.Preset ) != null ? BankLimit( settings.Detail.NumKids ) : null;

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

	/// <summary>The body-existence exclusions: a resting member is inside the Staff Room (state 3 deletes the source) and a carried one is the dangling sprite instead (state 7 deletes it).</summary>
	internal static bool HasBodySprite( StaffMember member ) =>
		member.State is not (StaffState.Resting or StaffState.PickedUp) && member.Job is not (StaffJob.Resting or StaffJob.PickedUp);

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

	/// <summary>
	/// The slot and frame the clip's script shows at its <paramref name="steps"/>-th step: the flattened frame formula the bounded sprite VM's programs are checked against (the tests); the game plays the VM
	/// (<see cref="StaffSpriteVm"/>, <see cref="StaffSpriteState"/>).
	/// </summary>
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
public sealed class StaffSpriteSet : IStaffSpriteBanks
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

	/// <summary>The animation file of the bank a person of a role and variant is drawn from, null when the role has none.</summary>
	public SpriteAnimationFile? AnimationsOf( StaffType type, int variantKey ) => AtlasFor( type, variantKey ) is >= 0 and var index ? Atlases[index].Animations : null;

	/// <summary>How many banks the role's folder loaded (after the staff limit), 0 for none.</summary>
	public int BankCount( StaffType type ) => ranges[(int)type].Count;

	/// <summary>The atlas index drawing a person of a role and variant, -1 when the role has no bank.</summary>
	public int AtlasFor( StaffType type, int variantKey )
	{
		var (first, count) = ranges[(int)type];
		return count == 0 ? -1 : first + StaffSpriteRules.BankIndex( variantKey, count );
	}
}
