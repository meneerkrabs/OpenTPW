namespace OpenTPW;

/// <summary>
/// CPU-only level lifetime and sampled staff clock. Full UI events are synchronous host events; each outer update
/// performs its eligible partial copy before source publication, including paused updates and a root under rename.
/// </summary>
public sealed class StaffSpritePresentation : IDisposable
{
	private static long nextSession;
	private readonly Func<IReadOnlyList<StaffMember>> roster;
	private readonly Dictionary<StaffMember, StaffBodySprite> bodies = new();
	private ParkStaff? attachedRoster;
	private bool disposed;
	public StaffSpritePresentation( StaffSpriteMetadataCatalog catalog, Func<IReadOnlyList<StaffMember>> roster, long hostTick = 0, bool ready = true )
	{
		Catalog = catalog;
		this.roster = roster;
		Session = System.Threading.Interlocked.Increment( ref nextSession );
		Registry = new StaffSpriteRegistry( Session );
		EpochHostTick = hostTick;
		EpochMilliseconds = LastSample = GuestSimulation.BodySpriteSampleAtTick( hostTick );
		Ready = ready;
		foreach ( var member in roster() ) Attach( member );
	}
	public long Session { get; }
	public StaffSpriteRegistry Registry { get; }
	public StaffSpriteMetadataCatalog Catalog { get; }
	public long EpochHostTick { get; private set; }
	public int EpochMilliseconds { get; private set; }
	public int LastSample { get; private set; }
	public bool Ready { get; private set; }
	public bool IsDisposed => disposed;
	internal int BoundPersonCount => bodies.Count;
	public event Action? Invalidated;

	public void AttachRoster( ParkStaff staff )
	{
		if ( ReferenceEquals( attachedRoster, staff ) ) return;
		if ( attachedRoster != null )
		{
			attachedRoster.MemberAdded -= MemberAdded;
			attachedRoster.MembersReplacing -= InvalidateRoster;
		}
		attachedRoster = staff;
		staff.MemberAdded += MemberAdded;
		staff.MembersReplacing += InvalidateRoster;
		foreach ( var member in staff.Members ) Attach( member );
	}
	private void MemberAdded( StaffMember member ) => Attach( member );
	public StaffBodySprite Attach( StaffMember member )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( bodies.TryGetValue( member, out var existing ) && existing.IsLive ) return existing;
		member.BodySprite?.Invalidate();
		var body = new StaffBodySprite( this, new StaffSpriteIdentity( Session, member ) );
		bodies[member] = body;
		member.BodySprite = body;
		return body;
	}
	internal void Detach( StaffBodySprite body )
	{
		if ( bodies.TryGetValue( body.Identity.Member, out var current ) && ReferenceEquals( current, body ) )
			bodies.Remove( body.Identity.Member );
	}
	internal bool Contains( StaffSpriteIdentity identity ) => !disposed && identity.Session == Session && identity.NumbersMatch
		&& bodies.TryGetValue( identity.Member, out var body ) && ReferenceEquals( body.Identity, identity ) && roster().Contains( identity.Member );

	public void SetHostTick( long tick ) => LastSample = unchecked( EpochMilliseconds + GuestSimulation.BodySpriteSampleAtTick( tick ) - GuestSimulation.BodySpriteSampleAtTick( EpochHostTick ) );
	public void Schedule() => Registry.Schedule( LastSample );

	/// <summary>One reconstruction after all final clock and position restore. No saved VM history or RNG is invented.</summary>
	public void Reconstruct( long hostTick )
	{
		EpochHostTick = hostTick;
		EpochMilliseconds = LastSample = GuestSimulation.BodySpriteSampleAtTick( hostTick );
		Ready = true;
		foreach ( var member in roster() )
		{
			var body = Attach( member );
			body.EnsureInitialized( reconstructed: true );
		}
	}

	public void Present( Action? partialRefresh = null, float elapsedSeconds = 0 )
	{
		if ( disposed ) return;
		partialRefresh?.Invoke();
		foreach ( var body in bodies.Values ) body.Publish( elapsedSeconds );
	}

	private void InvalidateRoster()
	{
		foreach ( var body in bodies.Values.ToArray() ) body.Invalidate();
		bodies.Clear();
		Invalidated?.Invoke();
	}
	public void Dispose()
	{
		if ( disposed ) return;
		InvalidateRoster();
		disposed = true;
		Registry.Dispose();
		if ( attachedRoster != null )
		{
			attachedRoster.MemberAdded -= MemberAdded;
			attachedRoster.MembersReplacing -= InvalidateRoster;
			attachedRoster = null;
		}
	}
}
