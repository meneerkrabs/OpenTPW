namespace OpenTPW;

/// <summary>
/// The pools extension's own guest sprite sets (<c>content/pools/esprites/Generic/Kids</c> and <c>Kidsheads</c>, docs/POOLS.md): the eight Theme Park World kid sets and their heads redrawn at twice
/// the pixel size (pixels per unit 256, so a guest keeps its world size with twice the detail). While the pools extension and the guest art extension (<see cref="FeatureGates.PoolGuestArt"/>) are on they replace the game's kid and kid-head atlases in a park, together: both folders
/// must hold exactly the eight sets, else the game's own are used. They also stay out of a park whose game kid sets are not the Theme Park World ones (Theme Park Inc has four, SPR_BD, SPR_GB, SPR_GK
/// and SPR_KB, so a <c>Type % 8</c> mapping would be wrong), and out of a browser park that loads before they are downloaded (<see cref="PoolSpriteDelivery"/>: the browser build does not embed them).
/// <para>
/// Known limitation: the sets are read from the extension's folder with System.IO, not through the game's virtual file system, so an <c>overrides</c> mod of <c>/esprites/Generic/Kids*</c> and a texture
/// pack's upscaled kid atlases (found under another key, <c>esprites/pools/kids/...</c>) do not apply to them while the extension is on.
/// </para>
/// </summary>
// [EXT:pools-guest-art] OpenTPW's own guest art for parks with the pools extension; the original game's guests are untouched without it
public static class PoolGuestSprites
{
	/// <summary>The sets a replacement has to hold: one for each of the eight kid types.</summary>
	public const int KidSets = 8;
	/// <summary>The names of the Theme Park World kid sets in the order of the guest types, which the replacement sets repeat.</summary>
	public static readonly IReadOnlyList<string> KidNames = new[] { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };

	/// <summary>The folder of the replacement kid sets below the pools content.</summary>
	public static string? KidsDirectory => PoolContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Generic", "Kids" ) : null;
	/// <summary>The folder of the replacement kid head sets below the pools content.</summary>
	public static string? KidHeadsDirectory => PoolContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Generic", "Kidsheads" ) : null;

	/// <summary>The kid and head sets that replace the game's, loaded together.</summary>
	public sealed record Replacement( List<GuestSpriteAtlas> Kids, List<GuestSpriteAtlas> Heads );

	/// <summary>
	/// The replacement kid and head sets while the pools extension and the <c>opentpw.pools-guest-art</c> extension (on by default, a presentation switch) are on, the game's kid sets <paramref name="gameKids"/> are the Theme Park World ones and both folders are complete; null (use the game's) otherwise.
	/// </summary>
	public static Replacement? LoadReplacement( IReadOnlyList<GuestSpriteAtlas> gameKids )
	{
		if ( !FeatureGates.Pools || !FeatureGates.PoolGuestArt || !Matches( gameKids ) )
			return null;
		// in the browser the sets are fetched in the background (PoolSpriteDelivery): a park that loads before every file is in keeps the game's guests
		if ( !PoolSpriteDelivery.Ready( PoolSpriteDelivery.LandGroup, "this park keeps the game's own guests" ) )
			return null;
		var kids = LoadFolder( KidsDirectory, "kids" );
		var heads = LoadFolder( KidHeadsDirectory, "kidsheads" );
		if ( !Matches( kids ) || !Matches( heads ) )
			return null;
		Log?.Trace( "Pools: the kid and head sprites of this park are the extension's own 2x sets (esprites/Generic/Kids and Kidsheads of content/pools), not the game's." );
		return new Replacement( kids, heads );
	}

	private static bool Matches( IReadOnlyList<GuestSpriteAtlas> sets ) => sets.Count == KidSets && sets.Select( set => set.Name ).SequenceEqual( KidNames, StringComparer.OrdinalIgnoreCase );

	/// <summary>Loads the <c>.ESP</c> sets of a folder with the bank each selects (sorted by name); the sets that fail are skipped with a warning. Empty when the folder is missing.</summary>
	internal static List<GuestSpriteAtlas> LoadFolder( string? folder, string packFolder, string packRoot = "pools" )
	{
		var sets = new List<GuestSpriteAtlas>();
		if ( folder == null || !System.IO.Directory.Exists( folder ) )
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
				sets.Add( new GuestSpriteAtlas( stem, new SpriteBankFile( bankStream ), animations, packFolder, packRoot ) );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log?.Warning( $"The pools sprite set {esp} is not available: {exception.Message}" );
			}
		}
		return sets;
	}
}
