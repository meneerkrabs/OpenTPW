namespace OpenTPW;

public partial class Level
{
	/// <summary>The world variable <c>CurrentObstacle</c> (<c>park+0x1EAB6E</c>): the thing of the mission's obstacle, 0 for none.</summary>
	internal ushort ThemeParkIncCurrentObstacle { get; private set; }

	private ThemeParkIncMissionWorld? tpiMissionWorld;
	private IReadOnlyDictionary<int, ThemeParkIncObstacleType>? tpiObstacleTypes;

	/// <summary>The zone's obstacle types (<c>levels/&lt;zone&gt;/obstacles</c>), read on first use.</summary>
	internal IReadOnlyDictionary<int, ThemeParkIncObstacleType> ThemeParkIncObstacleTypes => tpiObstacleTypes ??= OpenTPW.ThemeParkIncObstacleTypes.Load( FileSystem, LevelName );

	/// <summary>Opens the obstacle thing <paramref name="thing"/> (<c>0x5E8BD0</c>): the level holds no obstacle things yet, so no thing can be named.</summary>
	private void OpenThemeParkIncObstacle( ushort thing ) =>
		throw new InvalidOperationException( $"Thing {thing} is no Theme Park Inc obstacle of this level." );

	/// <summary>Gives the park's economy (also one a load put in place) this level as the world of its Theme Park Inc mission controller.</summary>
	private void AttachThemeParkIncMissionWorld()
	{
		if ( IsThemeParkIncSlice && Park?.Economy is { TpiMissions: not null } economy && economy.TpiMissionWorld == null )
			economy.TpiMissionWorld = tpiMissionWorld ??= new ThemeParkIncMissionWorld( this );
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

		// [EXT:tpi-capability-gates] the cutscene thing (class 22, CUTS: 0x5D0400, its update 0x5D02F0 and end 0x5D0840) is not ported: nothing plays the flight, so its end (message 0x29) never comes
		public void PlayCamera( ThemeParkIncCameraRequest request ) =>
			Log.Warning( $"Theme Park Inc camera flight {request.Animation} on thing {request.Thing} (mode {request.Mode}) is not ported; the mission controller waits for its end." );
	}
}
