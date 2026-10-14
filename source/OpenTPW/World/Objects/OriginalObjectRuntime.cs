namespace OpenTPW;

/// <summary>Snapshot for the economy/UI slices: what the ride's own script reports.</summary>
public readonly record struct OriginalObjectState( bool IsOpen, bool IsRunning, bool IsBroken, int OnRide, int CompletedCycles, RideVMState ScriptState );

/// <summary>
/// CPU part of a placed original object: its catalog entry, model hierarchy and animation channels, and
/// its original RSE script in a <see cref="RideVM"/> with <see cref="OriginalObjectEffects"/>. No GPU
/// resources; <see cref="OriginalObject"/> renders it. Used directly by the corpus tests.
/// </summary>
public sealed partial class OriginalObjectRuntime
{
	private readonly Dictionary<string, ModelAnimation?> clips = new( StringComparer.OrdinalIgnoreCase );
	private readonly SortedSet<string> unresolvedAnimations = new( StringComparer.Ordinal );
	private bool wasRunning;
	private bool stopped;
	private readonly OriginalObjectEffects effects;

	/// <param name="world">The park's script world; it also allocates the attraction id. Null: a world of its own.</param>
	public OriginalObjectRuntime( ObjectCatalogEntry entry, RideScriptWorld? world = null, int? seed = null, bool open = true )
		: this( entry, world, seed, open, attractionId: null )
	{
	}

