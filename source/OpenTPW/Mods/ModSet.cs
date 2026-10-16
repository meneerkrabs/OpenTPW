using System.Security.Cryptography;
using System.Text;

namespace OpenTPW.Mods;

/// <summary>A compiled-in extension and whether it is on in this run.</summary>
public sealed record ResolvedExtension( IOpenTpwExtension Extension, bool Enabled );

/// <summary>
/// An enabled files-based mod in load order. <see cref="ContentSha256"/> is the SHA-256 of the sorted
/// (mod-relative path, file SHA-256) list of every file in the mod's folder, the manifest included.
/// </summary>
public sealed record ResolvedMod( ModManifest Manifest, string Directory, ModSource Source, string ContentSha256 )
{
	public string Id => Manifest.Id;
}

/// <summary>Why a mod cannot run, in terms the mods screen can word in every language ([EXT:mods-ui]).</summary>
public enum ModRefusalKind
{
	/// <summary>Anything else: unreadable files, a shadowed or duplicate id, an invalid manifest.</summary>
	Other,
	/// <summary>The mod needs another OpenTPW version or the other game edition.</summary>
	Incompatible,
	/// <summary>An input folder is not chosen or no longer exists.</summary>
	MissingInput,
	/// <summary>Another enabled mod overrides the same file.</summary>
	Conflict,
	/// <summary>A dependency is missing, disabled, refused, out of range, or part of a cycle.</summary>
	Dependency,
}

/// <summary>A mod that was found or enabled but cannot run, and why.</summary>
public sealed record ModRefusal( string Id, string Reason, ModRefusalKind Kind = ModRefusalKind.Other );

