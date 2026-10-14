using System.Globalization;

namespace OpenTPW;

/// <summary>Pylon control flags (pylon <c>+0</c>, COAST-plan §3.2).</summary>
[Flags]
public enum CoasterPylonFlags : uint
{
	None = 0,
	Standard = 0x4,
	Loop = 0x4000,
	Trough = 0x8000
}

/// <summary>
/// One of the two 288-byte pylon controls of a coaster definition (COAST-plan §3.2): the StdPylon at definition
/// <c>+168</c> and the LoopPylon at <c>+456</c>. Angles are radians (the settings give degrees), place distances
/// whole cells; the loader stores both the squared distances and the float distances.
/// </summary>
public sealed class CoasterPylonControl
{
	public CoasterPylonFlags Flags { get; internal set; }
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
	/// <summary>Whether the pylon model has set the mesh heights, the stack offset and the raised minimum (<see cref="CoasterDefinition.ApplyPylonModel"/>).</summary>
	public bool HasModelHeights { get; internal set; }

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
// [APPROX:COAST-014] the PC twins in tp.exe agree with the Mac readings for the rating step and final (ORACLE X11/X12; the step's stale term differs, what C# follows), COAST 2-6 (0x43b1f0..0x43b330), the mode and speed setters, the guest predicates, the debit 0x4d01f0, the validator 0x434740 (plus a move-mode skip for 0x10000 with 0x20000 and the closure re-validation), the sampler 0x440c10 and the hop 0x43ce20; the differences are COAST 6's literal 0 floor (the Mac reading takes a global), the round-to-nearest sine-table indices (fistp) of the PC where the Mac and C# truncate (the swing uses the PC rule; the validator's and curve's Mac tables truncate), x87 arithmetic, the three-node station (COAST-029) and the creation parameters of the end node; the cost routine 0x447830 is not compared — evidence needed: 0x447830 against the Mac cost 0x1004B168
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

	// [APPROX:COAST-015] without the pylon model (no game data) the pylon mesh heights at loft t = 0 and t = 1 (+188/+192) are taken as fMinHeight and fMaxHeight; ApplyPylonModel replaces them with the model's track dummy heights h0 and h1 when the model is read
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
	/// The definition of a catalogue coaster with its pylon models read: the settings over the defaults, then each pylon's mesh heights h0 and h1, its
	/// raised minimum and its stack offset from the model (<c>0x42fad0</c> → <c>0x430590</c>). A pylon model the archive lacks keeps the settings' heights.
	/// </summary>
	public static CoasterDefinition FromCatalogEntry( ObjectCatalogEntry entry )
	{
		ArgumentNullException.ThrowIfNull( entry );
		var definition = FromSettings( entry.Settings );
		using var assets = new CoasterAssets( entry, definition );
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
		// The original applies h0 and h1 as they come (0x4309a4 has no guard on h1 <= h0, and a missing dummy reads the constant 0 at 0x6fde74).
		// OpenTPW skips a model whose track dummy is missing or does not move with the loft clip (the Gorilla coaster 3180's loop pylon, h0 = h1 =
		// 9.47, below its own raised minimum 10) and keeps the settings' heights: with h1 <= h0 every node of that pylon would stand at h0.
		if ( rig.TagNode < 0 || rig.TagHigh <= rig.TagLow )
			return;
		var pylon = Pylon( loop );
		pylon.MeshHeightLow = rig.TagLow;
		pylon.MeshHeightHigh = rig.TagHigh;
		pylon.MinHeight = MathF.Max( pylon.MinHeight, rig.TagLow );
		pylon.StackOffset = rig.StackOffset;
		pylon.HasModelHeights = true;
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

		var flags = AlwaysFlags;
		// The always-set 0xA0 already holds 0x20, so the setting is kept on its own for the end-node count (COAST-029).
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
