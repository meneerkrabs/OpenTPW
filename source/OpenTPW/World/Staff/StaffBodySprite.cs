namespace OpenTPW;

/// <summary>One person's runtime-only source binding. Explicit source events, not display-state polling, own its lifetime.</summary>
public sealed class StaffBodySprite
{
	private readonly StaffSpritePresentation owner;
	private bool invalidated;
	private bool poseInitialized;
	private float poseX, poseY, lastX, lastY;
	internal StaffBodySprite( StaffSpritePresentation owner, StaffSpriteIdentity identity )
	{
		this.owner = owner;
		Identity = identity;
		owner.Registry.Removed += OnRemoved;
	}
	public StaffSpriteIdentity Identity { get; }
	public StaffSpritePresentation Presentation => owner;
	public StaffSpriteTicket? SourceTicket { get; private set; }
	public int PendingTemplate { get; private set; }
	public int PendingInterval { get; private set; }
	public int PersonAngle { get; private set; } = 1024;
	public bool Initialized { get; private set; }
	public bool Reconstructed { get; private set; }
	/// <summary>Identity/session liveness, independent of whether the body source currently exists (rest and carrying preserve identity).</summary>
	public bool IsLive => !invalidated && owner.Contains( Identity ) && ReferenceEquals( Identity.Member.BodySprite, this );
	public StaffSpriteDiagnostic Diagnostic { get; private set; }
	private StaffSpriteBankMetadata Bank => owner.Catalog.Select( Identity.Member.Type, StaffSpriteRules.KeyOf( Identity.Member ) );

	public StaffSpriteVmState? ReadSourceState() => TryResolve( out var state ) ? state.DeepCopy() : null;
	internal bool TryResolve( out StaffSpriteVmState state )
	{
		state = null!;
		return IsLive && SourceTicket is { } ticket && owner.Registry.TryResolve( ticket, out state );
	}
	public StaffSpriteDrawRecord? DrawRecord => TryResolve( out var state ) && SourceTicket is { } ticket
		? new StaffSpriteDrawRecord( state, ticket.Session, ticket.Slot, ticket.Generation, false,
			!Bank.Available ? StaffSpriteDiagnostic.MetadataUnavailable : owner.Registry.Diagnostic( ticket ) ) : null;

	public void EnsureInitialized() => EnsureInitialized( reconstructed: false );
	internal void EnsureInitialized( bool reconstructed )
	{
		if ( Initialized || !owner.Ready || !IsLive || Identity.Member.CellX == null || Identity.Member.CellY == null ) return;
		Initialized = true;
		Reconstructed = reconstructed;
		if ( Identity.Member.Job == StaffJob.Resting || Identity.Member.State == StaffState.PickedUp
			|| Identity.Member.State == StaffState.Resting && Identity.Member.Job != StaffJob.ToRestArea ) return;
		CreateSource();
		// [APPROX:UI-092] Fresh load reconstructs an entry, not elapsed history. Unknown performance selection uses entry zero without RNG.
		if ( reconstructed )
			RequestTemplate( Identity.Member.Job switch
			{
				StaffJob.Patrol or StaffJob.ToStrike or StaffJob.ToLitter or StaffJob.ToToilet or StaffJob.ToRide => 9,
				StaffJob.ToRestArea => 1, StaffJob.Striking => 19,
				StaffJob.Sweeping or StaffJob.Cleaning => 17, StaffJob.Repairing => 18,
				StaffJob.Researching => 10, StaffJob.Performing => 13, _ => 3
			} );
	}

	/// <summary>Actual placement/return constructs one fresh Stand; repeated reconciliation cannot replace a live body.</summary>
	public void CreateSource()
	{
		if ( !IsLive || !owner.Ready || SourceTicket != null ) return;
		Initialized = true;
		var bank = Bank;
		SourceTicket = owner.Registry.Allocate( Identity, StaffSpriteOwnerKind.Source, handle =>
		{
			var state = StaffSpriteVm.Create( StaffSpriteProgram.Stand, owner.LastSample );
			state.Handle = handle;
			state[StaffSpriteRegister.Kind] = bank.Kind;
			state[StaffSpriteRegister.Variant] = bank.Variant;
			state[StaffSpriteRegister.FrameCount] = (uint)bank.Slot0FrameCount;
			return state;
		} );
		poseInitialized = false;
		Diagnostic = bank.Available ? StaffSpriteDiagnostic.None : StaffSpriteDiagnostic.MetadataUnavailable;
	}

	/// <summary>Separate direct-performance construction initializes intro/hold; ordinary requests never do.</summary>
	internal bool CreatePerformanceSource( int index )
	{
		if ( SourceTicket != null || !IsLive || !owner.Ready || !Bank.TryPerformance( index, out var performance ) ) return false;
		CreateSource();
		if ( !TryResolve( out var state ) ) return false;
		StaffSpriteVm.ApplyPerformance( state, performance.Program, performance.Slot, performance.FrameCount );
		state[StaffSpriteRegister.Intro] = (uint)performance.Intro;
		state[StaffSpriteRegister.Hold] = (uint)performance.Hold;
		return true;
	}

