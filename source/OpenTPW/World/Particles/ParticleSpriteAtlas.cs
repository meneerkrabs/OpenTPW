namespace OpenTPW;

/// <summary>A particle sprite frame's place in the atlas (UVs in 0..1) and its size and hotspot in source pixels.</summary>
public readonly record struct ParticleAtlasFrame( float U0, float V0, float U1, float V1, int Width, int Height, int OriginX, int OriginY );

/// <summary>
/// The <c>particles</c> sprite set (<c>esprites.wad/Generic/Particles</c>: SPR_PA and SPR_PB, each an ESP animation table and
/// a TPC frame bank) packed into one RGBA atlas, plus a white block for untextured particles. A particle's sprite code is
/// bank * 16 + animation (PA = bank 0, PB = bank 1), its frame an index into that animation.
/// </summary>
// [BIN:STP-PPC:0x1006AC2C particles sprite set] the sprite code is bank*16 + animation of the `particles` set (medium confidence)
// [DATA:esprites.wad/Generic/Particles/SPR_PA.ESP] the template frame counts match the animation lengths: code 11 and 12 are 16 frames, 5 is 14, 3 is 8, 13 is 6, 14 is 9, 15 is 1
public sealed class ParticleSpriteAtlas
{
	public const string SpriteDirectory = "/esprites/Generic/Particles";
	public const int AtlasWidth = 1024;
	private const int Padding = 2;
	private const int WhiteSize = 4;

	private readonly (SpriteAnimationFile Animations, ParticleAtlasFrame[] Frames)[] banks;

	public int Width { get; }
	public int Height { get; }
	/// <summary>RGBA8, row-major. Empty after <see cref="ReleasePixels"/>: a renderer drops it once the texture is uploaded (a park's and the lobby's renderer each held their own copy for as long as they lived).</summary>
	public byte[] Pixels { get; private set; }

	/// <summary>Forgets the CPU copy of the pixels after the GPU upload; the frames' positions stay.</summary>
	public void ReleasePixels() => Pixels = Array.Empty<byte>();
	/// <summary>An opaque white block, the frame of untextured particles.</summary>
	public ParticleAtlasFrame White { get; }

	public ParticleSpriteAtlas( IReadOnlyList<(SpriteBankFile Bank, SpriteAnimationFile Animations)> sets )
	{
		banks = new (SpriteAnimationFile, ParticleAtlasFrame[])[sets.Count];
		var placements = new List<(int Set, int Frame, int X, int Y)>();
		var x = Padding;
		var y = Padding;
		var rowHeight = WhiteSize;
		var white = (X: x, Y: y);
		x += WhiteSize + Padding;
		for ( var set = 0; set < sets.Count; set++ )
		{
			var frames = sets[set].Bank.Frames;
			for ( var index = 0; index < frames.Count; index++ )
			{
				var frame = frames[index];
				if ( x + frame.Width + Padding > AtlasWidth )
				{
					x = Padding;
					y += rowHeight + Padding;
					rowHeight = 0;
				}
				placements.Add( (set, index, x, y) );
				x += frame.Width + Padding;
				rowHeight = Math.Max( rowHeight, frame.Height );
			}
		}
		Width = AtlasWidth;
		var needed = y + rowHeight + Padding;
		Height = 1;
		while ( Height < needed )
			Height *= 2;
		Pixels = new byte[Width * Height * 4];
		for ( var row = 0; row < WhiteSize; row++ )
			Array.Fill( Pixels, (byte)255, ((white.Y + row) * Width + white.X) * 4, WhiteSize * 4 );
		// the UVs sit inside the block, half a texel from its edge
		White = new ParticleAtlasFrame( (white.X + 1f) / Width, (white.Y + 1f) / Height, (white.X + WhiteSize - 1f) / Width, (white.Y + WhiteSize - 1f) / Height, WhiteSize, WhiteSize, -WhiteSize / 2, -WhiteSize / 2 );
		for ( var set = 0; set < sets.Count; set++ )
			banks[set] = (sets[set].Animations, new ParticleAtlasFrame[sets[set].Bank.Frames.Count]);
		foreach ( var (set, index, px, py) in placements )
		{
			var bank = sets[set].Bank;
			var frame = bank.Frames[index];
			bank.DecodeRgba( frame, Pixels, Width * 4, px, py );
			banks[set].Frames[index] = new ParticleAtlasFrame( (float)px / Width, (float)py / Height, (float)(px + frame.Width) / Width, (float)(py + frame.Height) / Height,
				frame.Width, frame.Height, frame.OriginX, frame.OriginY );
		}
	}

	/// <summary>
	/// The atlas frame of a sprite code and frame number, or null for a code outside the loaded sets or an empty animation.
	/// </summary>
	public ParticleAtlasFrame? Find( int spriteCode, int frame )
	{
		var bank = spriteCode >> 4;
		var slot = spriteCode & 15;
		if ( spriteCode < 0 || bank >= banks.Length )
			return null;
		var (animations, frames) = banks[bank];
		var animation = animations.Animations[slot];
		if ( animation.IsEmpty )
			return null;
		var index = animation.GetFrame( 0, Math.Max( 0, frame ) );
		return index >= 0 && index < frames.Length ? frames[index] : null;
	}

	/// <summary>Loads SPR_PA and SPR_PB through the game file system, or null when the set is missing.</summary>
	public static ParticleSpriteAtlas? Load()
	{
		var sets = new List<(SpriteBankFile, SpriteAnimationFile)>();
		try
		{
			foreach ( var name in new[] { "SPR_PA", "SPR_PB" } )
			{
				using var espStream = FileSystem.OpenRead( $"{SpriteDirectory}/{name}.ESP" );
				using var bankStream = FileSystem.OpenRead( $"{SpriteDirectory}/{name}.TPC" );
				sets.Add( (new SpriteBankFile( bankStream ), new SpriteAnimationFile( espStream )) );
			}
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidDataException or NullReferenceException )
		{
			Log.Warning( $"The particle sprites are not available ({exception.Message}); particles are not drawn." );
			return null;
		}
		return new ParticleSpriteAtlas( sets );
	}
}
