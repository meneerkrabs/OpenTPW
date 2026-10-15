namespace OpenTPW;

/// <summary>
/// The bump-ride module state of one park (the original's module globals): the cosmetic generator, the controllers in creation order, the module frame counter, the
/// vehicle pool every controller's vehicles share, the collision-object pool every search walks and the buoy budget. Owned by the park's <see cref="RideScriptWorld"/>,
/// so every ride runtime reaches it; it holds nothing for a park without a kart, water or bumper ride. A controller made without a world gets one of its own.
/// </summary>
// [BIN:TPW-EXE:0x00546C80 module tick] one call per world substep: the frame counter 0x877B5C rises, every live vehicle of the pool 0x877B68 (256 records of 0xAC, in pool order) is stepped (0x5474B0) and integrated (0x547F50), then every live vehicle collides with every other live vehicle of the pool, whichever ride owns it, and runs its stuck check, walls (0x5497B0), keep-out circles (0x5494D0) and effect player (0x547170)
public sealed class BumpWorld
{
	/// <summary>The module pools: 256 vehicles (0x877B68), 2048 collision objects (0x877B70), 1024 buoys (0x877B78).</summary>
	public const int VehiclePool = 256;
	public const int ObjectPool = TrackSections.MaximumCollisionObjects;
	public const int BuoyPool = TrackSections.MaximumBuoys;

	private readonly List<BumpController> controllers = new();
	private readonly BumpVehicle?[] vehicleSlots = new BumpVehicle?[VehiclePool];
	private readonly CollisionObject?[] objectSlots = new CollisionObject?[ObjectPool];
	/// <summary>The live collision objects in pool order: every object search walks them so (the original walks the pool and stops after the live count).</summary>
	private readonly List<CollisionObject> objectOrder = new();
	private double tickRemainder;

	public IReadOnlyList<BumpController> Controllers => controllers;

	/// <summary>The cosmetic LCG state (DAT 0x877B90): only the spray, smoke, TOOT and CRUNCH gates read it; gameplay draws use the world generator.</summary>
	// [BIN:TPW-EXE:0x00547F50 module lcg] s = s × 214013 + 2531011 (0x343FD, 0x269EC3), gating the water boats' spray (one in 8 every 32nd tick), the kart smoke and the horn and crash cues; the start value of the global is not traced and starts at 0 here
	public uint CosmeticState { get; internal set; }

	/// <summary>The module frame counter (0x877B5C): +1 per module tick; the 32-tick gates of every controller read it.</summary>
	// [BIN:TPW-EXE:0x00546C80 frame counter] the tick adds one to 0x877B5C before the vehicle pass; 0x547F50 tests its low five bits for the horn, exhaust, sparks and spray gates of every controller
	public long FrameCount { get; internal set; }

	/// <summary>The fractional tick time left over (one module clock for every controller).</summary>
	public double TickRemainder => tickRemainder;

	/// <summary>The live collision objects of every controller, in pool order.</summary>
	public IReadOnlyList<CollisionObject> Objects => objectOrder;

	/// <summary>Live buoys of every controller (the pool of 1024 bounds them).</summary>
	public int BuoyCount => controllers.Sum( controller => controller.Buoys.Count );

	/// <summary>The vehicle in pool slot <paramref name="slot"/>, or null.</summary>
	public BumpVehicle? VehicleAt( int slot ) => slot >= 0 && slot < VehiclePool ? vehicleSlots[slot] : null;

	/// <summary>Draws the next cosmetic value: the LCG state as a signed 32-bit word.</summary>
	public int NextCosmetic()
	{
		CosmeticState = unchecked( CosmeticState * 214013u + 2531011u );
		return unchecked((int)CosmeticState);
	}

	internal void Add( BumpController controller )
	{
		if ( !controllers.Contains( controller ) )
			controllers.Add( controller );
	}

	internal void Remove( BumpController controller )
	{
		controllers.Remove( controller );
		ForgetObjectsOf( controller );
		ReleaseObjects( controller );
		for ( var slot = 0; slot < VehiclePool; slot++ )
		{
			if ( vehicleSlots[slot]?.Controller == controller )
				vehicleSlots[slot] = null;
		}
	}

	/// <summary>The vehicles of other rides that sat in <paramref name="owner"/>'s objects lose them (the objects are rebuilt or gone; the original keeps a stale pointer).</summary>
	internal void ForgetObjectsOf( BumpController owner )
	{
		foreach ( var controller in controllers )
		{
			if ( controller == owner )
				continue;
			foreach ( var vehicle in controller.Vehicles )
			{
				if ( vehicle.ObjectOwner == owner )
				{
					vehicle.ObjectIndex = -1;
					vehicle.ObjectOwner = null;
				}
			}
		}
	}

