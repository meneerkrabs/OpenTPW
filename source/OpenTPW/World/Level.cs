using OpenTPW.UI;

namespace OpenTPW;

public class Level : IDisposable
{
	internal static Level Current { get; set; }

	public RootPanel Hud { get; set; }
	public Sun SunLight { get; set; }

	public SettingsFile Global { get; private init; }
	private ParkLayout parkLayout = null!;
	internal SandboxTextOverlay TextOverlay { get; private set; } = null!;
	public PrototypeRide? PlacedRide { get; private set; }
	public bool IsPlacing { get; set; }
	public string LastActionMessage { get; private set; } = "";
	/// <summary>Imported original level data; null for the generic sandbox.</summary>
	public OriginalPark? OriginalPark { get; private set; }
	public OriginalTerrain? OriginalTerrain { get; private set; }
	/// <summary>Park management simulation (money, clock, staff, research); original levels only for now.</summary>
	public ParkEconomyRuntime? Park { get; private set; }
	private bool wasMouseDown;
	private readonly FixedStepClock simulationClock = new();
	/// <summary>Park visitors (imported original levels only; docs/GUESTS.md).</summary>
	public GuestSimulation? Guests { get; private set; }
	public GuestRenderer? GuestRenderer { get; private set; }
	/// <summary>Fixed seed so a level run is reproducible.</summary>
	public const ulong GuestSeed = 0x5450_5747_7565_7374;

	/// <summary>Level directory name, e.g. <c>jungle</c>.</summary>
	public string LevelName { get; }
	/// <summary>
	/// Set when this level shows a shared park read-only (docs/ONLINE.md): building, sandbox saves and
	/// any economy writes must be refused while it is set.
	/// </summary>
	public ParkVisitInfo? Visit { get; }
	public bool IsReadOnlyVisit => Visit != null;
	private OnlinePanel? onlinePanel;

	public Level( string levelName, bool loadOriginalLevel = false, ParkVisitInfo? visit = null, OnlineFolders? onlineFolders = null )
	{
		if ( visit != null && visit.Level != levelName )
			throw new ArgumentException( "The visit level must match the loaded level.", nameof( visit ) );
		LevelName = levelName;
		Visit = visit;
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;
		if ( visit != null ? !visit.IsSandbox : loadOriginalLevel )
		{
			OriginalPark = OriginalPark.Load( levelName );
			if ( !IsReadOnlyVisit )
				Park = ParkEconomyRuntime.ForOriginalLevel( OriginalPark );
		}

		SetupEntities();
		SetupHud( onlineFolders );
		if ( visit?.Payload.PrototypeRide is { } ride )
			RestoreVisitedRide( ride );
	}

