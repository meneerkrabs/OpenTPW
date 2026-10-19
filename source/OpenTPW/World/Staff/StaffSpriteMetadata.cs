namespace OpenTPW;

/// <summary>Owner and draw outcomes that never repair or replace VM words.</summary>
public enum StaffSpriteDiagnostic
{
	None, MetadataUnavailable, InvalidPerformance, InvalidAngleArithmetic, InvalidVm, SourceAbsent,
	StaleIdentity, Retired, WorldRouteNotImplemented, NoOutput, FrameSuppressed, InvalidSlot, MissingSlot,
	InvalidFrame, InvalidDirection, UnsupportedTransform, UnsupportedResource, InvalidPose,
	TextureUnavailable, ResourceMismatch
}

public readonly record struct StaffSpritePerformanceMetadata( int Program, int Slot, int FrameCount, int Intro, int Hold );

/// <summary>A frozen CPU bank. Neither the input collections nor a renderer can change its metadata.</summary>
public sealed class StaffSpriteBankMetadata
{
	private readonly SpriteAnimation[] slots;
	private readonly GuestAtlasFrame[] frames;
	private readonly SpritePerformance[] performances;
	public uint Kind { get; }
	public uint Variant { get; }
	public int Atlas { get; }
	public string Identity { get; }
	public string Layout { get; }
	public int Width { get; }
	public int Height { get; }
	public bool Available { get; }
	public bool Multipart { get; }
	public int Slot0FrameCount => Available && slots.Length > 0 ? slots[0].FramesPerDirection : 0;
	public int PerformanceCount { get; }
	public IReadOnlyList<SpritePerformance> Performances { get; }
	public IReadOnlyList<SpriteAnimation> Slots { get; }
	public IReadOnlyList<GuestAtlasFrame> Frames { get; }

	public StaffSpriteBankMetadata( uint kind, uint variant, int atlas, string identity, int width, int height,
		IEnumerable<SpriteAnimation> slots, IEnumerable<GuestAtlasFrame> frames,
		IEnumerable<SpritePerformance>? performances = null, int performanceCount = 0, bool multipart = false )
	{
		Kind = kind; Variant = variant; Atlas = atlas; Identity = identity;
		Width = width; Height = height; Multipart = multipart;
		this.slots = slots.ToArray(); this.frames = frames.ToArray();
		this.performances = performances?.ToArray() ?? Array.Empty<SpritePerformance>();
		Slots = Array.AsReadOnly( this.slots ); Frames = Array.AsReadOnly( this.frames );
		Performances = Array.AsReadOnly( this.performances ); PerformanceCount = performanceCount;
		Available = Validate();
		// A value identity for diagnostics and test/resource matching; this contains no native pointer or pixel data.
		Layout = $"{width}:{height}:{multipart};" + string.Join( ";", this.slots.Select( s => $"{s.Slot},{s.FirstFrame},{s.FramesPerDirection},{s.Directions}" ) )
			+ ";" + string.Join( ";", this.frames.Select( f => $"{f.X},{f.Y},{f.Width},{f.Height},{f.OriginX},{f.OriginY},{f.PixelsPerUnitX},{f.PixelsPerUnitY}" ) );
	}

	public static StaffSpriteBankMetadata Unavailable( uint kind, uint variant, string identity ) =>
		new( kind, variant, -1, identity, 0, 0, Array.Empty<SpriteAnimation>(), Array.Empty<GuestAtlasFrame>() );

	public static StaffSpriteBankMetadata FromAtlas( uint kind, uint variant, int index, string identity, GuestSpriteAtlas atlas ) =>
		new( kind, variant, index, identity, atlas.Width, atlas.Height, atlas.Animations.Animations, atlas.Frames,
			atlas.Animations.Performances, atlas.Animations.PerformanceCount );

	private bool Validate()
	{
		if ( Width <= 0 || Height <= 0 || slots.Length != SpriteAnimationFile.SlotCount || frames.Length == 0 )
			return false;
		for ( var index = 0; index < slots.Length; index++ )
		{
			var slot = slots[index];
			if ( slot.Slot != index || slot.FirstFrame < 0 || slot.FramesPerDirection < 0 || slot.Directions < 0 || slot.Directions > 8 )
				return false;
			if ( slot.IsEmpty )
				continue;
			if ( slot.Directions == 0 || (long)slot.FirstFrame + (long)slot.FramesPerDirection * slot.Directions > frames.Length )
				return false;
		}
		if ( slots[0].IsEmpty )
			return false;
		return frames.All( f => f.X >= 0 && f.Y >= 0 && f.Width > 0 && f.Height > 0
			&& (long)f.X + f.Width <= Width && (long)f.Y + f.Height <= Height );
	}

