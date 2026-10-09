using ImGuiNET;

namespace OpenTPW.UI;

public sealed class ParkLayout
{
	private readonly Level level;
	private string errorMessage = "";

	public ParkLayout( Level level )
	{
		this.level = level;
	}

	public void Draw()
	{
		ImGui.SetNextWindowPos( new System.Numerics.Vector2( 16, 16 ), ImGuiCond.FirstUseEver );
		ImGui.SetNextWindowSize( new System.Numerics.Vector2( 320, 0 ), ImGuiCond.Always );
		if ( ImGui.Begin( "OpenTPW - Jungle sandbox", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize ) )
		{
			ImGui.TextWrapped( "Original assets; prototype motion. Full simulation and original ride scripts are not implemented yet." );
			ImGui.Separator();
			if ( level.PlacedRide == null )
			{
				if ( ImGui.Button( level.IsPlacing ? "Cancel placement" : "Place Totem tower" ) )
					level.IsPlacing = !level.IsPlacing;
				if ( level.IsPlacing )
					ImGui.TextWrapped( "Click the ground inside the park. Leave enough room around the ride." );
			}
			else
			{
				ImGui.Text( level.PlacedRide.IsRunning ? "Ride: running" : "Ride: stopped" );
				if ( ImGui.Button( level.PlacedRide.IsRunning ? "Stop ride" : "Start ride" ) )
				{
					if ( level.PlacedRide.IsRunning )
						level.PlacedRide.Stop();
					else
						level.PlacedRide.Start();
				}
				ImGui.SameLine();
				if ( ImGui.Button( "Remove ride" ) )
					level.RemoveRide();
			}
			ImGui.Separator();
			try
			{
				if ( ImGui.Button( "Save sandbox" ) )
				{
					level.SaveSandbox();
					errorMessage = "";
				}
				ImGui.SameLine();
				if ( ImGui.Button( "Load sandbox" ) )
				{
					level.LoadSandbox();
					errorMessage = "";
				}
			}
			catch ( Exception exception )
			{
				errorMessage = exception.Message;
				Log.Warning( $"Sandbox save/load failed: {exception.Message}" );
			}
			if ( errorMessage.Length > 0 )
				ImGui.TextWrapped( errorMessage );
			else if ( level.LastActionMessage.Length > 0 )
				ImGui.TextWrapped( level.LastActionMessage );
			ImGui.Separator();
			ImGui.TextWrapped( "WASD: pan | right drag: pan | wheel: zoom | Q/E: rotate | F1: editor" );
		}
		ImGui.End();
	}
}
