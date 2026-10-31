using System.Globalization;

namespace OpenTPW;

/// <summary>Pylon control flags (pylon <c>+0</c>, COAST-plan §3.2).</summary>
[Flags]
public enum CoasterPylonFlags : uint
{
	None = 0,
	Standard = 0x4,
	/// <summary>Pylon flag 0x10 (<c>bDontDisableBaseOnStack</c>, <c>0x42ec47</c>): the model's ground node stays visible on the pylons stacked above the first.</summary>
	KeepBaseOnStack = 0x10,
	Loop = 0x4000,
	Trough = 0x8000
}

/// <summary>
/// What a pylon can be edited by hand with (pylon control <c>+0</c> bits 0x20..0x200), from the animations its model has (<c>0x430590</c>): the loft clip (type 3) lets the height drag
/// move it, the rotate clip (type 10) the rotate drag, the incline clip (type 2) and the bank clip (type 9) the two axes of the tilt drag and a second rotate clip (type 10, index 1) the loop tilt.
/// </summary>
[Flags]
public enum CoasterPylonCapabilities : uint
{
	None = 0,
	Loft = 0x20,
	Rotate = 0x40,
	Incline = 0x80,
	Bank = 0x100,
	LoopTilt = 0x200
}

/// <summary>
/// How a pylon control maps a node value to the fraction of one of its model's clips (pylon <c>+0xd0</c>..<c>+0x11c</c>, four floats per clip:
/// <c>f…AnimStartCap</c>, <c>f…AnimEndCap</c>, <c>f…AnimMultiplier</c>, <c>f…AnimAdd</c>): value · Multiplier + Add, raised to StartCap, then lowered to EndCap.
/// </summary>
// [BIN:TPW-EXE:0x42ff1a clip map defaults] the definition defaults give every one of the five maps of both pylon controls (definition +0x178.. and +0x298.., pylon +0xd0..+0x11c) StartCap 0, EndCap 1, Multiplier 1, Add 0; the loader copies each settings value over its field when nonzero (0x42eeec..0x42f161)
public readonly record struct CoasterClipMap( float StartCap, float EndCap, float Multiplier, float Add )
{
	public static readonly CoasterClipMap Identity = new( 0, 1, 1, 0 );

	/// <summary>The clip fraction of a value (<c>0x430ca0</c>): v·Multiplier + Add (+ <paramref name="centre"/>), below StartCap → StartCap, then above EndCap → EndCap.</summary>
	public float Fraction( float value, float centre = 0 )
	{
		var fraction = value * Multiplier + Add + centre;
		if ( fraction < StartCap )
			fraction = StartCap;
		if ( EndCap < fraction )
			fraction = EndCap;
		return fraction;
	}
}

/// <summary>
/// One of the two 288-byte pylon controls of a coaster definition (COAST-plan §3.2): the StdPylon at definition
/// <c>+168</c> and the LoopPylon at <c>+456</c>. Angles are radians (the settings give degrees), place distances
/// whole cells; the loader stores both the squared distances and the float distances.
/// </summary>
public sealed class CoasterPylonControl
{
	public CoasterPylonFlags Flags { get; internal set; }
	/// <summary>The hand edits the pylon's model allows. Without the model (no game data) a standard pylon takes all four drags and a loop pylon the loop tilt.</summary>
	// [BIN:TPW-EXE:0x430590 pylon capabilities] pylon flags 0x20 (loft clip), 0x40 (rotate), 0x80 (incline), 0x100 (bank) and 0x200 (second rotate clip) are set when the model has the animation of that type (0x472d10); the drag handlers test them (0x441a00, 0x441e80, 0x442010, 0x441cd0)
	public CoasterPylonCapabilities Capabilities { get; internal set; } = CoasterPylonCapabilities.Loft | CoasterPylonCapabilities.Rotate | CoasterPylonCapabilities.Incline | CoasterPylonCapabilities.Bank;
	/// <summary><c>pcMeshFilename</c> (StdPylon, LoopPylon), or empty.</summary>
	public string MeshName { get; internal set; } = "";
	public int MinPlaceDistance { get; internal set; }
	public int MaxPlaceDistance { get; internal set; }
	/// <summary>Pylon <c>+156</c>: the squared minimum distance the validator compares d² with.</summary>
	public int MinPlaceDistanceSquared => MinPlaceDistance * MinPlaceDistance;
	/// <summary>Pylon <c>+160</c>.</summary>
	public int MaxPlaceDistanceSquared => MaxPlaceDistance * MaxPlaceDistance;
	/// <summary>Entrance turn limits after the station node, at the minimum and the maximum distance (+116..+128).</summary>
	public float EntranceMinAngleAtMinDistance { get; internal set; }
	public float EntranceMaxAngleAtMinDistance { get; internal set; }
	public float EntranceMinAngleAtMaxDistance { get; internal set; }
	public float EntranceMaxAngleAtMaxDistance { get; internal set; }
	/// <summary>Normal turn limits (+132..+144).</summary>
	public float MinAngleAtMinDistance { get; internal set; }
	public float MaxAngleAtMinDistance { get; internal set; }
	public float MinAngleAtMaxDistance { get; internal set; }
	public float MaxAngleAtMaxDistance { get; internal set; }
	public float MaxRotationDeviance { get; internal set; }
	public float MaxLoopRotationDeviance { get; internal set; }
	/// <summary>The settings value × 10 × π/180, as the loader stores it (+172).</summary>
	public float MaxLoopHeightDifference { get; internal set; }
	public float MinHeight { get; internal set; }
	public float MaxHeight { get; internal set; }
	public int MaxStackedPylons { get; internal set; }
	public float CollisionPossibleRadius { get; internal set; }
	public float StraightDotThreshold { get; internal set; }
	/// <summary>The three cost rates (+92/+96/+100), copied from the settings unconditionally.</summary>
	public float CostPerUnit { get; internal set; }
	public float CostPer2DLength { get; internal set; }
	public float CostPerHeight { get; internal set; }
	/// <summary>Pylon mesh height at loft parameter t = 0 and t = 1 (+188/+192).</summary>
	public float MeshHeightLow { get; internal set; }
	public float MeshHeightHigh { get; internal set; }
	/// <summary>How far above the track dummy of the pylon below the next pylon of a stack stands (+0xc4).</summary>
	public float StackOffset { get; internal set; }
	/// <summary>The loft clip's mapping of the height parameter (+0xd0..+0xdc): clip fraction = clamp(t · Multiplier + Add, StartCap, EndCap).</summary>
	public float LoftStartCap { get; internal set; }
	public float LoftEndCap { get; internal set; } = 1;
	public float LoftMultiplier { get; internal set; } = 1;
	public float LoftAdd { get; internal set; }
	/// <summary>The rotate clip's map (+0xe0..+0xec), fed the yaw over 2π.</summary>
	public CoasterClipMap RotateMap { get; internal set; } = CoasterClipMap.Identity;
	/// <summary>The incline clip's map (+0xf0..+0xfc), fed the pitch over 2π and centred on 0.5.</summary>
	public CoasterClipMap InclineMap { get; internal set; } = CoasterClipMap.Identity;
	/// <summary>The bank clip's map (+0x100..+0x10c), fed the bank over 2π wrapped into [−½, ½) and centred on 0.5.</summary>
	public CoasterClipMap BankMap { get; internal set; } = CoasterClipMap.Identity;
	/// <summary>The second rotate (loop tilt) clip's map (+0x110..+0x11c), fed the loop rotate parameter wrapped into [0, 1).</summary>
	public CoasterClipMap LoopRotateMap { get; internal set; } = CoasterClipMap.Identity;
	/// <summary>The loft clip's map (+0xd0..+0xdc).</summary>
	public CoasterClipMap LoftMap => new( LoftStartCap, LoftEndCap, LoftMultiplier, LoftAdd );
	/// <summary>Whether the pylon model has set the mesh heights, the stack offset and the raised minimum (<see cref="CoasterDefinition.ApplyPylonModel"/>).</summary>
	public bool HasModelHeights { get; internal set; }
	/// <summary>
	/// The pylon model the knot pass poses for every node of this pylon (<c>0x430ed0</c>): its posed track tags are the node's knots. Null without the model
	/// (no game data), when every node is one knot at its position.
	/// </summary>
	public ICoasterTagModel? TagModel { get; internal set; }

