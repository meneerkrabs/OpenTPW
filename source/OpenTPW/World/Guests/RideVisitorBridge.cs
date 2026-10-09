namespace OpenTPW;

/// <summary>
/// Connects real guests to an original RSE script. The host side follows the corpus protocol
/// (docs/GUESTS.md): between slices, outside CRIT_LOCK, the host writes the front guest's id to
/// VAR_LETMEON when it is 0, and when the script puts a guest id in VAR_LETMEOFF the host releases that
/// guest at the exit and writes 0 back. The visitor opcodes (HUSH, WALKON, HOP, WALKOFF, WALKGET, LIMBO,
/// UNLIMBO, FORCEUNLIMBO, INLIMBO, LIMBOSPACE, BOUNCE, UNBOUNCE, FORCEUNBOUNCE, BOUNCING, ADDHEAD,
/// DELHEAD) operate on the guests the script holds. Semantics are inferred from corpus control flow,
/// not traced from the original executable; walk animations along ride nodes are not simulated (WALKON
/// and WALKOFF complete immediately).
/// </summary>
public sealed class RideVisitorBridge : IRideVisitorBridge
{
	/// <summary>BOUNCE with a zero duration (VAR_DURATION has no source yet) keeps a guest this long. Approximation.</summary>
	public const double DefaultBounceSeconds = 10;

	private readonly List<int> queue = new();
	private readonly List<int> onRide = new();          // boarded, in boarding order
	private readonly Queue<int> walkingOff = new();     // WALKOFF done, waiting for WALKGET
	private readonly HashSet<int> hopped = new();
	private readonly List<(int Guest, double Until)> limbo = new();
	private readonly List<(int Guest, double Until)> bouncing = new();
	private RideVM? vm;
	private int offered;
	private Func<bool> isOpen = () => true;

	public RideVisitorBridge( int attractionId, string name, RideVisitorKind kind, int capacity, int excitementLevel, int attractionValue,
		GuestNeeds satisfies = GuestNeeds.None, int price = 0 )
	{
		if ( attractionId <= 0 )
			throw new ArgumentOutOfRangeException( nameof( attractionId ) );
		AttractionId = attractionId;
		Name = name;
		Kind = kind;
		Capacity = Math.Max( 1, capacity );
		ExcitementLevel = excitementLevel;
		AttractionValue = attractionValue;
		Satisfies = satisfies;
		Price = price;
	}

	public int AttractionId { get; }
	public string Name { get; }
	public RideVisitorKind Kind { get; }
	public int Capacity { get; }
	public int ExcitementLevel { get; }
	public int AttractionValue { get; }
	public GuestNeeds Satisfies { get; }
	public int Price { get; }
	public (int X, int Y) EntranceCell { get; set; }
	public (int X, int Y) ExitCell { get; set; }
	public bool HasCells { get; set; }
	public int QueueLength => queue.Count;
	/// <summary>Approximation: 4 × capacity (the .sam QueueWaitTimeConstant is not understood).</summary>
	public int MaximumQueueLength { get; set; }
	public IRideVisitorHost? Host { get; set; }
	public bool IsOpen => vm != null && vm.State is (RideVMState.Running or RideVMState.Waiting) && isOpen();
	public int OfferedGuest => offered;
	public IReadOnlyList<int> Queue => queue;
	/// <summary>Guests the script holds (boarded and not yet released).</summary>
	public IReadOnlyList<int> Riders => onRide;
	public int BoardedTotal { get; private set; }
	public int ReleasedTotal { get; private set; }

	/// <summary>Binds the bridge to the script it serves; <paramref name="open"/> reports whether the owner opened the ride.</summary>
	public void Attach( RideVM script, Func<bool> open )
	{
		vm = script;
		isOpen = open;
		if ( MaximumQueueLength <= 0 )
			MaximumQueueLength = Capacity * 4;
	}

	public bool TryJoinQueue( int guestId )
	{
		if ( guestId <= 0 || !IsOpen || queue.Count >= MaximumQueueLength || queue.Contains( guestId ) || Holds( guestId ) )
			return false;
		queue.Add( guestId );
		return true;
	}

	public int GetQueuePosition( int guestId ) => queue.IndexOf( guestId );

	public bool LeaveQueue( int guestId ) => queue.Remove( guestId );

	// Scripts declare different variable subsets (e.g. features without VAR_LETMEOFF), so access by name.
	private int Get( RideVariables variable )
	{
		var index = vm?.GetVariableIndex( variable.ToString() ) ?? -1;
		return index < 0 ? 0 : vm!.Variables[index];
	}

	private void Set( RideVariables variable, int value )
	{
		var index = vm?.GetVariableIndex( variable.ToString() ) ?? -1;
		if ( index >= 0 )
			vm!.Variables[index] = value;
	}

	private bool Holds( int guestId ) => guestId == offered || onRide.Contains( guestId );

	/// <summary>Host side of the protocol; call once per tick before the script advances.</summary>
	public void HostStep()
	{
		if ( vm == null )
			return;
		if ( vm.State != RideVMState.Running && vm.State != RideVMState.Waiting )
		{
			ReleaseAll();
			return;
		}
		if ( vm.InCriticalSection )
			return;

		var off = Get( RideVariables.VAR_LETMEOFF );
		if ( off != 0 )
		{
			Set( RideVariables.VAR_LETMEOFF, 0 );
			Release( off );
		}

		if ( offered != 0 && Get( RideVariables.VAR_LETMEON ) != offered )
		{
			var guest = offered;
			offered = 0;
			MarkBoarded( guest );
		}

		if ( offered != 0 && !isOpen() )
		{
			// The script never took the guest before closing; give it back.
			Set( RideVariables.VAR_LETMEON, 0 );
			var guest = offered;
			offered = 0;
			Host?.OnVisitorTurnedAway( this, guest );
		}

		if ( !isOpen() )
		{
			foreach ( var guest in queue.ToArray() )
			{
				queue.Remove( guest );
				Host?.OnVisitorTurnedAway( this, guest );
			}
			return;
		}

		if ( offered == 0 && Get( RideVariables.VAR_LETMEON ) == 0 && queue.Count > 0 )
		{
			offered = queue[0];
			queue.RemoveAt( 0 );
			Set( RideVariables.VAR_LETMEON, offered );
			Host?.OnVisitorOffered( this, offered );
		}
	}

