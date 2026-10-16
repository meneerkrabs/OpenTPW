namespace OpenTPW;

/// <summary>How a staff animation slot plays (the original's script entries, table 0x75a418).</summary>
public enum StaffPlayback
{
	/// <summary>All frames, looping.</summary>
	Loop,
	/// <summary>All frames at half the frame rate, looping (the patrol walk, the sweep).</summary>
	HalfRateLoop,
	/// <summary>The frames once, then the last one held (researching, striking).</summary>
	OnceThenHold,
	/// <summary>The first frame only (state 6).</summary>
	Single,
	/// <summary>The slot once, then <see cref="StaffClip.ThenSlot"/> looping (the guard's catch, then carrying the kid).</summary>
	OnceThenLoop
}

/// <summary>One staff animation: an ESP slot, how it plays and the original script number it stands for.</summary>
/// <param name="MaxFrames">Frames of the slot used (the dangling cursor sprite loops frames 0-3 of a slot that may hold more); 0 = all.</param>
public readonly record struct StaffClip( int Script, int Slot, StaffPlayback Playback, int ThenSlot = -1, int MaxFrames = 0 );

/// <summary>The sprite banks of one role in a theme: a folder of <c>esprites.wad</c> and the banks the variant picks from.</summary>
public sealed record StaffBankSet( string Folder, IReadOnlyList<string> Banks );

/// <summary>
/// Which original sprites draw a staff member and which clip plays (tp.exe category table 0x763f88, body sprite 0x4d4140, scripts 0x75a418; docs/STAFF-PLACEMENT.md).
/// Pure rules: no assets, no GPU.
/// </summary>
public static class StaffSpriteRules
{
	// [BIN:TPW-EXE:0x4d4190 script table 0x75a418] each script is a list of (ESP slot, frames); the numbers are the table's indices
	public static readonly StaffClip Stand = new( 3, 0, StaffPlayback.Single );
	public static readonly StaffClip Walk = new( 1, 1, StaffPlayback.Loop );
	public static readonly StaffClip Run = new( 2, 2, StaffPlayback.Loop );
	/// <summary>Script 9: the walk at half rate (patrol, to a strike, a handyman to litter or a toilet).</summary>
	public static readonly StaffClip WalkHalf = new( 9, 1, StaffPlayback.HalfRateLoop );
	/// <summary>Script 10: researching, 4 frames then hold.</summary>
	public static readonly StaffClip Research = new( 10, 4, StaffPlayback.OnceThenHold, MaxFrames: 4 );
	/// <summary>Script 0x11: sweeping and cleaning, 4 frames at half rate.</summary>
	public static readonly StaffClip Sweep = new( 0x11, 4, StaffPlayback.HalfRateLoop, MaxFrames: 4 );
	/// <summary>Script 0x12: a mechanic repairing, an 8-frame loop.</summary>
	public static readonly StaffClip Repair = new( 0x12, 4, StaffPlayback.Loop, MaxFrames: 8 );
	/// <summary>Script 0x13: striking, 4 frames then hold.</summary>
	public static readonly StaffClip Strike = new( 0x13, 5, StaffPlayback.OnceThenHold, MaxFrames: 4 );
	/// <summary>Script 0x14: state 6, a single frame of slot 3.</summary>
	// [APPROX:ECON-052] the original's staff state 6 has no StaffJob, so no clip plays it: SetState 0x5054D0 case 6 stamps the idle clock (+0x200) and plays script 0x14 (slot 3, one frame), and ModelState 0x5056E0 returns the member to state 0 after 3 × the role's rate (table 0x785330); no direct SetState or role-setter call in tp.exe passes 6 and only the staff serializer 0x504DE0 restores it from a save, so it looks unreachable in play — evidence needed: a writer of state 6 outside the direct calls (a struct copy or another store), or its absence
	public static readonly StaffClip State6 = new( 0x14, 3, StaffPlayback.Single );
	/// <summary>Script 0x15: the dangling cursor sprite, slot 6 frames 0-3 looping.</summary>
	public static readonly StaffClip Dangle = new( 0x15, 6, StaffPlayback.Loop, MaxFrames: 4 );
	/// <summary>Script 0x16: a guard catching (slot 7 once) and then carrying the kid (slot 8 looping).</summary>
	public static readonly StaffClip CatchAndCarry = new( 0x16, 7, StaffPlayback.OnceThenLoop, ThenSlot: 8 );

	/// <summary>Frames per second of a full-rate clip.</summary>
	// [APPROX:GUEST-016] the staff clips' frame rate (10 per second, half rate 5), the 'moving' test (a position change within the last 0.6 s, 1.3 s for members the world moves a cell per second) and the smoothing of the drawn position replace tp.exe's timing: the sprite player 0x475360 runs one script step when the millisecond clock passes the sprite's due time +0x7c and adds its interval +0x80 (62 ms at creation, 0x4758f0), and the movement step 0x4fa2a0 sets the heading from the position change, picks the walk or run clip and, through 0x4d41d0 and 0x4d4190, sets the interval to clamp(trunc(62 / v), 0, 250) ms once per turn (v = step length × 3.829657e-05, doubled for the walk clip) — evidence needed: the frames a script step draws and the step length per turn
	public const float FramesPerSecond = 10f;

	private static readonly int[] PerformanceSlots = { 4, 10, 3, 11 };