	public bool IsLoop => (Flags & CoasterPylonFlags.Loop) != 0;
	public bool IsTrough => (Flags & CoasterPylonFlags.Trough) != 0;

	internal CoasterPylonControl Clone() => (CoasterPylonControl)MemberwiseClone();
}

/// <summary>
/// The coaster definition (COAST-plan §3.1, §9.1) built like <c>0x302cc</c>: the defaults of <c>0x2d274</c>, then the
/// coaster's <c>coaster.sam</c> values copied over them by <c>0x2fde8</c>/<c>0x2f7dc</c>. Floats and integers are
/// copied only when nonzero, so an absent or zero value keeps the default; the three pylon cost rates are copied
/// unconditionally (absent = 0). No PC <c>.sam</c> sets a motion, distance, angle or rating key, so the PC game
/// runs on these defaults.
/// </summary>
// [APPROX:COAST-014] the PC twins in tp.exe agree with the Mac readings for the rating step and final (ORACLE X11/X12; the step's stale term differs, what C# follows), COAST 2-6 (0x43b1f0..0x43b330; COAST 6's literal 0 floor is built), the mode and speed setters, the guest predicates, the debit 0x4d01f0, the validator 0x434740 (plus a move-mode skip for 0x10000 with 0x20000 and the closure re-validation), the sampler 0x440c10 (its angles 0x43e590 and the loop knot factor 0x432430 round the sine-table index to the nearest, built) and the hop 0x43ce20; what differs is the x87 arithmetic of the rating pass 0x43c450 and the ride motion only (COAST-032), which no oracle run has compared yet; the cost 0x447830 is built (the Mac formula, length and height multiplied by f32 0.1 (0x6fe1f0) in x87, dx squared through a double store and dz through a single store (0x4478dd..0x447913), summed as (length rate * length + unit) + height rate * height) and compared bit-exactly with isolated tp.exe runs
public sealed class CoasterDefinition
{
	/// <summary><c>Bumper.WhichTrackType</c> of a coaster (<c>0xe2fe4</c>: type record +156 == 3).</summary>
	public const int TrackType = 3;
	/// <summary>Definition flag set by <c>bSpecialIsTrough</c> (the three flumes).</summary>
	public const uint TroughFlag = 0x100;
	public const uint AlwaysFlags = 0xA0;
	public const uint VisibleEndPylonFlag = 0x20;
	public const uint AutoBankFlag = 0x10;
	/// <summary>Definition flag set by <c>bIsSuspendedCoaster</c>: the cars swing (and the track does not auto-bank).</summary>
	public const uint SuspendedFlag = 0x8;
	/// <summary>single(π/180), the factor the loader turns degrees into radians with.</summary>
	public static readonly float DegreesToRadians = (float)(Math.PI / 180);

	// ---- Definition fields (defaults of 0x2d274) --------------------------------------------------------