	/// <summary>Shows the shared park's prototype ride; the visitor cannot move or remove it.</summary>
	private void RestoreVisitedRide( OpenTPW.Online.Packages.PrototypeRideState ride )
	{
		var position = new Vector3( ride.X, ride.Y, 0 );
		var allowed = OriginalPark != null ? CheckOriginalPlacement( position ) == OriginalPlacementResult.Allowed : ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius );
		if ( !allowed )
		{
			LastActionMessage = "The shared park's ride position is not buildable here; the ride is not shown.";
			return;
		}
		PlacedRide = new PrototypeRide( position );
		RegisterRideWithGuests( PlacedRide );
		if ( ride.Open )
			PlacedRide.Start();
	}

	private void SetupEntities()
	{
		SunLight = new Sun() { Position = new( 0, 100, 100 ) };

		if ( OriginalPark == null )
		{
			_ = new Terrain();
			ParkCameraMode.TargetExtent = ParkCameraMode.SandboxTargetExtent;
			ParkCameraMode.InitialTarget = Vector3.Zero;
		}
		else
		{
			OriginalTerrain = new OriginalTerrain( OriginalPark );
			var origin = OriginalParkPlacement.GetOrigin( OriginalPark.Heightfield );
			ParkCameraMode.TargetExtent = Math.Max( origin.X, origin.Y );
			var save = OriginalPark.Save;
			// Focus the imported paths, else the MAP initial path at the entrance, else the terrain centre.
			var field = OriginalPark.Heightfield;
			var paths = save?.PathCells.ToList() ?? Enumerable.Range( 0, field.CellCountX * field.CellCountZ )
				.Select( index => (X: index % field.CellCountX, Y: index / field.CellCountX) )
				.Where( cell => OriginalPark.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).ToList();
			var focus = paths.Count > 0
				? (X: (int)paths.Average( cell => cell.X ), Y: (int)paths.Average( cell => cell.Y ))
				: (X: field.CellCountX / 2, Y: field.CellCountZ / 2);
			var center = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, focus.X, focus.Y );
			ParkCameraMode.InitialTarget = new Vector3( center.X, center.Y, 0 );
			Log.Trace( $"Original {OriginalPark.LevelName} level: {OriginalTerrain.SurfaceCellCount} heightfield cells, {OriginalTerrain.TerrainMeshCount} terrain meshes"
				+ (save == null ? "; no original save." : $"; Easymode import: {save.PathCells.Count} path cells, {save.PlacedObjects.Count} placed objects, {save.FixedItems.Count} fixed items.") );
		}
		Camera.SetCameraMode<ParkCameraMode>();
		if ( OriginalPark != null )
		{
			SetupGuests( OriginalPark );
			if ( Park != null && Guests != null )
				Park.AttachGuests( Guests );
		}
	}

	private void SetupGuests( OriginalPark park )
	{
		var grid = GuestPathGrid.FromOriginal( park.Map, park.Save );
		Guests = new GuestSimulation( grid, GuestSettings.Load( park.LevelName ), GuestSeed );
		var sprites = GuestSpriteAtlas.LoadKids();
		GuestRenderer = new GuestRenderer( Guests, park.Heightfield, sprites );
		Log.Trace( $"Guests: {grid.WalkableCount} walkable path cells, {sprites.Count} original kid sprite sets, {Guests.Settings.Types.Count} .sam peep types." );
	}

	/// <summary>Walkable path cell next to (or nearest to) a footprint; the prototype Totem has no catalog entrance cell yet.</summary>
	internal static (int X, int Y)? FindRideEntrance( GuestPathGrid grid, int minX, int minY, int maxX, int maxY )
	{
		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for ( var y = 0; y < grid.CountY; y++ )
		{
			for ( var x = 0; x < grid.CountX; x++ )
			{
				if ( !grid.IsWalkable( x, y ) )
					continue;
				var distance = Math.Max( 0, Math.Max( minX - x, x - maxX ) ) + Math.Max( 0, Math.Max( minY - y, y - maxY ) );
				if ( distance < bestDistance )
				{
					bestDistance = distance;
					best = (x, y);
				}
			}
		}
		return best;
	}

	private void RegisterRideWithGuests( PrototypeRide ride )
	{
		if ( Guests == null || OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ride.Position.X, ride.Position.Y, out var x, out var y ) )
			return;
		const int radius = OriginalRideFootprintRadiusCells;
		var entrance = FindRideEntrance( Guests.Grid, x - radius, y - radius, x + radius, y + radius );
		if ( entrance == null )
			return;
		ride.Visitors.EntranceCell = entrance.Value;
		ride.Visitors.ExitCell = entrance.Value;
		ride.Visitors.HasCells = true;
		Guests.Register( ride.Visitors );
		Park?.LinkAttraction( ride.Visitors, PrototypeRide.InfoId );
		Log.Trace( $"{ride.Name}: guests queue and exit at path cell {entrance.Value} (nearest path cell; the prototype has no catalog entrance)." );
	}

	/// <summary>Footprint of the prototype ride in original cells: a square around the clicked cell.</summary>
	public const int OriginalRideFootprintRadiusCells = 2;

	public bool TryGetPlacementPosition( Vector2 mousePosition, Vector2 viewportSize, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( OriginalPark == null )
		{
			var found = ParkPlacement.TryGetPosition( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var sandboxPosition );
			position = sandboxPosition;
			return found;
		}
		if ( !ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) )
			return false;
		if ( !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ground.X, ground.Y, out var x, out var y ) )
			return false;
		position = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, x, y );
		return true;
	}

	public OriginalPlacementResult CheckOriginalPlacement( Vector3 position )
	{
		if ( OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, position.X, position.Y, out var x, out var y ) )
			return OriginalPlacementResult.OutsideTerrain;
		const int radius = OriginalRideFootprintRadiusCells;
		return OriginalParkPlacement.Check( OriginalPark, x - radius, y - radius, x + radius, y + radius );
	}

	private void SetupHud( OnlineFolders? folders )
	{
		Hud = new();

		parkLayout = new ParkLayout( this );
		onlinePanel = new OnlinePanel( this, folders ?? OnlineFolders.FromEnvironment() );
		TextOverlay = new SandboxTextOverlay();
		// BF4 UI draws at output size after the world blit, not through the world upscaler.
		global::Global.Render.OnOverlayRender += TextOverlay.Draw;
	}

	public void Update()
	{
		Camera.Update();
		parkLayout.Draw();
		onlinePanel?.Draw();
		if ( IsPlacing && !wasMouseDown && Input.Mouse.Left && !ImGuiNET.ImGui.GetIO().WantCaptureMouse )
		{
			if ( TryGetPlacementPosition( Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ), out var position ) )
				PlaceRide( position );
		}
		wasMouseDown = Input.Mouse.Left;
		simulationClock.Advance( Time.Delta, deltaTime =>
		{
			Guests?.Tick( deltaTime );
			PlacedRide?.Simulate( deltaTime );
			Park?.FixedTick();
		} );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	public bool PlaceRide( Vector3 position )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: building is disabled.";
			return false;
		}
		if ( PlacedRide != null )
			return false;
		if ( OriginalPark != null )
		{
			var result = CheckOriginalPlacement( position );
			if ( result != OriginalPlacementResult.Allowed )
			{
				LastActionMessage = $"Cannot build here: {result}.";
				return false;
			}
		}
		else if ( !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			return false;
		PlacedRide = new PrototypeRide( position );
		RegisterRideWithGuests( PlacedRide );
		IsPlacing = false;
		Log.Trace( $"Placed prototype ride at {position}; it runs its original Totem.RSE script." );
		return true;
	}

	public void RemoveRide()
	{
		if ( IsReadOnlyVisit )
			return;
		PlacedRide?.Delete();
		if ( PlacedRide != null )
		{
			Guests?.Unregister( PlacedRide.Visitors );
			Park?.UnlinkAttraction( PlacedRide.Visitors );
		}
		PlacedRide = null;
		IsPlacing = false;
	}

	public void SaveSandbox()
	{
		RequireSandbox();
		var state = PlacedRide == null ? SandboxSave.CreateEmpty() : new SandboxSave(
			SandboxSave.CurrentVersion, PlacedRide.Position.X, PlacedRide.Position.Y, true, PlacedRide.IsOpen );
		SandboxSave.Save( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ), state );
		LastActionMessage = "Sandbox saved. This is not an original TPWS save.";
	}

	public void LoadSandbox()
	{
		RequireSandbox();
		var state = SandboxSave.Load( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ) );
		var position = new Vector3( state.RideX, state.RideY, 0 );
		if ( state.HasRide && !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			throw new InvalidDataException( "Saved ride footprint is outside the park." );
		var replacement = state.HasRide ? new PrototypeRide( position ) : null;
		if ( state.IsRunning )
			replacement!.Start();
		RemoveRide();
		PlacedRide = replacement;
		simulationClock.Reset();
		LastActionMessage = "Sandbox loaded; the ride script restarts from its first instruction.";
	}

	private void RequireSandbox()
	{
		if ( IsReadOnlyVisit )
			throw new InvalidOperationException( "Saving and loading are disabled while visiting a shared park." );
		if ( OriginalPark != null )
			throw new InvalidOperationException( "Sandbox saves are disabled for imported original levels; original saves are read-only." );
	}

	public void Dispose()
	{
		onlinePanel?.Dispose();
		global::Global.Render.OnOverlayRender -= TextOverlay.Draw;
	}

	public void Render()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Render();
	}
}