	/// <summary>Validate the exact entry, including holes. Never clamp an invalid entry to another performance.</summary>
	public bool TryPerformance( int index, out StaffSpritePerformanceMetadata performance )
	{
		performance = default;
		if ( !Available || PerformanceCount is < 1 or > 4 || (uint)index >= 4 || index >= performances.Length )
			return false;
		var entry = performances[index];
		if ( entry.Program is < 0 or > 3 || entry.Slot < 0 || entry.Slot >= slots.Length || slots[entry.Slot].IsEmpty
			|| entry.Intro < 0 || entry.Hold < 0 )
			return false;
		performance = new( entry.Program, entry.Slot, slots[entry.Slot].FramesPerDirection, entry.Intro, entry.Hold );
		return true;
	}

	/// <summary>Textures may be replaced only for the same frozen bank and exact packed frame layout.</summary>
	public bool Matches( GuestSpriteAtlas atlas ) => Available
		&& string.Equals( Path.GetFileNameWithoutExtension( Identity ), atlas.Name, StringComparison.OrdinalIgnoreCase )
		&& Width == atlas.Width && Height == atlas.Height
		&& slots.SequenceEqual( atlas.Animations.Animations ) && frames.SequenceEqual( atlas.Frames );
}

/// <summary>One immutable level catalog, built before any GPU upload and never hot-filled after resource recovery.</summary>
public sealed class StaffSpriteMetadataCatalog
{
	private readonly Dictionary<(uint Kind, uint Variant), StaffSpriteBankMetadata> banks;
	private readonly Dictionary<uint, StaffSpriteBankMetadata[]> roles;
	private readonly Dictionary<uint, StaffSpriteBankMetadata> missingRoles;
	public IReadOnlyList<StaffSpriteBankMetadata> Banks { get; }

	public StaffSpriteMetadataCatalog( StaffSpriteSet sprites ) : this( sprites.MetadataBanks ) { }

	public StaffSpriteMetadataCatalog( IEnumerable<StaffSpriteBankMetadata> banks )
	{
		var copy = banks.ToArray();
		missingRoles = Enum.GetValues<StaffType>().Select( KindOf ).ToDictionary( kind => kind,
			kind => StaffSpriteBankMetadata.Unavailable( kind, 0, $"missing-{kind}-0" ) );
		this.banks = copy.ToDictionary( bank => (bank.Kind, bank.Variant) );
		roles = copy.GroupBy( bank => bank.Kind ).ToDictionary( group => group.Key, group => group.OrderBy( bank => bank.Variant ).ToArray() );
		Banks = Array.AsReadOnly( copy );
	}

	public static uint KindOf( StaffType type ) => type switch
	{
		StaffType.Entertainer => 4, StaffType.Handyman => 5, StaffType.Mechanic => 6,
		StaffType.Guard => 7, StaffType.Researcher => 8,
		_ => throw new ArgumentOutOfRangeException( nameof( type ) )
	};

	public StaffSpriteBankMetadata Select( StaffType type, int variantKey )
	{
		var kind = KindOf( type );
		if ( !roles.TryGetValue( kind, out var role ) || role.Length == 0 )
			return Resolve( kind, 0 );
		return role[StaffSpriteRules.BankIndex( variantKey & int.MaxValue, role.Length )];
	}

	public StaffSpriteBankMetadata Resolve( uint kind, uint variant ) => banks.TryGetValue( (kind, variant), out var bank )
		? bank : variant == 0 && missingRoles.TryGetValue( kind, out var missing ) ? missing
		: StaffSpriteBankMetadata.Unavailable( kind, variant, $"missing-{kind}-{variant}" );

	public StaffSpriteFrameResolution ResolveFrame( StaffSpriteDrawRecord record, float cameraYaw = 0 ) =>
		StaffSpriteFrameResolver.Resolve( this, record, cameraYaw );
}