/// <summary>
/// The extensions and mods of one run (docs/MODS.md), resolved once at startup: dependency order first, then official
/// before community, then the player's order, then the ordinal id. Two enabled mods that override the same file are
/// both refused unless one declares the other in <c>after</c>; nothing is resolved by a silent "last one wins".
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public sealed class ModSet
{
	public const int MaximumFilesPerMod = 20000;
	public const long MaximumBytesPerMod = 2L * 1024 * 1024 * 1024;
	private const int MaximumDepth = 32;

	private readonly HashSet<string> enabled;

	private ModSet( IReadOnlyList<ResolvedExtension> extensions, IReadOnlyList<ResolvedMod> mods, IReadOnlyList<string> order, IReadOnlyList<ModRefusal> refused )
	{
		Extensions = extensions;
		Mods = mods;
		Order = order;
		Refused = refused;
		enabled = new HashSet<string>( order, StringComparer.Ordinal );
		Digest = ComputeDigest( mods );
	}

	/// <summary>Every compiled-in extension, enabled or not, in declaration order.</summary>
	public IReadOnlyList<ResolvedExtension> Extensions { get; }
	/// <summary>The enabled files-based mods in load order.</summary>
	public IReadOnlyList<ResolvedMod> Mods { get; }
	/// <summary>The ids of every enabled extension and mod in load order.</summary>
	public IReadOnlyList<string> Order { get; }
	/// <summary>Mods that were found or enabled but cannot run, with the reasons.</summary>
	public IReadOnlyList<ModRefusal> Refused { get; }
	/// <summary>SHA-256 over the enabled files-based mods' (id, version, content digest) in load order; empty without such mods.</summary>
	public string Digest { get; }

	public bool IsEnabled( string id ) => enabled.Contains( id );

	/// <summary>The official extensions with their defaults and no files-based mods.</summary>
	public static ModSet Defaults( IReadOnlyList<IOpenTpwExtension> extensions ) =>
		Resolve( Array.Empty<DiscoveredMod>(), ModSettings.Default, null, GameEdition.ThemeParkWorld, extensions );

	public static ModSet Resolve( IReadOnlyList<DiscoveredMod> discovered, ModSettings settings, ModVersion? openTpwVersion, GameEdition edition,
		IReadOnlyList<IOpenTpwExtension> extensions, IReadOnlyCollection<string>? disabled = null )
	{
		disabled ??= Array.Empty<string>();
		var refused = new List<ModRefusal>();
		var nodes = new Dictionary<string, Node>( StringComparer.Ordinal );
		foreach ( var extension in extensions )
		{
			if ( !nodes.TryAdd( extension.Id, new Node( extension.Id, extension.Version, ModCategory.Official, null, null ) ) )
				throw new InvalidOperationException( $"Two official extensions use the id '{extension.Id}'." );
			nodes[extension.Id].Enabled = !disabled.Contains( extension.Id ) && settings.IsEnabled( extension.Id, extension.DefaultEnabled );
		}

		foreach ( var mod in discovered )
		{
			if ( !mod.IsValid )
			{
				refused.Add( new ModRefusal( mod.Id, $"{mod.Directory}: {mod.Error}" ) );
				continue;
			}
			var manifest = mod.Manifest!;
			if ( nodes.ContainsKey( manifest.Id ) )
			{
				refused.Add( new ModRefusal( manifest.Id, $"{mod.Directory}: the id belongs to an OpenTPW extension or another mod" ) );
				continue;
			}
			var node = new Node( manifest.Id, manifest.Version, manifest.Category, manifest, mod );
			nodes.Add( manifest.Id, node );
			// files-based mods are opt-in
			node.Enabled = !disabled.Contains( manifest.Id ) && settings.IsEnabled( manifest.Id, false );
			if ( node.Enabled && Incompatibility( manifest, settings, openTpwVersion, edition ) is { } reason )
				Refuse( node, reason.Text, refused, reason.Kind );
			if ( !node.Enabled )
				continue;
			var (files, error) = ReadFiles( mod.Directory );
			if ( error != null )
				Refuse( node, error, refused );
			else
				node.Files = files;
		}

		// Refusing a mod can refuse the mods that need it, so repeat until nothing changes.
		IReadOnlyList<Node> order;
		while ( true )
		{
			var changed = false;
			foreach ( var node in nodes.Values.Where( node => node.Enabled ).OrderBy( node => node.Id, StringComparer.Ordinal ).ToList() )
			{
				foreach ( var dependency in node.Manifest?.Dependencies ?? Array.Empty<ModDependency>() )
				{
					if ( !nodes.TryGetValue( dependency.Id, out var target ) )
						changed |= Refuse( node, $"needs {dependency.Id} {dependency.Version}, which is not installed", refused, ModRefusalKind.Dependency );
					else if ( !target.Enabled )
						changed |= Refuse( node, $"needs {dependency.Id} {dependency.Version}, which is disabled or refused", refused, ModRefusalKind.Dependency );
					else if ( !dependency.Version.Contains( target.Version ) )
						changed |= Refuse( node, $"needs {dependency.Id} {dependency.Version}, not {target.Version}", refused, ModRefusalKind.Dependency );
					if ( !node.Enabled )
						break;
				}
			}
			changed |= RefuseConflicts( nodes, refused );
			order = Sort( nodes, settings, out var cyclic );
			foreach ( var node in cyclic )
				changed |= Refuse( node, "is part of, or needs a mod in, a dependency or 'after' cycle", refused, ModRefusalKind.Dependency );
			if ( !changed )
				break;
		}

		var resolvedExtensions = extensions.Select( extension => new ResolvedExtension( extension, nodes[extension.Id].Enabled ) ).ToList();
		var mods = order.Where( node => node.Discovered != null )
			.Select( node => new ResolvedMod( node.Manifest!, node.Discovered!.Directory, node.Discovered.Source, ContentDigest( node.Files! ) ) ).ToList();
		return new ModSet( resolvedExtensions, mods, order.Select( node => node.Id ).ToList(), refused );
	}

	/// <summary>
	/// SHA-256 of the sorted list of (mod-relative path, file SHA-256): one <c>path NUL hex LF</c> line per file in UTF-8,
	/// paths in '/' form and Unicode normalization form C, sorted by ordinal (UTF-16 code unit) order, hashes in lowercase hex.
	/// </summary>
	public static string ContentDigest( IEnumerable<(string Path, string Sha256)> files )
	{
		var text = new StringBuilder();
		foreach ( var (path, sha) in files.OrderBy( file => file.Path, StringComparer.Ordinal ) )
			text.Append( path ).Append( '\0' ).Append( sha ).Append( '\n' );
		return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text.ToString() ) ) ).ToLowerInvariant();
	}

	/// <summary>
	/// Every file of a mod folder with its SHA-256, by '/' path in Unicode normalization form C (so macOS, Windows and
	/// Linux copies of a mod get the same digest). Operating-system clutter (<see cref="IsIgnoredFile"/>) is skipped.
	/// Symbolic links, reparse points and other non-regular files, names that differ only by case or normalization, and
	/// oversized mods are refused.
	/// </summary>
	public static (IReadOnlyList<(string Path, string Sha256)>? Files, string? Error) ReadFiles( string directory )
	{
		var files = new List<(string, string)>();
		var names = new Dictionary<string, string>( StringComparer.Ordinal );
		long bytes = 0;
		var pending = new Stack<(string Path, string Relative, int Depth)>();
		pending.Push( (directory, "", 0) );
		try
		{
			while ( pending.TryPop( out var current ) )
			{
				if ( current.Depth > MaximumDepth )
					return (null, $"folders are nested deeper than {MaximumDepth} levels");
				foreach ( var entry in new DirectoryInfo( current.Path ).EnumerateFileSystemInfos() )
				{
					var name = entry.Name.Normalize( NormalizationForm.FormC );
					var relative = current.Relative.Length == 0 ? name : current.Relative + "/" + name;
					if ( entry.LinkTarget != null || entry.Attributes.HasFlag( FileAttributes.ReparsePoint ) )
						return (null, $"'{relative}' is a symbolic link or reparse point; mods may not contain links");
					if ( entry is FileInfo && IsIgnoredFile( name ) )
						continue;
					if ( names.TryGetValue( FoldKey( relative ), out var other ) )
						return (null, $"'{relative}' and '{other}' differ only by case or Unicode normalization");
					names.Add( FoldKey( relative ), relative );
					if ( entry is DirectoryInfo )
					{
						pending.Push( (entry.FullName, relative, current.Depth + 1) );
						continue;
					}
					if ( ModFiles.NotRegularFile( entry.FullName ) is { } reason )
						return (null, $"'{relative}' {reason}");
					var file = (FileInfo)entry;
					bytes += file.Length;
					if ( files.Count >= MaximumFilesPerMod || bytes > MaximumBytesPerMod )
						return (null, $"the mod has more than {MaximumFilesPerMod} files or {MaximumBytesPerMod} bytes");
					using var stream = file.OpenRead();
					files.Add( (relative, Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant()) );
				}
			}
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return (null, $"its files could not be read ({exception.Message})");
		}
		return (files, null);
	}

	/// <summary>Files that operating systems leave in folders: <c>.DS_Store</c>, AppleDouble <c>._*</c>, <c>Thumbs.db</c> and <c>desktop.ini</c>.</summary>
	public static bool IsIgnoredFile( string name ) =>
		name.Equals( ".DS_Store", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "._", StringComparison.Ordinal )
		|| name.Equals( "Thumbs.db", StringComparison.OrdinalIgnoreCase ) || name.Equals( "desktop.ini", StringComparison.OrdinalIgnoreCase );

	/// <summary>The key two paths share when a case-insensitive or normalizing file system would treat them as one.</summary>
	private static string FoldKey( string path ) => path.Normalize( NormalizationForm.FormC ).ToLowerInvariant();

	private static (string Text, ModRefusalKind Kind)? Incompatibility( ModManifest manifest, ModSettings settings, ModVersion? openTpwVersion, GameEdition edition )
	{
		if ( manifest.OpenTpw != null && openTpwVersion is { } version && !manifest.OpenTpw.Contains( version ) )
			return ($"needs OpenTPW {manifest.OpenTpw}, not {version}", ModRefusalKind.Incompatible);
		if ( manifest.Edition is { } wanted && wanted != edition )
			return ($"is for {ModManifest.EditionToken( wanted )}, not {ModManifest.EditionToken( edition )}", ModRefusalKind.Incompatible);
		foreach ( var input in manifest.Inputs )
		{
			if ( !settings.Inputs.TryGetValue( manifest.Id, out var inputs ) || !inputs.TryGetValue( input.Id, out var path ) )
				return ($"needs the input '{input.Id}' ({input.Kind}{(input.Edition == null ? "" : ", " + input.Edition)}), which is not chosen", ModRefusalKind.MissingInput);
			if ( !Directory.Exists( path ) )
				return ($"needs the input '{input.Id}', but its folder '{path}' does not exist", ModRefusalKind.MissingInput);
		}
		return null;
	}

	/// <summary>
	/// Two enabled mods that override the same file (compared without case or normalization differences) where neither
	/// declares the other in 'after'. All pairs are judged against one snapshot of the enabled mods and refused together,
	/// so a third mod with the same file cannot win because the first two were refused before it was compared.
	/// </summary>
	private static bool RefuseConflicts( Dictionary<string, Node> nodes, List<ModRefusal> refused )
	{
		var owners = new Dictionary<string, List<Node>>( StringComparer.Ordinal );
		foreach ( var node in nodes.Values.Where( node => node.Enabled && node.Manifest?.Content.Overrides != null ).OrderBy( node => node.Id, StringComparer.Ordinal ) )
		{
			var prefix = FoldKey( node.Manifest!.Content.Overrides! ) + "/";
			foreach ( var (path, _) in node.Files! )
			{
				var key = FoldKey( path );
				if ( !key.StartsWith( prefix, StringComparison.Ordinal ) )
					continue;
				if ( !owners.TryGetValue( key[prefix.Length..], out var list ) )
					owners[key[prefix.Length..]] = list = new List<Node>();
				list.Add( node );
			}
		}
		var reasons = new Dictionary<Node, string>();
		foreach ( var (path, list) in owners.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
			for ( var first = 0; first < list.Count; first++ )
				for ( var second = first + 1; second < list.Count; second++ )
				{
					var (a, b) = (list[first], list[second]);
					if ( a.Manifest!.After.Contains( b.Id ) || b.Manifest!.After.Contains( a.Id ) )
						continue;
					reasons.TryAdd( a, $"overrides '{path}' like {b.Id}, and neither declares the other in 'after'" );
					reasons.TryAdd( b, $"overrides '{path}' like {a.Id}, and neither declares the other in 'after'" );
				}
		var changed = false;
		foreach ( var (node, reason) in reasons.OrderBy( pair => pair.Key.Id, StringComparer.Ordinal ) )
			changed |= Refuse( node, reason, refused, ModRefusalKind.Conflict );
		return changed;
	}

	/// <summary>Topological order of the enabled nodes; ties by official first, the player's order, then the ordinal id.</summary>
	private static IReadOnlyList<Node> Sort( Dictionary<string, Node> nodes, ModSettings settings, out IReadOnlyList<Node> cyclic )
	{
		var active = nodes.Values.Where( node => node.Enabled ).ToList();
		var incoming = active.ToDictionary( node => node.Id, _ => 0, StringComparer.Ordinal );
		var outgoing = active.ToDictionary( node => node.Id, _ => new List<Node>(), StringComparer.Ordinal );
		foreach ( var node in active )
		{
			var before = (node.Manifest?.Dependencies.Select( dependency => dependency.Id ) ?? Enumerable.Empty<string>())
				.Concat( node.Manifest?.After ?? Array.Empty<string>() ).Distinct( StringComparer.Ordinal );
			foreach ( var id in before )
				if ( incoming.ContainsKey( id ) )
				{
					outgoing[id].Add( node );
					incoming[node.Id]++;
				}
		}
		var position = settings.Order.Select( ( id, index ) => (id, index) ).ToDictionary( pair => pair.id, pair => pair.index, StringComparer.Ordinal );
		var ready = new PriorityQueue<Node, (int Category, int Position, string Id)>( Comparer<(int Category, int Position, string Id)>.Create( ( x, y ) =>
		{
			var result = x.Category.CompareTo( y.Category );
			if ( result == 0 ) result = x.Position.CompareTo( y.Position );
			return result != 0 ? result : string.CompareOrdinal( x.Id, y.Id );
		} ) );
		(int, int, string) Key( Node node ) => (node.Category == ModCategory.Official ? 0 : 1, position.TryGetValue( node.Id, out var index ) ? index : int.MaxValue, node.Id);
		foreach ( var node in active.Where( node => incoming[node.Id] == 0 ) )
			ready.Enqueue( node, Key( node ) );
		var order = new List<Node>();
		while ( ready.TryDequeue( out var node, out _ ) )
		{
			order.Add( node );
			foreach ( var next in outgoing[node.Id] )
				if ( --incoming[next.Id] == 0 )
					ready.Enqueue( next, Key( next ) );
		}
		cyclic = active.Where( node => incoming[node.Id] > 0 ).OrderBy( node => node.Id, StringComparer.Ordinal ).ToList();
		return order;
	}

	private static bool Refuse( Node node, string reason, List<ModRefusal> refused, ModRefusalKind kind = ModRefusalKind.Other )
	{
		if ( !node.Enabled )
			return false;
		node.Enabled = false;
		refused.Add( new ModRefusal( node.Id, reason, kind ) );
		return true;
	}

	private static string ComputeDigest( IReadOnlyList<ResolvedMod> mods )
	{
		if ( mods.Count == 0 )
			return "";
		var text = new StringBuilder();
		foreach ( var mod in mods )
			text.Append( mod.Id ).Append( '\0' ).Append( mod.Manifest.Version ).Append( '\0' ).Append( mod.ContentSha256 ).Append( '\n' );
		return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text.ToString() ) ) ).ToLowerInvariant();
	}

	private sealed class Node( string id, ModVersion version, ModCategory category, ModManifest? manifest, DiscoveredMod? discovered )
	{
		public string Id { get; } = id;
		public ModVersion Version { get; } = version;
		public ModCategory Category { get; } = category;
		public ModManifest? Manifest { get; } = manifest;
		public DiscoveredMod? Discovered { get; } = discovered;
		public bool Enabled { get; set; }
		public IReadOnlyList<(string Path, string Sha256)>? Files { get; set; }
	}
}