	/// <summary>
	/// The constructor body. <paramref name="attractionId"/> null allocates the next id from the world; a restored
	/// id (park-resume restore, <see cref="CreateForRestore"/>) is taken as this object's id and the world's
	/// frontier adopts it — no new allocation, no hole closed.
	/// </summary>
	internal OriginalObjectRuntime( ObjectCatalogEntry entry, RideScriptWorld? world, int? seed, bool open, int? attractionId )
	{
		ArgumentNullException.ThrowIfNull( entry );
		Entry = entry;
		world ??= new RideScriptWorld();
		if ( attractionId is { } restored )
		{
			AttractionId = restored;
			world.AdoptAttractionId( restored );
		}
		else
			AttractionId = world.AllocateAttractionId();
		Visitors = CreateVisitorBridge( entry, AttractionId );
		RememberCatalogueDefaults();
		if ( CoasterDefinition.IsCoaster( entry.Settings ) )
		{
			Coaster = new CoasterRide( CoasterDefinition.FromSettings( entry.Settings ), world.Coasters, entry.DisplayName );
			Visitors.ExcitementSource = () => Coaster.GuestExcitement;
		}
		Model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		if ( Model.Kind != ModelFileKind.Geometry )
			throw new InvalidDataException( $"{entry.ModelPath} is not a geometry model." );
		Visitors.Heads = RideHeadSpace.Create( Model, AttractionId );
		// Fixed items keep their last vertex pose when a clip ends (ObjectAnimator.UpdateVertices).
		Animator = new ObjectAnimator( Model, keepsPoseOnClipChange: entry.IsFixedItem );
		effects = new OriginalObjectEffects( this );
		if ( entry.ScriptPath != null )
		{
			using var stream = entry.FileSystem.OpenRead( entry.ScriptPath );
			Script = new RideVM( stream, new RideVMOptions
			{
				Effects = effects,
				World = world,
				Seed = seed,
				SourceName = entry.ScriptPath,
				ResolveScript = ResolveChildScript
			} );
			// Upgrade level 0 values from the .sam layers.
			// [DATA:<object>.sam:Upgrades[0].InitCapacity]
			SetVariable( nameof( RideVariables.VAR_CAPACITY ), entry.InitialCapacity );
			var level0 = entry.Upgrades.FirstOrDefault( level => level.Level == 0 );
			// [BIN:TPW-EXE:0x4db565 ride init duration] [DATA:<object>.sam:Upgrades[0].InitDuration] written only when above 0: the low byte, clamped to [UsageInfo.MinDuration, UsageInfo.MaxDuration] (type +0x12c/+0x130), into script variable 3 and ride +0x5c; DurationUnit is never read. The same clamp runs at ride settings (0x4dd720) and upgrade (0x4df8f0).
			if ( ClampInitDuration( entry.Settings, level0?.GetInt( "InitDuration" ) ?? 0 ) is { } duration )
				SetVariable( nameof( RideVariables.VAR_DURATION ), duration );
			// [BIN:TPW-EXE:0x4db51c ride init speed] [DATA:<object>.sam:Upgrades[0].InitSpeed] the script speed bias, written only when above 0 (setter 0x55a300).
			if ( level0?.GetInt( "InitSpeed" ) is > 0 and var speed )
				Script.SpeedBias = speed;
			SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), open ? 0 : 1 );
			// [BIN:TPW-EXE:0x441970 guest predicate twin] the controller exists, +0x140 == 0, +0x3C bit 0 set and bit 1 clear, and it is not the coaster being edited (0x790fec); callers: 0x4dd920 counts test (Mac 0xdc834, X15), 0x4df290 reopen guard (0xde654), 0x4ffff0 queue state 11 (0xed244: a coaster (track type 3, 0x4e2b50) failing it gets 0x50be80(13), unlink 0x4ddd20, dismissal 0x5012f0, state 6); the open routine 0x4df390 logs "Opening non-openable ride" up to 5 times and opens anyway
			// [APPROX:COAST-024] guests do not join or board a coaster whose track is not complete (the station counts as closed to them); the +0x140 == 0 test and the not-being-edited exclusion are not modelled (OpenTPW has no such state: an edit un-completes the track) and the state 11 thought-13 exit (GUESTS.md) belongs to the guest and queue owner
			Visitors.Attach( Script, () => !stopped && IsOpen && (Coaster?.IsOperational ?? true) );
		}
	}

	/// <summary>
	/// The ride-init duration rule (0x4db565..0x4db64f): null when <paramref name="initDuration"/> is not above 0 (nothing is written),
	/// else its low byte clamped to [MinDuration, MaxDuration] of the object type (a byte below the minimum takes the minimum, one above the maximum the maximum).
	/// </summary>
	internal static int? ClampInitDuration( ObjectSettings settings, int initDuration )
	{
		if ( initDuration <= 0 )
			return null;
		var value = initDuration & 0xFF;
		var min = settings.GetInt( "UsageInfo.MinDuration" );
		var max = settings.GetInt( "UsageInfo.MaxDuration" );
		if ( value < min )
			return min & 0xFF;
		if ( max < value )
			return max & 0xFF;
		return value;
	}

	public ObjectCatalogEntry Entry { get; }
	/// <summary>The attraction id this runtime's bridge carries (allocated at construction or restored; the allocator frontier moves past it).</summary>
	public int AttractionId { get; }
	/// <summary>The coaster controller of a coaster station (<c>Bumper.WhichTrackType 3</c>), else null.</summary>
	public CoasterRide? Coaster { get; }
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

	/// <summary>Any script variable by its declared name (e.g. the gate's <c>VAR_STATUS</c>); 0 when the script has none.</summary>
	public int GetVariableByName( string name )
	{
		var index = Script?.GetVariableIndex( name ) ?? -1;
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
		// [EXT:vm-fault-containment] a faulted root script stops its object: riders, trains, controller and sounds are released
		if ( Script is { State: RideVMState.Faulted } )
		{
			Stop();
			return;
		}
		Visitors.HostStep();
		Script?.Advance( deltaSeconds );
		if ( Script is { State: RideVMState.Faulted } )
		{
			Stop();
			return;
		}
		Coaster?.Advance( deltaSeconds );
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
		eventOrigin = null;
		ClearParticleRequests();
		effects.StopSounds();
		Visitors.ReleaseAll();
		if ( Coaster != null )
		{
			Coaster.ClearTrains();
			Coaster.World.Remove( Coaster );
		}
		Script?.Stop();
		Animator.StopAll();
	}

	// ---- Park-resume snapshot ----

	/// <summary>
	/// Fresh runtime for a park-resume restore: identical to the constructor but with the saved attraction id (no
	/// allocation; the world's frontier adopts it, holes below included). Pass the saved script seed and open flag,
	/// restore the state with <see cref="RestoreSnapshot"/>, then rebind script ids with
	/// <see cref="RideScriptWorld.RestoreSnapshot"/>. A coaster station's controller state is restored onto its
	/// <see cref="Coaster"/> by the coaster part (<see cref="CoasterSave"/>), after this.
	/// </summary>
	public static OriginalObjectRuntime CreateForRestore( ObjectCatalogEntry entry, RideScriptWorld world, int? seed, bool open, int attractionId )
	{
		ArgumentNullException.ThrowIfNull( world );
		return new OriginalObjectRuntime( entry, world, seed, open, attractionId );
	}

	/// <summary>
	/// Explicit state of this runtime (see <see cref="OriginalObjectRuntimeSnapshot"/>). The script's instruction
	/// state lives in the <see cref="RideScriptWorld"/> snapshot; this carries its identity so the world restore can
	/// check the candidate against it. A coaster station's controller state (track, trains, riders) is the coaster
	/// part's, captured beside this through <see cref="CoasterSave"/>.
	/// </summary>
	public OriginalObjectRuntimeSnapshot CaptureSnapshot()
	{
		// [APPROX:HEADS-005] child/sound head tables are not saved; refuse rather than lose a rider's head
		if ( Visitors.ChildHeadsOccupied() )
			throw new InvalidOperationException( $"'{Entry.ArchivePath}': a child or sound script holds a rider head, which the park-resume snapshot cannot store." );
		if (effects.HasLiveEventLoops)
			throw new NotSupportedException("An attraction is still playing a looping EVENT or repair-hide sound. Live audio cannot be saved; stop the owning object or sound first.");
		return new OriginalObjectRuntimeSnapshot
		{
			AttractionId = AttractionId,
			EntryArchivePath = Entry.ArchivePath,
			EntryInfoId = Entry.InfoId,
			ModelPath = Entry.ModelPath,
			HasScript = Script != null,
			ScriptIdentityHash = Script == null ? 0 : RideScriptIdentity.Hash( Script.Script ),
			Animator = Animator.CaptureSnapshot(),
			Heads = Visitors.Heads?.CaptureSnapshot(),
			Stopped = stopped,
			WasRunning = wasRunning,
			CompletedCycles = CompletedCycles,
			AnimationsPlayed = AnimationsPlayed,
			UnresolvedAnimations = unresolvedAnimations.ToArray(),
			HeldScream = effects.HoldsScream,
			OwnedScreamCount = effects.OwnedScreamCount,
			StoppedScriptFault = stopped && Script is { State: RideVMState.Faulted } ? Script.FaultMessage : null
		};
	}

	/// <summary>
	/// Restores a snapshot into this fresh candidate (built by <see cref="CreateForRestore"/> with the snapshot's
	/// attraction id, seed and open flag). Validates the entry and script identity first and throws
	/// <see cref="InvalidDataException"/> — without changing the candidate — on a mismatch. A stopped snapshot
	/// replays <see cref="Stop"/>'s teardown (a fresh
	/// candidate has no riders, chains or trains to lose). Returns the explicit gaps: live audio state (held/owned
	/// scream chains) cannot cross a restart and is reported, never silently dropped.
	/// </summary>
	public IReadOnlyList<string> RestoreSnapshot( OriginalObjectRuntimeSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		snapshot.ValidateSnapshot();
		if ( !string.Equals( Entry.ArchivePath, snapshot.EntryArchivePath, StringComparison.Ordinal ) || Entry.InfoId != snapshot.EntryInfoId || !string.Equals( Entry.ModelPath, snapshot.ModelPath, StringComparison.Ordinal ) )
			throw new InvalidDataException( $"the snapshot belongs to '{snapshot.EntryArchivePath}' ({snapshot.EntryInfoId}, {snapshot.ModelPath}), this runtime is '{Entry.ArchivePath}' ({Entry.InfoId}, {Entry.ModelPath})." );
		if ( ( Script != null ) != snapshot.HasScript )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': {(snapshot.HasScript ? "the snapshot has a script, this entry has none" : "the snapshot has no script, this entry has one")}." );
		if ( AttractionId != snapshot.AttractionId )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': attraction id {AttractionId} does not match the snapshot's {snapshot.AttractionId}; construct the candidate with CreateForRestore." );
		if ( snapshot.HasScript && RideScriptIdentity.Hash( Script!.Script ) != snapshot.ScriptIdentityHash )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': the entry's script does not match the snapshot's script identity." );
		if ( ( Visitors.Heads != null ) != ( snapshot.Heads != null ) )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': head-space presence does not match the snapshot." );

		var gaps = new List<string>();
		if ( snapshot.Stopped )
		{
			// Stop()'s teardown, replayed on the fresh candidate: the script leaves the world (the saved world
			// snapshot does not contain it), the visitors and animator are cleared.
			stopped = true;
			effects.StopSounds();
			Visitors.ReleaseAll();
			if ( Coaster != null )
			{
				Coaster.ClearTrains();
				Coaster.World.Remove( Coaster );
			}
			Script?.Stop();
			Animator.StopAll();
			if ( snapshot.StoppedScriptFault is { } fault )
				Script!.RestoreStoppedFault( fault );
		}
		wasRunning = snapshot.WasRunning;
		CompletedCycles = snapshot.CompletedCycles;
		AnimationsPlayed = snapshot.AnimationsPlayed;
		unresolvedAnimations.Clear();
		foreach ( var unresolved in snapshot.UnresolvedAnimations )
			unresolvedAnimations.Add( unresolved );
		if ( snapshot.Heads != null )
			Visitors.Heads!.RestoreSnapshot( snapshot.Heads );
		Animator.RestoreSnapshot( snapshot.Animator, ResolveSnapshotClip );
		if ( snapshot.HeldScream )
			gaps.Add( $"{Entry.ArchivePath}: the script held a STARTSCREAM chain at capture; live audio cannot cross a restart, the handle is cleared" );
		if ( snapshot.OwnedScreamCount > 0 )
			gaps.Add( $"{Entry.ArchivePath}: {snapshot.OwnedScreamCount} owned scream chain(s) at capture; live audio cannot cross a restart, they are dropped" );
		return gaps;
	}

	/// <summary>Snapshot-clip resolver through this object's own archive and clip cache (no saved paths).</summary>
	private ResolvedAnimatorClip? ResolveSnapshotClip( AnimatorClipIdentity identity )
	{
		var file = Entry.ResolveAnimation( identity.AnimationId, identity.Variant );
		if ( file == null )
			return null;
		var clip = LoadClip( file.Path );
		return clip == null ? null : new ResolvedAnimatorClip( file.Name, clip );
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
			settings.GetInt( "UsageInfo.ExcitementLevel" ), settings.GetInt( "Info.AttractionValue" ), needs, price )
		{
			Parameters = CreateQueueParameters( entry ),
			// [DATA:<object>.sam:UsageInfo.SpecialIngredient] type +344: 0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar (the ice cream shops); only Sugar changes a guest here (the speed boost)
			SpecialIngredient = settings.GetInt( "UsageInfo.SpecialIngredient" ),
			// [DATA:<object>.sam:UsageInfo.ThirstEffect/HungerEffect/VomitEffect/HappinessEffect/GoldenTicketCost/ISIndoors, Upgrades[0].CostOfUpgrade, Info.Id] the type words the attraction score and the visit effects read (type +324/+328/+332/+336/+196/+276/+440, ORACLE.md F24)
			ThirstEffect = settings.GetInt( "UsageInfo.ThirstEffect" ),
			HungerEffect = settings.GetInt( "UsageInfo.HungerEffect" ),
			VomitEffect = settings.GetInt( "UsageInfo.VomitEffect" ),
			HappinessEffect = settings.GetInt( "UsageInfo.HappinessEffect" ),
			// [DATA:<object>.sam:UsageInfo.LitterEffect] type +0x154, added to the guest's litter meter by the use routine (tp.exe 0x004FE1E0)
			LitterEffect = settings.GetInt( "UsageInfo.LitterEffect" ),
			// [DATA:<object>.sam:UsageInfo.InitCostOfGoods] type +320, the price test's base (tp.exe 0x004FDE50; the key table order InitCostOfGoods, ThirstEffect, HungerEffect, VomitEffect, HappinessEffect)
			InitCostOfGoods = settings.Has( "UsageInfo.InitCostOfGoods" ) ? settings.GetInt( "UsageInfo.InitCostOfGoods" ) : 0,
			GoldenTicketCost = settings.GetInt( "UsageInfo.GoldenTicketCost" ),
			UpgradeCost = entry.Upgrades.FirstOrDefault( upgrade => upgrade.Level == 0 )?.GetInt( "CostOfUpgrade" ) ?? 0,
			IsIndoors = settings.GetInt( "UsageInfo.ISIndoors" ) != 0,
			TypeId = entry.InfoId,
			// [DATA:<object>.sam:UsageInfo.InitCostOfGoods/InitPricePerUse/InitChanceOfLoosing] the sideshow words of the join excitement (0xdff70); the level mirrors the live economy values over them
			Sideshow = new SideshowTerms( settings.GetInt( "UsageInfo.InitCostOfGoods" ), settings.GetInt( "UsageInfo.InitPricePerUse" ), settings.GetInt( "UsageInfo.InitChanceOfLoosing" ) ),
			// [DATA:<object>.sam:UsageInfo.EntryCellStandPosX/Y] the jungle Rides.sam default is (0.5, 0.5); 0xde1d8 checks the range [0, 1] (a value outside it keeps the default here)
			EntryCellStandPos = (StandFraction( settings, "UsageInfo.EntryCellStandPosX" ), StandFraction( settings, "UsageInfo.EntryCellStandPosY" ))
		};
	}

	private static float StandFraction( ObjectSettings settings, string key ) =>
		settings.Has( key ) && settings.GetFloat( key ) is var value and >= 0 and <= 1 ? value : 0.5f;

	/// <summary>
	/// Queue rules from the .sam layers (docs/reverse/QUEUE-plan.md §2, §4): HasQueue, RunsContinuously, the
	/// capacity-test bypass, and the limit inputs. CAP and DUR are clamped like the host writes them
	/// (<c>UsageInfo.Min/MaxCapacity</c>, <c>Min/MaxDuration</c>); SPEED starts at InitSpeed, so G = 1.
	/// </summary>
	// [DATA:<object>.sam:Info.HasQueue/RunsContinuously, Bumper.WhichTrackType, UsageInfo.Min/MaxCapacity, Min/MaxDuration, Upgrades[0].InitCapacity/InitDuration/InitSpeed/QueueWaitTimeConstant]
	// [BIN:STP-PPC:0x100E149C capacity bypass] track type 3, or type record +156 (Bumper.WhichTrackType) == 2, skips the ONRIDE < CAPACITY test
	// The compatibility factory seeds level0; bound ordinary TPW rides resolve their exact economy tier (CatalogueTier partial).
	public static QueueParameters CreateQueueParameters( ObjectCatalogEntry entry )
	{
		return QueueParametersFor( entry, entry.Upgrades.FirstOrDefault( upgrade => upgrade.Level == 0 ) );
	}

	private static QueueParameters QueueParametersFor( ObjectCatalogEntry entry, ObjectUpgradeLevel? level )
	{
		var settings = entry.Settings;
		static int Clamp( int value, int minimum, int maximum ) => maximum >= minimum && maximum > 0 ? Math.Clamp( value, minimum, maximum ) : value;
		var capacity = Clamp( level?.GetInt( "InitCapacity" ) ?? 0, settings.GetInt( "UsageInfo.MinCapacity" ), settings.GetInt( "UsageInfo.MaxCapacity" ) );
		var duration = Clamp( level?.GetInt( "InitDuration" ) ?? 0, settings.GetInt( "UsageInfo.MinDuration" ), settings.GetInt( "UsageInfo.MaxDuration" ) );
		var initSpeed = level?.GetInt( "InitSpeed" ) ?? 0;
		var trackType = settings.GetInt( "Bumper.WhichTrackType" );
		return new QueueParameters( entry.HasQueue, settings.GetBool( "Info.RunsContinuously" ), trackType is 2 or 3,
			level?.GetFloat( "QueueWaitTimeConstant" ) ?? 0, initSpeed, initSpeed, capacity, duration, level?.GetInt( "InitDuration" ) ?? 0 );
	}

	private RideScriptFile? ResolveChildScript( string fileName )
	{
		var member = Entry.Scripts.FirstOrDefault( script => string.Equals( Path.GetFileName( script ), fileName, StringComparison.OrdinalIgnoreCase ) );
		if ( member == null )
			return null;
		using var stream = Entry.FileSystem.OpenRead( member );
		return new RideScriptFile( stream );
	}

	/// <summary>
	/// Triggers (animation, variant) on a channel (0xa6cc0 with flags 0, or 1 to loop) and returns the trigger's
	/// result: the clip length in milliseconds, or <see cref="ObjectAnimator.MissingClipMilliseconds"/> when the
	/// object has no such member (the channel then keeps its clip).
	/// </summary>
	internal int TriggerAnimation( int animation, int variant, int channel, bool loop, float speed )
	{
		var file = Entry.ResolveAnimation( animation, variant );
		var clip = file == null ? null : LoadClip( file.Path );
		if ( clip == null )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			return Animator.Trigger( channel, null, "", animation, variant, loop, speed );
		}
		try
		{
			var result = Animator.Trigger( channel, clip, file!.Name, animation, variant, loop, speed );
			AnimationsPlayed++;
			return result;
		}
		catch ( InvalidDataException )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			clips[file!.Path] = null;
			return Animator.Trigger( channel, null, "", animation, variant, loop, speed );
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
/// Generic effects for every original object script. Animation opcodes drive the object's channels
/// (<see cref="ObjectAnimator"/>) with the original's operands: the plain opcodes channel 0, the <c>_CH</c> opcodes
/// their channel operand; triggers return the trigger's millisecond result, which <see cref="OpcodeHandlers.Animation"/>
/// turns into the script's waits; FLUSHANIM(_CH) clears only the deferred clip; GETANIM(_CH) report the AnimID or −1.
/// COAST goes to the station's <see cref="CoasterRide"/>. Visitor opcodes go to the object's
/// <see cref="RideVisitorBridge"/> (guests slice). EVENT and REPAIREFFECT play their original sounds (their other
/// effects stay unimplemented). TPW YEAR/MONTH/DAY/HOUR sample host civil time; MIN/SEC remain unimplemented.
/// Everything else (objects/particles, the ride-type controllers TOUR/BUMP,
/// screams, reverb) stays an unimplemented effect.
/// </summary>
public sealed class OriginalObjectEffects : IRideScriptEffects, IRideAnimationQuery, IRideCanonicalEffects
{
	/// <summary>Channel of the plain animation opcodes: channel 0, the same record a _CH opcode addresses with channel 0.</summary>
	// [BIN:STP-PPC:0x100A6CC0 animation trigger] the channel record is model +0x10 + channel × 0x38; TRIGANIM (0x100AFB10), WAITANIM (0x100AFC68), LOOPANIM (0x100AFD7C) and TRIGANIMSPEED (0x100B005C) pass channel 0, TRIGANIM_CH (0x100B01C4) its operand
	public const int MainChannel = OpcodeHandlers.Animation.MainChannel;

