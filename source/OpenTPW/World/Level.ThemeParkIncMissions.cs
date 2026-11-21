namespace OpenTPW;

public partial class Level
{
	/// <summary>The world variable <c>CurrentObstacle</c> (<c>park+0x1EAB6E</c>): the thing of the mission's obstacle, 0 for none.</summary>
	internal ushort ThemeParkIncCurrentObstacle { get; private set; }

	private ThemeParkIncMissionWorld? tpiMissionWorld;
	private IReadOnlyDictionary<int, ThemeParkIncObstacleType>? tpiObstacleTypes;

	/// <summary>The zone's obstacle types (<c>levels/&lt;zone&gt;/obstacles</c>), read on first use.</summary>
	internal IReadOnlyDictionary<int, ThemeParkIncObstacleType> ThemeParkIncObstacleTypes => tpiObstacleTypes ??= OpenTPW.ThemeParkIncObstacleTypes.Load( FileSystem, LevelName );

	/// <summary>
	/// Opens the obstacle thing <paramref name="thing"/> as the controller's camera end does: the park camera's target goes to the obstacle (<c>0x466050</c>), then its opening (<c>0x5E8BD0</c>,
	/// <see cref="ThemeParkIncObstacles.OpenThing"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x466050 camera target] x = ((cell - 1) & 0x7F) * 10 + w * 5 and y = ((cell - 1) >> 7) * 10 + w * 5 into 0xA14480/0xA14488, the cell being the thing's +0x44 (the anchor the placement constructs it at)
	// and w the width of its type's Info.Shape (+0x1C, [0]) for both axes; nothing for thing 0
	private void OpenThemeParkIncObstacle( ushort thing )
	{
		if ( Guests?.Grid.Cells is not { } cells )
			throw new InvalidOperationException( "A Theme Park Inc obstacle opens only in a world with its cells." );
		if ( ThemeParkIncThing( thing ) is { } item && ThemeParkIncObstacleTypes.TryGetValue( item.Entry.InfoId, out var type )
			&& Camera.Mode is ParkCameraMode camera && OriginalPark?.Heightfield is { } heightfield )
		{
			var anchor = ThemeParkIncObstacles.AnchorWord( type ) - 1;
			var half = (type.Shape?.Width ?? 0) * 0.5f;
			var target = GuestRenderer.CellToEngine( heightfield, (anchor & 0x7F) + half, (anchor >> 7) + half );
			camera.Focus( new Vector3( target.X, target.Y, target.Z ) );
		}
		ThemeParkIncObstacles.OpenThing( cells, ThemeParkIncObstacleTypes, tpiObstacleThings ??= new ThemeParkIncObstacleThings( this ), thing );
	}

	private ThemeParkIncObstacleThings? tpiObstacleThings;

	/// <summary>The level's objects as the obstacle code's things.</summary>
	private sealed class ThemeParkIncObstacleThings( Level level ) : IThemeParkIncObstacleThings
	{
		public int? TypeOf( ushort thing ) => level.ThemeParkIncThing( thing )?.Entry.InfoId;

		// [APPROX:RIDES-040] the replacement is a fixed item (Info.DontApplyOffset 1, like every obstacle) and is drawn from the sentinel anchor (0, 0) as the prebuilt's fixed scene records are
		public void Place( int type ) => level.Objects.Add( level.Objects.Catalog.Find( type )
			?? throw new InvalidDataException( $"An obstacle is replaced with type {type}, which no object archive of {level.LevelName} declares." ), 0, 0, 0 );

		public void Destroy( ushort thing )
		{
			if ( level.ThemeParkIncThing( thing ) is { } item )
				level.Objects.Remove( item );
		}
	}