	public uint Flags { get; private set; }
	public float Curviness { get; private set; } = 1.25f;
	public float StraightDivisor { get; private set; } = 1.25f;
	public float AccelerationPerHeight { get; private set; } = 1.5f;
	public float UphillAccelModifier { get; private set; } = 1.0f;
	public float DownhillAccelModifier { get; private set; } = 1.0f;
	public float TrackHeightAdjust { get; private set; }
	public float WinchSpeed { get; private set; } = 5.0f;
	public float MinSpeed { get; private set; } = 5.0f;
	public float MaxSpeedAtMinSetting { get; private set; } = 80f;
	public float MaxSpeedAtMaxSetting { get; private set; } = 130f;
	public float FrictionMultiplier { get; private set; } = 0.8f;
	public float BendElevationLow { get; private set; }
	public float BendElevationHigh { get; private set; }
	public float GravityX { get; private set; }
	public float GravityY { get; private set; } = -0.3f;
	public float GravityZ { get; private set; }
	public float ForceMultiplier { get; private set; } = 0.2f;
	public float InitialPylonLoftHeight { get; private set; } = 10f;
	public int MaxTrains { get; private set; } = 1;
	/// <summary>
	/// <c>fEntranceSizeInterval</c> (definition <c>+796</c> = <c>+0x31c</c>): copied unconditionally, so 0 when the settings do not
	/// name it. The station window narrows toward its middle by this fraction (<c>0x43b750</c>).
	/// </summary>
	// [BIN:STP-PPC:0x1002FFB0 entrance interval] definition +796 ← settings 15220, unconditional; [BIN:TPW-EXE:0x42f717] definition +0x31c ← settings +0x3b74, unconditional; read only by the station window 0x43b750
	public float EntranceSizeInterval { get; private set; }
	/// <summary>Minimum headway between trains in milliseconds.</summary>
	public int TrainReleaseRate { get; private set; } = 15000;
	public float InitialSickness { get; private set; }
	public float InitialExcitement { get; private set; } = 1.0f;
	public float InitialUncomfortableness { get; private set; }
	public float GForceSicknessModifier { get; private set; } = 0.5f;
	public float UpsideDownSicknessModifier { get; private set; } = 3.0f;
	public float SpeedExcitementModifier { get; private set; } = 0.1f;
	public float GForceExcitementModifier { get; private set; } = 1.0f;
	public float UpsideDownExcitementModifier { get; private set; } = 1.0f;
	public float BankExcitementModifier { get; private set; } = 5.0f;
	public float XGForceUncomfortableModifier { get; private set; } = 1.0f;
	public float InvertedUncomfortableModifier { get; private set; } = 2.0f;
	public float NormalScreamStartExciteDelta { get; private set; } = 0.5f;
	public float TerrorScreamStartExciteDelta { get; private set; } = 1.0f;
	public float NormalScreamSustainExcitement { get; private set; } = 10f;
	public float TerrorScreamSustainExcitement { get; private set; } = 20f;
	/// <summary><c>sTrainType.uiMaxCars</c> (cars per train).</summary>
	public int MaxCars { get; private set; } = 1;
	/// <summary>The cars of one train (at least one).</summary>
	public int CarCount => Math.Max( 1, MaxCars );
	/// <summary>The car types (<c>asCarTypes</c>) and the front, centre and rear type of a train (<c>sTrainType</c>).</summary>
	public CoasterCarTypes CarTypes { get; private set; } = new();
	/// <summary>Whether the car models were read (<see cref="ApplyCarModels"/>); without them every car type has the generic metrics of <see cref="FallbackCarMetrics"/>.</summary>
	public bool HasCarModels { get; private set; }

	private CoasterCarMetrics[] carMetrics = Array.Empty<CoasterCarMetrics>();
	private float[] carOffsets = Array.Empty<float>();

	/// <summary>
	/// The metrics a car type takes without its model (no game data): 3.5 world units in front of and behind its pivot plus the type's distance adds,
	/// and four head sockets, so a generic train has its cars 7 units apart with four seats each.
	/// </summary>
	// [EXT:coaster-car-fallback] the original always has the car models (a type whose model fails to load has zero extents and no seat, which ApplyCarModels keeps); a definition built from settings alone (tests, a headless park without game data) takes these generic metrics instead
	public static CoasterCarMetrics FallbackCarMetrics( CoasterCarType type ) => new( type.FrontAdd + 3.5f, type.RearAdd + 3.5f, 4 );

	/// <summary>The car type index of car <paramref name="car"/> of a train (<c>0x436d10</c>: car 0 the front type, the last car the rear type, the others the centre type).</summary>
	public int CarTypeIndex( int car ) => CarTypes.TypeIndexFor( car, CarCount );

	/// <summary>The metrics of a car type index (an index without a type record takes an empty type).</summary>
	public CoasterCarMetrics CarTypeMetrics( int typeIndex ) => typeIndex >= 0 && typeIndex < carMetrics.Length ? carMetrics[typeIndex] : FallbackOrEmpty( new CoasterCarType( "", 0, 0, 0, false ) );

	/// <summary>The metrics of car <paramref name="car"/> of a train.</summary>
	public CoasterCarMetrics CarMetrics( int car ) => CarTypeMetrics( CarTypeIndex( car ) );

	private CoasterCarMetrics FallbackOrEmpty( CoasterCarType type ) => HasCarModels ? new CoasterCarMetrics( type.FrontAdd, type.RearAdd, 0 ) : FallbackCarMetrics( type );

	/// <summary>Whether car <paramref name="car"/> carries seats: its type is the train's centre type (<c>0x43bb7b</c>).</summary>
	public bool IsSeatCar( int car ) => CarTypeIndex( car ) == CarTypes.CentreIndex;

	/// <summary>The cars the seats are split over (<c>0x43ba90</c>): every car, less one when the front type is not the centre type and one when the rear type is not.</summary>
	// [BIN:TPW-EXE:0x43bb37 seat cars] param_4 = cars − (train definition +0xc ≠ +0x10) − (+0x14 ≠ +0x10): the front and rear type records compared with the centre one
	public int SeatCarCount => CarCount - (CarTypes.FrontIndex != CarTypes.CentreIndex ? 1 : 0) - (CarTypes.RearIndex != CarTypes.CentreIndex ? 1 : 0);

	/// <summary>The head sockets of one train (definition <c>+0x2f8</c>, the train definition's <c>+8</c>): the sockets of each car's type summed over the cars.</summary>
	// [BIN:TPW-EXE:0x42fc38 train heads] definition +0x2f8 = Σ over the uiMaxCars cars of the head count (+0x30) of the front type for car 0, the rear type for the last car and the centre type for the others
	public int HeadsPerTrain
	{
		get
		{
			var heads = 0;
			for ( var car = 0; car < MaxCars; car++ )
				heads += CarMetrics( car ).Heads;
			return heads;
		}
	}

