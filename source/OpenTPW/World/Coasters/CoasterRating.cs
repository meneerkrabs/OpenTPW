using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>The coaster's ratings after the rating pass (controller <c>+256..+276</c>, COAST-plan §6.4, §9.1).</summary>
public readonly record struct CoasterRatingResult( float Sickness, float Excitement, int CountedSections, float Length, float MaxHeight, float MaxSpeed, float LapTime, int Trains );

/// <summary>
/// The definition factors the rating step reads (definition <c>+812..+856</c>, named by the operand each multiplies).
/// </summary>
public readonly record struct CoasterRatingFactors(
	float GForceSickness, float UpsideDownSickness, float Speed, float GForceExcitement, float BankExcitement,
	float XGForceUncomfortable, float InvertedUncomfortable, float NormalScreamStart, float TerrorScreamStart,
	float NormalScreamSustain, float TerrorScreamSustain )
{
	// [BIN:STP-PPC:0x100415B4 rating step] definition +812 GForceSickness × |F|₁, +816 UpsideDownSickness × u, +820 Speed × v, +824 GForceExcitement × |F|₁, +832 BankExcitement × |bank|, +836 XGForceUncomfortable, +840 InvertedUncomfortable, +844/+848 scream start deltas, +852/+856 scream sustain excitements
	public static CoasterRatingFactors From( CoasterDefinition definition ) => new(
		definition.GForceSicknessModifier, definition.UpsideDownSicknessModifier, definition.SpeedExcitementModifier,
		definition.GForceExcitementModifier, definition.BankExcitementModifier, definition.XGForceUncomfortableModifier,
		definition.InvertedUncomfortableModifier, definition.NormalScreamStartExciteDelta, definition.TerrorScreamStartExciteDelta,
		definition.NormalScreamSustainExcitement, definition.TerrorScreamSustainExcitement );
}

/// <summary>The rating final's results (controller <c>+256..+276</c>).</summary>
public readonly record struct CoasterRatingFinal( float Sickness, float Excitement, int Sections, float Length, float MaxHeight, float MaxSpeed );

/// <summary>
/// The excitement and sickness accumulators of the rating (reset <c>0x41510</c>, step <c>0x415b4</c>, final
/// <c>0x41900</c>), in single precision. The "previous force" and "previous bank" are cleared at reset and never
/// written, so the step uses the current magnitudes. <see cref="Step"/>, <see cref="Restore"/> and
/// <see cref="FinalFor"/> are the seams the PPC differential oracle compares (docs/reverse/ORACLE.md, hooks
/// <c>coaster_rating_step</c> and <c>coaster_rating_final</c>).
/// </summary>
public sealed class CoasterRating
{
	/// <summary>The decay of the running values per sample.</summary>
	public const float Decay = 0.5f;
	/// <summary>Weight of the peak excitement in the final rating.</summary>
	public const float PeakExcitementWeight = 1.7f;
	public const float Maximum = 100f;
	/// <summary>Sample flag bits the step owns: it clears 1, 2 and 4 before setting the scream flags.</summary>
	public const CoasterSampleFlags StepFlagMask = (CoasterSampleFlags)7;
	private static readonly float HalfPi = (float)(Math.PI / 2);

	// [BIN:STP-PPC:0x10041510 rating reset] sickness = fInitialSickness (+800), excitement = fInitialExcitement (+804), peaks 0, previous force (12 bytes) and bank cleared
	public CoasterRating( CoasterDefinition definition )
		: this( CoasterRatingFactors.From( definition ?? throw new ArgumentNullException( nameof( definition ) ) ), definition.InitialSickness, definition.InitialExcitement )
	{
	}

	public CoasterRating( CoasterRatingFactors factors, float sickness, float excitement )
	{
		Factors = factors;
		Sickness = sickness;
		Excitement = excitement;
		PreviousExcitement = excitement;
	}

	public CoasterRatingFactors Factors { get; }
	public float Sickness { get; private set; }
	public float Excitement { get; private set; }
	public float PeakSickness { get; private set; }
	public float PeakExcitement { get; private set; }
	public float PreviousExcitement { get; private set; }
	/// <summary>The normal and terror "screaming" states (stored as 1/0 by the original).</summary>
	public bool NormalScream { get; private set; }
	public bool TerrorScream { get; private set; }

