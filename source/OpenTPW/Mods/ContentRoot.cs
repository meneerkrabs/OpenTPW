namespace OpenTPW.Mods;

/// <summary>Where an object archive or asset comes from: the game data, the official bonus content or a mod.</summary>
public enum ContentRootKind { Data, Bonus, Mod }

/// <summary>
/// A content root (docs/MODS.md): the game data, the official bonus content or one mod. Its key prefix keeps members of
/// different roots with the same relative path apart in a park save's asset manifest (<c>data:/levels/…</c>,
/// <c>bonus:/levels/…</c>, <c>mod:&lt;id&gt;:/levels/…</c>). The default value is the game data.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public readonly record struct ContentRoot( ContentRootKind Kind, string? ModId = null )
{
	public const string DataPrefix = "data:";
	public const string BonusPrefix = "bonus:";
	public const string ModPrefix = "mod:";

	/// <summary>The game data (the default root).</summary>
	public static ContentRoot Data => default;

	/// <summary>The official bonus content (<c>_name_N</c> archives).</summary>
	public static ContentRoot Bonus => new( ContentRootKind.Bonus );

	/// <summary>The files of the mod with this id.</summary>
	public static ContentRoot Mod( string id ) =>
		ModManifest.IsValidId( id ) ? new ContentRoot( ContentRootKind.Mod, id ) : throw new ArgumentException( $"'{id}' is not a mod id.", nameof( id ) );

	public bool IsData => Kind == ContentRootKind.Data;
	public bool IsBonus => Kind == ContentRootKind.Bonus;
	public bool IsMod => Kind == ContentRootKind.Mod;

	/// <summary>The prefix of this root's asset-manifest keys: <c>data:</c>, <c>bonus:</c> or <c>mod:&lt;id&gt;:</c>.</summary>
	public string KeyPrefix => Kind switch
	{
		ContentRootKind.Data => DataPrefix,
		ContentRootKind.Bonus => BonusPrefix,
		ContentRootKind.Mod when ModId != null => ModPrefix + ModId + ":",
		_ => throw new InvalidOperationException( "A mod content root needs a mod id." ),
	};

	/// <summary>The asset-manifest key of a root-relative path such as <c>/levels/jungle/rides/totem/totem.sam</c>.</summary>
	public string Key( string path ) => KeyPrefix + path;

	/// <summary>
	/// Splits an asset-manifest key into its root and root-relative path. Only the three prefixes are known; for a mod
	/// root the id must be a valid mod id and the path must start with '/'.
	/// </summary>
	public static bool TryParseKey( string? key, out ContentRoot root, out string path )
	{
		root = default;
		path = "";
		if ( key == null )
			return false;
		if ( key.StartsWith( DataPrefix, StringComparison.Ordinal ) )
		{
			path = key[DataPrefix.Length..];
			return true;
		}
		if ( key.StartsWith( BonusPrefix, StringComparison.Ordinal ) )
		{
			(root, path) = (Bonus, key[BonusPrefix.Length..]);
			return true;
		}
		if ( !key.StartsWith( ModPrefix, StringComparison.Ordinal ) )
			return false;
		var end = key.IndexOf( ':', ModPrefix.Length );
		if ( end < 0 || !ModManifest.IsValidId( key[ModPrefix.Length..end] ) || end + 1 >= key.Length || key[end + 1] != '/' )
			return false;
		(root, path) = (new ContentRoot( ContentRootKind.Mod, key[ModPrefix.Length..end] ), key[(end + 1)..]);
		return true;
	}

	public override string ToString() => Kind switch
	{
		ContentRootKind.Mod => "mod " + ModId,
		ContentRootKind.Bonus => "bonus content",
		_ => "game data",
	};
}
