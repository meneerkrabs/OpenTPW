namespace OpenTPW;

/// <summary>
/// A frame's place in a <see cref="GuestSpriteAtlas"/> plus its hotspot offsets in source pixels and the frame's two pixels-per-unit fields
/// (<see cref="SpriteFrame.PixelsPerUnitX"/>, <see cref="SpriteFrame.PixelsPerUnitY"/>; 128 in every shipped frame).
/// </summary>
public readonly record struct GuestAtlasFrame( int X, int Y, int Width, int Height, int OriginX, int OriginY, ushort PixelsPerUnitX = GuestAtlasFrame.ReferencePixelsPerUnit, ushort PixelsPerUnitY = GuestAtlasFrame.ReferencePixelsPerUnit )
{
	/// <summary>The pixels per unit the world scale <see cref="GuestRenderer.UnitsPerPixel"/> is calibrated for: every shipped frame has it.</summary>
	public const ushort ReferencePixelsPerUnit = 128;

	/// <summary>Horizontal factor on the world size of a pixel: 128 / A (1 when the field is 0, which no shipped frame has; [EXT:zero-pixels-per-unit] the original would divide by it).</summary>
	public float ScaleX => PixelsPerUnitX == 0 ? 1f : ReferencePixelsPerUnit / (float)PixelsPerUnitX;
	/// <summary>Vertical factor on the world size of a pixel: 128 / B (1 when the field is 0).</summary>
	public float ScaleY => PixelsPerUnitY == 0 ? 1f : ReferencePixelsPerUnit / (float)PixelsPerUnitY;

	/// <summary>
	/// The quad's extents in world units across and up the camera plane from the hotspot. [BIN:TPW-EXE:0x005890E0 sprite placement] the original spans
	/// <c>originX / A · scale</c> to <c>(width + originX) / A · scale</c> (Y likewise with B), so a pixel is <paramref name="unitsPerPixel"/> · 128 / A wide
	/// and · 128 / B tall; with A = B = 128 the factors are exactly 1 and the result is the fixed-scale quad bit for bit.
	/// </summary>
	/// <param name="mirror">The frame is drawn mirrored: its horizontal span is reflected about the hotspot.</param>
	/// <param name="unitsPerPixel">World size of a pixel at 128 pixels per unit (times the sprite's own scale where it has one).</param>
	public (float X0, float X1, float Top, float Bottom) Extents( bool mirror, float unitsPerPixel )
	{
		var left = mirror ? -(OriginX + Width) : OriginX;
		var unitX = unitsPerPixel * ScaleX;
		var unitY = unitsPerPixel * ScaleY;
		return (left * unitX, (left + Width) * unitX, -OriginY * unitY, -(OriginY + Height) * unitY);
	}
}

/// <summary>
/// One original kid sprite set (<c>esprites.wad/Generic/Kids/SPR_xx.FPC</c> + <c>.ESP</c>) packed into an
/// RGBA atlas on the CPU. Animation slots used by OpenTPW (identified visually, not traced): 0 standing
/// (1 frame), 1 walking (8 frames), 2 running (8 frames), 14 idle/waiting (2 frames); every slot has 5
/// directions from "away from the camera" (0) through "facing right" (2) to "towards the camera" (4).
/// The other three directions are drawn mirrored.
/// </summary>
public sealed class GuestSpriteAtlas
{
	public const int StandSlot = 0;
	public const int WalkSlot = 1;
	public const int RunSlot = 2;
	public const int WaitSlot = 14;
	/// <summary>The width every shipped set is packed into.</summary>
	public const int AtlasWidth = 1024;
	/// <summary>The height up to which a set stays in a narrower atlas; a taller one doubles the width (to <see cref="MaximumWidth"/>) first. 2048 is also the size WebGL2 guarantees.</summary>
	public const int MaximumHeight = 2048;
	/// <summary>The widest and tallest atlas: a set that does not fit 4096 by 4096 is refused (<see cref="InvalidDataException"/>). A frame is at most <see cref="SpriteBankFile.MaximumDimension"/> wide, so the width always grows to fit it.</summary>
	public const int MaximumWidth = 4096;
	/// <summary>Pixels between frames (and the atlas edge): 2, or 4 for frames drawn at more than 128 pixels per unit, whose mip levels would otherwise bleed one frame into the next.</summary>
	private const int Padding = 2;
	private const int HighResolutionPadding = 4;

