namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The pylon each player is editing in a drag of the coaster tool's panel (<see cref="CoasterToolPanel"/>): the station's attraction id and the edit the press began, with
/// the node as it was and the cost the release settles against (<see cref="CoasterBuilder.NodeEdit"/>). The level's state, not the panel's, so a drag's commands do the same on every peer and in a
/// replay (<see cref="CoasterPanelCommand"/>).
/// </summary>
public sealed class CoasterEdits
{
	private readonly Dictionary<int, (int Station, CoasterBuilder.NodeEdit Edit)> edits = new();

	/// <summary>The edit <paramref name="player"/> holds, with its station, or null.</summary>
	public (int Station, CoasterBuilder.NodeEdit Edit)? Of( int player ) => edits.TryGetValue( player, out var edit ) ? edit : null;

	internal void Begin( int player, int station, CoasterBuilder.NodeEdit edit ) => edits[player] = (station, edit);

	internal void End( int player ) => edits.Remove( player );

	/// <summary>Drops every edit (a load replaced the park, whose coasters they held).</summary>
	public void Clear() => edits.Clear();
}