	/// <summary>The head sockets of the whole fleet (definition <c>+0x318</c>): the sockets of one train × <c>uiMaxTrains</c>.</summary>
	// [BIN:TPW-EXE:0x42fd66 fleet heads] definition +0x318 = +0x2f8 × +0x2e8 (uiMaxTrains)
	public int FleetHeads => HeadsPerTrain * MaxTrains;

	/// <summary>
	/// The three distances between neighbouring cars (definition <c>+0x30c</c>, <c>+0x310</c>, <c>+0x314</c>): front type's rear plus the centre type's
	/// front, the centre type's rear plus its front, and the rear type's front plus the centre type's rear.
	/// </summary>
	// [BIN:TPW-EXE:0x42fc20 car pair distances] +0x30c = front.+0x28 + centre.+0x24, +0x310 = centre.+0x28 + centre.+0x24, +0x314 = rear.+0x24 + centre.+0x28 (each pair is measured against the centre type, also in a two-car train)
	public (float Front, float Centre, float Rear) CarPairDistances
	{
		get
		{
			var front = CarTypeMetrics( CarTypes.FrontIndex );
			var centre = CarTypeMetrics( CarTypes.CentreIndex );
			var rear = CarTypeMetrics( CarTypes.RearIndex );
			return (front.RearExtent + centre.FrontExtent, centre.RearExtent + centre.FrontExtent, rear.FrontExtent + centre.RearExtent);
		}
	}

	/// <summary>
	/// The car the ride view rides: the first car of the train when its type is the centre type, else the second one (train <c>+0x34</c> points at car 0, or at car 1 when the front type differs from the centre type).
	/// </summary>
	// [BIN:TPW-EXE:0x436f15 ride view car] the train builder 0x436d10 points train +0x34 at the car array's first record when the train definition's front and centre type records are the same (+0x2fc == +0x300, 0x436f21), else at the second record (0x436f2d..0x436f33); the ride camera takes that car's object (0x436880: controller +0x124 → +0x34 → +0xc)
	public int RideCameraCar => Math.Min( CarTypes.FrontIndex != CarTypes.CentreIndex ? 1 : 0, Math.Max( 0, CarCount - 1 ) );

	/// <summary>The reference car of a train (train <c>+0x2c</c> = (cars − 1) ≫ 1, the middle car): the train's position is this car's.</summary>
	// [BIN:TPW-EXE:0x43baef reference car] train +0x2c = (train +0xc − 1) >> 1 whenever the car count is set (0x43ba90)
	public int ReferenceCar => (CarCount - 1) >> 1;

	/// <summary>
	/// Each car's offset from the reference car along the track in world units (car <c>+0x38</c>, <c>0x43bc20</c>): the cars in front of it add, going
	/// forward, the pair distance their own index selects (front pair for car 0, rear pair for the last car, else centre), the cars behind it subtract it.
	/// </summary>
	public IReadOnlyList<float> CarOffsets => carOffsets;

	// [BIN:TPW-EXE:0x43bc20 car offsets] the reference car's +0x38 = 0; from it toward car 0 the sum grows by +0x1c (car index 0), +0x24 (the last index) or +0x20 of the train definition (definition +0x30c/+0x314/+0x310); behind it the sum falls by the same choice; +0x3c = +0x38 / spacing
	private void RebuildCarLayout() => carOffsets = CarOffsetsFor( CarPairDistances, CarCount );

	/// <summary>The cars' offsets around the reference car from the three pair distances, every running sum a single (the sum stays on the x87 stack at 24 bits).</summary>
	internal static float[] CarOffsetsFor( (float Front, float Centre, float Rear) pairs, int count )
	{
		float Pair( int car ) => car == 0 ? pairs.Front : car == count - 1 ? pairs.Rear : pairs.Centre;
		var offsets = new float[count];
		var reference = (count - 1) >> 1;
		var sum = 0f;
		for ( var car = reference - 1; car >= 0; car-- )
		{
			sum = sum + Pair( car );
			offsets[car] = sum;
		}
		sum = 0f;
		for ( var car = reference + 1; car < count; car++ )
		{
			sum = sum - Pair( car );
			offsets[car] = sum;
		}
		return offsets;
	}

	/// <summary>The generic metrics for every car type (no model read).</summary>
	private void SetFallbackCarMetrics()
	{
		carMetrics = CarTypes.Types.Select( FallbackCarMetrics ).ToArray();
		RebuildCarLayout();
	}

	/// <summary>
	/// Takes the car types' metrics from their models (<c>0x42fad0</c> → <c>0x430130</c> for the front, centre and rear type): the model extents plus
	/// the distance adds and the head sockets; a type the archive has no model for keeps zero extents and no seat, as the original's record does.
	/// </summary>
	internal void ApplyCarModels( Func<int, CoasterCarRig?> rig )
	{
		ArgumentNullException.ThrowIfNull( rig );
		ApplyCarMetrics( index => rig( index )?.Metrics( CarTypes.Types[index] ) );
	}

	/// <summary>The core of <see cref="ApplyCarModels"/>: each car type's metrics from its model, null for a type without one.</summary>
	internal void ApplyCarMetrics( Func<int, CoasterCarMetrics?> metrics )
	{
		ArgumentNullException.ThrowIfNull( metrics );
		var models = Enumerable.Range( 0, CarTypes.Types.Count ).Select( metrics ).ToArray();
		// An archive that holds none of the car models (a synthetic entry) is a definition without game data: it keeps the generic metrics.
		if ( models.All( model => model == null ) )
			return;
		HasCarModels = true;
		carMetrics = models.Select( model => model ?? new CoasterCarMetrics( 0, 0, 0 ) ).ToArray();
		RebuildCarLayout();
	}

	/// <summary>The StdPylon (definition <c>+168</c>).</summary>
	public CoasterPylonControl StandardPylon { get; private set; } = DefaultStandardPylon();
	/// <summary>The LoopPylon (definition <c>+456</c>), a trough pylon on trough coasters.</summary>
	public CoasterPylonControl LoopPylon { get; private set; } = DefaultLoopPylon();
	/// <summary>The coaster's settings name a second pylon control (<c>asPylonControls[1]</c>): loop pylons can be placed.</summary>
	public bool HasLoopPylon { get; private set; }