	public string Name { get; }
	public SpriteAnimationFile Animations { get; }
	public GuestAtlasFrame[] Frames { get; }
	public int Width { get; }
	public int Height { get; }
	/// <summary>Each frame and the bank (palette) it decodes from: the set's bank, or its <c>.TPC</c> for a slot taken from there (<see cref="LoadSets"/>).</summary>
	private readonly (SpriteBankFile Bank, SpriteFrame Frame)[] sources;
	private readonly object pixelsGate = new();
	private byte[]? pixels;
	/// <summary>
	/// RGBA8, row-major. Decoded from the bank on first use and kept until <see cref="ReleasePixels"/>: a renderer drops it after the GPU upload (the bank keeps one byte per
	/// pixel instead of four), and a texture-pack reload decodes it again.
	/// </summary>
	public byte[] Pixels
	{
		get
		{
			lock ( pixelsGate )
			{
				if ( pixels == null )
				{
					var decoded = new byte[Width * Height * 4];
					for ( var index = 0; index < sources.Length; index++ )
						sources[index].Bank.DecodeRgba( sources[index].Frame, decoded, Width * 4, Frames[index].X, Frames[index].Y );
					pixels = decoded;
				}
				return pixels;
			}
		}
	}
	/// <summary>Key of this atlas in the optional texture pack (docs/TEXTURE-PACKS.md); not a file in the game data.</summary>
	public string PackKey { get; }

	/// <summary>Forgets the decoded <see cref="Pixels"/> (they are decoded again on the next use).</summary>
	public void ReleasePixels()
	{
		lock ( pixelsGate )
			pixels = null;
	}

	/// <param name="packFolder">Folder of the set under <c>esprites/&lt;packRoot&gt;</c> for the texture pack key (<c>kids</c>, <c>kidsheads</c>, <c>costumes</c>, ...).</param>
	/// <param name="packRoot">The <c>esprites</c> folder the set lives in: <c>generic</c>, or a theme for the costumes.</param>
	public GuestSpriteAtlas( string name, SpriteBankFile bank, SpriteAnimationFile animations, string packFolder = "kids", string packRoot = "generic" )
		: this( name, bank, animations, null, -1, packFolder, packRoot )
	{
	}

	/// <param name="lowDetail">The set's <c>.TPC</c> bank when the frames of <paramref name="lowDetailSlot"/> are taken from it instead of <paramref name="bank"/>; null for none.</param>
	/// <param name="lowDetailSlot">The animation slot whose frames <paramref name="lowDetail"/> supplies (the frame numbers are the same in both banks of a set).</param>
	internal GuestSpriteAtlas( string name, SpriteBankFile bank, SpriteAnimationFile animations, SpriteBankFile? lowDetail, int lowDetailSlot, string packFolder = "kids", string packRoot = "generic" )
	{
		Name = name;
		Animations = animations;
		sources = bank.Frames.Select( frame => (bank, frame) ).ToArray();
		if ( lowDetail != null && lowDetailSlot >= 0 && lowDetailSlot < animations.Animations.Count && animations.Animations[lowDetailSlot] is { IsEmpty: false } slot )
		{
			var end = Math.Min( slot.FirstFrame + slot.FrameCount, Math.Min( sources.Length, lowDetail.Frames.Count ) );
			for ( var index = slot.FirstFrame; index < end; index++ )
				sources[index] = (lowDetail, lowDetail.Frames[index]);
		}
		PackKey = $"esprites/{packRoot.ToLowerInvariant()}/{packFolder}/{name.ToLowerInvariant()}.atlas";
		Frames = new GuestAtlasFrame[sources.Length];
		var padding = sources.Any( source => source.Frame.PixelsPerUnitX > GuestAtlasFrame.ReferencePixelsPerUnit || source.Frame.PixelsPerUnitY > GuestAtlasFrame.ReferencePixelsPerUnit ) ? HighResolutionPadding : Padding;
		// Every shipped set fits 1024 pixels across and at most 2048 down; a set drawn at twice the size or more is packed into a wider atlas rather than a very tall one,
		// and a frame wider than the atlas row makes the atlas wider too.
		var width = AtlasWidth;
		var widest = sources.Length == 0 ? 0 : sources.Max( source => source.Frame.Width );
		while ( width < widest + 2 * padding && width < MaximumWidth )
			width *= 2;
		int needed;
		while ( (needed = Pack( sources, Frames, width, padding )) > MaximumHeight && width < MaximumWidth )
			width *= 2;
		if ( needed > MaximumWidth || width < widest + 2 * padding )
			throw new InvalidDataException( $"Sprite set {name} does not fit a {MaximumWidth} x {MaximumWidth} atlas ({widest} px wide frames, {needed} rows at {width} across)." );
		Width = width;
		Height = 1;
		while ( Height < needed )
			Height *= 2;
	}