	/// <summary>Sets the running state, as the step's globals hold it between samples (oracle seam).</summary>
	public void Restore( float sickness, float excitement, float peakSickness, float peakExcitement, float previousExcitement, bool normalScream, bool terrorScream )
	{
		Sickness = sickness;
		Excitement = excitement;
		PeakSickness = peakSickness;
		PeakExcitement = peakExcitement;
		PreviousExcitement = previousExcitement;
		NormalScream = normalScream;
		TerrorScream = terrorScream;
	}

	/// <summary>
	/// One sample: dS = GForceSickness·|F|₁ + UpsideDownSickness·u, dE = Speed·v + GForceExcitement·|F|₁ +
	/// BankExcitement·|bank|, dU = XGForceUncomfortable·|Fx| + (inverted, or Fz); S = ½S + dS, E = ½E + max(dE − dU, 0);
	/// peaks follow. Returns the sample flags with bits 1, 2, 4 replaced by the screams (from index 3 on).
	/// </summary>
	// [BIN:STP-PPC:0x100415B4 rating step] |F|₁ = (|Fx| + |Fy|) + |Fz|; U = max(|bank| − π/2, 0), u = U + (U ≠ 0); separate single multiplies and adds; inverted discomfort u × InvertedUncomfortable when u ≠ 0 and Fy < 0.1, tripled when Fy < 0, otherwise the f0 the call site 0x1003FFA8 leaves live: Fz (sample +40, ORACLE F7, 509/509 bit-exact); S and E decay by 0.5; screams after sample 2 (flags & ~7, then 4 normal, 2 terror)
	public CoasterSampleFlags Step( int index, float speed, float bank, SVector3 force, CoasterSampleFlags flags = CoasterSampleFlags.None )
	{
		var d = Factors;
		var fx = MathF.Abs( force.X );
		var magnitude = fx + MathF.Abs( force.Y ) + MathF.Abs( force.Z );
		var absoluteBank = MathF.Abs( bank );
		var inverted = MathF.Max( absoluteBank - HalfPi, 0f );
		var upside = inverted + (inverted != 0 ? 1f : 0f);
		var sickness = d.GForceSickness * magnitude + d.UpsideDownSickness * upside;
		var excite = d.Speed * speed + d.GForceExcitement * magnitude + d.BankExcitement * absoluteBank;
		float discomfort;
		if ( upside != 0 && force.Y < 0.1f )
		{
			discomfort = upside * d.InvertedUncomfortable;
			if ( force.Y < 0 )
				discomfort *= 3f;
		}
		else
			discomfort = force.Z;
		discomfort = d.XGForceUncomfortable * fx + discomfort;
		var delta = MathF.Max( excite - discomfort, 0f );
		Sickness = Decay * Sickness + sickness;
		Excitement = Decay * Excitement + delta;
		PeakSickness = MathF.Max( PeakSickness, Sickness );
		PeakExcitement = MathF.Max( PeakExcitement, Excitement );
		flags &= ~StepFlagMask;
		if ( index > 2 )
		{
			var rise = Excitement - PreviousExcitement;
			if ( rise > d.NormalScreamStart )
			{
				NormalScream = true;
				flags |= CoasterSampleFlags.NormalScream;
			}
			else if ( NormalScream && Excitement > d.NormalScreamSustain )
				flags |= CoasterSampleFlags.NormalScream;
			else
				NormalScream = false;
			if ( rise > d.TerrorScreamStart )
			{
				TerrorScream = true;
				flags |= CoasterSampleFlags.TerrorScream;
			}
			else if ( TerrorScream && Excitement > d.TerrorScreamSustain )
				flags |= CoasterSampleFlags.TerrorScream;
			else
				TerrorScream = false;
		}
		PreviousExcitement = Excitement;
		return flags;
	}

	/// <summary>sickness = clamp(S + peakS, 0, 100) and excitement = clamp(½E + 1.7·peakE, 0, 100) (<c>0x41900</c>).</summary>
	public (float Sickness, float Excitement) Final()
	{
		var final = FinalFor( Sickness, Excitement, PeakSickness, PeakExcitement, Array.Empty<uint>(), 0, 0, 0, 0 );
		return (final.Sickness, final.Excitement);
	}

