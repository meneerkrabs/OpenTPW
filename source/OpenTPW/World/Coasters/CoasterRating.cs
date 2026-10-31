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

	// [BIN:TPW-EXE:0x43dbf0 rating reset] sickness = fInitialSickness (type +0x320), excitement = fInitialExcitement (+0x324), the previous excitement the same value, peaks 0, both scream states 0; the previous force (0x7923c8..0x7923d0) and bank (0x7923dc) are cleared and no routine writes them
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
	/// One sample, in tp.exe's operation order at the thread's 24-bit precision: dS = |F|·GForceSickness + u·UpsideDownSickness, dE = max(((Speed·v) + (|F|·GForceExcitement + |bank|·BankExcitement))
	/// − (XGForceUncomfortable·|Fx| + W), 0), S = dS + ½S, E = dE + ½E; the peaks follow. Returns the sample flags with bits 1, 2, 4 replaced by the screams (from index 3 on).
	/// </summary>
	// [BIN:TPW-EXE:0x43dc50 rating step] |F| = (|Fy| + |Fz|) + |Fx| (every term the difference to the previous force, which is 0), U = |bank| − π/2 when above 0 (0.0 otherwise, NaN included), u = (U ≠ 0 ? 1 : 0) + U; the inverted discomfort W = u × InvertedUncomfortable (×3 when Fy < 0) when u ≠ 0 and Fy < 0.1, otherwise u itself (0x43ddfb..0x43de1f: the PC adds u where the Mac added the stale Fz); every product and sum a single, no fused multiply-add (ORACLE X11, X44)
	public CoasterSampleFlags Step( int index, float speed, float bank, SVector3 force, CoasterSampleFlags flags = CoasterSampleFlags.None )
	{
		var d = Factors;
		var absX = MathF.Abs( force.X );
		var absY = MathF.Abs( force.Y );
		var absZ = MathF.Abs( force.Z );
		var magnitude = absY + absZ + absX;
		var absoluteBank = MathF.Abs( bank );
		var excess = absoluteBank - HalfPi;
		var inverted = excess > 0f ? excess : 0f;
		var upside = (inverted != 0f ? 1f : 0f) + inverted;
		var sicknessDelta = upside * d.UpsideDownSickness + magnitude * d.GForceSickness;
		var excite = d.Speed * speed + (magnitude * d.GForceExcitement + absoluteBank * d.BankExcitement);
		// the term after the inverted arm: u * InvertedUncomfortable (tripled below 0) when u is not 0 and Fy < 0.1 (NaN counts as below), u itself otherwise
		float tail;
		if ( upside != 0f && !(force.Y >= 0.1f) )
		{
			tail = d.InvertedUncomfortable * upside;
			if ( !(force.Y >= 0f) )
				tail *= 3f;
		}
		else
			tail = upside;
		var discomfort = d.XGForceUncomfortable * absX + tail;
		var delta = excite - discomfort;
		if ( !(delta > 0f) )
			delta = 0f;
		// [BIN:TPW-EXE:0x43de44 rating decay] S = dS − S × (−½) and E = dE − E × (−½): the half is exact, so each is one rounded sum
		Sickness = sicknessDelta + Sickness * Decay;
		Excitement = delta + Excitement * Decay;
		if ( !(PeakSickness > Sickness) )
			PeakSickness = Sickness;
		if ( !(PeakExcitement > Excitement) )
			PeakExcitement = Excitement;
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

	/// <summary>sickness = clamp(peakS + S, 0, 100) and excitement = clamp(½E + 1.7·peakE, 0, 100) (<c>0x43df90</c>).</summary>
	public (float Sickness, float Excitement) Final()
	{
		var final = FinalFor( Sickness, Excitement, PeakSickness, PeakExcitement, Array.Empty<uint>(), 0, 0, 0, 0 );
		return (final.Sickness, final.Excitement);
	}

	/// <summary>
	/// The final routine <c>0x43df90</c> on given accumulators: the clamped ratings, the count of sections whose flags have both 0x40 and 0x80 set, the length (the sample spacing times
	/// the controller's end coordinate, passed here as <paramref name="spacing"/> and <paramref name="endCoordinate"/>) and the maxima (oracle seam).
	/// </summary>
	// [BIN:TPW-EXE:0x43df90 rating final] sickness = peakS + S; excitement = ½E + peakE × 1.7 (the half exact, the product and the sum single); each is replaced by 100 when above it or NaN (0x43dfce..0x43dfe3, 0x43e00a..0x43e027) and then by 0 when below it (0x43dfeb..0x43e002, 0x43e02f..0x43e04c: −0 stays); +0x108 counts the sections with both flags 0x40 and 0x80 set along the +0x54 list (0x43e04e..0x43e07f); +0x10c = +0xac × +0xa8, +0x110 = max height, +0x114 = max speed (0x43e07f..0x43e0a1)
	public static CoasterRatingFinal FinalFor( float sickness, float excitement, float peakSickness, float peakExcitement, IEnumerable<uint> sectionFlags,
		float endCoordinate, float spacing, float maxHeight, float maxSpeed )
	{
		var rawSickness = peakSickness + sickness;
		var rawExcitement = excitement * Decay + peakExcitement * PeakExcitementWeight;
		var sections = sectionFlags.Count( flags => (flags & 0xC0) == 0xC0 );
		return new CoasterRatingFinal( Clamp( rawSickness ), Clamp( rawExcitement ), sections, spacing * endCoordinate, maxHeight, maxSpeed );
	}

	// [BIN:TPW-EXE:0x43dfce rating clamp] v = 100 when 100 < v or v is NaN (fcomp sets C0 for unordered), then v = 0 only when v < 0: a −0 stays −0
	private static float Clamp( float value )
	{
		if ( Maximum < value || float.IsNaN( value ) )
			value = Maximum;
		if ( value < 0f )
			value = 0f;
		return value;
	}

	/// <summary>The guests' excitement of a coaster ride: trunc(½E + 50), E in [0, 100].</summary>
	// [BIN:STP-PPC:0x100DFF70 ride excitement] track type 3: 0 without a coaster controller (0x100B5E24), else trunc(fmadds(E, 0.5, 50)) with E the controller's excitement (getter 0x10041A14)
	public static int GuestExcitement( float excitement ) => (int)MathF.FusedMultiplyAdd( excitement, 0.5f, 50f );

	/// <summary>
	/// The train count after a rating pass: min(uiMaxTrains, max(1, trunc(lap / (uiTrainReleaseRate × 0.001)))).
	/// </summary>
	// [BIN:TPW-EXE:0x43ca87 train count] the release rate (type +0x2ec, an unsigned) loaded as an integer, times the single 0.001 (0x6fe030), divided into the lap time (0x43ca87..0x43ca90), truncated by _ftol (0x67a830: NaN and out of range give 0x80000000), at least 1 (signed) and, as an unsigned compare, at most uiMaxTrains (type +0x2e8) (0x43ca98..0x43caae)
	public static int TrainCount( float lapSeconds, int releaseRateMilliseconds, int maxTrains )
	{
		var headway = (float)((double)(uint)releaseRateMilliseconds * 0.001f);
		var computed = lapSeconds / headway;
		var trains = float.IsNaN( computed ) || computed <= -2147483648f ? int.MinValue : computed >= 2147483648f ? int.MinValue : (int)computed;
		if ( trains < 1 )
			trains = 1;
		return (uint)trains >= (uint)maxTrains ? maxTrains : trains;
	}

	/// <summary>
	/// The rating-pass speed step: v = max((y − y_next) × mod × PerHeight + v, winch), × friction on friction samples,
	/// floored at the winch speed again; a section whose speed sits on the floor is a winch section; then the cap.
	/// </summary>
	// [BIN:TPW-EXE:0x43c580 rating speed] (y − y_next) × fUphillAccelModifier (+0x28; +0x2c when y_next is not above y) × fAccelerationPerHeight (+0x24), plus the speed, each a single and none fused; a value not above fWinchSpeed (+0x44) becomes it (0x43c596..0x43c5b7); × fFrictionMultiplier (+0x54) on sample flag 8 (0x43c5bf..0x43c5c8); not above the floor again becomes it (0x43c5d0..0x43c5e0); equal to it marks the node 0x800 (0x43c5e3..0x43c5f3); the cap (controller +0x34) replaces a value that is not below it, a NaN excepted (0x43c5f9..0x43c606)
	public static (float Speed, bool Winch) NextSpeed( CoasterDefinition definition, float speed, float y, float yNext, bool friction, float cap )
	{
		var modifier = yNext > y ? definition.UphillAccelModifier : definition.DownhillAccelModifier;
		var value = (y - yNext) * modifier * definition.AccelerationPerHeight + speed;
		if ( !(value > definition.WinchSpeed) )
			value = definition.WinchSpeed;
		if ( friction )
			value = definition.FrictionMultiplier * value;
		if ( !(value > definition.WinchSpeed) )
			value = definition.WinchSpeed;
		var winch = value == definition.WinchSpeed;
		if ( value >= cap )
			value = cap;
		return (value, winch);
	}

	/// <summary>
	/// The rating pass <c>0x43c450</c> over samples 1..n−2: trough friction flags, force terms, the speed step with
	/// winch marking, the rating step, maxima and the lap time; then the end samples, the suspended swing, the final
	/// rating and the train count. Writes each sample's speed, force, flags and (suspended coasters, after the pass)
	/// roll; returns the winch sections.
	/// </summary>
	// [BIN:TPW-EXE:0x43c450 rating pass] start speed fWinchSpeed (type +0x44), max speed and max height 0.0; samples 1..n−2 (the loop is skipped for n − 1 ≤ 1); trough coasters (type flag 0x100) set sample flag 8 where ay / sqrt((ax² + ay²) + az²) of the next segment exceeds 0.01745 and clear it elsewhere (0x43c510..0x43c544); the force is a × v × 0.1 with a = −(((behind + ahead) / spacing) × 1.0) per axis, two single products in that order (0x43c60c..0x43c65d); speed into +0x1c; the rating step reads the roll +0x18; lap += spacing / v; afterwards sample 0 gets speed fWinchSpeed and force 0, sample n−1 the speed and force of sample n−2 (0x43c6ca..0x43c71d); then the braking start, the suspended swing (flag 0x8), the final and the train count
	public static (CoasterRatingResult Result, HashSet<int> WinchSections) RatePass( CoasterDefinition definition, CoasterSample[] samples, float spacing, float cap, int? tailSection = null, float? endCoordinate = null )
	{
		var tail = tailSection ?? (samples.Length == 0 ? 0 : samples.Max( sample => sample.Section ));
		var tailSample = samples.Length == 0 ? 0 : CoasterRide.BrakingSpeedSample( samples, tail ) + 1;
		return RatePassAt( definition, samples, spacing, cap, tailSample, endCoordinate ?? CoasterRide.PlatformStartSample( samples, tail ) );
	}

	/// <summary>
	/// The rating pass with the controller's tail sample (the end node's <c>+0xfc</c>) and end coordinate (<c>+0xa8</c>) given directly, as the oracle runs it; <see cref="RatePass"/> derives both
	/// from the samples' sections.
	/// </summary>
	// [BIN:TPW-EXE:0x43c729 braking start] +0x134 = (n − 1) − v / spacing with v the speed of the sample before the tail node's sample (+0x58 node +0xfc), replaced by 0 when it is not above 0 (NaN included) (0x43c72c..0x43c763)
	internal static (CoasterRatingResult Result, HashSet<int> WinchSections) RatePassAt( CoasterDefinition definition, CoasterSample[] samples, float spacing, float cap, int tailSample, float endCoordinate, float[]? brakingOut = null )
	{
		var rating = new CoasterRating( definition );
		var winch = new HashSet<int>();
		var speed = definition.WinchSpeed;
		var lap = 0f;
		var maxSpeed = 0f;
		var maxHeight = 0f;
		var count = samples.Length;
		// [BIN:TPW-EXE:0x6fe038 trough slope] the climb threshold is the single 0.01745
		const float TroughClimb = 0.01745f;
		// [BIN:TPW-EXE:0x6fe03c force scale] the force is multiplied by the single 0.1, not divided by 10
		const float ForceScale = 0.1f;
		// [BIN:TPW-EXE:0x74ce34 gravity] g = 1.0 (a single multiply by it and a negation per axis); fGravityX/Y/Z and fForceMultiplier take no part
		const float Gravity = 1f;
		for ( var index = 1; index < count - 1; index++ )
		{
			ref var sample = ref samples[index];
			var previous = samples[index - 1].Position;
			var next = samples[index + 1].Position;
			var ahead = new SVector3( next.X - sample.Position.X, next.Y - sample.Position.Y, next.Z - sample.Position.Z );
			var behind = new SVector3( previous.X - sample.Position.X, previous.Y - sample.Position.Y, previous.Z - sample.Position.Z );
			if ( definition.IsTrough )
			{
				if ( ahead.Y / CoasterX87.Length( ahead ) > TroughClimb )
					sample.Flags |= CoasterSampleFlags.Friction;
				else
					sample.Flags &= ~CoasterSampleFlags.Friction;
			}
			var ax = -((behind.X + ahead.X) / spacing * Gravity);
			var ay = -((behind.Y + ahead.Y) / spacing * Gravity);
			var az = -((behind.Z + ahead.Z) / spacing * Gravity);
			var (value, onFloor) = NextSpeed( definition, speed, sample.Position.Y, next.Y, (sample.Flags & CoasterSampleFlags.Friction) != 0, cap );
			speed = value;
			if ( onFloor )
				winch.Add( sample.Section );
			sample.Speed = speed;
			sample.Force = new SVector3( ax * speed * ForceScale, ay * speed * ForceScale, az * speed * ForceScale );
			sample.Flags = rating.Step( index, speed, sample.Bank, sample.Force, sample.Flags );
			if ( !(maxSpeed > speed) )
				maxSpeed = speed;
			if ( !(maxHeight > sample.Position.Y) )
				maxHeight = sample.Position.Y;
			lap += spacing / speed;
		}
		if ( count > 0 )
		{
			samples[0].Speed = definition.WinchSpeed;
			samples[0].Force = SVector3.Zero;
		}
		if ( count > 1 )
		{
			samples[^1].Speed = samples[^2].Speed;
			samples[^1].Force = samples[^2].Force;
		}
		var braking = count > 0 ? CoasterRide.BrakingStartFor( count, samples[Math.Clamp( tailSample - 1, 0, count - 1 )].Speed, spacing ) : 0f;
		if ( brakingOut != null && brakingOut.Length > 0 )
			brakingOut[0] = braking;
		if ( definition.IsSuspended && count > 0 )
		{
			// [BIN:TPW-EXE:0x43c9e8 swing fade] the fade runs over (i − ctl+0x134)/(ctl+0xa8 − ctl+0x134): the braking start is (n − 1) − v/spacing with n the sample count and ctl+0xa8 the sample coordinate of the end node's knot
			Swing( definition, samples, braking, endCoordinate );
		}
		var final = FinalFor( rating.Sickness, rating.Excitement, rating.PeakSickness, rating.PeakExcitement, Array.Empty<uint>(), endCoordinate, spacing, maxHeight, maxSpeed );
		var trains = TrainCount( lap, definition.TrainReleaseRate, definition.MaxTrains );
		return (new CoasterRatingResult( final.Sickness, final.Excitement, 0, endCoordinate * spacing, maxHeight, maxSpeed, lap, trains ), winch);
	}

	/// <summary>A vector length the way the coaster code takes it: x² in double, y² and z² single, summed in double, sqrt, rounded to single.</summary>
	// [BIN:STP-PPC:0x1003FD5C vector length] fmul x·x (double), fmuls y·y and z·z, fadd, fadd, sqrt, frsp
	public static float MixedLength( SVector3 v ) => (float)Math.Sqrt( ((double)v.X * v.X + (v.Y * v.Y)) + (v.Z * v.Z) );

	/// <summary>The suspended cars' swing: each sample's roll follows the force plus gravity in the sample frame, fading over the braking zone.</summary>
	// [BIN:TPW-EXE:0x43c76c suspended swing] (0x43c76c..0x43ca45, definition flag 0x8, sample stride 0x38) per sample with prev starting at π: F = sample force (+0x20..+0x28) + definition gravity (+0x60..+0x68); F is rotated into the sample frame by the angles −yaw (+0x10, X/Z), −pitch (+0x14, Y/Z) and −old roll (+0x18, X/Y) through the runtime sine table (0x7b0968, mask 0x7b0960, scale 0x7b095c); θ = atan2(−X, Y) (0x46f230); d = fmod(θ − prev + 3π, 2π) − π; rate = min(|F′|·definition +0x6C fForceMultiplier, 1); w = fmod(prev + d·rate + 3π, 2π) − π; roll = w − π (+2π when below −π); past the braking start (controller +0x134, computed just before from the tail node's sample) roll ×= 1 − clamp((i − b)/(n − b), 0, 1); prev = w. The Mac rule (0x100400D8..0x10040438) is the same restated; COAST-018 narrowed
	// [BIN:TPW-EXE:0x4708d0 swing table] the runtime table is 4096 entries T[k] = f32(fsin(k/4096 · f32(2π))) with scale f32(4096 · f32(1/2π)) and mask 4095 (CoasterTrig.PcSin/PcCos); the index is fistp of the scaled angle, rounded to nearest even (0x43c7be..0x43c7cd, 0x43c7d0..0x43c7e5)
	// [BIN:TPW-EXE:0x0043c7ff rotation steps] each rotation is the x87 sequence on the stack: sin·z + cos·x stored as x (0x43c816) and cos·z − x·sin stored as z (0x43c824) from the old x, then cos·y − sin·z and cos·z + y·sin, then cos·x − sin·y and cos·y + x·sin, every multiply, add and subtract rounded to a single (the thread's 24-bit precision, [BIN:TPW-EXE:0x00563914 control word]); θ − prev takes the unrounded fpatan result (0x43c918 stores a copy only), the sums with 3π (a double constant, 0x6fe048) are single-rounded and fmod is exact (0x67b24a)
	internal static void Swing( CoasterDefinition definition, CoasterSample[] samples, float brakingStart, float totalSamples )
	{
		var total = totalSamples;
		var previous = MathF.PI;
		for ( var index = 0; index < samples.Length; index++ )
		{
			ref var sample = ref samples[index];
			var x = sample.Force.X + definition.GravityX;
			var y = sample.Force.Y + definition.GravityY;
			var z = sample.Force.Z + definition.GravityZ;
			var sinYaw = CoasterTrig.PcSin( -sample.Yaw );
			var cosYaw = CoasterTrig.PcCos( -sample.Yaw );
			var oldX = x;
			x = sinYaw * z + cosYaw * x;
			z = cosYaw * z - oldX * sinYaw;
			var sinPitch = CoasterTrig.PcSin( -sample.Pitch );
			var cosPitch = CoasterTrig.PcCos( -sample.Pitch );
			var oldY = y;
			y = cosPitch * y - sinPitch * z;
			z = cosPitch * z + oldY * sinPitch;
			var sinBank = CoasterTrig.PcSin( -sample.Bank );
			var cosBank = CoasterTrig.PcCos( -sample.Bank );
			oldX = x;
			x = cosBank * x - sinBank * y;
			y = cosBank * y + oldX * sinBank;
			// fpatan hands its result on unrounded; the store at 0x43c918 only copies it
			// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
			var target = DetMath.Atan2( -x, y );
			var turned = (float)((double)(float)(target - previous) + ThreePi);
			var difference = (float)(turned % TwoPiDouble - MathF.PI);
			float squares = x * x;
			squares += y * y;
			squares += z * z;
			var rate = MathF.Sqrt( squares ) * definition.ForceMultiplier;
			if ( rate > 1f )
				rate = 1f;
			var scaled = difference * rate;
			scaled += previous;
			var wrapped = (float)((double)scaled + ThreePi);
			var swing = (float)(wrapped % TwoPiDouble - MathF.PI);
			var roll = -(MathF.PI - swing);
			var stored = roll < -MathF.PI ? roll + TwoPiSingle : roll;
			if ( index > brakingStart )
			{
				var fraction = (index - brakingStart) / (total - brakingStart);
				if ( fraction >= 1f )
					fraction = 1f;
				if ( !(fraction > 0f) )
					fraction = 0f;
				stored *= 1f - fraction;
			}
			sample.Bank = stored;
			previous = swing;
		}
	}

	/// <summary>The double constants 0x6fe048 (−3π, subtracted) and 0x6fe050 (2π): three and two times the single-precision π.</summary>
	private const double ThreePi = 9.42477822303772;
	private const double TwoPiDouble = 6.2831854820251465;
	/// <summary>The single the roll's fix-up adds (<c>0x6fe058</c> = −2π, subtracted).</summary>
	private const float TwoPiSingle = 6.2831855f;
}