	/// <summary>A park built from a Theme Park Inc save (the prebuilt Science park) takes its World variable <c>CurrentObstacle</c>; a continuation restores its own later.</summary>
	// [BIN:TPI-EXE:0x64BC00 World loader] CurrentObstacle is one of the World variables a park file carries (the prebuilt's names its 1700 gate)
	private void SeedThemeParkIncCurrentObstacle()
	{
		if ( IsThemeParkIncSlice && resumeState == null && OriginalPark?.Save?.ThemeParkIncWorld is { } world )
		{
			ThemeParkIncCurrentObstacle = world.Variables.CurrentObstacle;
			// [BIN:TPI-EXE:0x6A3D08 zone entry] a zone entered from the career's own zone world (the prebuilt park of a new game) sets 0xB8C004 and the arrival flag 0xB8C02C
			tpiArrivalPending = true;
		}
	}

	/// <summary>Gives the park's economy (also one a load put in place) this level as the world of its Theme Park Inc mission controller and cutscene thing.</summary>
	private void AttachThemeParkIncMissionWorld()
	{
		if ( IsThemeParkIncSlice && Park?.Economy is { TpiMissions: not null } economy && economy.TpiMissionWorld == null )
			economy.TpiMissionWorld = tpiMissionWorld ??= new ThemeParkIncMissionWorld( this );
		if ( IsThemeParkIncSlice && Park?.Economy is { TpiCutscene: not null } flights && flights.TpiCutsceneWorld == null )
			flights.TpiCutsceneWorld = tpiCutsceneWorld ??= new ThemeParkIncCutsceneWorld( this );
	}

	private ThemeParkIncCutsceneWorld? tpiCutsceneWorld;

	/// <summary>Whether the zone arrival's flyover is still to be tried (<c>0x6A43D8</c>): a world built from the level, not from a save of the player's.</summary>
	private bool tpiArrivalPending;

	/// <summary>
	/// The game state's first update of a Theme Park Inc world entered from the level (the prebuilt Science park of a new game): the zone arrival's flyover, once (<c>0x6A43D8</c>). A continued park
	/// (Load A Game) has none: the zone entry's flag is cleared for a loaded game without the career's zone save.
	/// </summary>
	// [BIN:TPI-EXE:0x6A43E4 loaded game] with a loaded game (0xB8C030 != 0) and no career zone save (0xB8C004 != 1) the flag 0xB8C02C is cleared (0x6A44AF), so the flyover does not play
	private void UpdateThemeParkIncArrival()
	{
		if ( !tpiArrivalPending )
			return;
		tpiArrivalPending = false;
		Park?.Economy.StartThemeParkIncArrival();
	}

	/// <summary>The Theme Park Inc flight's camera: the ride view on the thing's seat 1, followed as the thing's animation moves it, with the pointer look over the flight's input window.</summary>
	// [BIN:TPI-EXE:0x5E5B20 seat camera] 0x5E5B20(0, 1): thing +0x5C = 1; an info +0x14C of 3 runs the gate 0x4087FB first; neither IsObstacle (+0x4C) nor IsCameraPath (+0x60): script variable 12 = 1, which an obstacle and a
	// camera path skip; then 0x4668C0 (tp.exe 0x42A560) with seat 1: the first dummy record of the model with type bit 0x1000 and value 1 (RideSeat), its node matrix the camera frame, camera mode 4 (the ride view)
	private bool EnterThemeParkIncFlightView( OriginalObject thing )
	{
		LeaveThemeParkIncCameras();
		return EnterWinningView( thing );
	}

	/// <summary>Leaves any ride or walk camera (<c>0x50B4E0</c>), back to the park camera: a ride view entered from the walk leaves the walk too.</summary>
	// [BIN:TPI-EXE:0x50B4E0 leave cameras] camera word 0xA1465C bit 2 (the walk): 0x4675A0 (the walk left); else bit 4 (a ride view): 0x4657F0 (the ride left), and with bit 0x400 (entered from the walk) 0x4675A0 after it
	private void LeaveThemeParkIncCameras()
	{
		ExitRideView();
		ExitFreeWalk();
	}

	/// <summary>Whether <paramref name="item"/> is the thing of the Theme Park Inc flight that plays now, whose seat the ride view follows though it is no ride.</summary>
	internal bool IsThemeParkIncFlightThing( OriginalObject item ) =>
		Park?.Economy.TpiCutscene is { Flying: true } cutscene && cutscene.AnimThing == item.ThingId;

