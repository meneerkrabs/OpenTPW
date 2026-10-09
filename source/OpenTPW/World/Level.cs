using OpenTPW.UI;

namespace OpenTPW;

public class Level
{
	internal static Level Current { get; set; }

	public RootPanel Hud { get; set; }
	public Sun SunLight { get; set; }

	public SettingsFile Global { get; private init; }
	private ParkLayout parkLayout = null!;
	public PrototypeRide? PlacedRide { get; private set; }
	public bool IsPlacing { get; set; }
	public string LastActionMessage { get; private set; } = "";
	private bool wasMouseDown;
	private readonly FixedStepClock simulationClock = new();

	public Level( string levelName )
	{
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;

		SetupEntities();
		SetupHud();
	}

	private void SetupEntities()
	{
		SunLight = new Sun() { Position = new( 0, 100, 100 ) };

		_ = new Terrain();
		Camera.SetCameraMode<ParkCameraMode>();
	}

	private void SetupHud()
	{
		Hud = new();

		parkLayout = new ParkLayout( this );
	}

	public void Update()
	{
		Camera.Update();
		parkLayout.Draw();
		if ( IsPlacing && !wasMouseDown && Input.Mouse.Left && !ImGuiNET.ImGui.GetIO().WantCaptureMouse )
		{
			if ( ParkPlacement.TryGetPosition( Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ), Camera.ViewMatrix, Camera.ProjMatrix, out var position ) )
				PlaceRide( position );
		}
		wasMouseDown = Input.Mouse.Left;
		simulationClock.Advance( Time.Delta, deltaTime => PlacedRide?.Simulate( deltaTime ) );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	public bool PlaceRide( Vector3 position )
	{
		if ( PlacedRide != null || !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
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
		var state = PlacedRide == null ? SandboxSave.CreateEmpty() : new SandboxSave(
			SandboxSave.CurrentVersion, PlacedRide.Position.X, PlacedRide.Position.Y, true, PlacedRide.IsOpen );
		SandboxSave.Save( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ), state );
		LastActionMessage = "Sandbox saved. This is not an original TPWS save.";
	}

	public void LoadSandbox()
	{
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

	public void Render()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Render();
	}
}