	/// <summary>Places the frames in rows of <paramref name="width"/> pixels and returns the height the rows need; fills <paramref name="frames"/>.</summary>
	private static int Pack( (SpriteBankFile Bank, SpriteFrame Frame)[] sources, GuestAtlasFrame[] frames, int width, int padding )
	{
		var x = padding;
		var y = padding;
		var rowHeight = 0;
		for ( var index = 0; index < sources.Length; index++ )
		{
			var frame = sources[index].Frame;
			if ( x + frame.Width + padding > width )
			{
				x = padding;
				y += rowHeight + padding;
				rowHeight = 0;
			}
			frames[index] = new GuestAtlasFrame( x, y, frame.Width, frame.Height, frame.OriginX, frame.OriginY, frame.PixelsPerUnitX, frame.PixelsPerUnitY );
			x += frame.Width + padding;
			rowHeight = Math.Max( rowHeight, frame.Height );
		}
		return y + rowHeight + padding;
	}

	/// <summary>Atlas frame for an animation slot, a sprite direction (0–4) and a step; falls back to slot 0.</summary>
	public int GetFrame( int slot, int direction, int step )
	{
		var animation = Animations.Animations[slot];
		if ( animation.IsEmpty )
			animation = Animations.Animations[StandSlot];
		// [EXT:pools-activities] a swim set has art for some slots only (the strokes): a slot without art falls back to the first slot that has some
		if ( animation.IsEmpty )
			animation = Animations.Animations.FirstOrDefault( candidate => !candidate.IsEmpty );
		if ( animation.IsEmpty )
			return 0;
		var frame = animation.GetFrame( direction, step );
		return Math.Clamp( frame, 0, Frames.Length - 1 );
	}

	/// <summary>[EXT:pools-render] Whether the set has any art for the animation slot (a swim set has it for the strokes, slots 8 to 10, until the swimwear slots are drawn).</summary>
	public bool HasArt( int slot ) => !Animations.Animations[slot].IsEmpty;

	/// <summary>
	/// [EXT:pools-render] The slot a set can draw for a wished slot: the slot itself when it has art, else treading water (10), the breaststroke (9) or the crawl (8), the swim slots every swim set has.
	/// A swimwear slot whose art a set does not have yet is drawn as the swimmer in the water rather than not at all.
	/// </summary>
	public int UsableSlot( int slot )
	{
		if ( HasArt( slot ) )
			return slot;
		foreach ( var fallback in new[] { 10, 9, 8 } )
			if ( HasArt( fallback ) )
				return fallback;
		return slot;
	}

	public int FramesPerDirection( int slot )
	{
		var animation = Animations.Animations[slot];
		return animation.IsEmpty ? 1 : animation.FramesPerDirection;
	}