	// ---- The vehicle pool ----------------------------------------------------------------------------

	/// <summary>Takes the first free pool slot for <paramref name="vehicle"/>; −1 when the pool is full.</summary>
	// [BIN:TPW-EXE:0x00549DB0 create vehicle] the first pool record whose flag bit 0 is clear, of 256; none: no vehicle
	internal int AllocateVehicle( BumpVehicle vehicle )
	{
		for ( var slot = 0; slot < VehiclePool; slot++ )
		{
			if ( vehicleSlots[slot] != null )
				continue;
			vehicleSlots[slot] = vehicle;
			return slot;
		}
		return -1;
	}

	/// <summary>Puts a restored vehicle back in its saved slot, or in the first free slot when another ride's vehicle holds it (a save from before the shared pool).</summary>
	internal int PlaceVehicle( BumpVehicle vehicle, int slot )
	{
		if ( slot >= 0 && slot < VehiclePool && vehicleSlots[slot] == null )
		{
			vehicleSlots[slot] = vehicle;
			return slot;
		}
		return AllocateVehicle( vehicle );
	}

	internal void FreeVehicle( BumpVehicle vehicle )
	{
		if ( vehicle.Slot >= 0 && vehicle.Slot < VehiclePool && vehicleSlots[vehicle.Slot] == vehicle )
			vehicleSlots[vehicle.Slot] = null;
	}

	// ---- The collision-object pool --------------------------------------------------------------------

	/// <summary>
	/// Replaces <paramref name="owner"/>'s collision objects in the pool: its old ones are freed, then the new ones take the first free slots in order. False (and
	/// nothing placed) when the pool cannot hold them.
	/// </summary>
	// [BIN:TPW-EXE:0x0054B1E0 object allocation] the first record of the 2048 whose flag bit 0 is clear; none: "Out of Collision Objects"
	internal bool PlaceObjects( BumpController owner, IReadOnlyList<CollisionObject> objects )
	{
		ReleaseObjects( owner );
		var free = 0;
		for ( var slot = 0; slot < ObjectPool && free < objects.Count; slot++ )
		{
			if ( objectSlots[slot] == null )
				free++;
		}
		if ( free < objects.Count )
			return false;
		var next = 0;
		foreach ( var item in objects )
		{
			while ( objectSlots[next] != null )
				next++;
			item.Owner = owner;
			item.ModuleSlot = next;
			objectSlots[next] = item;
		}
		RebuildOrder();
		return true;
	}

	/// <summary>Moves <paramref name="owner"/>'s objects to saved pool slots (a park-resume restore); throws when a slot is out of range or taken.</summary>
	internal void RestoreObjectSlots( BumpController owner, IReadOnlyList<int> slots )
	{
		var objects = owner.Objects;
		if ( slots.Count != objects.Count )
			throw new InvalidDataException( $"A saved controller places {slots.Count} collision objects, but its walked circuit has {objects.Count}." );
		ReleaseObjects( owner );
		for ( var index = 0; index < objects.Count; index++ )
		{
			var slot = slots[index];
			if ( slot < 0 || slot >= ObjectPool || objectSlots[slot] != null )
				throw new InvalidDataException( $"A saved collision object sits in pool slot {slot}, which is out of range or taken." );
			objects[index].Owner = owner;
			objects[index].ModuleSlot = slot;
			objectSlots[slot] = objects[index];
		}
		RebuildOrder();
	}

	private void ReleaseObjects( BumpController owner )
	{
		var any = false;
		for ( var slot = 0; slot < ObjectPool; slot++ )
		{
			if ( objectSlots[slot]?.Owner != owner )
				continue;
			objectSlots[slot]!.ModuleSlot = -1;
			objectSlots[slot] = null;
			any = true;
		}
		if ( any )
			RebuildOrder();
	}

	private void RebuildOrder()
	{
		objectOrder.Clear();
		foreach ( var item in objectSlots )
		{
			if ( item != null )
				objectOrder.Add( item );
		}
	}

	// ---- The module tick -----------------------------------------------------------------------------

