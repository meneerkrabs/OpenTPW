namespace OpenTPW;

/// <summary>The level's particle pool as the advisor's screen effects use it; a handle is whatever the pool returns (null when it refused).</summary>
internal interface IAdvisorParticlePool
{
	/// <summary>Starts effect <paramref name="effectId"/> at a native position (tp.exe <c>0x521E60</c>).</summary>
	object? Create( int effectId, (int X, int Y, int Z) native );
	/// <summary>Moves a live emitter (tp.exe <c>0x51FE30</c>); a dead handle is ignored.</summary>
	void Move( object handle, (int X, int Y, int Z) native );
	/// <summary>Ends an emitter, its particles running out (tp.exe <c>0x51FEB0</c>: life −2); a dead handle is ignored.</summary>
	void Kill( object handle );
}

/// <summary>
/// The advisor's screen effects: three groups of up to five screen-space emitters (tp.exe's table at <c>0xF86900</c>, 0x44 bytes a
/// group) that circle a centre moving from one screen point to another, on the advisor clock. The accessory bits of a fixed
/// sequence's clips (the ticket and key advice) fill group 0 when the clip starts (<see cref="ApplyAccessoryBits"/>); the response's
/// start and end and the kill empty it. Screen units: −1..1, x right, y down (<see cref="ParticleScreenSpace"/>).
/// </summary>
// [BIN:TPW-EXE:0x5ED580 groups reset] the advisor's set-up (0x5989C0) zeroes the 0x33 words of the three groups (no emitter is ended) and builds the 512-entry sine table (0x5442E0)
// [BIN:TPW-EXE:0x599800 advisor frame] after the accessory update the frame moves every group (0x5ED770), active response or not
internal sealed class AdvisorEffectGroups
{
	public const int GroupCount = 3;
	public const int MaximumEmitters = 5;

	/// <summary>The accessory bits (of <see cref="AdvisorSequenceEntry.Bits"/>, the entry's bits 16-22 shifted down) whose exact value selects an effect.</summary>
	public const int EffectMask = 0x3E;
	public const int OneTicketBit = 0x2, TwoTicketsBit = 0x4, ThreeTicketsBit = 0x8, TicketsAwayBit = 0x10, EffectsOffBit = 0x20;
	/// <summary>The screen-space templates the accessory effects start: GoldenTicket (0x2C) and GoldenTicket2 (0x31).</summary>
	public const int GoldenTicketEffect = 0x2C, GoldenTicket2Effect = 0x31;

	private sealed class Group
	{
		public float FromX, FromY, ToX, ToY;
		public float Start, Duration;
		public float Spin;
		public float RadiusFromX, RadiusFromY, RadiusToX, RadiusToY;
		public int Count;
		public readonly object?[] Handles = new object?[MaximumEmitters];
	}

	private readonly Group[] groups = { new(), new(), new() };
	private readonly IAdvisorParticlePool pool;

	public AdvisorEffectGroups( IAdvisorParticlePool pool )
	{
		ArgumentNullException.ThrowIfNull( pool );
		this.pool = pool;
	}

	/// <summary>The number of emitters group <paramref name="group"/> holds.</summary>
	public int Count( int group ) => groups[group].Count;

	/// <summary>
	/// Sets group <paramref name="group"/> to <paramref name="count"/> emitters: missing ones are started with <paramref name="effectId"/>
	/// at the native origin, surplus ones ended. Returns the number started, or minus the number ended.
	/// </summary>
	// [BIN:TPW-EXE:0x5ED5A0 group size] more: each new slot from the old count up gets 0x521E60(effect, 0, 0, 0), kept even when refused, and the result is the count added; fewer: every nonzero handle from the new count up is ended (0x51FEB0) and cleared, the result is minus the number ended; the existing emitters keep their effect
	public int SetCount( int group, int count, int effectId )
	{
		if ( count is < 0 or > MaximumEmitters )
			throw new ArgumentOutOfRangeException( nameof( count ) );
		var state = groups[group];
		var old = state.Count;
		var result = 0;
		if ( old < count )
		{
			for ( var index = old; index < count; index++ )
				state.Handles[index] = pool.Create( effectId, (0, 0, 0) );
			result = count - old;
		}
		else
		{
			for ( var index = count; index < old; index++ )
			{
				if ( state.Handles[index] is { } handle )
				{
					pool.Kill( handle );
					state.Handles[index] = null;
					result--;
				}
			}
		}
		state.Count = count;
		return result;
	}