	/// <summary>
	/// Sprite direction for a heading relative to the camera: angle 0 = walking away from the camera,
	/// +90° = towards screen right. Returns the stored direction (0–4) and whether to mirror it.
	/// </summary>
	public static (int Direction, bool Mirror) SelectDirection( float headingRight, float headingAway )
	{
		var angle = MathF.Atan2( headingRight, headingAway );
		var direction = (int)MathF.Round( MathF.Abs( angle ) / (MathF.PI / 4) );
		return (Math.Clamp( direction, 0, 4 ), angle < 0 && direction is > 0 and < 4);
	}

	/// <summary>Loads every kid set under <c>esprites/Generic/Kids</c> (sorted by name), or none when absent.</summary>
	public static List<GuestSpriteAtlas> LoadKids() => LoadSets( "/esprites/Generic/Kids", "kids" );

	/// <summary>
	/// Loads every head set under <c>esprites/Generic/Kidsheads</c>: each <c>SPR_xx.ESP</c> with its <c>SPR_xx.TPC</c> bank
	/// (the heads ship no <c>.FPC</c>), or none when absent. A rider's head is drawn from the set of its kid type.
	/// </summary>
	// [DATA:esprites.wad/Generic/Kidsheads/SPR_xx.ESP+TPC] heads are 8 frames × 7 directions (docs/GUESTS.md); the 5-direction selection of kid sets is reused
	public static List<GuestSpriteAtlas> LoadKidHeads() => LoadSets( "/esprites/Generic/Kidsheads", "kidsheads" );

	/// <summary>
	/// Loads the theme's costume sets (<c>esprites/&lt;theme&gt;/Costumes</c>, one per theme), drawn for guests in costume (sprite category 2, tp.exe
	/// <c>0x00541F70</c>), or none when absent. They share the kids' animation layout.
	/// </summary>
	// [DATA:esprites.wad/<theme>/Costumes/SPR_xx.ESP+FPC] Jungle SPR_TI, Fantasy SPR_BK, Hallow SPR_HK (175 frames like the kids), Space SPR_SK (135 frames, its own slot table)
	public static List<GuestSpriteAtlas> LoadCostumes( string theme ) => LoadSets( $"/esprites/{theme}/Costumes", "costumes", theme );

	/// <summary>Loads the theme's costume heads (<c>esprites/&lt;theme&gt;/Costumeheads</c>: Jungle and Space only), or none.</summary>
	// [DATA:esprites.wad/<theme>/Costumeheads/SPR_xx.ESP+TPC] 8 frames × 7 directions like the kid heads; Fantasy and Hallow ship none
	public static List<GuestSpriteAtlas> LoadCostumeHeads( string theme ) => LoadSets( $"/esprites/{theme}/Costumeheads", "costumeheads", theme );

	/// <summary>Loads the balloon set (<c>esprites/Generic/Balloons/SPR_BL</c>, sprite category 10), or none.</summary>
	// [DATA:esprites.wad/Generic/Balloons/SPR_BL.ESP+TPC] 8 frames: four colours (slots 0 red, 1 green, 2 blue, 3 yellow), each the whole balloon then its burst; no directions
	public static List<GuestSpriteAtlas> LoadBalloons() => LoadSets( "/esprites/Generic/Balloons", "balloons" );

	/// <summary>Loads the thought bubbles (<c>esprites/Generic/Thoughts</c>: <c>SPR_TB</c> then <c>SPR_TC</c>, sprite category 9), or none.</summary>
	// [DATA:esprites.wad/Generic/Thoughts/SPR_TB.ESP+TPC,SPR_TC.ESP+TPC] TB: 16 bubbles, slot n is frame n; TC: 10 frames, its slots 0-9 show frames 0, 1, 8, 9, 7, 2, 3, 4, 5, 6 (the staff's bubbles, a thumb up and four arrows)
	public static List<GuestSpriteAtlas> LoadThoughts() => LoadSets( "/esprites/Generic/Thoughts", "thoughts" );

	/// <summary>Loads the litter set (<c>esprites/Generic/Litter/SPR_LI</c>, sprite category 11), or none.</summary>
	// [DATA:esprites.wad/Generic/Litter/SPR_LI.ESP+TPC] 8 frames, no directions: slots 0-4 the ordinary kinds (frames 0-4), slot 5 frame 5 the fallback, slot 6 frame 7 and slot 7 frame 6 (the 34 x 21 puddle) the two special kinds
	public static List<GuestSpriteAtlas> LoadLitter() => LoadSets( "/esprites/Generic/Litter", "litter" );