	/// <summary>
	/// Advances the module clock when <paramref name="caller"/> drives it (the first controller: the module ticks once per world step, not once per ride);
	/// whole 31 ms ticks run, the rest carries over.
	/// </summary>
	// [BIN:TPW-EXE:0x0054F55F world substep] every substep calls the module tick once, before the scripts (0x5516B0); OpenTPW ticks the module where the first controller's ride advances, so the rides placed before it run their script step first
	internal void Advance( BumpController caller, double deltaSeconds )
	{
		if ( controllers.Count == 0 || controllers[0] != caller || !double.IsFinite( deltaSeconds ) || deltaSeconds <= 0 )
			return;
		tickRemainder += deltaSeconds;
		while ( tickRemainder >= BumpController.TickSeconds - 1e-9 )
		{
			tickRemainder = Math.Max( 0, tickRemainder - BumpController.TickSeconds );
			Tick();
		}
	}

	internal void RestoreTickRemainder( double remainder ) => tickRemainder = remainder;

	/// <summary>
	/// The end of a park-resume restore, once every controller is back: the saved module clock, the objects' saved pool slots and the vehicles that sat in another
	/// ride's object. A save from before the shared module carries none of these: the first controller's clock stays, the objects keep the pool slots the restore
	/// gave them, and no vehicle names another ride's object.
	/// </summary>
	internal void FinishRestore( long? frameCount, double? remainder, IReadOnlyList<(BumpController Controller, IReadOnlyList<int>? Slots)> objectSlots )
	{
		if ( frameCount is { } frames )
		{
			if ( frames < 0 )
				throw new InvalidDataException( "The saved module frame counter is negative." );
			FrameCount = frames;
		}
		if ( remainder is { } rest )
		{
			if ( !double.IsFinite( rest ) || rest < 0 || rest >= BumpController.MaximumSavedRemainder )
				throw new InvalidDataException( "The saved module clock is outside one tick." );
			tickRemainder = rest;
		}
		var placed = objectSlots.Where( item => item.Slots != null ).ToList();
		foreach ( var (controller, _) in placed )
			ReleaseObjects( controller );
		foreach ( var (controller, slots) in placed )
			RestoreObjectSlots( controller, slots! );
		foreach ( var controller in controllers )
		{
			foreach ( var vehicle in controller.Vehicles )
			{
				if ( vehicle.RestoredObjectAttraction < 0 )
					continue;
				var owner = controllers.FirstOrDefault( item => item.AttractionId == vehicle.RestoredObjectAttraction && item != controller )
					?? throw new InvalidDataException( $"A saved vehicle sits in an object of attraction {vehicle.RestoredObjectAttraction}, which holds no other controller." );
				if ( vehicle.ObjectIndex < 0 || vehicle.ObjectIndex >= owner.Objects.Count )
					throw new InvalidDataException( $"A saved vehicle sits in object {vehicle.ObjectIndex} of attraction {owner.AttractionId}, which has {owner.Objects.Count}." );
				vehicle.ObjectOwner = owner;
				vehicle.RestoredObjectAttraction = -1;
			}
		}
	}

	/// <summary>One module tick: every controller's vehicles in one pass in pool order, then the collisions across the whole pool.</summary>
	public void Tick()
	{
		FrameCount++;
		foreach ( var controller in controllers )
			controller.TickCount++;
		for ( var slot = 0; slot < VehiclePool; slot++ )
		{
			if ( vehicleSlots[slot] is not { } vehicle || vehicle.Controller is not { } owner )
				continue;
			vehicle.Flags &= ~BumpVehicle.Crunched;
			owner.StepVehicle( vehicle );
			if ( vehicle.IsLive )
				owner.IntegrateVehicle( vehicle );
		}
		for ( var slot = 0; slot < VehiclePool; slot++ )
		{
			if ( vehicleSlots[slot] is not { IsLive: true } vehicle || vehicle.Controller is not { } owner )
				continue;
			if ( ((BumpController.ObjectOf( vehicle )?.Flags ?? 0) & CollisionObject.Crossing) == 0 && (vehicle.Flags & 0x200) == 0 )
			{
				for ( var otherSlot = 0; otherSlot < VehiclePool; otherSlot++ )
				{
					if ( vehicleSlots[otherSlot] is { } other )
						owner.Collide( vehicle, other );
				}
			}
			owner.FinishVehicle( vehicle );
		}
	}

	/// <summary>Controller part of <see cref="WorldStateHash"/>; called only while a controller exists, so every park without a kart or water ride keeps its pins.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( (long)CosmeticState );
		hash.Add( FrameCount );
		hash.Add( controllers.Count );
		foreach ( var controller in controllers )
			controller.AddCanonicalState( hash );
		// the shared pools' order decides the passes and the searches
		foreach ( var vehicle in vehicleSlots )
			hash.Add( vehicle?.Controller?.AttractionId ?? -2 );
		foreach ( var item in objectOrder )
		{
			hash.Add( item.ModuleSlot );
			hash.Add( item.Owner?.AttractionId ?? -2 );
		}
	}
}
