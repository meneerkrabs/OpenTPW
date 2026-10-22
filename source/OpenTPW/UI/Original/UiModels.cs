namespace OpenTPW.UI.Original;

/// <summary>An ambiguous binding is never resolved by selecting an arbitrary asset.</summary>
public sealed class UiModelBindingException : InvalidOperationException
{
	public int? DrawingKey { get; }

	internal UiModelBindingException( string message, int? drawingKey = null ) : base( message )
	{
		DrawingKey = drawingKey;
	}
}

/// <summary>
/// Original drawing registry keyed by the actual root-node name hash. Existing presentation
/// filenames remain explicit asset aliases; they do not become original drawing keys.
/// </summary>
public sealed class UiModels
{
	private const int MaximumAssets = 4096;
	private readonly Dictionary<string, UiModel?> requests = new( StringComparer.Ordinal );
	private readonly List<UiModel> assets = new();
	private readonly Dictionary<int, UiModel> bindings = new();
	private readonly Func<string, UiModel>? customLoad;
	private bool initialized;
	/// <summary>Whether the registry also takes the current language's banner meshes (the default registry; fixtures leave them out).</summary>
	private readonly bool languageMeshes;
	private readonly Dictionary<int, UiModel> languageBindings = new();
	private int languageVersion = -1;
	private UiModelBindingException? bindingFailure;

	/// <summary>Default instances build the complete ui.wad registry; a custom loader remains available to headless fixtures.</summary>
	public UiModels( Func<string, UiModel>? load = null )
	{
		customLoad = load;
		initialized = load is not null;
		languageMeshes = load is null;
	}

	/// <summary>Creates a complete registry from already decoded assets, with the same collision/exclusion policy.</summary>
	public static UiModels FromAssets( IEnumerable<UiModel> models )
	{
		ArgumentNullException.ThrowIfNull( models );
		var registry = new UiModels { initialized = true };
		foreach ( var model in models ) registry.Register( model );
		return registry;
	}

	public IReadOnlyList<UiModel> Assets
	{
		get { EnsureInitialized(); return assets.ToArray(); }
	}

	public int BindingCount
	{
		get { EnsureInitialized(); return bindings.Count; }
	}

	/// <summary>Case-sensitive original root-name arithmetic over the Latin-1 bytes retained by ModelFile.</summary>
	public static int RootNameKey( string name )
	{
		ArgumentException.ThrowIfNullOrEmpty( name );
		if ( name.Length > 255 || name.Any( value => value is '\0' or > '\xff' ) )
			throw new ArgumentException( "Original UI root names require at most 255 nonzero single-byte characters.", nameof(name) );
		uint hash = 0;
		foreach ( var value in name ) hash = unchecked((hash ^ (uint)(int)(sbyte)(byte)value) * 47u);
		return unchecked((int)hash);
	}

	/// <summary>Strict original lookup; missing keys and collisions have explicit diagnostics.</summary>
	public UiModel GetByDrawingKey( int key )
	{
		EnsureInitialized();
		return bindings.TryGetValue( key, out var model ) ? model
			: throw new FileNotFoundException( $"Original UI drawing key 0x{unchecked((uint)key):x8} is not registered." );
	}

	public UiModel GetByRootName( string name )
	{
		var model = GetByDrawingKey( RootNameKey( name ) );
		if ( model.RootNodeName != name )
			throw new UiModelBindingException( $"UI root name '{name}' hashes to '{model.RootNodeName}' but is not that stored name.", model.DrawingKey );
		return model;
	}