	/// <summary>Loads the special-effect set (<c>esprites/Generic/SpecialFX/SPR_SF</c>, sprite category 12: the upgrade balloon), or none.</summary>
	// [DATA:esprites.wad/Generic/SpecialFX/SPR_SF.ESP+TPC] one 32 x 39 frame (slot 0) whose hotspot lies 85 pixels below its top edge: a blue balloon with a yellow up arrow
	public static List<GuestSpriteAtlas> LoadSpecialEffects() => LoadSets( "/esprites/Generic/SpecialFX", "specialfx" );

	/// <summary>
	/// Each set's ESP chooses its bank (<see cref="SpriteAnimationFile.BankExtension"/>): the FPC when it has one at full detail, else the TPC. With <paramref name="lowDetailSlot"/>
	/// the frames of that slot come from the set's TPC whatever the ESP chooses: the staff tools' dangling sprite, which the FPC banks hold as 2 x 2 pixel placeholders.
	/// </summary>
	// [APPROX:GUEST-022] tp.exe draws the .tpc of every set (the detail flag its ESP loader 0x540D90 tests is never set, SpriteAnimationFile.FlagA); OpenTPW draws the .fpc of the sets with byte 0x10C = 1, and the .tpc only for a slot asked for here
	internal static List<GuestSpriteAtlas> LoadSets( string directory, string packFolder, string packRoot = "generic", int lowDetailSlot = -1 )
	{
		var sets = new List<GuestSpriteAtlas>();
		string[] files;
		try
		{
			files = FileSystem.GetFiles( directory ).Select( file => Path.GetFileName( file ) ).ToArray();
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or NullReferenceException )
		{
			return sets;
		}
		foreach ( var esp in files.Where( file => file.EndsWith( ".ESP", StringComparison.OrdinalIgnoreCase ) ).OrderBy( file => file, StringComparer.OrdinalIgnoreCase ) )
		{
			var stem = Path.GetFileNameWithoutExtension( esp );
			SpriteAnimationFile animations;
			using ( var espStream = FileSystem.OpenRead( $"{directory}/{esp}" ) )
				animations = new SpriteAnimationFile( espStream );
			var bank = files.FirstOrDefault( file => string.Equals( file, stem + animations.BankExtension, StringComparison.OrdinalIgnoreCase ) );
			if ( bank == null )
				continue;
			using var bankStream = FileSystem.OpenRead( $"{directory}/{bank}" );
			try
			{
				sets.Add( new GuestSpriteAtlas( stem, new SpriteBankFile( bankStream ), animations, LowDetailBank( directory, files, stem, bank, lowDetailSlot ), lowDetailSlot, packFolder, packRoot ) );
			}
			catch ( InvalidDataException exception ) when ( exception.Message.StartsWith( "Sprite set ", StringComparison.Ordinal ) )
			{
				Log?.Warning( $"The sprite set {stem} in {directory} is skipped: {exception.Message}" );
			}
		}
		return sets;
	}

	/// <summary>The set's TPC when a slot is to be taken from it and the chosen bank is another one; null when there is none or it does not decode.</summary>
	private static SpriteBankFile? LowDetailBank( string directory, string[] files, string stem, string chosen, int slot )
	{
		if ( slot < 0 || chosen.EndsWith( ".TPC", StringComparison.OrdinalIgnoreCase )
			|| files.FirstOrDefault( file => string.Equals( file, stem + ".TPC", StringComparison.OrdinalIgnoreCase ) ) is not { } tpc )
			return null;
		try
		{
			using var stream = FileSystem.OpenRead( $"{directory}/{tpc}" );
			return new SpriteBankFile( stream );
		}
		catch ( InvalidDataException exception )
		{
			Log?.Warning( $"The low-detail bank {tpc} in {directory} is not used: {exception.Message}" );
			return null;
		}
	}
}