	public void DeleteSource()
	{
		if ( SourceTicket is not { } ticket ) return;
		SourceTicket = null;
		owner.Registry.Remove( ticket );
		// Pending fields survive real deletion/creation, but never a pre-update consumption.
	}
	public void Invalidate()
	{
		if ( invalidated ) return;
		invalidated = true;
		owner.Registry.RemoveIdentity( Identity );
		SourceTicket = null;
		owner.Registry.Removed -= OnRemoved;
		owner.Detach( this );
		if ( ReferenceEquals( Identity.Member.BodySprite, this ) ) Identity.Member.BodySprite = null;
		Diagnostic = StaffSpriteDiagnostic.StaleIdentity;
	}
	private void OnRemoved( StaffSpriteTicket ticket, StaffSpriteRemoval reason )
	{
		if ( SourceTicket != ticket ) return;
		SourceTicket = null;
		Diagnostic = reason == StaffSpriteRemoval.VmRetired ? StaffSpriteDiagnostic.Retired : StaffSpriteDiagnostic.SourceAbsent;
	}

	public void RequestTemplate( int request ) { if ( IsLive ) PendingTemplate = request; }
	public void RequestInterval( int interval ) { if ( IsLive ) PendingInterval = interval; }
	public void RequestPerformance( uint alreadyDrawnWord )
	{
		if ( !IsLive ) return;
		var count = Bank.PerformanceCount;
		PendingTemplate = !Bank.Available || count is < 1 or > 4 ? -1 : 13 + (count == 1 ? 0 : (int)(alreadyDrawnWord % (uint)(count - 1)));
	}

	/// <summary>Template then interval, exactly once before the state handler. Absent sources consume both fields without replay.</summary>
	public void ApplyPending()
	{
		var request = PendingTemplate;
		var interval = PendingInterval;
		PendingTemplate = PendingInterval = 0;
		if ( !TryResolve( out var state ) ) return;
		if ( request != 0 )
		{
			if ( request is >= 13 and <= 16 || request == -1 )
			{
				if ( !Bank.Available ) Diagnostic = StaffSpriteDiagnostic.MetadataUnavailable;
				else if ( request == -1 || !Bank.TryPerformance( request - 13, out var performance ) ) Diagnostic = StaffSpriteDiagnostic.InvalidPerformance;
				else
				{
					StaffSpriteVm.ApplyPerformance( state, performance.Program, performance.Slot, performance.FrameCount );
					Diagnostic = StaffSpriteDiagnostic.None;
				}
			}
			else if ( StaffSpritePrograms.IsEntry( (StaffSpriteProgram)request ) ) StaffSpriteVm.ApplyTemplate( state, (StaffSpriteProgram)request );
			else Diagnostic = StaffSpriteDiagnostic.InvalidVm;
		}
		StaffSpriteVm.ApplyInterval( state, interval );
	}

	/// <summary>Only a continuing moving branch calls this; an arrived/stuck return never writes the angle or requests.</summary>
	public void Moving( int beforeX, int beforeY, int afterX, int afterY, bool fast, bool escort )
	{
		if ( !IsLive ) return;
		if ( StaffSpriteMotionMath.TryMovementAngle( beforeX, beforeY, afterX, afterY, out var angle ) ) PersonAngle = angle;
		else Diagnostic = StaffSpriteDiagnostic.InvalidAngleArithmetic;
		var run = Identity.Member.Type == StaffType.Guard && fast;
		var desired = run ? StaffSpriteProgram.Run : escort ? StaffSpriteProgram.Hold : StaffSpriteProgram.Walk;
		// [BIN:TPW-EXE:0x00475C50 restart comparison] an absent handle compares false, so WALK still queues its request.
		if ( !TryResolve( out var state ) || state.RestartPc != new StaffSpritePc( desired, 0 ) ) RequestTemplate( (int)desired );
		RequestInterval( StaffSpriteMotionMath.IntervalForStep( beforeX, beforeY, afterX, afterY, run ) );
	}
	public void EnterStrike() { PersonAngle = 0; RequestTemplate( 19 ); }
	public void StrikeAngle( uint alreadyDrawnWord ) { if ( IsLive ) PersonAngle = StaffSpriteMotionMath.StrikeAngle( PersonAngle, alreadyDrawnWord ); }
	public void AppearanceChanged()
	{
		if ( !TryResolve( out var state ) ) return;
		var bank = Bank;
		state[StaffSpriteRegister.Kind] = bank.Kind;
		state[StaffSpriteRegister.Variant] = bank.Variant;
	}

	internal void Publish( float elapsedSeconds )
	{
		if ( !TryResolve( out var state ) ) return;
		state[StaffSpriteRegister.Heading] = (uint)StaffSpriteMotionMath.Heading( PersonAngle );
		if ( Identity.Member.CellX is not { } x || Identity.Member.CellY is not { } y ) return;
		if ( !poseInitialized ) { poseX = lastX = x; poseY = lastY = y; poseInitialized = true; }
		var dx = x - lastX;
		var dy = y - lastY;
		var gapX = x - poseX;
		var gapY = y - poseY;
		// [APPROX:UI-092] Retain the host's spatial smoothing only; it cannot infer program requests, clocks or heading.
		if ( gapX * gapX + gapY * gapY > 4 || dx * dx + dy * dy > 2.25f ) (poseX, poseY) = (x, y);
		else
		{
			var blend = float.IsFinite( elapsedSeconds ) ? Math.Clamp( elapsedSeconds * 10, 0, 1 ) : 0;
			poseX += (x - poseX) * blend;
			poseY += (y - poseY) * blend;
		}
		(lastX, lastY) = (x, y);
		state[StaffSpriteRegister.X] = BitConverter.SingleToUInt32Bits( poseX );
		state[StaffSpriteRegister.Y] = BitConverter.SingleToUInt32Bits( poseY );
		state[StaffSpriteRegister.Z] = 0;
	}
}
