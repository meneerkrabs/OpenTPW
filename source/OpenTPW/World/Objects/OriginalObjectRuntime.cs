namespace OpenTPW;

/// <summary>Snapshot for the economy/UI slices: what the ride's own script reports.</summary>
public readonly record struct OriginalObjectState( bool IsOpen, bool IsRunning, bool IsBroken, int OnRide, int CompletedCycles, RideVMState ScriptState );

/// <summary>
/// CPU part of a placed original object: its catalog entry, model hierarchy and animation channels, and
/// its original RSE script in a <see cref="RideVM"/> with <see cref="OriginalObjectEffects"/>. No GPU
/// resources; <see cref="OriginalObject"/> renders it. Used directly by the corpus tests.
/// </summary>
public sealed class OriginalObjectRuntime
{
	private readonly Dictionary<string, ModelAnimation?> clips = new( StringComparer.OrdinalIgnoreCase );
	private readonly SortedSet<string> unresolvedAnimations = new( StringComparer.Ordinal );
	private bool wasRunning;
	private bool stopped;

	/// <param name="world">The park's script world; it also allocates the attraction id. Null: a world of its own.</param>
	public OriginalObjectRuntime( ObjectCatalogEntry entry, RideScriptWorld? world = null, int? seed = null, bool open = true )
	{
		ArgumentNullException.ThrowIfNull( entry );
		Entry = entry;
		world ??= new RideScriptWorld();
		Visitors = CreateVisitorBridge( entry, world.AllocateAttractionId() );
		Model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		if ( Model.Kind != ModelFileKind.Geometry )
			throw new InvalidDataException( $"{entry.ModelPath} is not a geometry model." );
		// Fixed items keep their last vertex pose when a clip ends (ObjectAnimator.UpdateVertices).
		Animator = new ObjectAnimator( Model, keepsPoseOnClipChange: entry.IsFixedItem );
		if ( entry.ScriptPath != null )
		{
			using var stream = entry.FileSystem.OpenRead( entry.ScriptPath );
			Script = new RideVM( stream, new RideVMOptions
			{
				Effects = new OriginalObjectEffects( this ),
				World = world,
				Seed = seed,
				SourceName = entry.ScriptPath,
				ResolveScript = ResolveChildScript
			} );
			// Upgrade level 0 values from the .sam layers; units of InitDuration follow Info.DurationUnit (not verified).
			// [DATA:<object>.sam:Upgrades[0].InitCapacity]
			SetVariable( nameof( RideVariables.VAR_CAPACITY ), entry.InitialCapacity );
			// [DATA:<object>.sam:Upgrades[0].InitDuration] [APPROX:RIDES-016] written raw; unit per Info.DurationUnit unverified — evidence needed: binary conversion of InitDuration
			SetVariable( nameof( RideVariables.VAR_DURATION ), entry.Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "InitDuration" ) ?? 0 );
			SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), open ? 0 : 1 );
			Visitors.Attach( Script, () => !stopped && IsOpen );
		}
	}

	public ObjectCatalogEntry Entry { get; }
	public ModelFile Model { get; }
	public ObjectAnimator Animator { get; }
	/// <summary>The object's original script, or null for the few objects without one.</summary>
	public RideVM? Script { get; }
	/// <summary>
	/// Visitor side of the script (guests slice, docs/GUESTS.md): queue, VAR_LETMEON/LETMEOFF host protocol and
	/// the visitor opcodes. Register it with <see cref="GuestSimulation"/> once its entrance/exit cells are set.
	/// </summary>
	public RideVisitorBridge Visitors { get; }
	/// <summary>Guests may choose this object (.sam <c>Info.IsChoosable</c>, category defaults included).</summary>
	public bool IsAttraction => Script != null && Entry.IsChoosable && !Entry.IsFixedItem && !Entry.IsTool;
	public bool IsOpen => GetVariable( RideVariables.VAR_RIDECLOSED ) == 0;
	public int CompletedCycles { get; private set; }
	/// <summary>Animation requests answered with an original clip.</summary>
	public int AnimationsPlayed { get; private set; }
	/// <summary>(animation, variant) requests without a matching member, as "a/v".</summary>
	public IReadOnlyCollection<string> UnresolvedAnimations => unresolvedAnimations;

	public OriginalObjectState State => new( IsOpen,
		GetVariable( RideVariables.VAR_RUNNING ) != 0,
		GetVariable( RideVariables.VAR_BROKEN ) != 0,
		GetVariable( RideVariables.VAR_ONRIDE ),
		CompletedCycles,
		Script?.State ?? RideVMState.Halted );

	public void Open()
	{
		if ( !stopped )
			SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), 0 );
	}

	public void Close()
	{
		if ( !stopped )
			SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), 1 );
	}

	/// <summary>A common variable by name (scripts declare different subsets); 0 when the script has none.</summary>
	public int GetVariable( RideVariables variable )
	{
		var index = Script?.GetVariableIndex( variable.ToString() ) ?? -1;
		return index < 0 ? 0 : Script!.Variables[index];
	}

	/// <summary>Writes a script variable by name (host inputs such as VAR_LETMEON); false when the script has none.</summary>
	public bool SetVariable( string name, int value )
	{
		var index = Script?.GetVariableIndex( name ) ?? -1;
		if ( index < 0 )
			return false;
		Script!.Variables[index] = value;
		return true;
	}

	/// <summary>One fixed simulation tick: the script slice, then the animation channels.</summary>
	public void Simulate( double deltaSeconds )
	{
		if ( stopped )
			return;
		Visitors.HostStep();
		Script?.Advance( deltaSeconds );
		Animator.Advance( deltaSeconds );
		var running = GetVariable( RideVariables.VAR_RUNNING ) != 0;
		// [APPROX:RIDES-023] A completed cycle = VAR_RUNNING 1 → 0 — evidence needed: original ride-cycle/income accounting
		if ( wasRunning && !running )
			CompletedCycles++;
		wasRunning = running;
	}

	public void Stop()
	{
		stopped = true;
		Visitors.ReleaseAll();
		Script?.Stop();
		Animator.StopAll();
	}

	/// <summary>
	/// Attraction data for guests from the .sam layers: kind by category (toilets via
	/// <c>UsageInfo.ProvidesRelief</c>), needs from <c>UsageInfo.HungerEffect</c>/<c>ThirstEffect</c>/
	/// <c>ProvidesRelief</c>, price <c>UsageInfo.InitPricePerUse</c> (rides charge none: TPW takes admission),
	/// capacity <c>Upgrades[0].InitCapacity</c>, excitement and attraction value.
	/// </summary>
	public static RideVisitorBridge CreateVisitorBridge( ObjectCatalogEntry entry, int attractionId )
	{
		var settings = entry.Settings;
		// [DATA:<object>.sam:UsageInfo.ProvidesRelief/HungerEffect/ThirstEffect/InitPricePerUse/ExcitementLevel, Info.AttractionValue]
		var relief = settings.GetBool( "UsageInfo.ProvidesRelief" );
		var kind = entry.Category switch
		{
			ObjectCategory.Ride => RideVisitorKind.Ride,
			ObjectCategory.Shop => RideVisitorKind.Shop,
			ObjectCategory.Sideshow => RideVisitorKind.Sideshow,
			_ => RideVisitorKind.Facility
		};
		var needs = GuestNeeds.None;
		if ( settings.GetInt( "UsageInfo.HungerEffect" ) > 0 && entry.Category == ObjectCategory.Shop )
			needs |= GuestNeeds.Hunger;
		if ( settings.GetInt( "UsageInfo.ThirstEffect" ) > 0 && entry.Category == ObjectCategory.Shop )
			needs |= GuestNeeds.Thirst;
		if ( relief )
			needs |= GuestNeeds.Toilet;
		var price = kind == RideVisitorKind.Ride ? 0 : settings.GetInt( "UsageInfo.InitPricePerUse" );
		return new RideVisitorBridge( attractionId, entry.DisplayName, kind, entry.InitialCapacity,
			settings.GetInt( "UsageInfo.ExcitementLevel" ), settings.GetInt( "Info.AttractionValue" ), needs, price );
	}

	private RideScriptFile? ResolveChildScript( string fileName )
	{
		var member = Entry.Scripts.FirstOrDefault( script => string.Equals( Path.GetFileName( script ), fileName, StringComparison.OrdinalIgnoreCase ) );
		if ( member == null )
			return null;
		using var stream = Entry.FileSystem.OpenRead( member );
		return new RideScriptFile( stream );
	}

	/// <summary>Plays (animation, variant) on a channel; returns the clip length in milliseconds or 0 when it has no member.</summary>
	internal int PlayAnimation( int animation, int variant, int channel, bool loop )
	{
		var file = Entry.ResolveAnimation( animation, variant );
		var clip = file == null ? null : LoadClip( file.Path );
		if ( clip == null )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			return 0;
		}
		// Scripts re-issue LOOPANIM for a loop that already runs (Bouncy every 500 ms); keep it running.
		// [APPROX:RIDES-004] Re-issued LOOPANIM of the running loop continues instead of restarting — evidence needed: capture of the Belly Bounce idle loop
		if ( loop && Animator.IsChannelLooping( channel, file!.Name ) )
			return 0;
		try
		{
			var seconds = Animator.Play( channel, clip, file!.Name, loop );
			AnimationsPlayed++;
			return loop ? 0 : (int)Math.Round( seconds * 1000 );
		}
		catch ( InvalidDataException )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			clips[file!.Path] = null;
			return 0;
		}
	}

	private ModelAnimation? LoadClip( string path )
	{
		if ( clips.TryGetValue( path, out var clip ) )
			return clip;
		try
		{
			var model = ObjectAssets.LoadModel( Entry.FileSystem, path );
			clip = model.Kind == ModelFileKind.Animation ? model.Clip : null;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log?.Warning( $"Original object animation {path} could not be read: {exception.Message}" );
			clip = null;
		}
		clips[path] = clip;
		return clip;
	}
}

