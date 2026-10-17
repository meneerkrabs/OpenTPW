namespace OpenTPW;

/// <summary>
/// The swim sprite sets of the pools extension (<c>content/pools/esprites/Swim/SPR_xx.ESP</c> and <c>.TPC</c>, docs/POOLS.md): one set for each of the eight Theme Park World kid sets, holding the three
/// swim strokes in the animation slots 8 (front crawl), 9 (breaststroke) and 10 (treading water) with five directions, as <c>tools/blender/opentpw_guest_sprites.py</c> renders them (docs/CONTENT-KIT.md).
/// They are loaded beside the game's own kid atlases, not instead of them, so the swimmers are drawn without any sprite-replacement mod; the set at index n belongs to the guests of type n, like
/// the kid sets (both lists are in name order).
/// </summary>
// [EXT:pools-render] OpenTPW's own swimmer art; the original game has no pools
public static class PoolSwimSprites
{
	/// <summary>The folder of the swim sets below the pools content.</summary>
	public static string? Directory => PoolContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Swim" ) : null;

	/// <summary>Loads every swim set (sorted by name), or none when the folder or the content is missing.</summary>
	public static List<GuestSpriteAtlas> Load()
	{
		var sets = new List<GuestSpriteAtlas>();
		if ( Directory is not { } folder || !System.IO.Directory.Exists( folder ) )
			return sets;
		var files = System.IO.Directory.GetFiles( folder ).Select( Path.GetFileName ).OfType<string>().ToArray();
		foreach ( var esp in files.Where( file => file.EndsWith( ".ESP", StringComparison.OrdinalIgnoreCase ) ).OrderBy( file => file, StringComparer.OrdinalIgnoreCase ) )
		{
			try
			{
				var stem = Path.GetFileNameWithoutExtension( esp );
				SpriteAnimationFile animations;
				using ( var espStream = File.OpenRead( Path.Combine( folder, esp ) ) )
					animations = new SpriteAnimationFile( espStream );
				var bank = files.FirstOrDefault( file => string.Equals( file, stem + animations.BankExtension, StringComparison.OrdinalIgnoreCase ) );
				if ( bank == null )
					continue;
				using var bankStream = File.OpenRead( Path.Combine( folder, bank ) );
				sets.Add( new GuestSpriteAtlas( stem, new SpriteBankFile( bankStream ), animations, "swim", "pools" ) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log?.Warning( $"The swim sprite set {esp} is not available: {exception.Message}" );
			}
		}
		return sets;
	}
}
