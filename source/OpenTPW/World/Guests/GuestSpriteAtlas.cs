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
	public string PackKey => $"esprites/generic/kids/{Name.ToLowerInvariant()}.atlas";

	public GuestSpriteAtlas( string name, SpriteBankFile bank, SpriteAnimationFile animations )
	{
		Name = name;
		Animations = animations;
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
	public static List<GuestSpriteAtlas> LoadKids()
	{
		const string directory = "/esprites/Generic/Kids";
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
			var bank = files.FirstOrDefault( file => string.Equals( file, stem + ".FPC", StringComparison.OrdinalIgnoreCase ) );
			if ( bank == null )
				continue;
			using var espStream = FileSystem.OpenRead( $"{directory}/{esp}" );
			using var bankStream = FileSystem.OpenRead( $"{directory}/{bank}" );
			sets.Add( new GuestSpriteAtlas( stem, new SpriteBankFile( bankStream ), new SpriteAnimationFile( espStream ) ) );
		}
		return sets;
	}
}
