using System.Security.Cryptography;
using OpenTPW.Mods;

namespace OpenTPW;

/// <summary>
/// Trusted asset manifest for park resume: the SHA-256 of every original-data file a resumed park's
/// simulation consumes, keyed by a canonical root-prefixed identifier. The save path captures this
/// beside the snapshot; the load path runs the same <see cref="Capture"/> against the freshly loaded
/// (trusted) catalog and balance settings and compares the two dictionaries — ordinal and
/// order-independent — before any candidate park is constructed. Manifest keys from a snapshot are
/// never used as file paths: paths come only from the trusted loaded catalog/settings, so a doctored
/// manifest cannot make the loader read anywhere.
/// </summary>
/// <remarks>
/// Scope: every <c>.sam</c> (settings, shape), <c>.rse</c> (script) and <c>.MD2</c> (model, animation,
/// rider-head geometry) member of every <see cref="ObjectCatalogEntry"/> archive — the full pinned
/// catalog set, so a park still resumes identically when it later places an object that was only
/// catalogued at save time — plus the category default <c>.sam</c> under each object file, the balance
/// layers <see cref="BalanceSettings"/> read (<c>/levels/Standard.sam</c> and the theme's
/// <c>Standard.sam</c> — which is all <see cref="GuestSettings"/> ever reads, whatever the difficulty —
/// the theme's <c>Easy_Standard.sam</c> in easy mode, <c>/Challenges.sam</c> and the theme's
/// <c>global.sam</c> when present), the advisor settings when present, and the theme's terrain
/// <c>base.map</c>/<c>base.MD2</c> (the heightfield placement and guest paths run on; Theme Park Inc's
/// <c>dlake.MD2</c> joins when present). Out of scope by design: textures and other presentation media
/// (guest sprites, sky, music, sounds, hoardings, fonts, <c>OBJECT_NAMES</c> strings), enhanced/custom
/// texture packs, and original <c>TPWS</c>/<c>TPWI</c> saves — changing them must not invalidate a park.
/// Easy_/Online_ overlays inside an archive are pinned with it: replacing any bytes of a trusted archive
/// invalidates, even where the offline game would not have layered the file.
/// </remarks>
internal static class ParkResumeAssetManifest
{
	/// <summary>Upper bound for manifest entries (defensive; a real theme pins a few hundred).</summary>
	public const int MaximumEntries = 8192;
	/// <summary>Upper bound for the length of one key.</summary>
	public const int MaximumKeyLength = 512;

	// Root identity prefixes (ContentRoot.KeyPrefix: data:, bonus:, mod:<id>:) keep members of the official bonus
	// content and of mods (their own file-system roots) from colliding with game-data members of the same relative path.
	private static readonly string DataRootPrefix = ContentRoot.Data.KeyPrefix;

	// The category default .sam layered under every object file of a category. Mirrors ObjectCatalog's
	// category directories (note <c>sideshow</c> → <c>SideShow.sam</c>); keep in sync with it.
	private static readonly (string Directory, string Defaults)[] CategoryDefaults =
	{
		("rides", "Rides.sam"),
		("shops", "Shops.sam"),
		("sideshow", "SideShow.sam"),
		("features", "Features.sam"),
		("upgrades", "Upgrades.sam")
	};

	/// <summary>
	/// Captures the asset manifest of a park from its trusted loaded catalog and balance settings. Keys are
	/// root-prefixed data-relative identifiers (<c>data:/levels/jungle/rides/totem/totem.sam</c>,
	/// <c>bonus:/…</c> for official bonus archives, <c>mod:&lt;id&gt;:/…</c> for a mod's), enumeration is ordinal-sorted, and values are lowercase
	/// hex SHA-256 of the member's content — WAD members are hashed through <see cref="BaseFileSystem.OpenRead"/>,
	/// i.e. decompressed, so re-packing an archive does not change a hash. Files absent from the root are absent
	/// from the result; comparing against the saved manifest reports that as a difference. Read-only.
	/// </summary>
	/// <exception cref="InvalidDataException">A key exceeds <see cref="MaximumKeyLength"/>, the manifest would
	/// exceed <see cref="MaximumEntries"/>, or one source produces duplicate keys (the trusted root is inconsistent).</exception>
	public static IReadOnlyDictionary<string, string> Capture( ObjectCatalog catalog, BalanceSettings settings )
	{
		ArgumentNullException.ThrowIfNull( catalog );
		ArgumentNullException.ThrowIfNull( settings );
		return Capture(catalog, settings.Theme, settings.IsEasy);
	}

	/// <summary>Trusted asset identity without constructing economy rules (the experimental TPI layout path).</summary>
	internal static IReadOnlyDictionary<string, string> Capture(ObjectCatalog catalog, string theme, bool easy)
	{
		var manifest = new SortedDictionary<string, string>( StringComparer.Ordinal );
		foreach ( var _ in CaptureSteps( catalog, theme, easy, manifest ) ) { }
		return manifest;
	}

