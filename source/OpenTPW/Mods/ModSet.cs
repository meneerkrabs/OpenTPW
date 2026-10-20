using System.Security.Cryptography;
using System.Text;
using OpenTPW.Scripting;

namespace OpenTPW.Mods;

/// <summary>A compiled-in extension and whether it is on in this run.</summary>
public sealed record ResolvedExtension( IOpenTpwExtension Extension, bool Enabled );

/// <summary>
/// An enabled files-based mod in load order. <see cref="ContentSha256"/> is the SHA-256 of the sorted
/// (mod-relative path, file SHA-256) list of every file in the mod's folder, the manifest included.
/// </summary>
public sealed record ResolvedMod( ModManifest Manifest, string Directory, ModSource Source, string ContentSha256, bool AffectsSimulation = false )
{
	public string Id => Manifest.Id;
	/// <summary>Every file of the mod's folder with its SHA-256, by '/' path in Unicode normalization form C (what <see cref="ContentSha256"/> covers).</summary>
	public IReadOnlyList<(string Path, string Sha256)> Files { get; init; } = Array.Empty<(string, string)>();
	/// <summary>The validated <c>objects.json</c> (<see cref="ModContent.Objects"/>), or null when the mod adds no objects.</summary>
	public ModObjectsFile? Objects { get; init; }
	/// <summary>The data folder of each input the mod's objects read from, by input id (the folder the player chose, or its Data folder).</summary>
	public IReadOnlyDictionary<string, string> InputDirectories { get; init; } = new Dictionary<string, string>();
	/// <summary>
	/// The mod's scripts: the exact bytes that went into <see cref="ContentSha256"/>, compiled at resolve time (docs/SCRIPT-MODS.md), or null
	/// when the mod has none. Whatever runs a script must use these retained bytes or their compiled programs, never read the path again.
	/// </summary>
	public ModScriptSet? Scripts { get; init; }
}

/// <summary>One script file of a mod: its mod-relative path, the bytes that were hashed, and the program compiled from exactly those bytes.</summary>
public sealed record ModScriptFile( string Path, ReadOnlyMemory<byte> Source, ScriptProgram Program );

/// <summary>The retained and compiled scripts of one resolved mod, in the manifest's file order, with the file each hook's function lives in.</summary>
public sealed class ModScriptSet
{
	internal ModScriptSet( IReadOnlyList<ModScriptFile> files, IReadOnlyDictionary<string, (int File, string Function)> hooks )
	{
		Files = files;
		Hooks = hooks;
	}

	public IReadOnlyList<ModScriptFile> Files { get; }
	/// <summary>Per declared hook: the index into <see cref="Files"/> and the function name.</summary>
	public IReadOnlyDictionary<string, (int File, string Function)> Hooks { get; }
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
	/// <summary>A script does not compile, breaks a script limit, or a hook names no suitable function.</summary>
	Script,
	/// <summary>Script mods are off for this run: safe mode (<c>--safe-mode</c> or a startup crash marker).</summary>
	SafeMode,
}