	public bool IsTrough => (Flags & TroughFlag) != 0;
	public bool AutoBanks => (Flags & AutoBankFlag) != 0;
	public bool IsSuspended => (Flags & SuspendedFlag) != 0;
	/// <summary><c>bVisibleEndPylon</c> as loaded. It selects nothing: the loader always sets 0x20 (in 0xA0), so completion always appends one end node.</summary>
	public bool VisibleEndPylon { get; private set; }

	/// <summary>The pylon control of a loop (true) or standard (false) pylon.</summary>
	public CoasterPylonControl Pylon( bool loop ) => loop ? LoopPylon : StandardPylon;

	private static CoasterPylonControl DefaultStandardPylon()
	{
		// [BIN:STP-PPC:0x1002D274 coaster defaults] StdPylon: flags 0x4, place distance 3..8 cells, entrance 140..180° at the minimum and 110..180° at the maximum distance, normal 80..180° and 50..180°, rotation deviance 45°, heights 10/10, one stacked pylon, radius 5, straight dot −0.999
		var pylon = new CoasterPylonControl
		{
			Flags = CoasterPylonFlags.Standard,
			MinPlaceDistance = 3,
			MaxPlaceDistance = 8,
			EntranceMinAngleAtMinDistance = 140 * DegreesToRadians,
			EntranceMaxAngleAtMinDistance = 180 * DegreesToRadians,
			EntranceMinAngleAtMaxDistance = 110 * DegreesToRadians,
			EntranceMaxAngleAtMaxDistance = 180 * DegreesToRadians,
			MinAngleAtMinDistance = 80 * DegreesToRadians,
			MaxAngleAtMinDistance = 180 * DegreesToRadians,
			MinAngleAtMaxDistance = 50 * DegreesToRadians,
			MaxAngleAtMaxDistance = 180 * DegreesToRadians,
			MaxRotationDeviance = 45 * DegreesToRadians,
			MaxLoopRotationDeviance = 0,
			MaxLoopHeightDifference = 0,
			MinHeight = 10,
			MaxHeight = 10,
			MaxStackedPylons = 1,
			CollisionPossibleRadius = 5,
			StraightDotThreshold = -0.999f
		};
		SetMeshHeights( pylon );
		return pylon;
	}

	private static CoasterPylonControl DefaultLoopPylon()
	{
		// [BIN:STP-PPC:0x1002D274 coaster defaults] LoopPylon: flags 0x4004, place distance 3..9 cells, entrance 120..174°, normal 0..174° at both distances, deviances 90°/90°, loop height difference 25, straight dot −0.8
		var pylon = new CoasterPylonControl
		{
			Flags = CoasterPylonFlags.Standard | CoasterPylonFlags.Loop,
			Capabilities = CoasterPylonCapabilities.Loft | CoasterPylonCapabilities.Rotate | CoasterPylonCapabilities.LoopTilt,
			MinPlaceDistance = 3,
			MaxPlaceDistance = 9,
			EntranceMinAngleAtMinDistance = 120 * DegreesToRadians,
			EntranceMaxAngleAtMinDistance = 174 * DegreesToRadians,
			EntranceMinAngleAtMaxDistance = 120 * DegreesToRadians,
			EntranceMaxAngleAtMaxDistance = 174 * DegreesToRadians,
			MinAngleAtMinDistance = 0,
			MaxAngleAtMinDistance = 174 * DegreesToRadians,
			MinAngleAtMaxDistance = 0,
			MaxAngleAtMaxDistance = 174 * DegreesToRadians,
			MaxRotationDeviance = 90 * DegreesToRadians,
			MaxLoopRotationDeviance = 90 * DegreesToRadians,
			// [BIN:STP-PPC:0x1002D5D4 loop pylon default] fMaxLoopHeightDifference (+172) defaults to the raw 25.0 (0x1002D5D8); only a coaster.sam value is scaled by 10·π/180 (0x1002FA60)
			MaxLoopHeightDifference = 25,
			MinHeight = 10,
			MaxHeight = 10,
			MaxStackedPylons = 1,
			CollisionPossibleRadius = 5,
			StraightDotThreshold = -0.8f
		};
		SetMeshHeights( pylon );
		return pylon;
	}

	// [EXT:coaster-pylon-fallback] (user-approved 2026-10-30, COAST-015 converted: the original always has its pylon models, so a definition built without game data, in tests and tools, exists only in OpenTPW) without the pylon model (no game data) the pylon mesh heights at loft t = 0 and t = 1 (+188/+192) are taken as fMinHeight and fMaxHeight; ApplyPylonModel replaces them with the model's track dummy heights h0 and h1 when the model is read
	private static void SetMeshHeights( CoasterPylonControl pylon )
	{
		pylon.MeshHeightLow = pylon.MinHeight;
		pylon.MeshHeightHigh = pylon.MaxHeight;
	}

	/// <summary>A definition with every code default (no settings).</summary>
	public static CoasterDefinition Default => new();

	/// <summary>True for an object whose settings make it a coaster station (<c>Bumper.WhichTrackType 3</c>).</summary>
	// [DATA:<ride>.sam:Bumper.WhichTrackType] 3 = coaster (Rides.sam comment lists 0 none, 1 car, 2 water track)
	public static bool IsCoaster( ObjectSettings settings ) => settings.GetInt( "Bumper.WhichTrackType" ) == TrackType;

	public static CoasterDefinition FromSettings( ObjectSettings settings ) => FromValues( settings.Values );

	/// <summary>
	/// The definition of a catalog entry from its own coaster.sam settings (<see cref="ObjectCatalogEntry.SharedSettings"/>), as the TrackMaster loader 0x42FAD0 receives them;
	/// an entry without shared files (synthetic ones) falls back to its object settings.
	/// </summary>
	public static CoasterDefinition FromEntry( ObjectCatalogEntry entry ) => FromSettings( entry.CoasterSettings );