	/// <summary>
	/// <see cref="Capture(ObjectCatalog, string, bool)"/> as steps, one catalogue archive, then the theme's settings files together, then each terrain file per step, for a caller that hashes between frames
	/// (a park about to be entered, docs/BROWSER-PERFORMANCE.md "Event hitches"): fills <paramref name="manifest"/>, which is complete once the steps have run out.
	/// </summary>
	internal static IEnumerable<bool> CaptureSteps(ObjectCatalog catalog, string theme, bool easy, SortedDictionary<string, string> manifest)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		// The full pinned catalog set, not the currently surviving objects: a resumed park can still place
		// anything its catalogue offered at save time.
		foreach ( var entry in catalog.Entries.OrderBy( entry => entry.ArchivePath, StringComparer.Ordinal ) )
		{
			var prefix = entry.Root.KeyPrefix;
			foreach ( var member in SimulationMembers( entry.FileSystem, entry.ArchivePath ) )
				Add( manifest, prefix + member, Hash( entry.FileSystem, member ) );
			yield return true;
		}

		// The category default .sam under each present category's object files. Bonus archives read these from
		// the game data root too, so they are keyed there once per category instead of per entry.
		foreach ( var (directory, defaults) in CategoryDefaults )
		{
			// A mod's object archive sits in its own root, but takes the same category defaults from the game data.
			if ( !catalog.Entries.Any( entry => IsInCategoryDirectory( entry.ArchivePath, theme, directory ) || (entry.Root.IsMod && ModObjectsFile.CategoryName( entry.Category ) == directory) ) )
				continue;
			AddWhenPresent( manifest, $"/levels/{theme}/{directory}/{defaults}" );
		}

		// The balance layers exactly as BalanceSettings.Load read them.
		foreach ( var layer in BalanceSettings.GetStandardLayers( theme, easy ) )
			AddWhenPresent( manifest, layer );
		AddWhenPresent( manifest, "/Challenges.sam" );
		AddWhenPresent( manifest, $"/levels/{theme}/global.sam" );
		// Advisor scoring settings (null advisor when absent — a park without them resumes without them).
		AddWhenPresent( manifest, "/Advisor/Advisor.sam" );
		// The terrain the placement grid and guest paths run on. The original data keeps these as members
		// of terrain.wad, which FileExists cannot see — OriginalPark lists the terrain directory instead,
		// and so does this (a root without a terrain directory pins none of them).
		yield return true;
		foreach ( var file in TerrainFiles( theme ) )
		{
			Add( manifest, DataRootPrefix + file, Hash( FileSystem, file ) );
			yield return true;
		}
	}

	private static void AddWhenPresent( SortedDictionary<string, string> manifest, string path )
	{
		if ( FileSystem.FileExists( path ) )
			Add( manifest, DataRootPrefix + path, Hash( FileSystem, path ) );
	}

	/// <summary>
	/// The theme's terrain base map, heightfield model and (Theme Park Inc) lake model as data-relative
	/// paths, in that fixed order — whichever the terrain directory lists; empty when it does not exist.
	/// </summary>
	private static IEnumerable<string> TerrainFiles( string theme )
	{
		string[] listed;
		try
		{
			listed = FileSystem.GetFiles( $"/levels/{theme}/terrain" );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return Array.Empty<string>();
		}
		var names = new[] { "base.map", "base.MD2", OriginalPark.LakeModelName };
		return names.Select( name => listed.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) ) )
			.Where( file => file != null )
			.Select( file => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( file! ) ) )
			.ToArray();
	}

	/// <summary>The archive's simulation-relevant members, ordinal-sorted and data-relative (no recursion: object files sit at the archive root).</summary>
	private static IEnumerable<string> SimulationMembers( BaseFileSystem fileSystem, string archivePath )
	{
		return fileSystem.GetFiles( archivePath )
			.Select( member => fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( member ) ) )
			.Where( IsSimulationMember )
			.OrderBy( member => member, StringComparer.Ordinal )
			.ToArray();
	}

	private static bool IsSimulationMember( string path )
	{
		var extension = Path.GetExtension( path );
		return extension.Equals( ".sam", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".rse", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".md2", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>Whether the archive sits directly in the theme's category directory (<c>/levels/&lt;theme&gt;/&lt;category&gt;/&lt;name&gt;</c>).</summary>
	private static bool IsInCategoryDirectory( string archivePath, string theme, string directory )
	{
		var prefix = $"/levels/{theme}/{directory}/";
		return archivePath.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && archivePath.IndexOf( '/', prefix.Length ) < 0;
	}

	/// <summary>Lowercase hex SHA-256 of the member's (decompressed) content, read through the file system it belongs to.</summary>
	private static string Hash( BaseFileSystem fileSystem, string relativePath )
	{
		using var stream = fileSystem.OpenRead( relativePath );
		if ( stream == null )
			throw new InvalidDataException( $"Asset '{relativePath}' is listed in its archive but could not be opened." );
		return Convert.ToHexStringLower( SHA256.HashData( stream ) );
	}

	private static void Add( SortedDictionary<string, string> manifest, string key, string hash )
	{
		if ( key.Length > MaximumKeyLength )
			throw new InvalidDataException( $"Asset key '{key[..Math.Min( key.Length, 80 )]}…' is longer than {MaximumKeyLength} characters." );
		if ( manifest.ContainsKey( key ) )
			throw new InvalidDataException( $"Asset '{key}' produces more than one manifest key." );
		if ( manifest.Count >= MaximumEntries )
			throw new InvalidDataException( $"The asset manifest exceeds {MaximumEntries} entries." );
		manifest.Add( key, hash );
	}
}