	/// <summary>
	/// The final routine <c>0x41900</c> on given accumulators: the clamped ratings, the count of sections whose flags
	/// have both 0x40 and 0x80 set, the length +168 × +172 and the maxima (oracle seam).
	/// </summary>
	// [BIN:STP-PPC:0x10041900 rating final] sickness = S + peakS, excitement = 0.5 × E + 1.7 × peakE, each clamped to [0, 100] (data 0x3d160..0x3d16c); +264 counts the sections with both flags 0x40 and 0x80 set (0x100419CC..0x100419EC, ORACLE F8); +268 = +168 × +172; +272 max Y; +276 max speed
	public static CoasterRatingFinal FinalFor( float sickness, float excitement, float peakSickness, float peakExcitement, IEnumerable<uint> sectionFlags,
		float sampleCount, float spacing, float maxHeight, float maxSpeed )
	{
		var rawSickness = sickness + peakSickness;
		var rawExcitement = Decay * excitement + PeakExcitementWeight * peakExcitement;
		var sections = sectionFlags.Count( flags => (flags & 0xC0) == 0xC0 );
		return new CoasterRatingFinal( Clamp( rawSickness ), Clamp( rawExcitement ), sections, sampleCount * spacing, maxHeight, maxSpeed );
	}

	private static float Clamp( float value ) => value > Maximum ? Maximum : value < 0 ? 0 : value;

	/// <summary>The guests' excitement of a coaster ride: trunc(½E + 50), E in [0, 100].</summary>
	// [BIN:STP-PPC:0x100DFF70 ride excitement] track type 3: 0 without a coaster controller (0x100B5E24), else trunc(fmadds(E, 0.5, 50)) with E the controller's excitement (getter 0x10041A14)
	public static int GuestExcitement( float excitement ) => (int)MathF.FusedMultiplyAdd( excitement, 0.5f, 50f );

	/// <summary>
	/// The train count after a rating pass: min(uiMaxTrains, max(1, trunc(lap / (uiTrainReleaseRate / 1000)))).
	/// </summary>
	// [BIN:STP-PPC:0x10040470 train count] lap seconds / (uiTrainReleaseRate (+748) / 1000) truncated, at least 1 (data 0x3d144), at most uiMaxTrains (+744)
	public static int TrainCount( float lapSeconds, int releaseRateMilliseconds, int maxTrains )
	{
		var headway = (float)releaseRateMilliseconds / 1000f;
		var computed = lapSeconds / headway;
		var trains = float.IsFinite( computed ) && computed < int.MaxValue ? (int)computed : int.MaxValue;
		return Math.Min( maxTrains, Math.Max( 1, trains ) );
	}

	/// <summary>
	/// The rating-pass speed step: v = max(mod·(PerHeight·(y − y_next)) + v, winch), × friction on friction samples,
	/// floored at the winch speed again; a section whose speed sits on the floor is a winch section; then the cap.
	/// </summary>
	// [BIN:STP-PPC:0x1003FE28 rating speed] acceleration = fAccelerationPerHeight × (y − y_next) (single), fused with fUphillAccelModifier (+40) when y_next > y else fDownhillAccelModifier (+44); floored at fWinchSpeed (+68); × fFrictionMultiplier (+84) on sample flag 8; floored again; v == winch marks section flag 0x800; then min(v, controller +52)
	public static (float Speed, bool Winch) NextSpeed( CoasterDefinition definition, float speed, float y, float yNext, bool friction, float cap )
	{
		var modifier = yNext > y ? definition.UphillAccelModifier : definition.DownhillAccelModifier;
		var acceleration = definition.AccelerationPerHeight * (y - yNext);
		var value = MathF.Max( MathF.FusedMultiplyAdd( modifier, acceleration, speed ), definition.WinchSpeed );
		if ( friction )
			value *= definition.FrictionMultiplier;
		value = MathF.Max( value, definition.WinchSpeed );
		var winch = value == definition.WinchSpeed;
		return (MathF.Min( value, cap ), winch);
	}