	/// <summary>
	/// The definition of a catalogue coaster with its pylon models read: the settings over the defaults, then each pylon's mesh heights h0 and h1, its
	/// raised minimum and its stack offset from the model (<c>0x42fad0</c> → <c>0x430590</c>). A pylon model the archive lacks keeps the settings' heights.
	/// </summary>
	public static CoasterDefinition FromCatalogEntry( ObjectCatalogEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		var definition = FromEntry( entry );
		using var assets = new CoasterAssets( entry, definition ) { ResolveWrap = false };
		// [BIN:TPW-EXE:0x42fbde car models] the loader reads the centre car type's model, then the front and the rear type's when they are other types (0x430130 at 0x42fbde, 0x42fbf3, 0x42fc08) before the pair distances and head sums
		var used = new HashSet<int> { definition.CarTypes.FrontIndex, definition.CarTypes.CentreIndex, definition.CarTypes.RearIndex };
		definition.ApplyCarModels( index => used.Contains( index ) ? assets.GetCar( index ) : null );
		foreach ( var loop in new[] { false, true } )
		{
			if ( !loop || definition.HasLoopPylon )
			{
				if ( assets.GetPylon( loop ) is { } rig )
					definition.ApplyPylonModel( loop, rig );
			}
		}
		return definition;
	}

	/// <summary>
	/// Takes a pylon's heights from its model (<c>0x430590</c>): h0 and h1 are the track dummy's heights at the two loft poses, fMinHeight is raised to h0
	/// when it is below, and the stack offset is the stack dummy's height above h0.
	/// </summary>
	// [BIN:TPW-EXE:0x4309a4 pylon heights] pylon +0xbc = h0, +0xc0 = h1, +0x68 (fMinHeight) = max(fMinHeight, h0) (0x4309a4..0x4309bb), +0xc4 = stack dummy Y − h0 or 0
	internal void ApplyPylonModel( bool loop, CoasterPylonRig rig )
	{
		Pylon( loop ).Capabilities = rig.Capabilities;
		// The original applies h0 and h1 as they come (0x4309a4 has no guard on h1 <= h0, and a missing dummy reads the constant 0 at 0x6fde74).
		// OpenTPW skips a model whose track dummy is missing or does not move with the loft clip and keeps the settings' heights: with h1 <= h0 every
		// node of that pylon would stand at h0. No PC pylon model needs it since the clips turn the nodes relatively (0x470d10).
		if ( rig.TagNode < 0 || rig.TagHigh <= rig.TagLow )
			return;
		var pylon = Pylon( loop );
		pylon.MeshHeightLow = rig.TagLow;
		pylon.MeshHeightHigh = rig.TagHigh;
		pylon.MinHeight = MathF.Max( pylon.MinHeight, rig.TagLow );
		pylon.StackOffset = rig.StackOffset;
		pylon.HasModelHeights = true;
		if ( rig.HasTags )
			pylon.TagModel = rig;
	}

