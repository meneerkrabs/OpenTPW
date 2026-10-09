using ImGuiNET;

namespace OpenTPW.UI;

public sealed class ParkLayout
{
	private readonly Level level;
	private string errorMessage = "";
	private readonly DisplaySettingsPanel displayPanel = new();

	public ParkLayout( Level level )
	{
		this.level = level;
	}

	public void Draw()
	{
		ImGui.SetNextWindowPos( new System.Numerics.Vector2( 16, 16 ), ImGuiCond.FirstUseEver );
		ImGui.SetNextWindowSize( new System.Numerics.Vector2( 320, 0 ), ImGuiCond.Always );
		var original = level.OriginalPark;
		if ( ImGui.Begin( original == null ? "OpenTPW - Jungle sandbox" : $"OpenTPW - original {original.LevelName} level (read-only import)", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize ) )
		{
			ImGui.TextWrapped( "Original assets. The Totem runs its original RSE script; its carriage motion is a procedural stand-in, and visitors, sounds and effects are not simulated." );
			if ( original != null )
				DrawOriginalImport( original );
			ImGui.Separator();
			if ( level.IsReadOnlyVisit )
				ImGui.TextWrapped( OnlineStrings.Get( OnlineLabel.ReadOnlyVisit ) );
			else if ( level.PlacedRide == null )
			{
				if ( ImGui.Button( level.IsPlacing ? "Cancel placement" : "Place Totem tower" ) )
					level.IsPlacing = !level.IsPlacing;
				if ( level.IsPlacing )
					ImGui.TextWrapped( "Click the ground inside the park. Leave enough room around the ride." );
			}
			else
			{
				var script = level.PlacedRide.Script;
				ImGui.Text( level.PlacedRide.IsOpen ? "Ride: open" : "Ride: closed" );
				ImGui.Text( $"Script: {script.ScriptName}, {script.State}, VAR_RUNNING={script[RideVariables.VAR_RUNNING]}" );
				if ( ImGui.Button( level.PlacedRide.IsOpen ? "Close ride" : "Open ride" ) )
				{
					if ( level.PlacedRide.IsOpen )
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
				if ( level.IsReadOnlyVisit )
					ImGui.TextWrapped( "Sandbox save/load is disabled while visiting." );
				else if ( original != null )
					ImGui.TextWrapped( "Sandbox save/load is disabled here; original saves are never written." );
				else if ( ImGui.Button( "Save sandbox" ) )
				{
					level.SaveSandbox();
					errorMessage = "";
				}
				if ( original == null && !level.IsReadOnlyVisit )
					ImGui.SameLine();
				if ( original == null && !level.IsReadOnlyVisit && ImGui.Button( "Load sandbox" ) )
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
			displayPanel.Draw();
			ImGui.TextWrapped( "WASD: pan | right drag: pan | wheel: zoom | Q/E: rotate | F1: editor" );
		}
		ImGui.End();
	}

	private static void DrawOriginalImport( OriginalPark park )
	{
		ImGui.Separator();
		if ( park.Save == null )
		{
			ImGui.TextWrapped( "No original Easymode save for this level; terrain and MAP rules only." );
			return;
		}
		ImGui.TextWrapped( $"Easymode.TPWI: {park.Save.PathCells.Count} path cells (sand tiles), {park.Save.PlacedObjects.Count} placed objects (orange footprints)." );
		foreach ( var item in park.Save.PlacedObjects )
			ImGui.BulletText( $"{park.DescribeObject( item.Record.InfoId )} at ({item.Record.X}, {item.Record.Y}), {item.Record.Width}x{item.Record.Height}, {item.Record.Rotation} deg" );
		foreach ( var item in park.Save.FixedItems )
			ImGui.BulletText( $"Fixed item: {park.DescribeObject( item.InfoId )} ({item.X}, {item.Y})" );
		ImGui.TextWrapped( "Not imported: money, time, guests, staff, ride state, object models, path styles." );
	}
}