	/// <summary>The level's objects and ride view as the cutscene thing's world.</summary>
	private sealed class ThemeParkIncCutsceneWorld( Level level ) : IThemeParkIncCutsceneWorld
	{
		public RideVM? ScriptOf( ushort thing ) => level.ThemeParkIncThing( thing )?.Script;

		public int? TypeOf( ushort thing ) => level.ThemeParkIncThing( thing )?.Entry.InfoId;

		public void EnterView( ushort thing )
		{
			if ( level.ThemeParkIncThing( thing ) is { } item )
				level.EnterThemeParkIncFlightView( item );
			else
				level.LeaveThemeParkIncCameras();
		}

		public void LeaveView() => level.LeaveThemeParkIncCameras();
	}

	/// <summary>The live attractions of <paramref name="type"/> in the thing list's order (newest first, <c>0xAA116C</c>).</summary>
	private IEnumerable<OriginalObject> ThemeParkIncAttractionsOf( int type )
	{
		var order = Things.Capture().Select( ( slot, index ) => (slot.Id, index) ).ToDictionary( pair => pair.Id, pair => pair.index );
		return Objects.Objects.Where( item => !item.IsDeleted && item.Entry.InfoId == type && item.ThingId is > 0 and <= ushort.MaxValue )
			.OrderBy( item => order.TryGetValue( item.ThingId, out var index ) ? index : int.MaxValue ).ThenByDescending( item => item.ThingId );
	}

	private OriginalObject? ThemeParkIncThing( ushort thing ) =>
		thing == 0 ? null : Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.ThingId == thing );

	private sealed class ThemeParkIncMissionWorld( Level level ) : IThemeParkIncMissionWorld
	{
		public ushort CurrentObstacle => level.ThemeParkIncCurrentObstacle;

		// [BIN:TPI-EXE:0x6476F0 current obstacle] Obstacle.Type 0 clears park+0x1EAB6E; then every class-3 thing whose type is it is assigned in the list's order without a break, so the last one wins;
		// with none found the old value stays
		public void FindCurrentObstacle( int type )
		{
			if ( type == 0 )
				level.ThemeParkIncCurrentObstacle = 0;
			foreach ( var item in level.ThemeParkIncAttractionsOf( type ) )
				level.ThemeParkIncCurrentObstacle = (ushort)item.ThingId;
		}

		// [BIN:TPI-EXE:0x647850 find attraction] walks the thing list from 0xAA116C and returns the first class-3 thing whose type record's +0 is the type, else 0
		public ushort FindAttraction( int type ) => level.ThemeParkIncAttractionsOf( type ).FirstOrDefault() is { } item ? (ushort)item.ThingId : (ushort)0;

		// [BIN:TPI-EXE:0x5F027E opened automatically] the type's object info (0xA09AC8->0x43C700) +0x138 == 1, the key Obstacle.OpenedAutomaticly; only the obstacles carry it
		public bool OpensAutomatically( int type ) => level.ThemeParkIncObstacleTypes.TryGetValue( type, out var obstacle ) && obstacle.OpenedAutomatically;

		public void OpenObstacle( ushort thing ) => level.OpenThemeParkIncObstacle( thing );

		// [BIN:TPI-EXE:0x637C40 thing destroy] the class's destroy handler, then the thing is marked dead and freed
		public void DestroyThing( ushort thing )
		{
			if ( level.ThemeParkIncThing( thing ) is { } item )
				level.Objects.Remove( item );
		}

		// [BIN:TPI-EXE:0x5F0B66 mission extent] the tick loads "%sextent.%03d" with 0x678B00 and does not look at its result
		public void LoadExtent( int index )
		{
			if ( !level.ApplyThemeParkIncExtent( index ) )
				Log.Warning( $"Theme Park Inc mission extent {index} is missing or short; the land keeps the cells read." );
		}

	}
}