	private readonly OriginalObjectRuntime ride;
	/// <summary>The ride script's held scream (the original's handle, refused while one is held).</summary>
	private SoundChain? screamChain;
	private readonly List<SoundChain> ownedScreams = new();
	// Positioned EVENT and repair-hide flag-0x8 voices share the existing exact-runtime cleanup ledger.
	private readonly List<AudioVoice> ownedEventLoops = new();

	public OriginalObjectEffects( OriginalObjectRuntime ride ) => this.ride = ride;

	/// <summary>Stops this runtime's chains, including handles lost by a refused STARTSCREAM, when the object is removed.</summary>
	internal void StopSounds()
	{
		foreach ( var chain in ownedScreams )
			chain.Stop();
		ownedScreams.Clear();
		foreach (var voice in ownedEventLoops) voice.Stop();
		ownedEventLoops.Clear();
		screamChain = null;
	}

	/// <summary>Whether the script holds a STARTSCREAM chain (park-resume capture; live audio is a reported gap).</summary>
	internal bool HoldsScream => screamChain != null;

	/// <summary>Chains this effects object still owns (park-resume capture; live audio is a reported gap).</summary>
	internal int OwnedScreamCount => ownedScreams.Count;
	internal bool HasLiveEventLoops => ownedEventLoops.Any(voice => voice.Loops && !voice.IsFinished);

