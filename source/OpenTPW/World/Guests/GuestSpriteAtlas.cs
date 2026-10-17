namespace OpenTPW;

/// <summary>A frame's place in a <see cref="GuestSpriteAtlas"/> plus its hotspot offsets in source pixels.</summary>
public readonly record struct GuestAtlasFrame( int X, int Y, int Width, int Height, int OriginX, int OriginY );

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
	public const int AtlasWidth = 1024;
	private const int Padding = 2;

	public string Name { get; }
	public SpriteAnimationFile Animations { get; }
	public GuestAtlasFrame[] Frames { get; }
	public int Width { get; }
	public int Height { get; }
	/// <summary>RGBA8, row-major.</summary>
	public byte[] Pixels { get; }
	/// <summary>Key of this atlas in the optional texture pack (docs/TEXTURE-PACKS.md); not a file in the game data.</summary>
	public string PackKey { get; }

	/// <param name="packFolder">Folder of the set under <c>esprites/&lt;packRoot&gt;</c> for the texture pack key (<c>kids</c>, <c>kidsheads</c>, <c>costumes</c>, ...).</param>
	/// <param name="packRoot">The <c>esprites</c> folder the set lives in: <c>generic</c>, or a theme for the costumes.</param>
	public GuestSpriteAtlas( string name, SpriteBankFile bank, SpriteAnimationFile animations, string packFolder = "kids", string packRoot = "generic" )
	{
		Name = name;
		Animations = animations;
		PackKey = $"esprites/{packRoot.ToLowerInvariant()}/{packFolder}/{name.ToLowerInvariant()}.atlas";
		Frames = new GuestAtlasFrame[bank.Frames.Count];
		var x = Padding;
		var y = Padding;
		var rowHeight = 0;
		for ( var index = 0; index < bank.Frames.Count; index++ )
		{
			var frame = bank.Frames[index];
			if ( x + frame.Width + Padding > AtlasWidth )
			{
				x = Padding;
				y += rowHeight + Padding;
				rowHeight = 0;
			}
			Frames[index] = new GuestAtlasFrame( x, y, frame.Width, frame.Height, frame.OriginX, frame.OriginY );
			x += frame.Width + Padding;
			rowHeight = Math.Max( rowHeight, frame.Height );
		}
		Width = AtlasWidth;
		var needed = y + rowHeight + Padding;
		Height = 1;
		while ( Height < needed )
			Height *= 2;
		Pixels = new byte[Width * Height * 4];
		for ( var index = 0; index < bank.Frames.Count; index++ )
			bank.DecodeRgba( bank.Frames[index], Pixels, Width * 4, Frames[index].X, Frames[index].Y );
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

	/// <summary>Each set's ESP chooses its bank (<see cref="SpriteAnimationFile.BankExtension"/>): the FPC when it has one at full detail, else the TPC.</summary>
	internal static List<GuestSpriteAtlas> LoadSets( string directory, string packFolder, string packRoot = "generic" )
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
			sets.Add( new GuestSpriteAtlas( stem, new SpriteBankFile( bankStream ), animations, packFolder, packRoot ) );
		}
		return sets;
	}
}