	/// <summary>
	/// Moves group <paramref name="group"/>'s centre towards (<paramref name="x"/>, <paramref name="y"/>): from that point itself when
	/// <paramref name="jump"/>, else from the previous target; a nonzero <paramref name="milliseconds"/> restarts the move's clock at <paramref name="now"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x5ED660 group move] mode 0: from = the previous target; otherwise from = (x, y); target = (x, y); a nonzero time stores the advisor clock (0x5988E0) as the start and the time as the length
	public void SetPosition( int group, bool jump, float x, float y, float milliseconds, int now )
	{
		var state = groups[group];
		if ( jump )
			(state.FromX, state.FromY) = (x, y);
		else
			(state.FromX, state.FromY) = (state.ToX, state.ToY);
		(state.ToX, state.ToY) = (x, y);
		if ( milliseconds != 0f )
		{
			state.Start = now;
			state.Duration = milliseconds;
		}
	}

	/// <summary>Sets group <paramref name="group"/>'s orbit radii (from the previous ones unless <paramref name="jump"/>) and its turns per move (<paramref name="spin"/>).</summary>
	// [BIN:TPW-EXE:0x5ED700 group orbit] mode 0: from = the previous radii; otherwise from = (rx, ry); target = (rx, ry); the fifth argument is the turns over the move's length
	public void SetRadius( int group, bool jump, float radiusX, float radiusY, float spin )
	{
		var state = groups[group];
		if ( jump )
			(state.RadiusFromX, state.RadiusFromY) = (radiusX, radiusY);
		else
			(state.RadiusFromX, state.RadiusFromY) = (state.RadiusToX, state.RadiusToY);
		(state.RadiusToX, state.RadiusToY) = (radiusX, radiusY);
		state.Spin = spin;
	}

	/// <summary>Places every emitter of every nonempty group for advisor-clock time <paramref name="now"/>.</summary>
	// [BIN:TPW-EXE:0x5ED770 group update] t = (clock - start) / length; the centre is from + (to - from) * t (t not clamped), the radii from + (to - from) * min(t, 1); emitter i of n sits at angle a = ftol(frac(i / n + t * spin) * 512) of the 512-entry table (0x5428A0 / 0x5428C0: table << 2, times 1/1024): (cx + rx sin a, cy - ry cos a), moved (0x51FE30) to native (ftol((x + 1) * 50000), 0, ftol((y + 1) * 37500))
	// [BIN:TPW-EXE:0x00563914 DirectDraw set-up] SetCooperativeLevel flags 0x851 (full screen) and 0x808 (0x563B7A, windowed) carry DDSCL_FPUSETUP (0x800) and not FPUPRESERVE (0x1000), so the x87 runs in single precision after the set-up; the arithmetic below is single precision
	// [APPROX:ADVISOR-026] see the register: the node bits the original keeps on the model instance between responses are not carried here
	public void Update( int now )
	{
		foreach ( var state in groups )
		{
			if ( state.Count == 0 )
				continue;
			var t = ((float)now - state.Start) / state.Duration;
			var clamped = 1f < t ? 1f : t;
			var centreX = (state.ToX - state.FromX) * t + state.FromX;
			var centreY = (state.ToY - state.FromY) * t + state.FromY;
			var radiusX = (state.RadiusToX - state.RadiusFromX) * clamped + state.RadiusFromX;
			var radiusY = (state.RadiusToY - state.RadiusFromY) * clamped + state.RadiusFromY;
			for ( var index = 0; index < state.Count; index++ )
			{
				var turns = index * (1f / state.Count) + t * state.Spin;
				var angle = Ftol( (turns - MathF.Truncate( turns )) * 512f );
				var sin = (BumpMath.Sin( angle ) << 2) * (1f / 1024f);
				var cos = (BumpMath.Cos( angle ) << 2) * (1f / 1024f);
				var x = centreX + radiusX * sin;
				var y = centreY - radiusY * cos;
				if ( state.Handles[index] is { } handle )
					pool.Move( handle, (Ftol( (x + 1f) * ParticleScreenSpace.CentreX ), 0, Ftol( (y + 1f) * ParticleScreenSpace.CentreZ )) );
			}
		}
	}