	public int Perform( RideEffectCall call )
	{
		switch ( call.Opcode )
		{
			case Opcode.YEAR:
			case Opcode.MONTH:
			case Opcode.DAY:
			case Opcode.HOUR:
				// [BIN:STP-PPC:0x100B2144 RSE civil time] YEAR/MONTH/DAY/HOUR use host time/localtime, not the park calendar (PPC-clock.md).
				if ( !call.VM.World.TryReadCivilTime( out var civil ) )
					break;
				return call.Opcode switch
				{
					Opcode.YEAR => civil.Year - 1900,
					Opcode.MONTH => civil.Month,
					Opcode.DAY => civil.Day,
					_ => civil.Hour
				};
			case Opcode.TRIGANIM:
			case Opcode.WAITANIM:
			case Opcode.TRIGWAITANIM:
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), MainChannel, loop: false, call.VM.ScriptSpeed );
			case Opcode.TRIGANIMSPEED:
				// [BIN:STP-PPC:0x100AFFC4 TRIGANIMSPEED] the clip runs at script speed × operand / 1000 (0x100B003C–0x100B0070); the handler also keeps the operand at script +228, whose reader is not traced
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), MainChannel, loop: false, RideAnimationTiming.SpeedOperand( call.VM.ScriptSpeed, call.Argument( 3 ) ) );
			case Opcode.LOOPANIM:
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), MainChannel, loop: true, call.VM.ScriptSpeed );
			case Opcode.TRIGANIM_CH:
			case Opcode.TRIGWAITANIM_CH:
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), Channel( call, 3 ), loop: false, call.VM.ScriptSpeed );
			case Opcode.WAITANIM_CH:
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), Channel( call, 2 ), loop: false, call.VM.ScriptSpeed );
			case Opcode.LOOPANIM_CH:
				return ride.TriggerAnimation( call.Argument( 0 ), call.Argument( 1 ), Channel( call, 2 ), loop: true, call.VM.ScriptSpeed );
			case Opcode.GETANIM:
				return ride.Animator.GetAnimation( MainChannel );
			case Opcode.GETANIM_CH:
				return ride.Animator.GetAnimation( Channel( call, 1 ) );
			case Opcode.FLUSHANIM:
				ride.Animator.Flush( MainChannel );
				return 0;
			case Opcode.FLUSHANIM_CH:
				ride.Animator.Flush( Channel( call, 0 ) );
				return 0;
		}
		if ( call.Opcode == Opcode.COAST && ride.Coaster != null )
			return ride.Coaster.Perform( call );
		if ( ride.Visitors.TryPerform( call, out var visitorResult ) )
			return visitorResult;
		// Routed EVENT and repair-hide sounds can use the placed origin; scream/show placement and reverb remain unimplemented.
		// [BIN:STP-PPC:0x100B19DC STARTSCREAM] the scream is held by the ride's script (the handle at the VM's +208); STOPSCREAM stops the handle, SINGLESCREAM is a one-shot with no handle
		if ( call.Opcode == Opcode.STARTSCREAM )
		{
			screamChain = GameAudio.StartScream( () => screamChain, call.Argument( 0 ), call.Argument( 1 ), null, call.VM.SpeedBias );
			ownedScreams.RemoveAll( chain => chain.IsStopped );
			if ( screamChain != null )
				ownedScreams.Add( screamChain );
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
		if ( call.Opcode == Opcode.STOPSCREAM )
		{
			screamChain?.Stop();
			screamChain = null;
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
		if ( call.Opcode == Opcode.SINGLESCREAM )
		{
			GameAudio.SingleScream( call.Argument( 0 ), (short)call.Argument( 1 ), null, call.VM.SpeedBias );
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
		if ( call.Opcode == Opcode.EVENT )
		{
			if (call.Argument(0) is 1 or 2) ride.ObserveParticleEvent(call);
			// [BIN:STP-PPC:0x100AE930 RSE EVENT effect switch] the type operand selects the sound category (3 level rides, 4 level ambient, 5 global rides, 6 global kids, 7 global staff, 8 global ambient, 9 global UI) and the id operand the event, submitted at the object's position
			var type = call.Argument(0);
			var voice = GameAudio.PlayRseEvent(type, unchecked((uint)call.Argument(2)), type is >= 3 and <= 9 ? ride.ReadEventOrigin() : null);
			ownedEventLoops.RemoveAll(loop => loop.IsFinished);
			if (voice is { Loops: true }) ownedEventLoops.Add(voice);
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
		if ( call.Opcode == Opcode.REPAIREFFECT )
		{
			// [BIN:STP-PPC:0x100B1E34 REPAIREFFECT] showing (value 1) starts a cat_kids repair sound (0x100ACC24, event 75 for the traced call); hiding (value 0) plays cat_rides 111 and 211 (0x100B1EC0/0x100B1F00) at the object's position
			var show = call.Argument( 0 );
			if ( show != 0 ) GameAudio.PlayRepairEffect( show );
			else
			{
				var voices = GameAudio.PlayRepairEffect( show, ride.ReadEventOrigin() );
				ownedEventLoops.RemoveAll( loop => loop.IsFinished );
				if ( voices.First is { Loops: true } ) ownedEventLoops.Add( voices.First );
				if ( voices.Second is { Loops: true } ) ownedEventLoops.Add( voices.Second );
			}
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
		return UnimplementedRideScriptEffects.Instance.Perform( call );
	}

	public int GetAnimationId( RideVM vm, int channel ) => ride.Animator.GetAnimationId( channel );

	public void AddCanonicalState( StateHasher hash ) => ride.Animator.AddCanonicalState( hash );

	/// <summary>The original indexes <c>object +16 + channel × 56</c> without a check; a negative channel has no record here.</summary>
	private static int Channel( RideEffectCall call, int operand )
	{
		var channel = call.Argument( operand );
		return channel >= 0 ? channel : throw new RideScriptException( $"{call.Opcode} channel {channel} is negative" );
	}
}