	private void MarkBoarded( int guest )
	{
		if ( guest <= 0 || onRide.Contains( guest ) )
			return;
		if ( guest == offered )
			offered = 0;
		queue.Remove( guest );
		onRide.Add( guest );
		BoardedTotal++;
		Host?.OnVisitorBoarded( this, guest );
	}

	private void Release( int guest )
	{
		if ( !onRide.Remove( guest ) )
			return;
		hopped.Remove( guest );
		limbo.RemoveAll( entry => entry.Guest == guest );
		bouncing.RemoveAll( entry => entry.Guest == guest );
		ReleasedTotal++;
		Host?.OnVisitorReleased( this, guest );
	}

	/// <summary>Releases every held guest and turns the queue away (ride removed or script stopped).</summary>
	public void ReleaseAll()
	{
		if ( offered != 0 )
		{
			var guest = offered;
			offered = 0;
			if ( vm != null && Get( RideVariables.VAR_LETMEON ) == guest )
				Set( RideVariables.VAR_LETMEON, 0 );
			Host?.OnVisitorTurnedAway( this, guest );
		}
		foreach ( var guest in queue.ToArray() )
			Host?.OnVisitorTurnedAway( this, guest );
		queue.Clear();
		foreach ( var guest in onRide.ToArray() )
			Release( guest );
		walkingOff.Clear();
	}

	/// <summary>Performs a visitor opcode; false for opcodes the bridge does not own.</summary>
	public bool TryPerform( RideEffectCall call, out int result )
	{
		result = 0;
		var now = call.VM.TimeMilliseconds;
		switch ( call.Opcode )
		{
			case Opcode.HUSH:
				MarkBoarded( call.Argument( 0 ) );
				return true;
			case Opcode.WALKON:
				MarkBoarded( call.Argument( 0 ) );
				return true;
			case Opcode.HOP:
				result = onRide.FirstOrDefault( guest => !hopped.Contains( guest ) );
				if ( result != 0 )
					hopped.Add( result );
				return true;
			case Opcode.WALKOFF:
			{
				var guest = call.Argument( 0 );
				if ( onRide.Contains( guest ) && !walkingOff.Contains( guest ) )
				{
					hopped.Add( guest );
					walkingOff.Enqueue( guest );
				}
				return true;
			}
			case Opcode.WALKGET:
				result = walkingOff.Count > 0 ? walkingOff.Dequeue() : 0;
				return true;
			case Opcode.LIMBO:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				if ( onRide.Contains( guest ) && !limbo.Any( entry => entry.Guest == guest ) )
					limbo.Add( (guest, now + Math.Max( 0, call.Argument( 1 ) ) * 1000.0) );
				return true;
			}
			case Opcode.UNLIMBO:
				result = TakeExpired( limbo, now, force: false );
				return true;
			case Opcode.FORCEUNLIMBO:
				result = TakeExpired( limbo, now, force: true );
				return true;
			case Opcode.INLIMBO:
				result = limbo.Count;
				return true;
			case Opcode.LIMBOSPACE:
				result = Math.Max( 0, call.VM.Script.LimboSize - limbo.Count );
				return true;
			case Opcode.BOUNCE:
			{
				var guest = call.Argument( 0 );
				MarkBoarded( guest );
				var seconds = call.Argument( 1 ) > 0 ? call.Argument( 1 ) : DefaultBounceSeconds;
				if ( onRide.Contains( guest ) && !bouncing.Any( entry => entry.Guest == guest ) )
					bouncing.Add( (guest, now + seconds * 1000.0) );
				return true;
			}
			case Opcode.UNBOUNCE:
			case Opcode.FORCEUNBOUNCE:
			{
				// Corpus: `TEST VAR_LETMEOFF; BRANCH_NZ; UNBOUNCE VAR_LETMEOFF` writes the leaving guest into LETMEOFF.
				var guest = TakeExpired( bouncing, now, call.Opcode == Opcode.FORCEUNBOUNCE );
				if ( call.IsVariable( 0 ) )
					call.SetOutput( 0, guest );
				result = guest;
				return true;
			}
			case Opcode.BOUNCING:
				result = bouncing.Count;
				return true;
			case Opcode.ADDHEAD:
			case Opcode.DELHEAD:
				// Head sprites (Info.DoHeadProcessing) are cosmetic; on-ride guests are not drawn yet.
				return true;
			default:
				return false;
		}
	}

	private static int TakeExpired( List<(int Guest, double Until)> list, double now, bool force )
	{
		for ( var index = 0; index < list.Count; index++ )
		{
			if ( force || list[index].Until <= now )
			{
				var guest = list[index].Guest;
				list.RemoveAt( index );
				return guest;
			}
		}
		return 0;
	}
}

/// <summary>Routes visitor opcodes to a <see cref="RideVisitorBridge"/> and everything else to <paramref name="inner"/>.</summary>
public sealed class VisitorRideScriptEffects( RideVisitorBridge bridge, IRideScriptEffects inner ) : IRideScriptEffects
{
	public int Perform( RideEffectCall call ) => bridge.TryPerform( call, out var result ) ? result : inner.Perform( call );
}