	/// <summary>
	/// Builds the definition from the (layered) settings keys: <c>sCoasterType.*</c>, <c>sTrainType.*</c> and
	/// <c>asPylonControls[0|1].*</c>. Only nonzero values replace a default; the pylon cost rates are copied as given.
	/// </summary>
	// [BIN:STP-PPC:0x1002FDE8 coaster loader] every float is copied over the definition default only when nonzero (fEntranceSizeInterval and the three pylon cost rates via 0x1002F7DC unconditionally); the loader always sets flags 0xA0, bVisibleEndPylon sets 0x20, bNoAutoBanking == 0 sets 0x10, bSpecialIsTrough sets 0x100 and turns pylon 1 from a loop (0x4000) into a trough (0x8000) pylon; fAutoBankDivisor is never copied
	public static CoasterDefinition FromValues( IReadOnlyDictionary<string, string> values )
	{
		var definition = new CoasterDefinition();
		float Float( string key, float current ) => TryFloat( values, key, out var value ) && value != 0 ? value : current;
		int Int( string key, int current ) => TryInt( values, key, out var value ) && value != 0 ? value : current;
		bool Flag( string key ) => TryInt( values, key, out var value ) && value != 0;
		const string Coaster = "sCoasterType.";
		definition.Curviness = Float( Coaster + "fCurviness", definition.Curviness );
		definition.StraightDivisor = Float( Coaster + "fStraightDivisor", definition.StraightDivisor );
		definition.AccelerationPerHeight = Float( Coaster + "fAccelerationPerHeight", definition.AccelerationPerHeight );
		definition.UphillAccelModifier = Float( Coaster + "fUphillAccelModifier", definition.UphillAccelModifier );
		definition.DownhillAccelModifier = Float( Coaster + "fDownhillAccelModifier", definition.DownhillAccelModifier );
		definition.TrackHeightAdjust = Float( Coaster + "fTrackHeightAdjust", definition.TrackHeightAdjust );
		definition.WinchSpeed = Float( Coaster + "fWinchSpeed", definition.WinchSpeed );
		definition.MinSpeed = Float( Coaster + "fMinSpeed", definition.MinSpeed );
		definition.MaxSpeedAtMinSetting = Float( Coaster + "fMaxSpeedAtMinSetting", definition.MaxSpeedAtMinSetting );
		definition.MaxSpeedAtMaxSetting = Float( Coaster + "fMaxSpeedAtMaxSetting", definition.MaxSpeedAtMaxSetting );
		definition.FrictionMultiplier = Float( Coaster + "fFrictionMultiplier", definition.FrictionMultiplier );
		definition.BendElevationLow = Float( Coaster + "fBendElevationLow", definition.BendElevationLow );
		definition.BendElevationHigh = Float( Coaster + "fBendElevationHigh", definition.BendElevationHigh );
		definition.GravityX = Float( Coaster + "fGravityX", definition.GravityX );
		definition.GravityY = Float( Coaster + "fGravityY", definition.GravityY );
		definition.GravityZ = Float( Coaster + "fGravityZ", definition.GravityZ );
		definition.ForceMultiplier = Float( Coaster + "fForceMultiplier", definition.ForceMultiplier );
		definition.InitialPylonLoftHeight = Float( Coaster + "fInitialPylonLoftHeight", definition.InitialPylonLoftHeight );
		definition.MaxTrains = Int( Coaster + "uiMaxTrains", definition.MaxTrains );
		definition.EntranceSizeInterval = TryFloat( values, Coaster + "fEntranceSizeInterval", out var entranceInterval ) ? entranceInterval : 0f;
		definition.TrainReleaseRate = Int( Coaster + "uiTrainReleaseRate", definition.TrainReleaseRate );
		definition.InitialSickness = Float( Coaster + "fInitialSickness", definition.InitialSickness );
		definition.InitialExcitement = Float( Coaster + "fInitialExcitement", definition.InitialExcitement );
		definition.InitialUncomfortableness = Float( Coaster + "fInitialUncomfortableness", definition.InitialUncomfortableness );
		definition.GForceSicknessModifier = Float( Coaster + "fGForceSicknessModifier", definition.GForceSicknessModifier );
		definition.UpsideDownSicknessModifier = Float( Coaster + "fUpsideDownSicknessModifier", definition.UpsideDownSicknessModifier );
		definition.SpeedExcitementModifier = Float( Coaster + "fSpeedExcitementModifier", definition.SpeedExcitementModifier );
		definition.GForceExcitementModifier = Float( Coaster + "fGForceExcitementModifier", definition.GForceExcitementModifier );
		definition.UpsideDownExcitementModifier = Float( Coaster + "fUpsideDownExcitementModifier", definition.UpsideDownExcitementModifier );
		definition.BankExcitementModifier = Float( Coaster + "fBankExcitementModifier", definition.BankExcitementModifier );
		definition.XGForceUncomfortableModifier = Float( Coaster + "fXGForceUncomfortableModifier", definition.XGForceUncomfortableModifier );
		definition.InvertedUncomfortableModifier = Float( Coaster + "fInvertedUncomfortableModifier", definition.InvertedUncomfortableModifier );
		definition.NormalScreamStartExciteDelta = Float( Coaster + "fNormalScreamStartExciteDelta", definition.NormalScreamStartExciteDelta );
		definition.TerrorScreamStartExciteDelta = Float( Coaster + "fTerrorScreamStartExciteDelta", definition.TerrorScreamStartExciteDelta );
		definition.NormalScreamSustainExcitement = Float( Coaster + "fNormalScreamSustainExcitement", definition.NormalScreamSustainExcitement );
		definition.TerrorScreamSustainExcitement = Float( Coaster + "fTerrorScreamSustainExcitement", definition.TerrorScreamSustainExcitement );
		// [DATA:coaster.sam:sTrainType.uiMaxCars]
		definition.MaxCars = Int( "sTrainType.uiMaxCars", definition.MaxCars );
		definition.CarTypes = CoasterCarTypes.FromValues( values );
		definition.SetFallbackCarMetrics();

		var flags = AlwaysFlags;
		// The always-set 0xA0 already holds 0x20, so the setting is kept on its own (it does not change the end node count, 0x435570).
		definition.VisibleEndPylon = Flag( Coaster + "bVisibleEndPylon" );
		if ( definition.VisibleEndPylon )
			flags |= VisibleEndPylonFlag;
		if ( !Flag( Coaster + "bNoAutoBanking" ) )
			flags |= AutoBankFlag;
		var trough = Flag( Coaster + "bSpecialIsTrough" );
		if ( trough )
			flags |= TroughFlag;
		// [BIN:STP-PPC:0x10030248 suspended coaster] bIsSuspendedCoaster sets definition flag 0x8 and clears 0x10, so suspended coasters never auto-bank
		if ( Flag( Coaster + "bIsSuspendedCoaster" ) )
			flags = (flags | SuspendedFlag) & ~AutoBankFlag;
		definition.Flags = flags;

		definition.StandardPylon = LoadPylon( values, 0, DefaultStandardPylon() );
		definition.LoopPylon = LoadPylon( values, 1, DefaultLoopPylon() );
		if ( trough )
			definition.LoopPylon.Flags = (definition.LoopPylon.Flags & ~CoasterPylonFlags.Loop) | CoasterPylonFlags.Trough;
		// [DATA:coaster.sam:asPylonControls[1]] only coasters whose settings name a second pylon control offer loop pylons (9 of the 12 PC coasters)
		definition.HasLoopPylon = values.Keys.Any( key => key.StartsWith( "asPylonControls[1].", StringComparison.OrdinalIgnoreCase ) );
		return definition;
	}

