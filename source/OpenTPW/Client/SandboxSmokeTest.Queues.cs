using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>Queue-tool check of the native smoke test: a HasQueue ride bought and placed through the HUD gets its queue laid by park clicks.</summary>
internal sealed partial class SandboxSmokeTest
{
	private const int SmokeQueueLength = 2;
	private bool queueToolVerified;

	/// <summary>
	/// Original levels with the HUD, as a player would: choose the cheapest available HasQueue ride, hover a site (the
	/// footprint shows blue blueprint markers), click it through <see cref="Level.UpdateWorldInput"/>, and expect the queue
	/// tool at the entrance's outside cell; then lay a short queue straight out of the entrance with two HUD clicks.
	/// The ride and its queue are removed again so later checks see the imported park.
	/// </summary>
	private void VerifyHudQueueTool()
	{
		queueToolVerified = true;
		if ( hud == null || hudContext == null || level.Paths == null || level.Guests == null || level.Park == null )
			return;
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.25f && point.X < canvas.Width * 0.75f && point.Y > canvas.Height * 0.15f && point.Y < canvas.Height * 0.65f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;

		var item = hud.Catalog.GetItems( BuildCategory.Rides )
			.Where( candidate => candidate.Entry is { HasQueue: true } && hud.Status.IsAvailable( candidate ) && (hud.Status.PriceOf( candidate ) ?? candidate.Cost) <= hud.Status.Money )
			.OrderBy( candidate => hud.Status.PriceOf( candidate ) ?? candidate.Cost ).FirstOrDefault();
		Require( item?.Entry != null, "find an available, affordable HasQueue ride in the build menu" );
		var entry = item!.Entry!;
		var grid = level.Guests.Grid;

		// A site whose footprint is allowed and whose entrance looks onto a free straight line for the queue.
		(int X, int Y) site = (-1, -1);
		ObjectAccessPoint entrance = default;
		for ( var index = 0; index < grid.CountX * grid.CountY && site.X < 0; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			if ( !Clickable( cell, out _ ) || level.CheckFreeObject( entry, cell.X, cell.Y, 0 ) != OriginalPlacementResult.Allowed )
				continue;
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			var door = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, 0 ).FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
			var (dx, dy) = (door.OutsideX - door.X, door.OutsideY - door.Y);
			if ( door.Kind != ObjectCellKind.Entrance || Math.Abs( dx ) + Math.Abs( dy ) != 1 )
				continue;
			var line = Enumerable.Range( 0, SmokeQueueLength + 1 ).Select( step => (door.OutsideX + dx * step, door.OutsideY + dy * step) ).ToArray();
			if ( line.All( queueCell => level.Paths.CheckCell( queueCell.Item1, queueCell.Item2 ) == CellBuildResult.Ok && Clickable( queueCell, out _ ) ) )
				(site, entrance) = (cell, door);
		}
		Require( site.X >= 0, $"find an on-screen site for {entry.DisplayName} with room for its queue" );
		Clickable( site, out var sitePoint );

		hud.BeginPlacing( item );
		Require( level.BuildEntry == entry, "choosing a ride in the build menu arms its placement" );
		hud.Update( hudContext, UiInput.Idle( sitePoint ) );
		var footprint = ObjectFootprint.GetCells( entry.Shape, Level.GetCentredAnchor( entry, site.X, site.Y, 0 ).X, Level.GetCentredAnchor( entry, site.X, site.Y, 0 ).Y, 0 ).Count();
		Require( level.Blueprint is { } placing && placing.Cells.Count( cell => cell.Marker is BlueprintMarker.Allowed or BlueprintMarker.Front ) == footprint && placing.Cells.All( cell => cell.Marker != BlueprintMarker.Refused ),
			"hovering an allowed site shows the footprint as blue blueprint markers" );

		// The park click: the HUD leaves it to Level while an object is being placed.
		var captured = hud.Update( hudContext, new UiInput( sitePoint, true, true, false, false, UiKeys.None ) );
		level.UpdateWorldInput( false, captured, sitePoint, viewport );
		level.UpdateWorldInput( true, captured, sitePoint, viewport );
		hud.Update( hudContext, new UiInput( sitePoint, false, false, true, false, UiKeys.None ) );
		level.UpdateWorldInput( false, false, sitePoint, viewport );
		var placed = level.Objects.FindAt( entrance.X, entrance.Y );
		Require( placed?.Entry == entry, $"a park click places {entry.DisplayName}" );
		Require( level.CellTool.IsActive && level.CellTool.Mode == CellToolMode.Queue && level.CellTool.Start == (entrance.OutsideX, entrance.OutsideY),
			$"placing {entry.DisplayName} starts the queue tool at its entrance (tool {level.CellTool.Mode}, start {level.CellTool.Start}, entrance outside {(entrance.OutsideX, entrance.OutsideY)})" );