/// <summary>
/// Generic effects for every original object script. Animation opcodes play the original clips through
/// <see cref="ObjectAnimations"/>: TRIGANIM/WAITANIM/TRIGWAITANIM/TRIGANIMSPEED on the main channel (reporting the
/// clip length for waits), LOOPANIM looping on the main channel, TRIGANIM_CH/LOOPANIM_CH on the channel given by
/// their last operand, GETANIM_CH 1 while that channel plays, FLUSHANIM stops all channels. Visitor opcodes
/// go to <see cref="OriginalObjectRuntime.Visitors"/>. Everything else (sounds, EVENT, objects/particles, ride-type
/// controllers TOUR/BUMP/COAST, park clock, screams, reverb) stays an unimplemented effect.
/// Visitor opcodes go to the object's <see cref="RideVisitorBridge"/> (guests slice).
/// </summary>
public sealed class OriginalObjectEffects : IRideScriptEffects
{
	/// <summary>Channel of the plain animation opcodes, kept apart from the numbered _CH channels (sideshows use _CH channels 0–2 next to LOOPANIM).</summary>
	// [APPROX:RIDES-005] Plain animation opcodes use a channel separate from the _CH channels — evidence needed: binary or sideshow capture (TRIGANIM_CH ... 0 next to LOOPANIM)
	public const int MainChannel = -1;

