using System.Text;

namespace OpenTPW.Mods;

/// <summary>
/// Makes the content of the run's enabled mods (docs/MODS.md) visible to the game, once at startup:
/// <list type="bullet">
/// <item><b>overrides</b>: loose files below a mod's <c>overrides</c> folder replace the game file at the same path (textures, sounds,
/// UI art, any loose data file), through <see cref="BaseFileSystem.Overrides"/> of the game's file system;</item>
/// <item><b>strings</b>: <c>strings/&lt;Language&gt;/&lt;NAME&gt;.str</c> replaces that language's string table
/// (<see cref="GameLanguage.StringOverride"/>), and is also an override of <c>/Language/&lt;Language&gt;/&lt;NAME&gt;.str</c>;</item>
/// <item><b>objects</b>: <c>objects.json</c> adds object archives (the mod's own, or from an input such as another original game's install) to
/// the theme catalogs as <see cref="CatalogContentRoot"/> entries.</item>
/// </list>
/// Mods are consulted in <see cref="ModSet"/> order, later mods over earlier ones; two mods that supply the same file were already refused by
/// <see cref="ModSet.Resolve"/> unless one is ordered after the other, so the last one is the one that declared <c>after</c>, never a silent winner.
/// With no mods enabled nothing is installed and every lookup is the game's.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public static class ModContentRoots
{
	/// <summary>
	/// Installs the mods' content hooks for the whole process (the game file system, the string tables and the object catalogs), or
	/// removes them when <paramref name="set"/> has no files-based mods. <paramref name="trace"/> gets one line about what was installed.
	/// </summary>
	public static void Activate( ModSet set, Action<string> trace )
	{
		Install( set, FileSystemOrNull() );
		if ( set.Mods.Count > 0 )
			trace( $"Mod content: {Describe( set )}." );
	}

	/// <summary>Installs the hooks of <paramref name="set"/> on <paramref name="fileSystem"/> until the returned scope is disposed (tests).</summary>
	internal static IDisposable Use( ModSet set, BaseFileSystem? fileSystem )
	{
		var previousFileSystem = fileSystem?.Overrides;
		var previousStrings = GameLanguage.StringOverride;
		var (previousRoots, previousKey) = ObjectCatalog.ContentRoots;
		Install( set, fileSystem );
		return new Restore( () =>
		{
			if ( fileSystem != null )
				fileSystem.Overrides = previousFileSystem;
			GameLanguage.StringOverride = previousStrings;
			ObjectCatalog.SetContentRoots( previousRoots, previousKey );
		} );
	}

	private static BaseFileSystem? FileSystemOrNull() => FileSystem;

	private static void Install( ModSet set, BaseFileSystem? fileSystem )
	{
		var index = FileIndex.Build( set );
		if ( fileSystem != null )
		{
			fileSystem.Overrides = index.IsEmpty ? null : index;
			if ( fileSystem.CacheLookups )
				fileSystem.InvalidateLookups();
		}
		GameLanguage.StringOverride = index.HasStrings ? index.OpenString : null;
		ObjectCatalog.SetContentRoots( CatalogRoots( set ), set.Digest );
	}

	/// <summary>The catalog content roots of the mods that add objects, in mod order: the mod's own folder, then each input it reads from.</summary>
	internal static IReadOnlyList<CatalogContentRoot> CatalogRoots( ModSet set )
	{
		var roots = new List<CatalogContentRoot>();
		foreach ( var mod in set.Mods.Where( mod => mod.Objects != null ) )
		{
			var own = mod.Objects!.Objects.Where( definition => definition.Input == null ).ToList();
			if ( own.Count > 0 )
				roots.Add( new CatalogContentRoot( $"mod:{mod.Id}", mod.Directory, CatalogRootKind.ModObjects, mod.Id, own ) );
			foreach ( var input in mod.Objects.Objects.Where( definition => definition.Input != null ).GroupBy( definition => definition.Input!, StringComparer.Ordinal ).OrderBy( group => group.Key, StringComparer.Ordinal ) )
				roots.Add( new CatalogContentRoot( $"mod:{mod.Id}/{input.Key}", mod.InputDirectories[input.Key], CatalogRootKind.ModObjects, mod.Id, input.ToList() ) );
		}
		return roots;
	}

	private static string Describe( ModSet set )
	{
		var index = FileIndex.Build( set );
		var objects = set.Mods.Sum( mod => mod.Objects?.Objects.Count ?? 0 );
		return $"{index.OverrideCount} override files, {index.StringCount} string tables, {objects} objects";
	}

	/// <summary>The key two paths share when a case-insensitive or normalizing file system would treat them as one.</summary>
	private static string FoldKey( string path ) => path.Replace( '\\', '/' ).Trim( '/' ).Normalize( NormalizationForm.FormC ).ToLowerInvariant();

	/// <summary>The override and string files of a mod set by folded path; later mods replace earlier ones.</summary>
	private sealed class FileIndex : IFileOverrides
	{
		private readonly Dictionary<string, string> files = new( StringComparer.Ordinal );

		public int OverrideCount { get; private set; }
		public int StringCount { get; private set; }
		public bool IsEmpty => files.Count == 0;
		public bool HasStrings => StringCount > 0;

		public static FileIndex Build( ModSet set )
		{
			var index = new FileIndex();
			foreach ( var mod in set.Mods )
			{
				if ( mod.Manifest.Content.Overrides is { } overrides )
					index.Add( mod, overrides, "", counted => index.OverrideCount += counted );
				if ( mod.Manifest.Content.Strings is { } strings )
					index.Add( mod, strings, "language/", counted => index.StringCount += counted );
			}
			return index;
		}

		private void Add( ResolvedMod mod, string root, string space, Action<int> count )
		{
			var prefix = FoldKey( root ) + "/";
			var added = 0;
			foreach ( var (path, _) in mod.Files )
			{
				var key = FoldKey( path );
				if ( !key.StartsWith( prefix, StringComparison.Ordinal ) )
					continue;
				var folded = space + key[prefix.Length..];
				// A later mod (or the same mod's strings folder) replaces an earlier file; ModSet refused unordered duplicates.
				if ( files.TryAdd( folded, Path.Combine( mod.Directory, path ) ) )
					added++;
				else
					files[folded] = Path.Combine( mod.Directory, path );
			}
			count( added );
		}

		public bool TryGetFile( string relativePath, out string absolutePath )
		{
			// Checked on use: a name that does not open as the normalized one (a decomposed name on Linux) falls back to the game's file.
			if ( files.TryGetValue( FoldKey( relativePath ), out var found ) && File.Exists( found ) )
			{
				absolutePath = found;
				return true;
			}
			absolutePath = "";
			return false;
		}

		/// <summary>The mod's string table for a language, or null.</summary>
		public Stream? OpenString( string language, string fileName ) =>
			TryGetFile( $"language/{language}/{fileName}", out var file ) ? File.Open( file, FileMode.Open, FileAccess.Read, FileShare.Read ) : null;
	}

	private sealed class Restore( Action restore ) : IDisposable
	{
		public void Dispose() => restore();
	}
}