	/// <summary>Explicit filename alias, including the shadow asset excluded from ordinary original registration.</summary>
	public UiModel GetAsset( string name )
	{
		EnsureInitialized();
		var exact = assets.Where( model => model.Name == name ).ToArray();
		var candidates = exact.Length > 0 ? exact : assets.Where( model => string.Equals( model.Name, name, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		if ( candidates.Length > 1 )
			throw new UiModelBindingException( $"UI asset alias '{name}' is ambiguous: {string.Join( ", ", candidates.Select( model => model.Name ).OrderBy( value => value, StringComparer.Ordinal ) )}." );
		return candidates.SingleOrDefault() ?? throw new FileNotFoundException( $"Original UI asset '{name}' is unavailable." );
	}

	/// <summary>
	/// Current widgets resolve exact original roots first, then unambiguous filename aliases.
	/// Missing assets keep their existing fallback behavior; ambiguous bindings fail explicitly.
	/// </summary>
	public UiModel? Get( string name )
	{
		if ( bindingFailure is not null ) throw bindingFailure;
		RefreshLanguageModels();
		if ( requests.TryGetValue( name, out var cached ) ) return cached;
		UiModel? model;
		try
		{
			EnsureInitialized();
			var key = RootNameKey( name );
			if ( bindings.TryGetValue( key, out var root ) && root.RootNodeName == name ) model = root;
			else if ( LanguageModel( key ) is { } banner && banner.RootNodeName == name ) model = banner;
			else
			{
				try { model = GetAsset( name ); }
				catch ( FileNotFoundException ) when ( customLoad is not null )
				{
					model = customLoad( name );
					Register( model );
					if ( !IsSpecialShadowAsset( model ) ) requests[model.RootNodeName] = model;
				}
			}
		}
		catch ( Exception exception ) when ( exception is not UiModelBindingException && exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException )
		{
			Log?.Warning( $"Original UI model {name} unavailable: {exception.Message}" );
			model = null;
		}
		requests[name] = model;
		return model;
	}

	/// <summary>
	/// The current language's banner mesh with drawing key <paramref name="key"/> (<c>s_congrats</c>, <c>s_bkrupt</c>, <c>s_paused</c>), registered beside the ui.wad models;
	/// reloaded when the language changes. A banner whose key a ui.wad model already holds stays unavailable rather than replacing it.
	/// </summary>
	// [BIN:TPW-EXE:0x004893C0 language meshes] the UI start-up registers the language folder's .md2 files in the same registry as ui.wad's (0x489060, keyed by the root node name's hash)
	private UiModel? LanguageModel( int key )
	{
		RefreshLanguageModels();
		return languageBindings.GetValueOrDefault( key );
	}

	/// <summary>Reloads the banner meshes after a language change, forgetting the lookups made with the old ones.</summary>
	private void RefreshLanguageModels()
	{
		if ( !languageMeshes || !GameLanguage.IsSelected )
			return;
		if ( languageVersion != GameLanguage.Version )
		{
			EnsureInitialized();
			languageVersion = GameLanguage.Version;
			languageBindings.Clear();
			requests.Clear();
			IReadOnlyList<string> paths;
			try { paths = GameLanguage.Current.EnumerateMeshes(); }
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException )
			{
				Log?.Warning( $"Language meshes unavailable: {exception.Message}" );
				paths = Array.Empty<string>();
			}
			foreach ( var path in paths )
			{
				try
				{
					var model = UiModel.LoadHostPath( path );
					if ( bindings.ContainsKey( model.DrawingKey ) || !languageBindings.TryAdd( model.DrawingKey, model ) )
						Log?.Warning( $"Language mesh {path} shares drawing key 0x{unchecked((uint)model.DrawingKey):x8} with another UI model and is not registered." );
				}
				catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException or UnauthorizedAccessException )
				{
					Log?.Warning( $"Language mesh {path} unavailable: {exception.Message}" );
				}
			}
		}
	}

	private void EnsureInitialized()
	{
		if ( bindingFailure is not null ) throw bindingFailure;
		if ( initialized ) return;
		foreach ( var path in FileSystem.GetFiles( "/ui" ).Where( path => path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
		{
			UiModel model;
			try { model = UiModel.LoadAssetPath( path ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException )
			{
				Log?.Warning( $"Original UI asset {path} unavailable: {exception.Message}" );
				continue;
			}
			Register( model );
		}
		initialized = true;
	}

	private void Register( UiModel model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( assets.Count >= MaximumAssets ) throw new InvalidDataException( "UI asset catalogue exceeds its resource limit." );
		if ( !IsSpecialShadowAsset( model ) )
		{
			if ( bindings.TryGetValue( model.DrawingKey, out var existing ) )
			{
				var descriptions = new[] { $"{existing.Name} ('{existing.RootNodeName}')", $"{model.Name} ('{model.RootNodeName}')" };
				throw bindingFailure = new UiModelBindingException( $"Duplicate original UI drawing key 0x{unchecked((uint)model.DrawingKey):x8}: {string.Join( ", ", descriptions.OrderBy( value => value, StringComparer.Ordinal ) )}.", model.DrawingKey );
			}
			bindings.Add( model.DrawingKey, model );
		}
		requests.Clear();
		assets.Add( model );
	}

	/// <summary>PEF code:0x13c164..0x13c190 excludes this file from ordinary registration; it remains an explicit shadow asset.</summary>
	public static bool IsSpecialShadowAsset( UiModel model ) => string.Equals( model.Name, "w_small_shadow", StringComparison.OrdinalIgnoreCase );
}