	private readonly OriginalObjectRuntime ride;

	public OriginalObjectEffects( OriginalObjectRuntime ride ) => this.ride = ride;

	public int Perform( RideEffectCall call )
	{
		switch ( call.Opcode )
		{
			case Opcode.TRIGANIM:
			case Opcode.WAITANIM:
			case Opcode.TRIGWAITANIM:
			// [APPROX:RIDES-007] TRIGANIMSPEED plays at normal speed; its 4th operand (e.g. 4000) is ignored — evidence needed: binary semantics of TRIGANIMSPEED
			case Opcode.TRIGANIMSPEED:
				return ride.PlayAnimation( call.Argument( 0 ), call.Argument( 1 ), MainChannel, loop: false );
			case Opcode.LOOPANIM:
				return ride.PlayAnimation( call.Argument( 0 ), call.Argument( 1 ), MainChannel, loop: true );
			case Opcode.TRIGANIM_CH:
				return ride.PlayAnimation( call.Argument( 0 ), call.Argument( 1 ), Math.Max( 0, call.Argument( 3 ) ), loop: false );
			case Opcode.LOOPANIM_CH:
				return ride.PlayAnimation( call.Argument( 0 ), call.Argument( 1 ), Math.Max( 0, call.Argument( 2 ) ), loop: true );
			case Opcode.GETANIM_CH:
				// [APPROX:RIDES-006] GETANIM_CH returns 1 while the channel plays, else 0 — evidence needed: binary semantics of GETANIM_CH
				return ride.Animator.IsChannelPlaying( call.Argument( 1 ) ) ? 1 : 0;
			case Opcode.FLUSHANIM:
				ride.Animator.StopAll();
				return 0;
		}
		if ( ride.Visitors.TryPerform( call, out var visitorResult ) )
			return visitorResult;
		return UnimplementedRideScriptEffects.Instance.Perform( call );
	}
}
