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
	internal OriginalObjectEffects Effects => effects;
	private readonly RideScriptWorld scriptWorld;

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
		scriptWorld = world;
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
			Coaster = new CoasterRide( CoasterDefinition.FromCatalogEntry( entry ), world.Coasters, entry.DisplayName );
			Visitors.ExcitementSource = () => Coaster.GuestExcitement;
			// [BIN:TPW-EXE:0x4398cd coaster crash sound] two trains bumping hard play the global cat_rides crash sound 12 at the bump's whole-unit place
			Coaster.Crashed += place => GameAudio.PlayRidesAt( GameAudio.CoasterCrashEvent, new Vector3( place.X, place.Y, place.Z ) );
		}
		// A water ride (Bumper.WhichTrackType 2) or a go-kart ride (1) gets a bump-ride controller (docs/TRACKS.md).
		if ( BumperTemplates.ForRide( entry.Settings.GetInt( "Bumper.WhichTrackType" ), entry.Settings.GetInt( "Bumper.BumperType" ) ) is { } template )
		{
			Bump = new BumpController( template, world.NextWorldDraw, world.Bumpers ) { AttractionId = AttractionId };
			world.Bumpers.Add( Bump );
			// every family's effects are played: the karts' and bumper cars' cues, the boats' spray, engine voice and unload particle (0x547F50, 0x54A909, 0x54AE50)
			Bump.Effect += OnBumpEffect;
			// the loading beams need the head dummies of the vehicle mesh before any draw (0x549C60 reads them from the loaded model)
			var seatHeads = new Dictionary<int, bool[]?>();
			Bump.SeatHeadSource = variant =>
			{
				if ( !seatHeads.TryGetValue( variant, out var flags ) )
					seatHeads[variant] = flags = TrackRenderer.ReadSeatHeadFlags( entry, variant );
				return flags;
			};
			Visitors.ExcitementBase = Bump.ExcitementBase;
		}
		Model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		if ( Model.Kind != ModelFileKind.Geometry )
			throw new InvalidDataException( $"{entry.ModelPath} is not a geometry model." );
		Visitors.Heads = RideHeadSpace.Create( Model, world.NextWorldDraw );
		// [EXT:object-lights] the light dummies of the model (none in any shipped model), with the object's .sam keys over them
		Lights = ObjectLightBank.Create( Model, entry.Settings, entry.DisplayName );
		// Fixed items keep their last vertex pose when a clip ends (ObjectAnimator.UpdateVertices).
		Animator = new ObjectAnimator( Model, keepsPoseOnClipChange: entry.IsFixedItem, holdsLastFrameAtClipEnd: ObjectAnimations.HasOnlyBaseClips( entry.Animations ) );
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
			// [BIN:TPW-EXE:0x4db54f ride init capacity] [DATA:<object>.sam:Upgrades[0].InitCapacity] written only when above 0, through the capacity setter 0x4dd7f0 (its low byte clamped to UsageInfo.MinCapacity..MaxCapacity when the two sum above 0) into script variable 2 and ride +0x5d
			if ( entry.InitialCapacity > 0 )
				SetVariable( nameof( RideVariables.VAR_CAPACITY ), ClampCapacity( entry.Settings, entry.InitialCapacity ) );
			var level0 = entry.Upgrades.FirstOrDefault( level => level.Level == 0 );
			// [BIN:TPW-EXE:0x4db565 ride init duration] [DATA:<object>.sam:Upgrades[0].InitDuration] written only when above 0: the low byte, clamped to [UsageInfo.MinDuration, UsageInfo.MaxDuration] (type +0x12c/+0x130), into script variable 3 and ride +0x5c; DurationUnit is not read here (the ride window reads it, 0x4af030). The same clamp runs at ride settings (0x4dd720) and upgrade (0x4df8f0).
			if ( ClampInitDuration( entry.Settings, level0?.GetInt( "InitDuration" ) ?? 0 ) is { } duration )
				SetVariable( nameof( RideVariables.VAR_DURATION ), duration );
			// [BIN:TPW-EXE:0x4db51c ride init speed] [DATA:<object>.sam:Upgrades[0].InitSpeed] the script speed bias, written only when above 0 (setter 0x55a300).
			if ( level0?.GetInt( "InitSpeed" ) is > 0 and var speed )
				Script.SpeedBias = speed;
			SetVariable( nameof( RideVariables.VAR_RIDECLOSED ), open ? 0 : 1 );
			// [BIN:TPW-EXE:0x441970 guest predicate twin] the controller exists, +0x140 == 0, +0x3C bit 0 set and bit 1 clear, and it is not the coaster being edited (0x790fec); callers: 0x4dd920 counts test (Mac 0xdc834, X15), 0x4df290 reopen guard (0xde654), 0x4ffff0 queue state 11 (0xed244: a coaster (track type 3, 0x4e2b50) failing it gets 0x50be80(13), unlink 0x4ddd20, dismissal 0x5012f0, state 6); the open routine 0x4df390 logs "Opening non-openable ride" up to 5 times and opens anyway
			// [BIN:TPW-EXE:0x00441970 coaster guest predicate in the queue bridge] the choice's counts-test, the reopen guard and state 11 read the predicate (the coaster being edited included, CoasterRide.BeingEdited); the join and the admission do not (RideVisitorBridge.AcceptsJoins, HostStep)
			// A kart or water ride is chosen only while its circuit is valid (the original's counts-test 0x004DD920 reads the track word the walk writes for track types 1 and 2, TRACK-005);
			// the queue's own test at 0x00500612 sends queued guests away only for a coaster or a kart ride (track type 1), not a water ride
			Visitors.Attach( Script, () => !stopped && IsOpen, () => (Coaster?.AcceptsGuests ?? true) && (Bump?.IsTrackValid ?? true),
				() => !(Coaster?.AcceptsGuests ?? true) || Bump is { IsWater: false, IsTrackValid: false } );
			// [BIN:TPW-EXE:0x005571A0 WALKOFF] the walk takes ftol(the distance between the walker's two slot nodes) x 100 ms (100 ms for 0): the slot's start node (u3, class 0x800, or 0x80 for the head action 4) and end node (u4, class 0x800) are looked up in the ride model (0x0044B220: the first dummy whose class mask matches and whose number equals) and measured at the time of the WALKOFF (0x00556B90 reads the live node); the Star and Tweety Tours slots span 10 model units (1000 ms)
			// [BIN:TPW-EXE:0x00556F40 WALKON] the walk-on's nodes are looked up in the ride model the same way (the first node of class 0x800, the second of class 0x80 for the head action 4, else 0x800), for every object that walks riders
			Visitors.WalkNodeDistance = WalkNodeDistance;
			Visitors.WalkNodeClassDistance = WalkNodeDistance;
			Visitors.WalkNodeHeading = WalkHeadingOf;
			if ( UsesTour )
			{
				Visitors.WalkOffMilliseconds = 1000;
				Visitors.WalkOffEffect = StartWalkOffEffect;
				Visitors.WalkOnEffect = StartWalkOffEffect;
			}
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

	/// <summary>
	/// The distance in model units between a WALKON slot's two nodes at the current pose: the start node of class 0x80 (the head action 4) or 0x800 and the end node of class 0x800, each the first
	/// dummy of the model whose class mask matches and whose number equals (0x0044B220); null when either is missing.
	/// </summary>
	internal float? WalkNodeDistance( int action, int fromNumber, int toNumber ) => WalkNodeDistance( action == 4 ? 0x80 : 0x800, fromNumber, 0x800, toNumber );

	/// <summary>The distance between the first dummy of class <paramref name="fromMask"/> numbered <paramref name="fromNumber"/> and the first of class <paramref name="toMask"/> numbered <paramref name="toNumber"/> at the current pose; null when either is missing.</summary>
	internal float? WalkNodeDistance( int fromMask, int fromNumber, int toMask, int toNumber )
	{
		var from = FindDummyNode( fromMask, fromNumber );
		var to = FindDummyNode( toMask, toNumber );
		if ( from == null || to == null )
			return null;
		var transforms = Animator.NodeTransforms;
		return System.Numerics.Vector3.Distance( transforms[from.Value].Translation, transforms[to.Value].Translation );
	}

	private Func<int, System.Numerics.Matrix4x4>? nodeEngine;

	/// <summary>Gives the runtime the engine-space matrix of a model node (the placed object's <c>GetNodeTransform</c>), which the heading of a walking body is taken from. Presentation only.</summary>
	internal void SetNodeEngine( Func<int, System.Numerics.Matrix4x4> transform ) => nodeEngine = transform;

	/// <summary>The heading byte WALKON stores for a walk from the first node to the second (<see cref="WalkerBodies.HeadingOf"/> of their engine-space offset); null when either node is missing or the object is not placed.</summary>
	// [BIN:TPW-EXE:0x00556F40 WALKON heading] the angle of the nodes' world offset (the original's x and z, which are the engine's X and Y), through the sprite heading rule
	internal int? WalkHeadingOf( int fromMask, int fromNumber, int toMask, int toNumber )
	{
		if ( nodeEngine == null || FindDummyNode( fromMask, fromNumber ) is not { } from || FindDummyNode( toMask, toNumber ) is not { } to )
			return null;
		var offset = nodeEngine( to ).Translation - nodeEngine( from ).Translation;
		return WalkerBodies.HeadingOf( offset.X, offset.Y );
	}

	internal int? FindDummyNode( int mask, int number )
	{
		for ( var index = 0; index < Model.DummyAttributes.Count; index++ )
		{
			var attribute = Model.DummyAttributes[index];
			if ( (attribute.Type & mask) != 0 && attribute.Value == number )
			{
				var node = Model.Counts.Unknown46 + index;
				return node < Animator.NodeTransforms.Count ? node : null;
			}
		}
		return null;
	}

	public ObjectCatalogEntry Entry { get; }

	/// <summary>Where the script's sound events are looked up when the object comes from another content root (null: the running game's categories).</summary>
	// [EXT:cross-edition-objects] a Theme Park Inc ride's event ids belong to Theme Park Inc's catalogues
	internal RseSoundSource? SoundSource => ContentSoundRoots.SourceOf( Entry );
	/// <summary>The attraction id this runtime's bridge carries (allocated at construction or restored; the allocator frontier moves past it).</summary>
	public int AttractionId { get; }
	/// <summary>The coaster controller of a coaster station (<c>Bumper.WhichTrackType 3</c>), else null.</summary>
	public CoasterRide? Coaster { get; }
	/// <summary>The bump-ride controller of a water ride (<c>Bumper.WhichTrackType 2</c>), else null (docs/TRACKS.md).</summary>
	public BumpController? Bump { get; }
	/// <summary>
	/// <see cref="Bump"/> while the ride is live: not stopped (a faulted or removed ride released it) and still in the park's controller list.
	/// The park-resume capture and restore use this one rule, so they agree on which rides carry a controller.
	/// </summary>
	public BumpController? LiveBump => Bump != null && !stopped && scriptWorld.Bumpers.Controllers.Contains( Bump ) ? Bump : null;
	public ModelFile Model { get; }
	/// <summary>
	/// [EXT:object-lights] The object's lights (docs/LIGHTS.md), or null when its model has no light dummy. Presentation only: not part of the canonical state a hash reads, saved with the runtime snapshot.
	/// </summary>
	public ObjectLightBank? Lights { get; }
	private bool noLightsLogged;
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
	public void Simulate( double deltaSeconds ) => Simulate( deltaSeconds, null );

	/// <summary>
	/// One fixed simulation tick. With a <paramref name="substep"/> the park's substep scheduler runs the script pass (<see cref="RideScriptWorld.RunPass"/>) and the bump module before the objects' ticks, and this
	/// tick steps the tour cars on the even substeps only; without one (a standalone object) the script gets a slice and the tour its clock on every tick.
	/// </summary>
	internal void Simulate( double deltaSeconds, SimulationSubstep? substep )
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
		if ( substep == null )
			Script?.Advance( deltaSeconds );
		if ( Script is { State: RideVMState.Faulted } )
		{
			Stop();
			return;
		}
		Coaster?.Advance( deltaSeconds );
		if ( Bump != null )
		{
			// the script speed bias is pushed to the controller as its performance on every script tick ([BIN:TPW-EXE:0x005516B0 RSSE tick] 0x00545180)
			Bump.SetPerformance( ScriptPerformance );
			Bump.Advance( deltaSeconds );
		}
		// the object pass then moves the kept ADDOBJ effects to their keys ([BIN:TPW-EXE:0x55189D ADDOBJ follow])
		FollowParticleObjects();
		// and the kept sounds, every eighth pass per script ([BIN:TPW-EXE:0x551A1C sound follow])
		FollowHeldSounds( substep );
		if ( substep is { } step )
		{
			// [BIN:TPW-EXE:0x54F5D7 tour cars] the tour tick runs after the script pass on the even substeps only
			if ( step.TourDue )
				Tour?.Tick();
			if ( step.Running )
				Tour?.PresentAt( step.SecondsSinceTourStep );
		}
		else
			Tour?.Advance( deltaSeconds );
		Animator.Advance( deltaSeconds );
		var running = GetVariable( RideVariables.VAR_RUNNING ) != 0;
		// [EXT:ride-cycle-counter] CompletedCycles counts VAR_RUNNING 1 → 0 edges as OpenTPW's own diagnostic counter: nothing in the original counts ride cycles (it only reads VAR_RUNNING as a level), so there is no original behaviour to match
		if ( wasRunning && !running )
			CompletedCycles++;
		wasRunning = running;
	}

	public void Stop()
	{
		stopped = true;
		eventOrigin = null;
		footprintMiddle = null;
		eventSoundSpace = null;
		ClearParticleRequests();
		StopHeldSounds();
		effects.StopSounds();
		Visitors.IsStopped = true;
		Visitors.ReleaseAll();
		if ( Coaster != null )
		{
			Coaster.ClearTrains();
			Coaster.World.Remove( Coaster );
		}
		StopBumpEffects();
		ReleaseBump();
		ReleaseTour();
		Script?.Stop();
		Animator.StopAll();
	}

	/// <summary>The boats leave and the controller leaves the world (a removed or faulted water ride).</summary>
	private void ReleaseBump()
	{
		if ( Bump == null )
			return;
		Bump.EjectAll();
		scriptWorld.Bumpers.Remove( Bump );
	}

	/// <summary>
	/// [EXT:object-lights] ENABLELIGHT, DISABLELIGHT, SETLIGHT and COLOURLIGHT. A light the model does not have is a no-op logged once per opcode and number, never a fault: the original
	/// looks the dummy up and uses the result without a check (0x555C7A..0x555D9A), and its instance builder never fills the light pointer of a dummy (0x44A870), so there it would fault.
	/// </summary>
	internal void PerformLight( RideEffectCall call )
	{
		var number = call.Argument( 0 );
		var argument = call.OperandCount > 1 ? call.Argument( 1 ) : 0;
		var second = call.OperandCount > 2 ? call.Argument( 2 ) : 0;
		var third = call.OperandCount > 3 ? call.Argument( 3 ) : 0;
		if ( Lights?.Apply( call.Opcode, number, argument, second, third ) == true )
			return;
		var first = Lights == null ? !noLightsLogged : Lights.NoteMissing( call.Opcode, number );
		noLightsLogged = true;
		if ( first )
			Log?.Warning( $"{Entry.DisplayName}: {call.Opcode} {number} names a light the model does not have; nothing changes (the original would fault)" );
	}

	/// <summary>The performance (0..100) the script's speed bias gives the controller; the creation bias is 50.</summary>
	// [BIN:TPW-EXE:0x005516B0 RSSE tick] every script tick pushes the script performance (+0x30, 0..100, default 50) to the controller (0x00545180); the ride's InitSpeed bias is the same word (RIDES-033)
	// [BIN:TPW-EXE:0x5516b0 bumper performance] the script loop pushes the script's speed bias (RideVM.SpeedBias, the ride's InitSpeed; 50 at creation) to the bumper controller every pass (0x545180)
	internal int ScriptPerformance => Script?.SpeedBias ?? RideVM.DefaultSpeedBias;

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
			HeldScream = effects.ScriptHoldsScream,
			HeldScreamBand = effects.HeldScreamBand,
			HeldScreamLevel = effects.HeldScreamLevel,
			HeldScreamSpeed = effects.HeldScreamSpeed,
			OwnedScreamCount = effects.LeakedScreamCount,
			StoppedScriptFault = stopped && Script is { State: RideVMState.Faulted } ? Script.FaultMessage : null,
			ParticleObjects = CaptureParticleObjects(),
			RepairParticle = CaptureRepairParticle(),
			ScriptHoldsScream = effects.ScriptHoldsScream,
			RideSpeed = speedWord,
			RideCapacity = capacityWord,
			RideDuration = durationWord,
			VehiclesRaised = VehiclesRaised ? true : null,
			Lights = Lights?.Capture()
		};
	}

	/// <summary>
	/// Restores a snapshot into this fresh candidate (built by <see cref="CreateForRestore"/> with the snapshot's
	/// attraction id, seed and open flag). Validates the entry and script identity first and throws
	/// <see cref="InvalidDataException"/> — without changing the candidate — on a mismatch. A stopped snapshot
	/// replays <see cref="Stop"/>'s teardown (a fresh
	/// candidate has no riders, chains or trains to lose). Starts no sound: a held scream comes back as the script's
	/// state and plays again from <see cref="ResumeHeldScream"/> once the park's audio runs. Returns the explicit gaps
	/// (none so far).
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

		// [EXT:cheats-ride-vehicles] the raised flag and the capacity it allows, checked before anything of the candidate changes
		CheckRaisedSnapshot( snapshot );
		var gaps = new List<string>();
		if ( snapshot.Stopped )
		{
			// Stop()'s teardown, replayed on the fresh candidate: the script leaves the world (the saved world
			// snapshot does not contain it), the visitors and animator are cleared.
			stopped = true;
			effects.StopSounds();
			Visitors.IsStopped = true;
			Visitors.ReleaseAll();
			if ( Coaster != null )
			{
				Coaster.ClearTrains();
				Coaster.World.Remove( Coaster );
			}
			ReleaseBump();
			ReleaseTour();
			Script?.Stop();
			Animator.StopAll();
			if ( snapshot.StoppedScriptFault is { } fault )
				Script!.RestoreStoppedFault( fault );
		}
		wasRunning = snapshot.WasRunning;
		RestoreParticleObjects( snapshot.Stopped ? null : snapshot.ParticleObjects );
		RestoreRepairParticle( snapshot.Stopped ? 0 : snapshot.RepairParticle );
		// The scream is the script's state: its handle, operands and lost handles come back exactly, whether or not either run had audio (an older save
		// without the script's own view of the handle falls back to the chain's). Its sound is not: see ResumeHeldScream. A stopped object's script
		// holds none (the teardown above cleared it), whatever an older or edited save says.
		if ( !snapshot.Stopped )
			effects.RestoreScreams( snapshot.ScriptHoldsScream ?? snapshot.HeldScream, snapshot.HeldScreamBand, snapshot.HeldScreamLevel, snapshot.HeldScreamSpeed, snapshot.OwnedScreamCount );
		VehiclesRaised = snapshot.VehiclesRaised == true;
		RestoreSettingWords( snapshot.RideSpeed, snapshot.RideCapacity, snapshot.RideDuration );
		// [EXT:object-lights] the lights' on/off and colour; a number this model no longer has is reported, a model without lights ignores the list
		if ( snapshot.Lights is { } savedLights )
		{
			var unknownLights = Lights?.Restore( savedLights ) ?? savedLights.Select( record => record.Number ).ToArray();
			if ( unknownLights.Count > 0 )
				gaps.Add( $"'{Entry.ArchivePath}': saved light(s) {string.Join( ", ", unknownLights )} are not in the model any more and were dropped." );
		}
		CompletedCycles = snapshot.CompletedCycles;
		AnimationsPlayed = snapshot.AnimationsPlayed;
		unresolvedAnimations.Clear();
		foreach ( var unresolved in snapshot.UnresolvedAnimations )
			unresolvedAnimations.Add( unresolved );
		if ( snapshot.Heads != null )
			Visitors.Heads!.RestoreSnapshot( snapshot.Heads );
		Animator.RestoreSnapshot( snapshot.Animator, ResolveSnapshotClip );
		if ( !snapshot.Stopped && snapshot.OwnedScreamCount > 0 )
			Log?.Trace( $"{Entry.ArchivePath}: {snapshot.OwnedScreamCount} lost scream handle(s) at capture are counted, their chains do not scream again." );
		return gaps;
	}

	/// <summary>
	/// Plays the script's held scream again once the park's audio runs: a restored handle comes back without its chain (the flow calls
	/// <see cref="Level.ResumeHeldScreams"/> after <see cref="GameAudio.EnterPark"/>). A stopped object holds none.
	/// </summary>
	internal void ResumeHeldScream()
	{
		if ( !stopped )
			effects.ResumeHeldScream();
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
			// [DATA:<object>.sam:UsageInfo.AppearanceEffect] type +0x15C: "1 = Balloon, 2 = Costume" (Shops.sam); the use routine tp.exe 0x004FE1E0 switches on it
			AppearanceEffect = settings.GetInt( "UsageInfo.AppearanceEffect" ),
			// [DATA:<object>.sam:UsageInfo.ThirstEffect/HungerEffect/VomitEffect/HappinessEffect/GoldenTicketCost/ISIndoors, Upgrades[0].CostOfUpgrade, Info.Id] the type words the attraction score and the visit effects read (type +324/+328/+332/+336/+196/+276/+440, ORACLE.md F24)
			ThirstEffect = settings.GetInt( "UsageInfo.ThirstEffect" ),
			HungerEffect = settings.GetInt( "UsageInfo.HungerEffect" ),
			VomitEffect = settings.GetInt( "UsageInfo.VomitEffect" ),
			HappinessEffect = settings.GetInt( "UsageInfo.HappinessEffect" ),
			// [DATA:<object>.sam:UsageInfo.LitterEffect] type +0x154, added to the guest's litter meter by the use routine (tp.exe 0x004FE1E0)
			LitterEffect = settings.GetInt( "UsageInfo.LitterEffect" ),
			// [DATA:<object>.sam:UsageInfo.InitCostOfGoods] type +320, the price test's base (tp.exe 0x004FDE50; the key table order InitCostOfGoods, ThirstEffect, HungerEffect, VomitEffect, HappinessEffect)
			InitCostOfGoods = settings.Has( "UsageInfo.InitCostOfGoods" ) ? settings.GetInt( "UsageInfo.InitCostOfGoods" ) : 0,
			// [DATA:<object>.sam:UsageInfo.RipOffOK] type +0x16C (key 42 of the UsageInfo table at tp.exe 0x007456E8), copied into the per-type record's word +0xC that the price test reads (0x004D3E00); 100 in the shipped Shops.sam, 250 in SideShow.sam
			RipOffOk = settings.Has( "UsageInfo.RipOffOK" ) ? settings.GetInt( "UsageInfo.RipOffOK" ) : 0,
			// [DATA:<object>.sam:UsageInfo.FatigueEffect] type +0xE8 (key 9 of the UsageInfo table at tp.exe 0x007456E8), "reduce fatigue value by this amount"; 5 in the shipped Rides.sam, Shops.sam and SideShow.sam
			FatigueEffect = settings.Has( "UsageInfo.FatigueEffect" ) ? settings.GetInt( "UsageInfo.FatigueEffect" ) : 0,
			GoldenTicketCost = settings.GetInt( "UsageInfo.GoldenTicketCost" ),
			UpgradeCost = entry.Upgrades.FirstOrDefault( upgrade => upgrade.Level == 0 )?.GetInt( "CostOfUpgrade" ) ?? 0,
			IsIndoors = settings.GetInt( "UsageInfo.ISIndoors" ) != 0,
			// [BIN:TPW-EXE:0x745a60 schema RequiresTeleport] the key after ChillsYouOut (+0xFC) is type record +0x100, which the guest state setter 0x501db0 tests in states 15 and 16
			RequiresTeleport = settings.GetInt( "UsageInfo.RequiresTeleport" ) != 0,
			RideHandlesSprite = settings.GetInt( "UsageInfo.RideHandlesSprite" ) != 0,
			TypeId = entry.InfoId,
			// [DATA:<object>.sam:UsageInfo.InitCostOfGoods/InitPricePerUse/InitChanceOfLoosing] the sideshow words of the join excitement (0xdff70); the level mirrors the live economy values over them
			Sideshow = new SideshowTerms( settings.GetInt( "UsageInfo.InitCostOfGoods" ), settings.GetInt( "UsageInfo.InitPricePerUse" ), settings.GetInt( "UsageInfo.InitChanceOfLoosing" ) ),
			// [DATA:<object>.sam:UsageInfo.EntryCellStandPosX/Y] the jungle Rides.sam default is (0.5, 0.5); 0xde1d8 checks the range [0, 1] (a value outside it keeps the default here)
			EntryCellStandPos = (StandFraction( settings, "UsageInfo.EntryCellStandPosX" ), StandFraction( settings, "UsageInfo.EntryCellStandPosY" )),
			// [DATA:<object>.sam:UsageInfo.ExitCellAppearPosX/Y] type record +0xDC and +0xE0 (0.5 in every theme's base .sam); ExitRide's point 0x004DEDF0(1) logs a value outside 0..1 ("Dodgy X exit point in SAM file") and uses it
			ExitCellAppearPos = (ExitFraction( settings, "UsageInfo.ExitCellAppearPosX" ), ExitFraction( settings, "UsageInfo.ExitCellAppearPosY" )),
			// [EXT:pools-changing-room] OpenTPW's own key: the object is a pool complex's changing room (docs/POOLS.md)
			IsChangingRoom = FeatureGates.Pools && PoolChangingRooms.IsChangingRoom( entry ),
			// [EXT:umbrellas-extension] the umbrellas extension's own objects carry Theme Park Inc's weather and generation percentages, which the host score applies for them alone; every other object keeps the game's score
			UmbrellaTerms = UmbrellaShopTermsOf( entry ),
			// [EXT:winter-extension] likewise the winter extension's own objects carry weather and season percentages for the host score
			WinterTerms = WinterShopTermsOf( entry )
		};
	}

	/// <summary>
	/// The weather and generation percentages of an object of the umbrellas extension (docs/UMBRELLAS.md, "Demand"): <c>WeatherModifier.Rain/Storm/Snow/HeatWavePercentage</c> and <c>GenerationModifier.KidScorePercentage</c> of
	/// its .sam; null for any other object and while the extension is off. A key a .sam leaves out is 0 for the weather (no change); the kid percentage is then 100.
	/// </summary>
	// [BIN:TPI-EXE:0x6101e0 score multipliers] Theme Park Inc's keys, which tp.exe does not read: only the extension's own objects use them
	private static UmbrellaShopTerms? UmbrellaShopTermsOf( ObjectCatalogEntry entry )
	{
		if ( !ParkResumeMods.IsUmbrellasObject( entry ) || !FeatureGates.Umbrellas )
			return null;
		var settings = entry.Settings;
		int Percentage( string key, int fallback ) => settings.Has( key ) ? settings.GetInt( key ) : fallback;
		return new UmbrellaShopTerms( Percentage( "WeatherModifier.RainPercentage", 0 ), Percentage( "WeatherModifier.StormPercentage", 0 ), Percentage( "WeatherModifier.SnowPercentage", 0 ),
			Percentage( "WeatherModifier.HeatWavePercentage", 0 ), Percentage( "GenerationModifier.KidScorePercentage", 100 ) );
	}

	/// <summary>
	/// The weather and season percentages of an object of the winter extension (docs/WINTER.md, "Demand"): <c>WeatherModifier.Rain/Storm/SnowPercentage</c> and <c>SeasonModifier.Winter/Spring/Summer/AutumnPercentage</c> of its .sam; null
	/// for any other object and while the extension is off. A key a .sam leaves out is 0 (no change).
	/// </summary>
	// [EXT:winter-season-terms] the weather keys are Theme Park Inc's names (tp.exe does not read them), the season keys are the extension's own: only the extension's objects use them
	private static WinterShopTerms? WinterShopTermsOf( ObjectCatalogEntry entry )
	{
		if ( !ParkResumeMods.IsWinterObject( entry ) || !FeatureGates.Winter )
			return null;
		var settings = entry.Settings;
		int Percentage( string key ) => settings.Has( key ) ? settings.GetInt( key ) : 0;
		return new WinterShopTerms( Percentage( "WeatherModifier.RainPercentage" ), Percentage( "WeatherModifier.StormPercentage" ), Percentage( "WeatherModifier.SnowPercentage" ),
			Percentage( "SeasonModifier.WinterPercentage" ), Percentage( "SeasonModifier.SpringPercentage" ), Percentage( "SeasonModifier.SummerPercentage" ), Percentage( "SeasonModifier.AutumnPercentage" ) );
	}

	private static float ExitFraction( ObjectSettings settings, string key ) => settings.Has( key ) ? settings.GetFloat( key ) : 0.5f;

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
		// the CAPACITY (+0x5d) and DURATION (+0x5c) words as ride init writes them with script variables 2 and 3 (0x4db54f, 0x4db565): the setters' clamps, only for a value above 0; 0 otherwise
		var initCapacity = level?.GetInt( "InitCapacity" ) ?? 0;
		var capacity = initCapacity > 0 ? ClampCapacity( settings, initCapacity ) : 0;
		var duration = ClampInitDuration( settings, level?.GetInt( "InitDuration" ) ?? 0 ) ?? 0;
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
	/// object has no such member (the channel then keeps its clip). With <paramref name="defer"/> a clip that still
	/// runs on the channel keeps it and the new one waits behind it (<see cref="ObjectAnimator.Trigger"/>).
	/// </summary>
	internal int TriggerAnimation( int animation, int variant, int channel, bool loop, float speed, bool defer = false )
	{
		var file = Entry.ResolveAnimation( animation, variant );
		var clip = file == null ? null : LoadClip( file.Path );
		if ( clip == null )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			return Animator.Trigger( channel, null, "", animation, variant, loop, speed, defer );
		}
		try
		{
			var result = Animator.Trigger( channel, clip, file!.Name, animation, variant, loop, speed, defer );
			AnimationsPlayed++;
			return result;
		}
		catch ( InvalidDataException )
		{
			unresolvedAnimations.Add( $"{animation}/{variant}" );
			clips[file!.Path] = null;
			return Animator.Trigger( channel, null, "", animation, variant, loop, speed, defer );
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
/// <see cref="RideVisitorBridge"/> (guests slice). EVENT and REPAIREFFECT play their original sounds and particles, STARTSCREAM/STOPSCREAM/SCREAMLEVEL/SINGLESCREAM
/// the screams. ADDOBJ keeps the particle effects (types 1-2) and sounds (types 3-9) it starts under its slot (<see cref="OriginalObjectRuntime"/>), KILLOBJ and FADEOBJ end them, SETOBJPARAM
/// gives the sounds a parameter, and a script's end ends what it still holds (<see cref="IRideScriptLifetime"/>). TPW YEAR/MONTH/DAY/HOUR/MIN/SEC sample host civil time. The ride-type controllers
/// TOUR/BUMP/COAST go to their controllers. The ride lights (ENABLELIGHT, DISABLELIGHT, SETLIGHT, COLOURLIGHT) act on the model's light dummies when it has them (<see cref="OriginalObjectRuntime.Lights"/>, docs/LIGHTS.md) and are a logged no-op when it does not. What stays an unimplemented effect (<see cref="RideVM.UnimplementedEffects"/>): the bounce rider opcodes where their object has no bounce
/// rider rendering, and ADDOBJ/EVENT type 10 (the object type's own sound list). DIPMUSIC sets the sound service's music duck request; SETREVERB's level is read by <see cref="ReverbSelector"/>.
/// </summary>
public sealed class OriginalObjectEffects : IRideScriptEffects, IRideAnimationQuery, IRideCanonicalEffects, IRideScriptLifetime, IRideSliceTail
{
	/// <summary>Channel of the plain animation opcodes: channel 0, the same record a _CH opcode addresses with channel 0.</summary>
	// [BIN:STP-PPC:0x100A6CC0 animation trigger] the channel record is model +0x10 + channel × 0x38; TRIGANIM (0x100AFB10), WAITANIM (0x100AFC68), LOOPANIM (0x100AFD7C) and TRIGANIMSPEED (0x100B005C) pass channel 0, TRIGANIM_CH (0x100B01C4) its operand
	public const int MainChannel = OpcodeHandlers.Animation.MainChannel;

	private readonly OriginalObjectRuntime ride;
	/// <summary>
	/// The ride script's scream handle as the script sees it (the original's handle at the VM's +208): a STARTSCREAM with a band takes it, and a second
	/// one is refused while it is held. This, the operands and <see cref="leakedScreams"/> are the scream's state, the same with or without audio, and a
	/// park save keeps them; <see cref="screamChain"/> only plays the held scream.
	/// </summary>
	private bool scriptHoldsScream;
	/// <summary>Handles a refused STARTSCREAM lost: each leaves its chain screaming unreferenced until the object stops.</summary>
	private int leakedScreams;
	/// <summary>The chain that plays the held scream: null without audio, before a resumed park's audio starts, or for a band without an event.</summary>
	private SoundChain? screamChain;
	private readonly List<SoundChain> ownedScreams = new();
	// Positioned EVENT and repair-hide flag-0x8 voices share the existing exact-runtime cleanup ledger.
	private readonly List<AudioVoice> ownedEventLoops = new();

	public OriginalObjectEffects( OriginalObjectRuntime ride ) => this.ride = ride;

	/// <summary>
	/// The script loop's speed setter after each slice of the station's root script (<c>0x5517D7</c> → <c>0x43B2B0</c>): once <c>COAST 8</c> bound the coaster (or the park import
	/// bound a saved script that holds its slot), the script's speed word becomes the coaster's speed setting when it differs.
	/// </summary>
	public void AfterSlice( RideVM vm )
	{
		// the slot +0xE0 is the root script's: COAST 8 stores it there, and the station's root script is the one that runs it
		if ( vm.Parent == null && ride.Coaster is { Initialised: true } coaster )
			coaster.ApplyScriptSpeed( vm.SpeedBias );
	}

	/// <summary>Stops this runtime's chains, including handles lost by a refused STARTSCREAM, when the object is removed; its script holds no scream any more.</summary>
	internal void StopSounds()
	{
		foreach ( var chain in ownedScreams )
			chain.Stop();
		ownedScreams.Clear();
		foreach (var voice in ownedEventLoops) voice.Stop();
		ownedEventLoops.Clear();
		screamChain = null;
		scriptHoldsScream = false;
		leakedScreams = 0;
		(HeldScreamBand, HeldScreamLevel, HeldScreamSpeed) = (0, 0, 0);
	}

	/// <summary>Whether the script holds a scream handle (set by a STARTSCREAM with a band, cleared by STOPSCREAM, a refusal or the object's stop), with or without audio; the scream diagnostics follow it.</summary>
	internal bool ScriptHoldsScream { get => scriptHoldsScream; set => scriptHoldsScream = value; }
	/// <summary>The held scream's STARTSCREAM band, level (the last SCREAMLEVEL's while it is held) and script speed word; 0 without a held scream.</summary>
	internal int HeldScreamBand { get; private set; }
	internal int HeldScreamLevel { get; private set; }
	internal int HeldScreamSpeed { get; private set; }
	/// <summary>Handles a refused STARTSCREAM lost (their chains scream on unreferenced until the object stops), counted with or without audio.</summary>
	internal int LeakedScreamCount => leakedScreams;
	/// <summary>Whether a live chain plays the held scream (presentation, never saved).</summary>
	internal bool PlaysHeldScream => screamChain is { IsStopped: false };

	/// <summary>
	/// Park resume: the saved handle, its operands and the count of lost handles come back as they were, with or without audio. No chain starts here
	/// (any this runtime played stops): a candidate park makes no sound before it replaces the live one; <see cref="ResumeHeldScream"/> plays the held
	/// scream once the park's audio runs.
	/// </summary>
	internal void RestoreScreams( bool held, int band, int level, int speed, int leaked )
	{
		foreach ( var chain in ownedScreams )
			chain.Stop();
		ownedScreams.Clear();
		screamChain = null;
		scriptHoldsScream = held;
		(HeldScreamBand, HeldScreamLevel, HeldScreamSpeed) = (band, level, speed);
		leakedScreams = leaked;
	}

	/// <summary>
	/// Plays the held scream from its operands when no chain plays it: a resumed park's handle comes back without one, and leaving the previous park stops
	/// every chain. The lost handles are not screamed again; the original's unreferenced chains end with the program too.
	/// </summary>
	internal void ResumeHeldScream()
	{
		if ( !scriptHoldsScream || PlaysHeldScream )
			return;
		ownedScreams.RemoveAll( chain => chain.IsStopped );
		screamChain = GameAudio.StartScream( HeldScreamBand, HeldScreamLevel, ride.ReadFootprintMiddle(), HeldScreamSpeed );
		if ( screamChain != null )
			ownedScreams.Add( screamChain );
	}
	internal bool HasLiveEventLoops => ownedEventLoops.Any(voice => voice.Loops && !voice.IsFinished);

	public int Perform( RideEffectCall call )
	{
		switch ( call.Opcode )
		{
			case Opcode.ENABLELIGHT:
			case Opcode.DISABLELIGHT:
			case Opcode.SETLIGHT:
			case Opcode.COLOURLIGHT:
				// [EXT:object-lights] the model's light dummies; the original's own lights are never created (docs/LIGHTS.md)
				ride.PerformLight( call );
				return 0;
			case Opcode.YEAR:
			case Opcode.MONTH:
			case Opcode.DAY:
			case Opcode.HOUR:
			case Opcode.MIN:
			case Opcode.SEC:
				// [BIN:STP-PPC:0x100B2144 RSE civil time] YEAR/MONTH/DAY/HOUR use host time/localtime, not the park calendar (PPC-clock.md).
				// [BIN:TPW-EXE:0x005567D8 civil time opcodes] opcodes 97-102 (YEAR, MONTH, DAY, HOUR, MIN, SEC; handlers 0x5565B2..0x55674E) each call time (0x67D3F0) and localtime (0x67D8E0) and store tm_year (raw), tm_mon + 1, tm_mday, tm_hour, tm_min or tm_sec in the result register (+0x48), written to the operand only when it is a variable; the branches test that register, so a literal operand sets only the flags
				if ( !call.VM.World.TryReadCivilTime( out var civil ) )
					break;
				return call.Opcode switch
				{
					Opcode.YEAR => civil.Year - 1900,
					Opcode.MONTH => civil.Month,
					Opcode.DAY => civil.Day,
					Opcode.HOUR => civil.Hour,
					Opcode.MIN => civil.Minute,
					_ => civil.Second
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
		// The water ride's BUMP commands go to its controller (docs/TRACKS.md); the visitor opcodes keep the guest protocol (BUMP 1 and 2 only move guest ids).
		if ( call.Opcode == Opcode.BUMP && ride.Bump is { } bump )
			return bump.Command( call.Argument( 0 ), call.OperandCount > 1 ? call.Argument( 1 ) : 0 );
		// The tour rides' TOUR commands go to their flying-car controller (docs/TOURS.md).
		if ( call.Opcode == Opcode.TOUR )
			return ride.PerformTour( call );
		if ( ride.Visitors.TryPerform( call, out var visitorResult ) )
			return visitorResult;
		// Routed EVENT, held ADDOBJ and repair-hide sounds use the placed origin; the screams sit at the footprint middle (0x556AF0); reverb is the sound service's (SETREVERB).
		// [BIN:STP-PPC:0x100B19DC STARTSCREAM] the scream is held by the ride's script (the handle at the VM's +208); STOPSCREAM stops the handle, SINGLESCREAM is a one-shot with no handle
		if ( call.Opcode == Opcode.STARTSCREAM )
		{
			// the handle, the refusal and the diagnostics follow the script's own view (in tp.exe with its sound running a chain, and so a handle, exists whenever the band is not 0; without a sound device 0x51bfc0 returns 0 and no handle is held, which OpenTPW deliberately does not model), so a run without audio holds, refuses and saves the same screams
			var (band, level) = (call.Argument( 0 ), call.Argument( 1 ));
			var refused = scriptHoldsScream;
			GameAudio.Screams.NoteStart( ride.Entry.DisplayName, band, refused );
			if ( refused )
			{
				// [BIN:STP-PPC:0x100B19DC refusal] a ride already holding a scream handle refuses: the engine logs "Started screaming without stopping first." and overwrites the handle with 0, so the held chain screams on unreferenced
				Log?.Trace( "RSSE: Started screaming without stopping first." );
				leakedScreams++;
			}
			ownedScreams.RemoveAll( chain => chain.IsStopped );
			// band 0 plays nothing and gives no handle (0x551130 returns 0)
			scriptHoldsScream = !refused && band != 0;
			(HeldScreamBand, HeldScreamLevel, HeldScreamSpeed) = scriptHoldsScream ? (band, level, call.VM.SpeedBias) : (0, 0, 0);
			screamChain = scriptHoldsScream ? GameAudio.StartScream( band, level, ride.ReadFootprintMiddle(), HeldScreamSpeed ) : null;
			if ( screamChain != null )
				ownedScreams.Add( screamChain );
			return 0;
		}
		if ( call.Opcode == Opcode.STOPSCREAM )
		{
			if ( scriptHoldsScream )
				GameAudio.Screams.NoteStop( ride.Entry.DisplayName );
			scriptHoldsScream = false;
			screamChain?.Stop();
			screamChain = null;
			(HeldScreamBand, HeldScreamLevel, HeldScreamSpeed) = (0, 0, 0);
			ownedScreams.RemoveAll( chain => chain.IsStopped );
			return 0;
		}
		if ( call.Opcode == Opcode.SINGLESCREAM )
		{
			GameAudio.SingleScream( call.Argument( 0 ), (short)call.Argument( 1 ), ride.ReadFootprintMiddle(), call.VM.SpeedBias );
			return 0;
		}
		if ( call.Opcode == Opcode.SCREAMLEVEL )
		{
			// [BIN:TPW-EXE:0x555FDA SCREAMLEVEL] one resolved operand: 0x551290(the script's scream handle (+0xD0), level, the script speed word) does nothing without a handle and otherwise sets the held voice's parameter 6 (the one the chain's links are matched against, as at STARTSCREAM) to clamp((level + speed) / 2, 0, 100), truncating toward zero (0x5512A0..0x5512B7); the script keeps the handle the call returns. A script without a held scream is untouched
			var level = call.Argument( 0 );
			// the held operands follow the script's handle, with or without audio; the chain, when one plays, takes the new parameter
			if ( scriptHoldsScream )
			{
				(HeldScreamLevel, HeldScreamSpeed) = (level, call.VM.SpeedBias);
				if ( screamChain is { IsStopped: false } held )
					held.Parameter = GameAudio.ScreamParameter( level, HeldScreamSpeed );
			}
			return 0;
		}
		// ADDOBJ and ADDOBJ_EXT of type 1/2 start particle effects (docs/PARTICLES.md) and of types 3-9 sounds, each kept under the record's slot; KILLOBJ and FADEOBJ end the records of the slot (type 10, the object type's own sound list, is the one that stays an unimplemented effect)
		if ( call.Opcode is Opcode.ADDOBJ or Opcode.ADDOBJ_EXT )
		{
			// [BIN:TPW-EXE:0x5573D0 object dispatcher] type 10 takes the object type's own sound bank (0x557686, 0x4145E0); no type outside 1-10 is served (0x557931 logs, the handle is 0 and the record is freed)
			if ( call.OperandCount > 0 && call.Argument( 0 ) == 10 )
				return UnimplementedRideScriptEffects.Instance.Perform( call );
			ride.ObserveParticleObject(call);
			ride.ObserveHeldSound(call);
			return 0;
		}
		if ( call.Opcode is Opcode.KILLOBJ or Opcode.FADEOBJ )
		{
			// [BIN:TPW-EXE:0x5522C5 KILLOBJ] [BIN:TPW-EXE:0x5523D0 FADEOBJ] every record with the slot ends: types 1-2 through 0x51FF70(handle, -2), types 3-10 stop or release a quality-enabled fade tail (OriginalObjectRuntime.EndHeldSounds)
			ride.KillParticleObjects(call.Argument(0));
			ride.EndHeldSounds(call.VM.ScriptId, call.Argument(0), call.Opcode == Opcode.FADEOBJ);
			return 0;
		}
		if ( call.Opcode == Opcode.SETOBJPARAM )
		{
			// [BIN:TPW-EXE:0x5524D9 SETOBJPARAM] the sound records of the slot get the parameter call (code, value); the particle records are skipped
			ride.SetHeldSoundParameter(call.VM.ScriptId, call.Argument(0), call.Argument(1), call.Argument(2));
			return 0;
		}
		if ( call.Opcode is Opcode.EVENT or Opcode.EVENT_EXT )
		{
			// [BIN:TPW-EXE:0x552707 EVENT_EXT] the same dispatcher 0x5573D0 with the fourth operand as the control; only the particle types act on it, the sound types ignore it
			if (call.Argument(0) is 1 or 2) ride.ObserveParticleEvent(call);
			// [BIN:STP-PPC:0x100AE930 RSE EVENT effect switch] the type operand selects the sound category (3 level rides, 4 level ambient, 5 global rides, 6 global kids, 7 global staff, 8 global ambient, 9 global UI) and the id operand the event, submitted at the object's position
			// [BIN:TPW-EXE:0x5575c7 EVENT sound] the key operand (the 2nd) picks the anchor (a model node, or the footprint middle for a negative key; an unknown key plays nothing) and the 3rd is the event id, for every sound type 3-9
			var type = call.Argument(0);
			AudioVoice? voice = null;
			if (type is >= 3 and <= 9)
			{
				if (ride.ResolveEventAnchor(call.Argument(1), out var anchor) == OriginalObjectRuntime.EventAnchorStatus.Play)
					voice = GameAudio.PlayRseEvent(type, unchecked((uint)call.Argument(2)), anchor, ride.SoundSource);
			}
			else
				voice = GameAudio.PlayRseEvent(type, unchecked((uint)call.Argument(2)), null, ride.SoundSource);
			ownedEventLoops.RemoveAll(loop => loop.IsFinished);
			if (voice is { Loops: true }) ownedEventLoops.Add(voice);
			// [BIN:TPW-EXE:0x557686 EVENT type 10] the object type's own sound list is not served; it is the one EVENT part that stays an unimplemented effect (types 0 and above 10 only log in the original)
			return type == 10 ? UnimplementedRideScriptEffects.Instance.Perform( call ) : 0;
		}
		if ( call.Opcode == Opcode.DIPMUSIC )
		{
			// [BIN:TPW-EXE:0x51E710 music duck request] the low word of the operand is the global request the sound service reads (a non-zero word silences the music channel)
			GameAudio.SetMusicDuck( unchecked((ushort)call.Argument( 0 )) );
			return 0;
		}
		if ( call.Opcode == Opcode.SETREVERB )
			return 0; // the VM keeps the level (+0xD8); ReverbSelector reads it each frame
		if ( call.Opcode == Opcode.REPAIREFFECT )
		{
			// [BIN:TPW-EXE:0x5562C5 REPAIREFFECT] a non-zero value plays the global cat_rides sounds 111 and 211 at the object's position (0x5562CD..0x556351); the value 0 jumps to 0x55635F and plays the band one-shot (cat_kids 75 family, 0x551320). The Mac handler (0x100B1E34) has the same two blocks, the pair first and the band sound at 0x100B1F78; the earlier port gave them the opposite values
			var show = call.Argument( 0 );
			// [BIN:TPW-EXE:0x5562bd REPAIREFFECT] the type's repair particle effect is switched before the sounds (0x556DE0)
			ride.SetRepairParticle( show != 0 );
			{
				// [BIN:TPW-EXE:0x55637a repair band] the band is the script's variable slot 5 (a script with fewer variables has none here, so it stays silent)
				var band = call.VM.Variables.Length > 5 ? call.VM.Variables[5] : 0;
				var voices = GameAudio.PlayRepairEffect( show, OriginalObjectRuntime.WholeUnits( ride.ReadEventOrigin() ), band );
				ownedEventLoops.RemoveAll( loop => loop.IsFinished );
				if ( voices.First is { Loops: true } ) ownedEventLoops.Add( voices.First );
				if ( voices.Second is { Loops: true } ) ownedEventLoops.Add( voices.Second );
			}
			return 0;
		}
		return UnimplementedRideScriptEffects.Instance.Perform( call );
	}

	/// <summary>A script of this object ended or was stopped: the sounds its ADDOBJ records hold end with it.</summary>
	public void ScriptDestroyed( RideVM vm ) => ride.EndScriptSounds( vm.ScriptId );

	public int GetAnimationId( RideVM vm, int channel ) => ride.Animator.GetAnimationId( channel );

	public void AddCanonicalState( StateHasher hash ) => ride.Animator.AddCanonicalState( hash );

	/// <summary>The original indexes <c>object +16 + channel × 56</c> without a check; a negative channel has no record here.</summary>
	private static int Channel( RideEffectCall call, int operand )
	{
		var channel = call.Argument( operand );
		return channel >= 0 ? channel : throw new RideScriptException( $"{call.Opcode} channel {channel} is negative" );
	}
}
