using ImGuiNET;

namespace OpenTPW.UI;

/// <summary>
/// Developer build menu for every original object of the level's theme (ImGui, not an original screen):
/// pick a catalog entry, rotate with the blueprint keys (comma/full stop) or the Rotate button, click the
/// ground to build; remove tool; open/close all rides. The frontend slice can build the original build
/// menu from <see cref="ObjectCatalog"/> instead.
/// </summary>
public sealed class ObjectBuildPanel
{
	private static readonly ObjectCategory[] Categories = { ObjectCategory.Ride, ObjectCategory.Shop, ObjectCategory.Sideshow, ObjectCategory.Feature };
	private readonly Level level;
	private int category;

	public ObjectBuildPanel( Level level ) => this.level = level;

	public void Draw()
	{
		if ( level.IsReadOnlyVisit )
		{
			ImGui.TextWrapped( OnlineStrings.Get( OnlineLabel.ReadOnlyVisit ) );
			return;
		}
		var objects = level.Objects;
		if ( objects == null )
			return;
		// No R shortcut here: R is the original research key (ParkHud.ScreenShortcuts) and would co-fire
		// with the build rotation; rotation stays on the HUD's blueprint keys (comma/full stop) and the button.
		ImGui.Separator();
		ImGui.Text( $"Original objects: {objects.Objects.Count} placed, {objects.Catalog.Buildable.Count()} buildable ({objects.Catalog.Theme})" );
		var labels = Categories.Select( item => item.ToString() ).ToArray();
		ImGui.Combo( "Category", ref category, labels, labels.Length );
		var entries = objects.Catalog.Buildable.Where( entry => entry.Category == Categories[category] ).OrderBy( entry => entry.DisplayName ).ToArray();
		if ( ImGui.BeginListBox( "##objects", new System.Numerics.Vector2( -1, 140 ) ) )
		{
			foreach ( var entry in entries )
			{
				var selected = level.BuildEntry == entry;
				if ( ImGui.Selectable( $"{entry.DisplayName}  {entry.Shape.Width}x{entry.Shape.Height}  ${entry.BuildCost}##{entry.InfoId}", selected ) )
				{
					level.BuildEntry = selected ? null : entry;
					level.IsRemovingObjects = false;
					level.IsPlacing = false;
				}
			}
			ImGui.EndListBox();
		}
		if ( level.BuildEntry != null )
		{
			ImGui.TextWrapped( $"Building {level.BuildEntry.DisplayName} (Info.Id {level.BuildEntry.InfoId}), rotation {level.BuildRotation} deg. Click the ground; comma/full stop rotates." );
			if ( ImGui.Button( "Rotate" ) )
				level.RotateBuild();
			ImGui.SameLine();
			if ( ImGui.Button( "Stop building" ) )
				level.BuildEntry = null;
		}
		var removing = level.IsRemovingObjects;
		if ( ImGui.Checkbox( "Remove tool", ref removing ) )
		{
			level.IsRemovingObjects = removing;
			if ( removing )
				level.BuildEntry = null;
		}
		ImGui.SameLine();
		// [EXT:online-coop-commands] every object opens or closes through one command, as the buttons did
		if ( ImGui.Button( "Open all" ) )
			level.Commands.Issue( new SetAllObjectsOpenCommand( true ) );
		ImGui.SameLine();
		if ( ImGui.Button( "Close all" ) )
			level.Commands.Issue( new SetAllObjectsOpenCommand( false ) );
	}
}
