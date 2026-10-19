namespace OpenTPW;

public enum StaffSpriteOwnerKind { Source, EmployeeClone }
public enum StaffSpriteRemoval { Explicit, IdentityInvalidated, SessionDisposed, VmRetired }

/// <summary>A live person identity includes its object, captured numbers and level session; reused numeric ids never join it.</summary>
public sealed class StaffSpriteIdentity
{
	internal StaffSpriteIdentity( long session, StaffMember member ) => (Session, Member, Id, ThingId) = (session, member, member.Id, member.ThingId);
	public long Session { get; }
	public StaffMember Member { get; }
	public int Id { get; }
	public int ThingId { get; }
	internal bool NumbersMatch => Member.Id == Id && Member.ThingId == ThingId;
}

/// <summary>Generation safety lives outside the VM's native-shaped slot Handle word.</summary>
public readonly record struct StaffSpriteTicket( long Session, int Slot, ulong Generation, StaffSpriteOwnerKind Kind, StaffSpriteIdentity Identity );

/// <summary>One staff-subset registry. No graph reads another instance, so ascending staff-only scheduling is independent of omitted guest handles.</summary>
public sealed class StaffSpriteRegistry
{
	private sealed class Entry( StaffSpriteTicket ticket, StaffSpriteVmState state )
	{
		internal readonly StaffSpriteTicket Ticket = ticket;
		internal readonly StaffSpriteVmState State = state;
		internal StaffSpriteTickResult LastTick;
	}
	private Entry?[] entries = new Entry?[1];
	private ulong[] generations = new ulong[1];
	private bool disposed;
	public StaffSpriteRegistry( long session ) => Session = session;
	public long Session { get; }
	public int Capacity => entries.Length - 1;
	public int Count { get; private set; }
	public event Action<StaffSpriteTicket, StaffSpriteRemoval>? Removed;
	internal event Action<StaffSpriteTicket, int, StaffSpriteTickResult>? Scheduled;

	internal StaffSpriteTicket Allocate( StaffSpriteIdentity identity, StaffSpriteOwnerKind kind, Func<uint, StaffSpriteVmState> create )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( identity.Session != Session || !identity.NumbersMatch ) throw new InvalidOperationException( "A staff allocation requires a current session identity." );
		var slot = 1;
		while ( slot < entries.Length && (entries[slot] != null || generations[slot] == ulong.MaxValue) ) slot++;
		if ( slot == entries.Length )
		{
			Array.Resize( ref entries, checked( entries.Length + 50 ) );
			Array.Resize( ref generations, entries.Length );
		}
		// Exhausted generations are permanently unavailable, never wrapped into a stale ticket.
		var generation = checked( generations[slot] + 1 );
		var state = create( (uint)slot );
		state.Handle = (uint)slot;
		var ticket = new StaffSpriteTicket( Session, slot, generation, kind, identity );
		generations[slot] = generation;
		entries[slot] = new Entry( ticket, state );
		Count++;
		return ticket;
	}

	internal bool TryResolve( StaffSpriteTicket ticket, out StaffSpriteVmState state )
	{
		state = null!;
		if ( disposed || ticket.Session != Session || ticket.Slot <= 0 || ticket.Slot >= entries.Length
			|| entries[ticket.Slot] is not { } entry || entry.Ticket != ticket || !ticket.Identity.NumbersMatch ) return false;
		state = entry.State;
		return true;
	}

	internal StaffSpriteDiagnostic Diagnostic( StaffSpriteTicket ticket ) => TryResolve( ticket, out _ )
		? entries[ticket.Slot]!.LastTick.Status == StaffSpriteTickStatus.Invalid ? StaffSpriteDiagnostic.InvalidVm : StaffSpriteDiagnostic.None
		: StaffSpriteDiagnostic.Retired;

	internal bool Remove( StaffSpriteTicket ticket, StaffSpriteRemoval reason = StaffSpriteRemoval.Explicit )
	{
		if ( ticket.Session != Session || ticket.Slot <= 0 || ticket.Slot >= entries.Length || entries[ticket.Slot]?.Ticket != ticket ) return false;
		entries[ticket.Slot] = null;
		Count--;
		Removed?.Invoke( ticket, reason );
		return true;
	}

	internal void RemoveIdentity( StaffSpriteIdentity identity )
	{
		foreach ( var ticket in Tickets().Where( ticket => ReferenceEquals( ticket.Identity, identity ) ).ToArray() )
			Remove( ticket, StaffSpriteRemoval.IdentityInvalidated );
	}

	internal IEnumerable<StaffSpriteTicket> Tickets()
	{
		for ( var slot = 1; slot < entries.Length; slot++ )
			if ( entries[slot] is { } entry ) yield return entry.Ticket;
	}

	public void Schedule( int now ) => Schedule( now, StaffSpritePrograms.Builtin );
	internal void Schedule( int now, StaffSpriteProgramSet programs )
	{
		if ( disposed ) return;
		for ( var slot = 1; slot < entries.Length; slot++ )
		{
			if ( entries[slot] is not { } entry ) continue;
			var result = StaffSpriteVm.Tick( entry.State, now, programs );
			if ( result.Status != StaffSpriteTickStatus.NotDue ) entry.LastTick = result;
			if ( result.Status == StaffSpriteTickStatus.RemovalDue ) Remove( entry.Ticket, StaffSpriteRemoval.VmRetired );
			Scheduled?.Invoke( entry.Ticket, now, result );
		}
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		foreach ( var ticket in Tickets().ToArray() ) Remove( ticket, StaffSpriteRemoval.SessionDisposed );
	}
}
