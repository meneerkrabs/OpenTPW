namespace OpenTPW;

/// <summary>Which drag a press in the coaster tool's panel starts (<see cref="BeginCoasterNodeEditCommand"/>): the selected button's, and for the tilt button on a loop pylon the loop rotation's.</summary>
public enum CoasterDrag : byte
{
	/// <summary>The move button: the pylon follows the pointer's cell; the release settles the money when it stands on another cell.</summary>
	Move,
	/// <summary>The height button: the loft parameter; the release settles the money.</summary>
	Height,
	/// <summary>The rotate button: the yaw.</summary>
	Rotate,
	/// <summary>The tilt button: the bank and the incline.</summary>
	Tilt,
	/// <summary>The tilt button on a loop pylon: the loop rotation.</summary>
	LoopTilt
}

/// <summary>
/// The values a drag may set, as the panel's press bounds them (<see cref="CoasterBuilder.DragRange"/>): the first value's range (the loft, the yaw, the bank or the loop rotation) and the second's (the
/// incline of a tilt). A tilt drags only a value whose range is open (its ends differ); the other ends of a pylon without that control are its value as it was.
/// </summary>
public readonly record struct CoasterDragRange( float Low, float High, float LowSecond, float HighSecond )
{
	/// <summary>Whether the panel's clamp can give <paramref name="value"/> for the first value: an end of the range, or a value between them (a stack whose top stands above the coaster's highest
	/// pylon leaves its lower piece a range whose ends are the wrong way round, and the clamp then gives one of the ends).</summary>
	public bool Holds( float value ) => value == Low || value == High || (value >= Low && value <= High);

	/// <summary>Whether the panel's clamp can give <paramref name="value"/> for the second value.</summary>
	public bool HoldsSecond( float value ) => value == LowSecond || value == HighSecond || (value >= LowSecond && value <= HighSecond);
}

/// <summary>
/// [EXT:online-coop-commands] The pylon each player is editing in a drag of the coaster tool's panel (<see cref="CoasterToolPanel"/>): the station's attraction id and builder, the edit the press began
/// (the node as it was and the cost the release settles against, <see cref="CoasterBuilder.NodeEdit"/>), the drag and the range its press allows. The level's state, not the panel's, so a drag's
/// commands do the same on every peer and in a replay (<see cref="CoasterPanelCommand"/>).
/// </summary>
public sealed class CoasterEdits
{
	/// <summary>One player's edit.</summary>
	public sealed record Held( int Station, CoasterBuilder Builder, CoasterBuilder.NodeEdit Edit, CoasterDrag Drag, CoasterDragRange Range );

	private readonly Dictionary<int, Held> edits = new();

	/// <summary>The edit <paramref name="player"/> holds, or null.</summary>
	public Held? Of( int player ) => edits.GetValueOrDefault( player );

	internal void Begin( int player, Held held ) => edits[player] = held;

	/// <summary>
	/// Ends <paramref name="player"/>'s edit as its release does: a height drag settles its money, a move settles it when the pylon stands on another cell than its press found it on (else the track is
	/// only fitted again), any other drag only fits the track again. An edit whose pylon is gone just ends.
	/// </summary>
	internal void End( int player )
	{
		if ( edits.Remove( player, out var held ) )
			Settle( held, putBack: false );
	}

	/// <summary>Releases <paramref name="player"/>'s edit where it stands, as the panel's release over nothing does: a moved pylon goes back to its cell first.</summary>
	internal void Release( int player )
	{
		if ( edits.Remove( player, out var held ) )
			Settle( held, putBack: true );
	}

	/// <summary>
	/// Releases every edit on the coaster station <paramref name="station"/>, lowest player first: before a command adds or removes pylons, so no edit settles against a pylon list that changed under it,
	/// and when the coaster leaves the tool.
	/// </summary>
	internal void ReleaseAll( int station )
	{
		foreach ( var player in edits.Where( pair => pair.Value.Station == station ).Select( pair => pair.Key ).Order().ToArray() )
			Release( player );
	}

	/// <summary>Whether a player's drag runs on the coaster station <paramref name="station"/>, another than <paramref name="except"/>'s when it is given.</summary>
	internal bool Runs( int station, int? except = null ) => edits.Any( pair => pair.Key != except && pair.Value.Station == station );

	/// <summary>Drops the edits on the coaster station <paramref name="station"/> without settling them (the station went, and its track with it, without a refund).</summary>
	internal void Drop( int station )
	{
		foreach ( var player in edits.Where( pair => pair.Value.Station == station ).Select( pair => pair.Key ).ToArray() )
			edits.Remove( player );
	}

	/// <summary>Drops every edit (a load replaced the park, whose coasters they held).</summary>
	public void Clear() => edits.Clear();

	private static void Settle( Held held, bool putBack )
	{
		var builder = held.Builder;
		var edit = held.Edit;
		if ( !builder.CanEdit( edit.Index ) )
			return;
		switch ( held.Drag )
		{
			case CoasterDrag.Height:
				builder.EndEdit( edit );
				return;
			case CoasterDrag.Move:
				if ( putBack && Moved( builder, edit ) )
					builder.EditMove( edit, edit.Start.X, edit.Start.Y );
				if ( Moved( builder, edit ) )
					builder.EndEdit( edit );
				else
					builder.FinishEdit();
				return;
			default:
				builder.FinishEdit();
				return;
		}
	}

	private static bool Moved( CoasterBuilder builder, CoasterBuilder.NodeEdit edit )
	{
		var node = builder.Track.Nodes[edit.Index];
		return (node.X, node.Y) != (edit.Start.X, edit.Start.Y);
	}
}