/// <summary>
/// An enabled extension or mod that changes the park simulation, as a park save records it (docs/MODS.md, docs/DETERMINISM.md).
/// An extension ships no files, so its <see cref="ContentSha256"/> is <see cref="ModSet.NoFilesSha256"/>.
/// </summary>
public sealed record SimulationMod( string Id, ModVersion Version, string ContentSha256 );

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

	/// <summary>The content digest recorded for an extension, which ships no files: the SHA-256 of no bytes.</summary>
	public const string NoFilesSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

	private readonly HashSet<string> enabled;

	private ModSet( IReadOnlyList<ResolvedExtension> extensions, IReadOnlyList<ResolvedMod> mods, IReadOnlyList<string> order, IReadOnlyList<ModRefusal> refused )
	{
		Extensions = extensions;
		Mods = mods;
		Order = order;
		Refused = refused;
		enabled = new HashSet<string>( order, StringComparer.Ordinal );
		Digest = ComputeDigest( mods );
		var byId = extensions.ToDictionary( extension => extension.Extension.Id, StringComparer.Ordinal );
		var modsById = mods.ToDictionary( mod => mod.Id, StringComparer.Ordinal );
		var simulation = new List<SimulationMod>();
		foreach ( var id in order )
		{
			if ( byId.TryGetValue( id, out var extension ) && extension.Extension.AffectsSimulation )
				simulation.Add( new SimulationMod( id, extension.Extension.Version, NoFilesSha256 ) );
			else if ( modsById.TryGetValue( id, out var mod ) && mod.AffectsSimulation )
				simulation.Add( new SimulationMod( id, mod.Manifest.Version, mod.ContentSha256 ) );
		}
		SimulationMods = simulation;
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

	/// <summary>
	/// The enabled extensions and mods that change the simulation, in load order: what a park save records and what
	/// loading it requires. Presentation-only mods and extensions (online, textures, sounds, strings) are not listed.
	/// </summary>
	public IReadOnlyList<SimulationMod> SimulationMods { get; }

	public bool IsEnabled( string id ) => enabled.Contains( id );

	/// <summary>
	/// This set with one presentation extension (one that does not change the simulation) switched on or off, for a run-scoped toggle such as the browser harness's evening hook: every other extension and mod,
	/// the load order of the rest and the refusals stay as they are. Switched on, the extension takes the place <see cref="Resolve"/> gives it with no player order (among the enabled extensions by ordinal id,
	/// before the files-based mods). A mod that depends on the extension is not re-checked, so switching off an extension an enabled mod needs is refused.
	/// </summary>
	internal ModSet WithPresentationExtension( IOpenTpwExtension extension, bool on )
	{
		ArgumentNullException.ThrowIfNull( extension );
		if ( extension.AffectsSimulation )
			throw new ArgumentException( $"{extension.Id} changes the simulation; only a presentation extension can be switched for a run.", nameof( extension ) );
		var extensions = Extensions.ToList();
		var index = extensions.FindIndex( item => item.Extension.Id == extension.Id );
		if ( index < 0 )
			throw new ArgumentException( $"{extension.Id} is not an extension of this set.", nameof( extension ) );
		if ( IsEnabled( extension.Id ) == on )
			return this;
		if ( !on && Mods.FirstOrDefault( mod => mod.Manifest.Dependencies.Any( dependency => dependency.Id == extension.Id ) ) is { } dependant )
			throw new InvalidOperationException( $"{dependant.Id} needs {extension.Id}; it cannot be switched off for a run." );
		extensions[index] = extensions[index] with { Enabled = on };
		var order = Order.Where( id => id != extension.Id ).ToList();
		if ( on )
		{
			var extensionIds = new HashSet<string>( extensions.Select( item => item.Extension.Id ), StringComparer.Ordinal );
			var after = order.FindIndex( id => extensionIds.Contains( id ) && string.CompareOrdinal( id, extension.Id ) > 0 );
			order.Insert( after >= 0 ? after : order.FindLastIndex( extensionIds.Contains ) + 1, extension.Id );
		}
		return new ModSet( extensions, Mods, order, Refused );
	}

	/// <summary>
	/// [EXT:extensions-apply-at-load] This set with official extensions switched on or off (<paramref name="changes"/>, id to on), for a change that applies at the next park load instead of the next start
	/// (docs/MODS.md, "When a change applies"): the files-based mods, their content and the refusals stay exactly as they are, and the enabled extensions take the order <see cref="Resolve"/> gives them
	/// (before every files-based mod, by <paramref name="playerOrder"/> and then by ordinal id), so a later start with the same choices resolves the same order. An extension no player can switch on, or
	/// switching off an extension an enabled mod needs, is refused (<see cref="InvalidOperationException"/>): those changes need a new start, where the mods are resolved again.
	/// </summary>
	internal ModSet WithExtensions( IReadOnlyDictionary<string, bool> changes, IReadOnlyList<string> playerOrder )
	{
		ArgumentNullException.ThrowIfNull( changes );
		ArgumentNullException.ThrowIfNull( playerOrder );
		var extensions = Extensions.ToList();
		var changed = false;
		foreach ( var (id, on) in changes.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
		{
			var index = extensions.FindIndex( item => item.Extension.Id == id );
			if ( index < 0 )
				throw new ArgumentException( $"{id} is not an extension of this set.", nameof( changes ) );
			if ( extensions[index].Enabled == on )
				continue;
			if ( on && !extensions[index].Extension.PlayerSelectable )
				throw new InvalidOperationException( $"{id} cannot be switched on yet." );
			if ( !on && Mods.FirstOrDefault( mod => mod.Manifest.Dependencies.Any( dependency => dependency.Id == id ) ) is { } dependant )
				throw new InvalidOperationException( $"{dependant.Id} needs {id}; switching it off needs a new start." );
			extensions[index] = extensions[index] with { Enabled = on };
			changed = true;
		}
		if ( !changed )
			return this;
		var position = new Dictionary<string, int>( StringComparer.Ordinal );
		for ( var index = 0; index < playerOrder.Count; index++ )
			position.TryAdd( playerOrder[index], index );
		var extensionIds = new HashSet<string>( extensions.Select( item => item.Extension.Id ), StringComparer.Ordinal );
		var order = extensions.Where( item => item.Enabled ).Select( item => item.Extension.Id )
			.OrderBy( id => position.TryGetValue( id, out var index ) ? index : int.MaxValue ).ThenBy( id => id, StringComparer.Ordinal )
			.Concat( Order.Where( id => !extensionIds.Contains( id ) ) ).ToList();
		return new ModSet( extensions, Mods, order, Refused );
	}

	/// <summary>
	/// Whether a mod changes the park simulation, judged from its manifest and files: it adds objects
	/// (<c>content.objects</c>), or it overrides a file that is not plainly presentation media (textures, sounds, text
	/// and fonts: <see cref="IsPresentationFile"/>), or it has scripts with a hook that can change the simulation (every v1 hook can). Unknown file types count as simulation, the safe side: such a
	/// mod is recorded in park saves.
	/// </summary>
	public static bool AffectsSimulation( ModManifest manifest, IEnumerable<string> files )
	{
		if ( manifest.Content.Objects != null || manifest.Content.Scripts is { AffectsSimulation: true } )
			return true;
		if ( manifest.Content.Overrides == null )
			return false;
		var prefix = FoldKey( manifest.Content.Overrides ) + "/";
		return files.Any( path => FoldKey( path ).StartsWith( prefix, StringComparison.Ordinal ) && !IsPresentationFile( path ) );
	}

	private static readonly HashSet<string> PresentationExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".fsh", ".tga", ".png", ".jpg", ".jpeg", ".bmp", ".wav", ".mp3", ".ogg", ".flac", ".str", ".bf4", ".ttf", ".otf",
	};

	/// <summary>A file of media that only changes how the game looks, sounds or reads (by extension, ignoring case).</summary>
	public static bool IsPresentationFile( string path ) => PresentationExtensions.Contains( Path.GetExtension( path ) );

	/// <summary>The official extensions with their defaults and no files-based mods.</summary>
	public static ModSet Defaults( IReadOnlyList<IOpenTpwExtension> extensions ) =>
		Resolve( Array.Empty<DiscoveredMod>(), ModSettings.Default, null, GameEdition.ThemeParkWorld, extensions );

	public static ModSet Resolve( IReadOnlyList<DiscoveredMod> discovered, ModSettings settings, ModVersion? openTpwVersion, GameEdition edition,
		IReadOnlyList<IOpenTpwExtension> extensions, IReadOnlyCollection<string>? disabled = null, bool scriptModsOff = false )
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
			// safe mode: a script mod is not read, hashed or compiled at all
			if ( node.Enabled && scriptModsOff && manifest.Content.Scripts != null )
				Refuse( node, "contains scripts, which are switched off for this run (safe mode)", refused, ModRefusalKind.SafeMode );
			if ( !node.Enabled )
				continue;
			var (files, retained, error, errorKind) = ReadFilesRetaining( mod.Directory, (manifest.Content.Scripts?.Files ?? Array.Empty<string>()).Append( ModManifest.FileName ) );
			if ( error != null )
				Refuse( node, error, refused, errorKind );
			// the manifest that was parsed at discovery must be the manifest that was hashed, so the files and hooks come from hashed bytes
			else if ( mod.ManifestBytes != null && !(retained.TryGetValue( FoldKey( ModManifest.FileName ), out var hashedManifest ) && hashedManifest != null && hashedManifest.AsSpan().SequenceEqual( mod.ManifestBytes )) )
				Refuse( node, $"{ModManifest.FileName} changed after it was read; start again", refused );
			else
			{
				node.Files = files;
				node.RetainedSources = retained;
				if ( ValidateContent( node, settings, edition ) is { } contentError )
					Refuse( node, contentError.Text, refused, contentError.Kind );
			}
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
			.Select( node => new ResolvedMod( node.Manifest!, node.Discovered!.Directory, node.Discovered.Source, ContentDigest( node.Files! ),
				AffectsSimulation( node.Manifest!, node.Files!.Select( file => file.Path ) ) )
				{ Files = node.Files!, Objects = node.Objects, InputDirectories = node.InputDirectories, Scripts = node.Scripts } ).ToList();
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
		var (files, _, error, _) = ReadFilesRetaining( directory, null );
		return (files, error);
	}

	/// <summary>
	/// <see cref="ReadFiles"/> that also keeps the bytes of the listed files (script files, by case- and normalization-folded path): each is read once,
	/// and the digest and the retained bytes come from that one read. A listed file larger than <see cref="ScriptLimits.MaxSourceBytes"/> is hashed but
	/// not kept (its entry is null), so a hostile file cannot fill memory; the caller refuses it.
	/// </summary>
	internal static (IReadOnlyList<(string Path, string Sha256)>? Files, IReadOnlyDictionary<string, byte[]?> Retained, string? Error, ModRefusalKind ErrorKind) ReadFilesRetaining(
		string directory, IEnumerable<string>? retainPaths )
	{
		var retain = new HashSet<string>( (retainPaths ?? Array.Empty<string>()).Select( FoldKey ), StringComparer.Ordinal );
		var retained = new Dictionary<string, byte[]?>( StringComparer.Ordinal );
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
					return (null, retained, $"folders are nested deeper than {MaximumDepth} levels", ModRefusalKind.Other);
				foreach ( var entry in new DirectoryInfo( current.Path ).EnumerateFileSystemInfos() )
				{
					var name = entry.Name.Normalize( NormalizationForm.FormC );
					var relative = current.Relative.Length == 0 ? name : current.Relative + "/" + name;
					if ( entry.LinkTarget != null || entry.Attributes.HasFlag( FileAttributes.ReparsePoint ) )
						return (null, retained, $"'{relative}' is a symbolic link or reparse point; mods may not contain links", ModRefusalKind.Other);
					if ( entry is FileInfo && IsIgnoredFile( name ) )
						continue;
					if ( names.TryGetValue( FoldKey( relative ), out var other ) )
						return (null, retained, $"'{relative}' and '{other}' differ only by case or Unicode normalization", ModRefusalKind.Other);
					names.Add( FoldKey( relative ), relative );
					if ( entry is DirectoryInfo )
					{
						pending.Push( (entry.FullName, relative, current.Depth + 1) );
						continue;
					}
					if ( ModFiles.NotRegularFile( entry.FullName ) is { } reason )
						return (null, retained, $"'{relative}' {reason}", ModRefusalKind.Other);
					var file = (FileInfo)entry;
					// a script file over the cap is refused here, before it is read or hashed
					if ( retain.Contains( FoldKey( relative ) ) && relative != ModManifest.FileName && file.Length > ScriptLimits.MaxSourceBytes )
						return (null, retained, $"script '{relative}' is larger than {ScriptLimits.MaxSourceBytes} bytes", ModRefusalKind.Script);
					bytes += file.Length;
					if ( files.Count >= MaximumFilesPerMod || bytes > MaximumBytesPerMod )
						return (null, retained, $"the mod has more than {MaximumFilesPerMod} files or {MaximumBytesPerMod} bytes", ModRefusalKind.Other);
					using var stream = file.OpenRead();
					if ( retain.Contains( FoldKey( relative ) ) )
					{
						var (hash, kept) = HashAndKeep( stream, ScriptLimits.MaxSourceBytes );
						retained[FoldKey( relative )] = kept;
						files.Add( (relative, hash) );
					}
					else
						files.Add( (relative, Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant() ) );
				}
			}
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
		{
			return (null, retained, $"its files could not be read ({exception.Message})", ModRefusalKind.Other);
		}
		return (files, retained, null, ModRefusalKind.Other);
	}

	/// <summary>Reads a stream once: its SHA-256 and, when it is at most <paramref name="limit"/> bytes, the bytes themselves (else null).</summary>
	private static (string Sha256, byte[]? Bytes) HashAndKeep( Stream stream, int limit )
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		using var kept = new MemoryStream();
		var buffer = new byte[16 * 1024];
		var keep = true;
		int read;
		while ( (read = stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			hash.AppendData( buffer, 0, read );
			if ( !keep )
				continue;
			if ( kept.Length + read > limit )
			{
				keep = false;
				kept.SetLength( 0 );
			}
			else
				kept.Write( buffer, 0, read );
		}
		return (Convert.ToHexString( hash.GetHashAndReset() ).ToLowerInvariant(), keep ? kept.ToArray() : null);
	}

	/// <summary>Files that operating systems leave in folders: <c>.DS_Store</c>, AppleDouble <c>._*</c>, <c>Thumbs.db</c> and <c>desktop.ini</c>.</summary>
	public static bool IsIgnoredFile( string name ) =>
		name.Equals( ".DS_Store", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "._", StringComparison.Ordinal )
		|| name.Equals( "Thumbs.db", StringComparison.OrdinalIgnoreCase ) || name.Equals( "desktop.ini", StringComparison.OrdinalIgnoreCase );

	/// <summary>The key two paths share when a case-insensitive or normalizing file system would treat them as one.</summary>
	private static string FoldKey( string path ) => ModManifest.FoldKey( path );

	private static (string Text, ModRefusalKind Kind)? Incompatibility( ModManifest manifest, ModSettings settings, ModVersion? openTpwVersion, GameEdition edition )
	{
		if ( manifest.OpenTpw != null && openTpwVersion is { } version && !manifest.OpenTpw.Contains( version ) )
			return ($"needs OpenTPW {manifest.OpenTpw}, not {version}", ModRefusalKind.Incompatible);
		if ( manifest.ScriptApi != null && !manifest.ScriptApi.Contains( ModVersion.Parse( ScriptLimits.ScriptApiVersion ) ) )
			return ($"needs script API {manifest.ScriptApi}, not {ScriptLimits.ScriptApiVersion}", ModRefusalKind.Incompatible);
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
	/// What a mod's content may hold, beyond what its manifest says: <c>objects.json</c> must be a valid file of the mod, in a
	/// Theme Park World park, reading only from inputs whose folder is a game data folder; files below <c>strings</c> must be
	/// <c>&lt;Language&gt;/&lt;NAME&gt;.str</c>. Returns the reason the mod cannot run, or null.
	/// </summary>
	private static (string Text, ModRefusalKind Kind)? ValidateContent( Node node, ModSettings settings, GameEdition edition )
	{
		var manifest = node.Manifest!;
		if ( manifest.Content.Strings is { } strings )
		{
			var prefix = FoldKey( strings ) + "/";
			foreach ( var (path, _) in node.Files! )
			{
				if ( !FoldKey( path ).StartsWith( prefix, StringComparison.Ordinal ) )
					continue;
				var parts = FoldKey( path )[prefix.Length..].Split( '/' );
				if ( parts.Length != 2 || !parts[1].EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) || parts[1].Length <= 4 )
					return ($"'{path}' is not a string table: files below '{strings}' must be <Language>/<NAME>.str", ModRefusalKind.Other);
			}
		}
		if ( manifest.Content.Scripts is { } scripts && CompileScripts( node, scripts ) is { } scriptError )
			return (scriptError, ModRefusalKind.Script);
		if ( manifest.Content.Objects is not { } objects )
			return null;
		if ( edition != GameEdition.ThemeParkWorld )
			return ("adds Theme Park World objects, which Theme Park Inc does not use", ModRefusalKind.Incompatible);
		var file = node.Files!.Select( entry => entry.Path ).FirstOrDefault( path => FoldKey( path ) == FoldKey( objects ) );
		if ( file == null )
			return ($"content.objects '{objects}' is not a file of the mod", ModRefusalKind.Other);
		try
		{
			node.Objects = ModObjectsFile.Load( Path.Combine( node.Discovered!.Directory, file ), manifest.Inputs.Select( input => input.Id ).ToList() );
		}
		catch ( InvalidDataException exception )
		{
			return (exception.Message, ModRefusalKind.Other);
		}
		foreach ( var inputId in node.Objects.Objects.Select( definition => definition.Input ).Where( id => id != null ).Distinct( StringComparer.Ordinal ) )
		{
			if ( !settings.Inputs.TryGetValue( manifest.Id, out var inputs ) || !inputs.TryGetValue( inputId!, out var folder ) )
				return ($"its objects read from the input '{inputId}', which is not chosen", ModRefusalKind.MissingInput);
			var data = DataRoots.FindDataDirectory( folder );
			if ( data == null )
				return ($"its objects read from the input '{inputId}', but '{folder}' is neither a Data folder nor an install holding one", ModRefusalKind.MissingInput);
			node.InputDirectories[inputId!] = data;
		}
		return null;
	}

	/// <summary>
	/// Checks and compiles a mod's scripts from the bytes <see cref="ReadFilesRetaining"/> kept (never the path again): every listed file exists,
	/// stays out of the other content folders, is within the per-file and the total size caps and compiles; each hook names a function that
	/// exists in exactly one file with the hook's parameter count. Returns the refusal reason, or null with <see cref="Node.Scripts"/> set.
	/// </summary>
	private static string? CompileScripts( Node node, ModScripts scripts )
	{
		var manifest = node.Manifest!;
		var compiled = new List<ModScriptFile>();
		long total = 0;
		foreach ( var listed in scripts.Files )
		{
			var path = node.Files!.Select( entry => entry.Path ).FirstOrDefault( candidate => FoldKey( candidate ) == FoldKey( listed ) );
			if ( path == null )
				return $"script file '{listed}' is not a file of the mod";
			foreach ( var other in new[] { manifest.Content.Overrides, manifest.Content.Strings } )
				if ( other != null && FoldKey( path ).StartsWith( FoldKey( other ) + "/", StringComparison.Ordinal ) )
					return $"script file '{path}' is inside the content folder '{other}'";
			if ( manifest.Content.Objects != null && FoldKey( path ) == FoldKey( manifest.Content.Objects ) )
				return $"script file '{path}' is also the objects file";
			if ( !node.RetainedSources!.TryGetValue( FoldKey( path ), out var bytes ) || bytes == null )
				return $"script '{path}' is larger than {ScriptLimits.MaxSourceBytes} bytes";
			total += bytes.Length;
			if ( total > ModManifest.MaximumScriptBytes )
				return $"the scripts are larger than {ModManifest.MaximumScriptBytes} bytes together";
			try
			{
				compiled.Add( new ModScriptFile( path, bytes, ScriptProgram.Compile( bytes ) ) );
			}
			catch ( ScriptCompileException exception )
			{
				return $"script '{path}' line {exception.Line}: {exception.Detail}";
			}
			catch ( Exception exception )
			{
				// a compiler fault on a pathological file must refuse the mod, not the start
				return $"script '{path}' could not be compiled ({exception.GetType().Name})";
			}
		}
		var hooks = new Dictionary<string, (int File, string Function)>( StringComparer.Ordinal );
		foreach ( var (hook, function) in scripts.Hooks.OrderBy( pair => pair.Key, StringComparer.Ordinal ) )
		{
			var owners = Enumerable.Range( 0, compiled.Count ).Where( index => compiled[index].Program.HasFunction( function ) ).ToList();
			if ( owners.Count == 0 )
				return $"hook '{hook}' names the function '{function}', which no script defines";
			if ( owners.Count > 1 )
				return $"hook '{hook}' names the function '{function}', which more than one script defines";
			var expected = ScriptLimits.HookParameters[hook];
			var actual = compiled[owners[0]].Program.ParameterCount( function );
			if ( actual != expected )
				return $"hook '{hook}' needs a function with {expected} parameters, but '{function}' has {actual}";
			hooks[hook] = (owners[0], function);
		}
		node.Scripts = new ModScriptSet( compiled, hooks );
		return null;
	}

	/// <summary>
	/// Two enabled mods that provide the same content where neither declares the other in 'after': the same file in
	/// <c>overrides</c> or <c>strings</c> (compared without case or normalization differences; a string table sits at
	/// <c>Language/&lt;Language&gt;/&lt;NAME&gt;.str</c> in the same space as the overrides), overlapping Info.Id ranges, or an object
	/// archive of the same name in the same theme and category. Only files can be put in order with 'after'. All pairs
	/// are judged against one snapshot of the enabled mods and refused together, so a third mod with the same content
	/// cannot win because the first two were refused before it was compared.
	/// </summary>
	private static bool RefuseConflicts( Dictionary<string, Node> nodes, List<ModRefusal> refused )
	{
		var owners = new Dictionary<string, List<Node>>( StringComparer.Ordinal );
		void Own( string key, Node node )
		{
			if ( !owners.TryGetValue( key, out var list ) )
				owners[key] = list = new List<Node>();
			if ( !list.Contains( node ) )
				list.Add( node );
		}
		var active = nodes.Values.Where( node => node.Enabled && node.Manifest != null ).OrderBy( node => node.Id, StringComparer.Ordinal ).ToList();
		foreach ( var node in active )
		{
			foreach ( var (root, space) in new[] { (node.Manifest!.Content.Overrides, ""), (node.Manifest.Content.Strings, "language/") } )
			{
				if ( root == null )
					continue;
				var prefix = FoldKey( root ) + "/";
				foreach ( var (path, _) in node.Files! )
				{
					var key = FoldKey( path );
					if ( key.StartsWith( prefix, StringComparison.Ordinal ) )
						Own( space + key[prefix.Length..], node );
				}
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
		var withObjects = active.Where( node => node.Objects != null ).ToList();
		for ( var first = 0; first < withObjects.Count; first++ )
			for ( var second = first + 1; second < withObjects.Count; second++ )
			{
				var (a, b) = (withObjects[first], withObjects[second]);
				if ( a.Objects!.FirstId <= b.Objects!.LastId && b.Objects.FirstId <= a.Objects.LastId )
				{
					reasons.TryAdd( a, $"reserves the Info.Ids {a.Objects.FirstId} to {a.Objects.LastId}, which overlap those of {b.Id} ({b.Objects.FirstId} to {b.Objects.LastId})" );
					reasons.TryAdd( b, $"reserves the Info.Ids {b.Objects.FirstId} to {b.Objects.LastId}, which overlap those of {a.Id} ({a.Objects.FirstId} to {a.Objects.LastId})" );
				}
				var shared = a.Objects.Objects.Select( ObjectKey ).Intersect( b.Objects.Objects.Select( ObjectKey ), StringComparer.OrdinalIgnoreCase ).FirstOrDefault();
				if ( shared != null )
				{
					reasons.TryAdd( a, $"adds the object archive '{shared}' like {b.Id}" );
					reasons.TryAdd( b, $"adds the object archive '{shared}' like {a.Id}" );
				}
			}
		var changed = false;
		foreach ( var (node, reason) in reasons.OrderBy( pair => pair.Key.Id, StringComparer.Ordinal ) )
			changed |= Refuse( node, reason, refused, ModRefusalKind.Conflict );
		return changed;
	}

	/// <summary>The identity an object archive has in the catalog: its theme, category and file name.</summary>
	private static string ObjectKey( ModObjectDefinition definition ) =>
		$"{definition.Theme}/{ModObjectsFile.CategoryName( definition.Category )}/{ObjectCatalog.ArchiveKey( Path.GetFileName( definition.Archive ) )}";

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
		public ModObjectsFile? Objects { get; set; }
		public IReadOnlyDictionary<string, byte[]?>? RetainedSources { get; set; }
		public ModScriptSet? Scripts { get; set; }
		public Dictionary<string, string> InputDirectories { get; } = new( StringComparer.Ordinal );
	}
}