	// [BIN:STP-PPC:0x1002F7DC pylon loader] place distances are whole cells (squares stored at +156/+160), angles in degrees × single(π/180), fMaxLoopHeightDifference × 10 × π/180; the cost rates +92/+96/+100 are copied as given
	private static CoasterPylonControl LoadPylon( IReadOnlyDictionary<string, string> values, int index, CoasterPylonControl pylon )
	{
		var prefix = $"asPylonControls[{index}].";
		float Float( string key, float current ) => TryFloat( values, prefix + key, out var value ) && value != 0 ? value : current;
		float Angle( string key, float current ) => TryFloat( values, prefix + key, out var value ) && value != 0 ? value * DegreesToRadians : current;
		int Int( string key, int current ) => TryInt( values, prefix + key, out var value ) && value != 0 ? value : current;
		float Rate( string key ) => TryFloat( values, prefix + key, out var value ) ? value : 0;
		if ( values.TryGetValue( prefix + "pcMeshFilename", out var mesh ) )
			pylon.MeshName = mesh;
		// [BIN:TPW-EXE:0x42ec47 pylon base flag] a pylon control whose bDontDisableBaseOnStack setting (+0x38b0 of the 0xbc-byte record) is nonzero gets pylon flag 0x10; 0x432df0 then leaves the ground node visible on stacked nodes
		if ( TryInt( values, prefix + "bDontDisableBaseOnStack", out var keepBase ) && keepBase != 0 )
			pylon.Flags |= CoasterPylonFlags.KeepBaseOnStack;
		pylon.MinPlaceDistance = Int( "uiMinPlaceDist", pylon.MinPlaceDistance );
		pylon.MaxPlaceDistance = Int( "uiMaxPlaceDist", pylon.MaxPlaceDistance );
		pylon.EntranceMinAngleAtMinDistance = Angle( "fEntranceMinXZAngleAtMinDist", pylon.EntranceMinAngleAtMinDistance );
		pylon.EntranceMaxAngleAtMinDistance = Angle( "fEntranceMaxXZAngleAtMinDist", pylon.EntranceMaxAngleAtMinDistance );
		pylon.EntranceMinAngleAtMaxDistance = Angle( "fEntranceMinXZAngleAtMaxDist", pylon.EntranceMinAngleAtMaxDistance );
		pylon.EntranceMaxAngleAtMaxDistance = Angle( "fEntranceMaxXZAngleAtMaxDist", pylon.EntranceMaxAngleAtMaxDistance );
		pylon.MinAngleAtMinDistance = Angle( "fMinXZAngleAtMinDist", pylon.MinAngleAtMinDistance );
		pylon.MaxAngleAtMinDistance = Angle( "fMaxXZAngleAtMinDist", pylon.MaxAngleAtMinDistance );
		pylon.MinAngleAtMaxDistance = Angle( "fMinXZAngleAtMaxDist", pylon.MinAngleAtMaxDistance );
		pylon.MaxAngleAtMaxDistance = Angle( "fMaxXZAngleAtMaxDist", pylon.MaxAngleAtMaxDistance );
		pylon.MaxRotationDeviance = Angle( "fMaxRotnAngleDeviance", pylon.MaxRotationDeviance );
		pylon.MaxLoopRotationDeviance = Angle( "fMaxLoopRotnAngleDeviance", pylon.MaxLoopRotationDeviance );
		if ( TryFloat( values, prefix + "fMaxLoopHeightDifference", out var loopHeight ) && loopHeight != 0 )
			pylon.MaxLoopHeightDifference = loopHeight * 10 * DegreesToRadians;
		pylon.MinHeight = Float( "fMinHeight", pylon.MinHeight );
		pylon.MaxHeight = Float( "fMaxHeight", pylon.MaxHeight );
		pylon.MaxStackedPylons = Int( "uiMaxStackedPylons", pylon.MaxStackedPylons );
		pylon.CollisionPossibleRadius = Float( "fCollisionPossibleRadius", pylon.CollisionPossibleRadius );
		// [BIN:TPW-EXE:0x42ef05 loft keys] pylon +0xd0 = fLoftAnimStartCap, +0xd4 = fLoftAnimEndCap, +0xd8 = fLoftAnimMultiplier, +0xdc = fLoftAnimAdd, each copied from the settings (+0x3914..+0x3920) when nonzero; no PC coaster.sam sets them, so the defaults 0, 1, 1, 0 stand
		pylon.LoftStartCap = Float( "fLoftAnimStartCap", pylon.LoftStartCap );
		pylon.LoftEndCap = Float( "fLoftAnimEndCap", pylon.LoftEndCap );
		pylon.LoftMultiplier = Float( "fLoftAnimMultiplier", pylon.LoftMultiplier );
		pylon.LoftAdd = Float( "fLoftAnimAdd", pylon.LoftAdd );
		// [BIN:TPW-EXE:0x42ef68 clip map keys] pylon +0xe0..+0xec = fRotateAnim{StartCap,EndCap,Multiplier,Add}, +0xf0..+0xfc fInclineAnim…, +0x100..+0x10c fBankAnim…, +0x110..+0x11c fLoopRotateAnim…, each copied when nonzero; the PC coaster.sam files set fInclineAnimMultiplier (4.5, 3 or −3) and fBankAnimMultiplier (−1 or −4.5)
		CoasterClipMap Map( string clip, CoasterClipMap current ) => new( Float( $"f{clip}AnimStartCap", current.StartCap ), Float( $"f{clip}AnimEndCap", current.EndCap ),
			Float( $"f{clip}AnimMultiplier", current.Multiplier ), Float( $"f{clip}AnimAdd", current.Add ) );
		pylon.RotateMap = Map( "Rotate", pylon.RotateMap );
		pylon.InclineMap = Map( "Incline", pylon.InclineMap );
		pylon.BankMap = Map( "Bank", pylon.BankMap );
		pylon.LoopRotateMap = Map( "LoopRotate", pylon.LoopRotateMap );
		pylon.StraightDotThreshold = Float( "fPylonStraightDotThreshold", pylon.StraightDotThreshold );
		pylon.CostPerUnit = Rate( "fCostPerUnit" );
		pylon.CostPer2DLength = Rate( "fCostPer2DLength" );
		pylon.CostPerHeight = Rate( "fCostPerHeight" );
		SetMeshHeights( pylon );
		return pylon;
	}

	private static bool TryFloat( IReadOnlyDictionary<string, string> values, string key, out float value )
	{
		value = 0;
		return TryGet( values, key, out var text ) && float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out value );
	}

	private static bool TryInt( IReadOnlyDictionary<string, string> values, string key, out int value )
	{
		value = 0;
		if ( !TryGet( values, key, out var text ) )
			return false;
		if ( int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value ) )
			return true;
		if ( !float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) || !float.IsFinite( number ) )
			return false;
		value = (int)number;
		return true;
	}

	private static bool TryGet( IReadOnlyDictionary<string, string> values, string key, out string text )
	{
		if ( values.TryGetValue( key, out text! ) )
			return true;
		// Settings dictionaries are case-insensitive; plain dictionaries from tests may not be.
		foreach ( var (candidate, value) in values )
		{
			if ( string.Equals( candidate, key, StringComparison.OrdinalIgnoreCase ) )
			{
				text = value;
				return true;
			}
		}
		text = "";
		return false;
	}
}