	/// <summary>
	/// A clip of a fixed sequence starts with accessory <paramref name="bits"/>: exactly one effect bit picks an arrangement of group 0
	/// (tickets or keys circling the advisor, flying off, or none). <paramref name="clipMilliseconds"/> is the clip's table length.
	/// </summary>
	// [BIN:TPW-EXE:0x00598EAA accessory effects] bits & 0x3E0000 nonzero: without an active response (0xF7966C) only 0x200000 acts (group 0 emptied); with one: 0x40000 plays 0xC5 (AdvisorAccessories.KeySound) and sets 2 x 0x31, moved from the last target to (0, 0) over the clip, radii to (0.6, 0.4), 2 turns; 0x20000 sets 1 x 0x2C and, when that started one, jumps it to (0.34, 0.62) over the clip with radii (0.01, 0.2), 1 turn; 0x80000 sets 3 x 0x31, to (0, 0), radii (0.6, 0.4), 3 turns; 0x100000 empties the group, sets 2 x 0x2C, to (0.8, -0.8), radii to (0, 0), 2 turns; 0x200000 empties it; the length is the started clip's table entry (0xF796C8[0xF7976C])
	public void ApplyAccessoryBits( int bits, bool active, float clipMilliseconds, int now )
	{
		var effect = bits & EffectMask;
		if ( effect == 0 )
			return;
		if ( !active )
		{
			if ( (bits & EffectsOffBit) != 0 )
				SetCount( 0, 0, 0 );
			return;
		}
		switch ( effect )
		{
			case TwoTicketsBit:
				SetCount( 0, 2, GoldenTicket2Effect );
				SetPosition( 0, false, 0f, 0f, clipMilliseconds, now );
				SetRadius( 0, false, 0.6f, 0.4f, 2f );
				break;
			case OneTicketBit:
				if ( SetCount( 0, 1, GoldenTicketEffect ) != 0 )
				{
					SetPosition( 0, true, 0.34f, 0.62f, clipMilliseconds, now );
					SetRadius( 0, true, 0.01f, 0.2f, 1f );
				}
				break;
			case ThreeTicketsBit:
				SetCount( 0, 3, GoldenTicket2Effect );
				SetPosition( 0, false, 0f, 0f, clipMilliseconds, now );
				SetRadius( 0, false, 0.6f, 0.4f, 3f );
				break;
			case TicketsAwayBit:
				SetCount( 0, 0, GoldenTicketEffect );
				SetCount( 0, 2, GoldenTicketEffect );
				SetPosition( 0, false, 0.8f, -0.8f, clipMilliseconds, now );
				SetRadius( 0, false, 0f, 0f, 2f );
				break;
			case EffectsOffBit:
				SetCount( 0, 0, 0 );
				break;
		}
	}

	/// <summary>The x87 float-to-int conversion with chop rounding: truncation, and the integer indefinite 0x80000000 for NaN or out of range.</summary>
	internal static int Ftol( float value ) =>
		float.IsNaN( value ) || value >= 2147483648f || value < -2147483648f ? int.MinValue : (int)value;
}
