using System.Numerics;

namespace OpenTPW;

/// <summary>
/// One of the three attribute-0x400000 dummies of a coaster station model (<c>0x436ac0</c>): its position on the park in original world units (10 per
/// cell) as <see cref="X"/>/<see cref="Z"/> along the ground and <see cref="Y"/> up. For the two end dummies <see cref="Y"/> is the height of the dummy
/// above the station's base (the height the station pylon is asked for); for the middle dummy it is the absolute world height of its knot.
/// </summary>
public readonly record struct CoasterStationPoint( float X, float Z, float Y );

/// <summary>
/// The station frame of a coaster: the End (dummy 1, half a cell before the '&lt;' column), Mid (dummy 2) and Start (dummy 3, half a cell past the
/// '&gt;' column) dummies of the station model with the station's placement applied. The track's three station nodes are built from them:
/// a StdPylon in the cell of End, a pylon-less node on Mid and a StdPylon in the cell of Start (COAST-029).
/// </summary>
// [BIN:TPW-EXE:0x436ac0 station nodes] with definition flag 0x20 (the loader always sets it) the loop runs i = 0..2 over the dummy numbered i + 1 that carries attribute 0x400000 (0x44b220), reads its world position (node instance +0x30..+0x38, placement included) and builds a StdPylon node in the cell ftol(x·0.1), ftol(z·0.1) for i = 0 and 2 (descriptor 0x12, height = dummy y − the station's base height) and a pylon-less node at the exact position for i = 1 (descriptor 0x13)
public sealed record CoasterStationLayout( CoasterStationPoint End, CoasterStationPoint Mid, CoasterStationPoint Start )
{
	/// <summary>
	/// The frame of a placed station model: the three dummies that carry attribute 0x400000 (numbered 1, 2, 3, the End, Mid and Start dummies) in the model's current pose
	/// (<paramref name="localTransforms"/>, each node relative to its parent), with the placement applied, in original world units. Null when the model lacks one of them.
	/// The parent chain and the placement are composed with <see cref="DetMatrix"/>: the frame becomes the track's station nodes and with them the coaster's canonical
	/// state, and System.Numerics' matrix product may fuse the multiply-add on one runtime and not on another (docs/DETERMINISM.md, "Browser and native").
	/// </summary>
	// [BIN:TPW-EXE:0x436b21 station dummies] the coaster station's construction looks up the dummy node numbered i + 1 with attribute 0x400000 (0x44b220, the first record in order) and reads its world position (node instance +0x30..+0x38, placement included); the end pylons are asked for the dummy's height above the station's base
	internal static CoasterStationLayout? FromModel( ModelFile model, IReadOnlyList<Matrix4x4> localTransforms, ObjectPlacement placement )
	{
		var map = AdvisorNodeMap.From( model );
		var points = new CoasterStationPoint[3];
		var toEngine = placement.DeterministicModelToEngine;
		for ( var index = 0; index < points.Length; index++ )
		{
			var node = map.FindNode( 0x400000, index + 1 );
			if ( node < 0 || node >= localTransforms.Count || node >= model.Nodes.Count )
				return null;
			var world = WorldOf( model, localTransforms, node );
			// ObjectPlacement.SwapAxes on a node matrix moves its translation (x, y, z) to (x, z, y): MD2 Y is up, the engine's Z
			var position = DetMatrix.TransformPosition( new System.Numerics.Vector3( world.M41, world.M43, world.M42 ), toEngine );
			var x = (position.X - placement.GridOrigin.X) / ObjectPlacement.ModelScale;
			var z = (position.Y - placement.GridOrigin.Y) / ObjectPlacement.ModelScale;
			var up = position.Z / ObjectPlacement.ModelScale;
			// The two end dummies give their height above the station's base; the middle one stays an absolute height (its knot is the exact dummy position).
			points[index] = new CoasterStationPoint( x, z, index == 1 ? up : up - placement.BaseHeight / ObjectPlacement.ModelScale );
		}
		return new CoasterStationLayout( points[0], points[1], points[2] );
	}

	/// <summary>A node's model-space matrix: its local matrix times its parent's, up the chain (the order of <c>ObjectAnimator</c>), with <see cref="DetMatrix.Multiply"/>.</summary>
	private static Matrix4x4 WorldOf( ModelFile model, IReadOnlyList<Matrix4x4> localTransforms, int node )
	{
		var world = localTransforms[node];
		for ( var parent = model.Nodes[node].ParentIndex; parent >= 0 && parent < localTransforms.Count; parent = model.Nodes[parent].ParentIndex )
			world = DetMatrix.Multiply( world, localTransforms[parent] );
		return world;
	}

	/// <summary>
	/// The frame of a station whose model is not at hand (a headless test, a station placed without game data): the End and Start dummies stand in the
	/// cell beyond the '&lt;' and the '&gt;' cell along the platform, the Mid dummy halfway between them, every height the coaster's initial pylon loft
	/// height (the Mid one above the ground of its cell).
	/// </summary>
	// [EXT:coaster-station-frame] the dummies' positions come from the station model; without it the frame follows the original's layout in every shipped station (End half a cell before the '<' column, Start half a cell past the '>' column, Mid between them, all three at one height); OpenTPW's own stand-in so a track can be built without game data
	public static CoasterStationLayout FromCells( (int X, int Y) depart, (int X, int Y) @return, CoasterSite site, float height )
	{
		var step = (X: Math.Sign( depart.X - @return.X ), Y: Math.Sign( depart.Y - @return.Y ));
		var head = (X: @return.X - step.X, Y: @return.Y - step.Y);
		var tail = (X: depart.X + step.X, Y: depart.Y + step.Y);
		var x = (head.X + tail.X) * 0.5f * CoasterTrack.CellUnits + CoasterTrack.HalfCell;
		var z = (head.Y + tail.Y) * 0.5f * CoasterTrack.CellUnits + CoasterTrack.HalfCell;
		var middleX = CoasterTrack.CellOf( x );
		var middleY = CoasterTrack.CellOf( z );
		return new CoasterStationLayout(
			new CoasterStationPoint( head.X * CoasterTrack.CellUnits + CoasterTrack.HalfCell, head.Y * CoasterTrack.CellUnits + CoasterTrack.HalfCell, height ),
			new CoasterStationPoint( x, z, site.GroundHeight( middleX, middleY ) + height ),
			new CoasterStationPoint( tail.X * CoasterTrack.CellUnits + CoasterTrack.HalfCell, tail.Y * CoasterTrack.CellUnits + CoasterTrack.HalfCell, height ) );
	}
}
