using OpenTPW.UI;

namespace OpenTPW;

public class Level
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
	private bool wasMouseDown;
	private readonly FixedStepClock simulationClock = new();

	public Level( string levelName, bool loadOriginalLevel = false )
	{
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;
		if ( loadOriginalLevel )
			OriginalPark = OriginalPark.Load( levelName );

		SetupEntities();
		SetupHud();
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

	private void SetupHud()
	{
		Hud = new();

		parkLayout = new ParkLayout( this );
		TextOverlay = new SandboxTextOverlay();
	}

	public void Update()
	{
		Camera.Update();
		parkLayout.Draw();
		if ( IsPlacing && !wasMouseDown && Input.Mouse.Left && !ImGuiNET.ImGui.GetIO().WantCaptureMouse )
		{
			if ( TryGetPlacementPosition( Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ), out var position ) )
				PlaceRide( position );
		}
		wasMouseDown = Input.Mouse.Left;
		simulationClock.Advance( Time.Delta, deltaTime => PlacedRide?.Simulate( deltaTime ) );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	public bool PlaceRide( Vector3 position )
	{
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
		IsPlacing = false;
		Log.Trace( $"Placed prototype ride at {position}; it runs its original Totem.RSE script." );
		return true;
	}

	public void RemoveRide()
	{
		PlacedRide?.Delete();
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
		if ( OriginalPark != null )
			throw new InvalidOperationException( "Sandbox saves are disabled for imported original levels; original saves are read-only." );
	}

	public void Render()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Render();
		TextOverlay.Draw();
	}
}