		var (stepX, stepY) = (entrance.OutsideX - entrance.X, entrance.OutsideY - entrance.Y);
		var end = (X: entrance.OutsideX + stepX * SmokeQueueLength, Y: entrance.OutsideY + stepY * SmokeQueueLength);
		Clickable( end, out var endPoint );
		hud.Update( hudContext, UiInput.Idle( endPoint ) );
		Require( level.CellTool.Ghost is { Completed: true } ghost && ghost.Line.Count == SmokeQueueLength + 1
			&& level.Blueprint!.Cells.Count == SmokeQueueLength + 1 && level.Blueprint.Cells.All( cell => cell.Marker == BlueprintMarker.Allowed ),
			"the queue tool previews the line out of the entrance as blue blueprint markers" );
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		var queued = Enumerable.Range( 0, SmokeQueueLength + 1 ).All( step => grid.IsQueue( entrance.OutsideX + stepX * step, entrance.OutsideY + stepY * step ) );
		Require( queued && placed!.Visitors.QueueCells.Count >= SmokeQueueLength + 1, "a HUD click lays the queue cells and the ride owns them" );
		// Clicking the end again ends the tool.
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		Require( !level.CellTool.IsActive && level.Blueprint!.Cells.Count == 0, "clicking the queue's end again ends the tool and clears the markers" );
		Log.Trace( $"HUD queue tool: placed {entry.DisplayName} at {site} and laid {SmokeQueueLength + 1} queue cells from {(entrance.OutsideX, entrance.OutsideY)} to {end}." );

		// The queue front the placement laid is NoModify, and the remove tool takes it with the queue behind it ([EXT:removable-access-cells]); the ride's entrance then resolves to the nearest path again.
		var front = (X: entrance.OutsideX, Y: entrance.OutsideY);
		var bridge = placed.Visitors;
		Require( grid.Cells.FlagsAt( front.X, front.Y ).HasFlag( ParkCellFlags.NoModify ), "the queue front the placement laid is NoModify" );
		Require( level.RemoveAt( front.X, front.Y ) && !grid.IsQueue( front.X, front.Y ) && !bridge.QueueCells.Any( cell => grid.IsQueue( cell.X, cell.Y ) ), "the remove tool takes the laid queue front and the queue behind it" );
		Require( grid.Cells.FlagsAt( front.X, front.Y ) == ParkCellFlags.None || !grid.Cells.FlagsAt( front.X, front.Y ).HasFlag( ParkCellFlags.NoModify ), "the removed front is no NoModify cell any more" );
		Require( grid.IsWalkable( bridge.EntranceCell.X, bridge.EntranceCell.Y ), $"the ride's entrance falls back to a walkable path cell, {bridge.EntranceCell}" );
		Require( level.BuildQueueCell( bridge, front.X, front.Y ) == QueueBuildResult.Ok && grid.IsQueue( front.X, front.Y ) && bridge.QueueCells.Contains( front ), "the queue can be laid on the removed front again" );
		// A free-standing queue cell (touching neither the front nor the back) is the ride's by its owner word and goes with the remove tool.
		(int X, int Y)? lone = null;
		for ( var index = 0; index < grid.CountX * grid.CountY && lone == null; index++ )
		{
			var cell = (X: index % grid.CountX, Y: index / grid.CountX);
			if ( Math.Abs( cell.X - front.X ) + Math.Abs( cell.Y - front.Y ) is >= 3 and <= 8 && level.Objects.FindAt( cell.X, cell.Y ) == null && level.CheckQueueCell( bridge, cell.X, cell.Y, Array.Empty<(int X, int Y)>() ) == QueueBuildResult.Ok )
				lone = cell;
		}
		Require( lone is { } free && level.BuildQueueCell( bridge, free.X, free.Y ) == QueueBuildResult.Ok && grid.IsQueue( free.X, free.Y ) && !bridge.QueueCells.Contains( free ),
			"a free-standing queue cell can be laid" );
		Require( level.RemoveAt( lone!.Value.X, lone.Value.Y ) && !grid.IsQueue( lone.Value.X, lone.Value.Y ), "the remove tool takes the free-standing queue cell" );

		for ( var step = SmokeQueueLength; step >= 0; step-- )
			level.RemoveAt( entrance.OutsideX + stepX * step, entrance.OutsideY + stepY * step );
		level.RemoveObjectAt( entrance.X, entrance.Y );
	}
}