	/// <summary>The key a person's sprite bank derives from: the same for the candidate and the member it becomes.</summary>
	// [APPROX:GUEST-014] tp.exe draws a candidate's variant first in its constructor (0x507600 → 0x541f70: (world draw >> 2) mod the category's loaded bank count; candidate +9, member +0x20) and draws bank = the category's first bank + variant (0x542010); OpenTPW makes the variant draw in its place (ParkStaff) but keeps no variant: the bank is (NameIndex + Id) modulo the folder's banks, in file order, and every bank of the folder is loaded where 0x541310 stops a flagged category at the limit of 0x41aa40 (1 or 2 by the graphics setting 0x7858d0) — evidence needed: which categories 0x541310 flags and the bank order within a folder
	public static int VariantKey( int nameIndex, int id ) => (nameIndex + id) & int.MaxValue;

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

	// [BIN:TPW-EXE:0x763f88 sprite categories] by thing type: handymen Generic\Handymen SPR_HA; mechanics Generic\Mechanics SPR_FA, SPR_OM; guards Generic\Guards SPR_GU; researchers Generic\Researchers SPR_RE; entertainers per theme: Jungle DI, EX, NA; Fantasy FL, GN, LA; Hallow DR, FK, HU; Space MO, SM, SP
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
	/// <paramref name="moving"/> is whether the member is being moved now; <paramref name="animations"/> (the bank's slots) lets an entertainer pick a
	/// performance the bank has.
	/// </summary>
	// [APPROX:GUEST-015] tp.exe (0x4d46d0) queues script 13 + (r mod (n − 1)) from a second world draw r, or 13 when n is 1, n being the bank's present performance entries (0x541fa0); scripts 13..16 of the table 0x75a418 hold the entry indices 0..3, so a full bank never plays its fourth entry: OpenTPW discards that draw and picks, per member, one of the bank's multi-frame slots 4, 10, 3, 11 while it performs (state 14) — evidence needed: the shipped banks' performance entries (0x540d90) and how 0x475b80 plays an entry
	public static StaffClip? SelectClip( StaffMember member, bool moving, IReadOnlyList<SpriteAnimation>? animations = null )
	{
		// a resting member is inside the Staff Room (0x5054d0 state 3 removes the model); a carried one is the dangling sprite instead
		if ( member.State is StaffState.Resting or StaffState.PickedUp || member.Job is StaffJob.Resting or StaffJob.PickedUp )
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
				return member.Job == StaffJob.Performing && !moving ? Perform( member.Id, animations ) : moving ? Walk : Stand;
		}
	}

	/// <summary>An entertainer's performance clip: one of the bank's multi-frame performance slots, chosen by the member (slot 4 when the bank lists none).</summary>
	public static StaffClip Perform( int seed, IReadOnlyList<SpriteAnimation>? animations )
	{
		var slot = 4;
		if ( animations != null )
		{
			var candidates = 0;
			foreach ( var candidate in PerformanceSlots )
				if ( candidate < animations.Count && animations[candidate].FramesPerDirection > 1 )
					candidates++;
			if ( candidates > 0 )
			{
				var pick = (seed & int.MaxValue) % candidates;
				foreach ( var candidate in PerformanceSlots )
					if ( candidate < animations.Count && animations[candidate].FramesPerDirection > 1 && pick-- == 0 )
					{
						slot = candidate;
						break;
					}
			}
		}
		return new StaffClip( 13, slot, StaffPlayback.Loop );
	}

	/// <summary>The slot and frame step of a clip <paramref name="elapsed"/> seconds after it started.</summary>
	public static (int Slot, int Step) Resolve( StaffClip clip, float elapsed, IReadOnlyList<SpriteAnimation> animations )
	{
		var slot = clip.Slot;
		var frames = FrameCount( clip, slot, animations );
		var ticks = (int)MathF.Max( 0, elapsed * FramesPerSecond );
		switch ( clip.Playback )
		{
			case StaffPlayback.Single:
				return (slot, 0);
			case StaffPlayback.HalfRateLoop:
				return (slot, ticks / 2 % frames);
			case StaffPlayback.OnceThenHold:
				return (slot, Math.Min( ticks, frames - 1 ));
			case StaffPlayback.OnceThenLoop:
				if ( ticks < frames )
					return (slot, ticks);
				return (clip.ThenSlot, (ticks - frames) % FrameCount( clip, clip.ThenSlot, animations ));
			default:
				return (slot, ticks % frames);
		}
	}

	private static int FrameCount( StaffClip clip, int slot, IReadOnlyList<SpriteAnimation> animations )
	{
		var count = slot >= 0 && slot < animations.Count && !animations[slot].IsEmpty ? animations[slot].FramesPerDirection : 1;
		return Math.Max( 1, clip.MaxFrames > 0 && slot == clip.Slot ? Math.Min( count, clip.MaxFrames ) : count );
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
	public static StaffSpriteSet Load( string level )
	{
		var atlases = new List<GuestSpriteAtlas>();
		var ranges = new (int First, int Count)[RoleCount];
		foreach ( var type in new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Researcher, StaffType.Entertainer } )
		{
			if ( StaffSpriteRules.Banks( type, level ) is not { } set )
				continue;
			var banks = GuestSpriteAtlas.LoadSets( set.Folder, ".FPC", $"staff-{type.ToString().ToLowerInvariant()}" );
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