	/// <summary>
	/// The rating pass <c>0x3fc18</c> over samples 1..n−2: trough friction flags, force terms, the speed step with
	/// winch marking, the rating step, maxima and the lap time; then the suspended swing, the final rating and the train count.
	/// Writes each sample's speed, force, flags and (suspended coasters, after the pass) roll; returns the winch sections.
	/// </summary>
	// [BIN:STP-PPC:0x1003FC18 rating pass] start speed fWinchSpeed (+68); samples 1..n−2; trough coasters (flag 0x100) set sample flag 8 where the next segment climbs more than 0.01745; force a·v/10 into sample +32..+40; speed into +28; the rating step reads the roll +24; lap += spacing / v; then the suspended swing (flag 0x8), final (max Y, max speed) and the train count
	public static (CoasterRatingResult Result, HashSet<int> WinchSections) RatePass( CoasterDefinition definition, CoasterSample[] samples, float spacing, float cap )
	{
		var rating = new CoasterRating( definition );
		var winch = new HashSet<int>();
		var speed = definition.WinchSpeed;
		var lap = 0f;
		var maxSpeed = 0f;
		var maxHeight = float.MinValue;
		var count = samples.Length;
		// [BIN:STP-PPC:0x1003FCC8 trough slope] the climb threshold 0.01745 (-13516(r2)) on the normalised next-segment Y
		const float TroughClimb = 0.01745f;
		// [BIN:STP-PPC:0x1003FCCC force scale] the force term is a × v / 10 (-13520(r2))
		const float ForceDivisor = 10f;
		for ( var index = 1; index < count - 1; index++ )
		{
			ref var sample = ref samples[index];
			var next = samples[index + 1].Position;
			if ( definition.IsTrough )
			{
				var segment = next - sample.Position;
				if ( segment.LengthSquared() > 0 && SVector3.Normalize( segment ).Y > TroughClimb )
					sample.Flags |= CoasterSampleFlags.Friction;
			}
			// [APPROX:COAST-021] the force term a = (d_prev + d_next) / spacing × (−g) reads d_prev = p_i − p_(i−1) and d_next = p_i − p_(i+1) (so a dip pushes riders into their seats, +Y), with g = 1.0 (data 0x3d140); fGravityX/Y/Z and fForceMultiplier take no part — evidence needed: the operands of the difference vectors in 0x1003FC18
			var a = (samples[index - 1].Position + next - 2 * sample.Position) / spacing;
			var (value, onFloor) = NextSpeed( definition, speed, sample.Position.Y, next.Y, (sample.Flags & CoasterSampleFlags.Friction) != 0, cap );
			speed = value;
			if ( onFloor )
				winch.Add( sample.Section );
			sample.Speed = speed;
			sample.Force = a * speed / ForceDivisor;
			sample.Flags = rating.Step( index, speed, sample.Bank, sample.Force, sample.Flags );
			maxSpeed = MathF.Max( maxSpeed, speed );
			maxHeight = MathF.Max( maxHeight, sample.Position.Y );
			lap += spacing / speed;
		}
		if ( count > 0 )
		{
			// The pass skips the first and last sample; give them their neighbours' speed for the motion.
			samples[0].Speed = samples[Math.Min( 1, count - 1 )].Speed;
			samples[^1].Speed = samples[Math.Max( 0, count - 2 )].Speed;
		}
		if ( definition.IsSuspended )
			Swing( definition, samples );
		var (sickness, excitement) = rating.Final();
		var trains = TrainCount( lap, definition.TrainReleaseRate, definition.MaxTrains );
		return (new CoasterRatingResult( sickness, excitement, 0, count * spacing, maxHeight == float.MinValue ? 0 : maxHeight, maxSpeed, lap, trains ), winch);
	}

	/// <summary>The suspended cars' swing: each sample's roll from its lateral force.</summary>
	// [APPROX:COAST-018] the suspended swing (definition flag 0x8, 0x100400D8..0x10040438: force plus gravity in the sample frame, an atan2 target approached at rate min(|F + g|·fForceMultiplier, 1), faded over the braking zone) is replaced by roll = lateral force × fForceMultiplier, clamped to ±π/2 — evidence needed: a restatement of 0x100400D8..0x10040438
	private static void Swing( CoasterDefinition definition, CoasterSample[] samples )
	{
		for ( var index = 0; index < samples.Length; index++ )
		{
			ref var sample = ref samples[index];
			var right = new SVector3( -sample.Direction.Z, 0, sample.Direction.X );
			if ( right.LengthSquared() == 0 )
				continue;
			right = SVector3.Normalize( right );
			sample.Bank = Math.Clamp( SVector3.Dot( sample.Force, right ) * definition.ForceMultiplier, -HalfPi, HalfPi );
		}
	}
}
