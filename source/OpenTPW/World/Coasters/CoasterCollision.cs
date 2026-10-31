using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The track's self-intersection pass (tp.exe <c>0x439ab0</c> → <c>0x439c40</c> → <c>0x43a2b0</c>, COAST-004): every link
/// (a node and its successor) is tested against every link that shares no node with it, by posing the track's collision
/// points at the samples of the two links and testing the two prisms. Every crossing is recorded in the lists of the two
/// links' first nodes and counted at controller <c>+0x140</c>; the guest predicate <c>0x441970</c> refuses a coaster while
/// that count is nonzero. There is no terrain or scenery test in this pass.
/// </summary>
public static class CoasterCollision
{
	/// <summary>The collision points of the definition's defaults, <c>0x74caa0</c>: the profile of the track around its centre line.</summary>
	// [BIN:TPW-EXE:0x74cac8 collision points] the defaults the definition takes (0x42ff03..0x42ff14: pointer 0x74caa0, count 5) are (4, 8), (4, 0), (0, -3), (-4, -1), (-4, 8); no PC coaster.sam names asTrackCollisionPoints, so these are the points of every PC coaster
	public static readonly IReadOnlyList<(float X, float Y)> DefaultPoints = new (float X, float Y)[] { (4, 8), (4, 0), (0, -3), (-4, -1), (-4, 8) };

	/// <summary>The most crossings one node's list holds (<c>0x439fda</c>); more set the node's overflow flag instead.</summary>
	public const int ListCapacity = 8;
	/// <summary>The most passes one run repeats while nodes stay marked; the original runs one pass per frame without a bound, a track whose overflowed lists keep re-marking each other (nodes with more than 8 crossings; measured: a 60-node random tangle reaches it) stops here. This bound is an OpenTPW safety measure, not a tp.exe value.</summary>
	public const int MaxPasses = 16;
	/// <summary>The angle sum a point inside a face must reach, 2π − 0.001 (<c>0x6fdf7c</c>).</summary>
	public const float AngleSumThreshold = 6.2821855f;

	/// <summary>One node's role in the pass: its sample index, the radius its pylon control gives (null without a control) and whether it ends a cut chain (node flag 0x10000).</summary>
	public readonly record struct Node( int SampleIndex, float? Radius, bool IsPylonless, bool CutEnd = false, int Column = -1 );

	/// <summary>The outcome of a pass: the controller count and each node's list of crossing partners.</summary>
	public sealed record Result( int Count, IReadOnlyList<IReadOnlyList<int>> Lists, IReadOnlyList<bool> Overflow );

	private sealed class Box
	{
		public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
	}

	private sealed class Segment
	{
		public int Index;
		public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
	}

	/// <summary>
	/// Runs the pass over a whole track as if every node had just been built (every node marked 0x200000): for each node in
	/// chain order its old entries go (<c>0x43ad50</c>), then its link is tested against every non-adjacent link after and
	/// before it. The last node starts no link.
	/// </summary>
	// [BIN:TPW-EXE:0x439ab0 intersection pass] a node marked 0x200000 (and not 0x10000) first drops its entries (0x43ad50), clears 0x600000, then tests its link against the links starting two nodes after it, and then those starting two nodes before it, with 0x439c40; the pass runs with the rounding mode set to truncate (0x439ac4), so every sine-table index below truncates
	// [APPROX:COAST-004] the x87 arithmetic is built and oracle-compared and the descriptor skips are ported; the lists (+0xb4, eight slots up to +0xd4) are read only by the drop 0x43ad50 and the save writer 0x437300 as far as traced (the other +0xb4 loads 0x43b159, 0x43b3e5 and 0x43b63f belong to a ring buffer of another object with its capacity at +0xb0 and cursors at +0xc4/+0xc8, 0x44ace9 is a stack slot; the loads at 0x430867, 0x43420a, 0x443087 and 0x44309c read the +0xb4/+0xb8 words of a pylon control or cell descriptor, a position pair, not the node's list); the count (+0xb0) feeds the section builder 0x43f4e0 (material variant 2 for a crossing link); a chain cut open by the demolish button is not sampled (CoasterRide.ChainCrossings)
	public static Result Run( IReadOnlyList<Node> nodes, IReadOnlyList<CoasterSample> samples, IReadOnlyList<(float X, float Y)>? points = null, int maxPasses = MaxPasses )
	{
		ArgumentNullException.ThrowIfNull( nodes );
		ArgumentNullException.ThrowIfNull( samples );
		points ??= DefaultPoints;
		var count = nodes.Count;
		var lists = new List<int>[count];
		var overflow = new bool[count];
		var dirty = new bool[count];
		for ( var node = 0; node < count; node++ )
			lists[node] = new List<int>();
		var total = 0;
		if ( count < 3 || samples.Count == 0 || points.Count == 0 )
			return new Result( 0, lists, overflow );

		// [BIN:TPW-EXE:0x439920 link boxes] every node with a successor gets the box of the samples from its own index to its successor's, grown by the radius of its pylon control (its successor's without one, 0 without either)
		var boxes = new Box?[count];
		for ( var node = 0; node + 1 < count; node++ )
		{
			var radius = nodes[node].Radius ?? nodes[node + 1].Radius ?? 0f;
			var first = nodes[node].SampleIndex;
			var last = Math.Min( nodes[node + 1].SampleIndex, samples.Count - 1 );
			var box = new Box { MinX = samples[first].Position.X, MinY = samples[first].Position.Y, MinZ = samples[first].Position.Z };
			(box.MaxX, box.MaxY, box.MaxZ) = (box.MinX, box.MinY, box.MinZ);
			for ( var sample = first + 1; sample <= last; sample++ )
			{
				var position = samples[sample].Position;
				box.MinX = Math.Min( box.MinX, position.X );
				box.MaxX = Math.Max( box.MaxX, position.X );
				box.MinY = Math.Min( box.MinY, position.Y );
				box.MaxY = Math.Max( box.MaxY, position.Y );
				box.MinZ = Math.Min( box.MinZ, position.Z );
				box.MaxZ = Math.Max( box.MaxZ, position.Z );
			}
			(box.MinX, box.MinY, box.MinZ) = (box.MinX - radius, box.MinY - radius, box.MinZ - radius);
			(box.MaxX, box.MaxY, box.MaxZ) = (box.MaxX + radius, box.MaxY + radius, box.MaxZ + radius);
			boxes[node] = box;
		}

		// [BIN:TPW-EXE:0x43ad50 drop entries] a node without overflow removes itself from the list of every partner in its own list; a node with overflow from every node's list; the count falls by every entry removed and by the node's own entries
		void Drop( int node )
		{
			var holders = overflow[node] ? Enumerable.Range( 0, count ) : lists[node].ToList();
			foreach ( var holder in holders )
			{
				var removed = lists[holder].RemoveAll( entry => entry == node );
				total -= removed;
				// [BIN:TPW-EXE:0x43ad50 re-test mark] a holder that lost entries and has its overflow flag (0x400000) is marked 0x200000, so the next visit of the pass re-tests it (a later node in this pass, an earlier one in the next pass)
				if ( removed != 0 && overflow[holder] )
					dirty[holder] = true;
			}
			total -= lists[node].Count;
			lists[node].Clear();
			overflow[node] = false;
		}

		void Record( int first, int second )
		{
			if ( lists[first].Count < ListCapacity )
			{
				lists[first].Add( second );
				total++;
			}
			else
				overflow[first] = true;
			if ( lists[second].Count < ListCapacity )
			{
				lists[second].Add( first );
				total++;
			}
			else
				overflow[second] = true;
		}

		bool Test( int a, int c )
		{
			var b = a + 1;
			var d = c + 1;
			// [BIN:TPW-EXE:0x439c5f descriptor skips] a link whose end node (B) shares its cell descriptor (node +0x100) with the other link's first node (C), or whose first node (A) shares it with the other link's end node (D), is not tested (0x439c5f..0x439c81); the descriptor is the cell's pylon column, whose node list holds every stacked pylon (0x433d71..0x433d88: +0x34 array, +0x30 height), so nodes of one cell share it and a pylon-less node has its own
			if ( (nodes[b].Column >= 0 && nodes[b].Column == nodes[c].Column) || (nodes[a].Column >= 0 && nodes[a].Column == nodes[d].Column) )
				return false;
			// [BIN:TPW-EXE:0x439c40 cut end] the link that starts on a node with 0x10000 (the end of a cut chain) is never tested (0x439c40 tests param_5's flag 0x10000)
			if ( nodes[c].CutEnd )
				return false;
			// [BIN:TPW-EXE:0x439c87 pylon-less skip] a link that ends on a pylon-less node (the end node) is not tested against the link that leaves the first node, nor the reverse (the end node stands where the first node does)
			if ( (nodes[b].IsPylonless && c == 0) || (a == 0 && nodes[d].IsPylonless) )
				return false;
			return LinksIntersect( boxes[a]!, boxes[c]!, nodes[a], nodes[b], nodes[c], nodes[d], samples, points );
		}

		// [BIN:TPW-EXE:0x439ab0 dirty marks] the pass visits the chain once per rebuild and only a node marked 0x200000 is re-tested; the original runs it again on every frame of the edit, so a node an overflowed holder re-marked is reached on the next pass; the passes repeat here until no mark is left
		for ( var node = 0; node + 1 < count; node++ )
			dirty[node] = true;
		for ( var pass = 0; pass < maxPasses; pass++ )
		{
			var any = false;
			for ( var node = 0; node + 1 < count; node++ )
			{
				if ( !dirty[node] )
					continue;
				any = true;
				Drop( node );
				dirty[node] = false;
				// [BIN:TPW-EXE:0x439ab0 cut end] a marked node with 0x10000 only drops its entries (0x439b72)
				if ( nodes[node].CutEnd )
					continue;
				for ( var other = node + 2; other + 1 < count; other++ )
				{
					if ( Test( node, other ) )
						Record( node, other );
				}
				for ( var other = node - 2; other >= 0; other-- )
				{
					if ( Test( node, other ) )
						Record( node, other );
				}
			}
			if ( !any )
				break;
		}
		return new Result( total, lists, overflow );
	}

	/// <summary>The link test of <c>0x439c40</c>: the boxes overlap, the samples inside the shared box become segments, and two segments whose prisms cross are a hit.</summary>
	private static bool LinksIntersect( Box boxA, Box boxC, Node a, Node b, Node c, Node d, IReadOnlyList<CoasterSample> samples, IReadOnlyList<(float X, float Y)> points )
	{
		// [BIN:TPW-EXE:0x439cc6 box overlap] all six strict comparisons A.min < C.max and C.min < A.max
		if ( !(boxA.MinX < boxC.MaxX && boxC.MinX < boxA.MaxX && boxA.MinY < boxC.MaxY && boxC.MinY < boxA.MaxY && boxA.MinZ < boxC.MaxZ && boxC.MinZ < boxA.MaxZ) )
			return false;
		var shared = new Box
		{
			MinX = Math.Max( boxA.MinX, boxC.MinX ), MinY = Math.Max( boxA.MinY, boxC.MinY ), MinZ = Math.Max( boxA.MinZ, boxC.MinZ ),
			MaxX = Math.Min( boxA.MaxX, boxC.MaxX ), MaxY = Math.Min( boxA.MaxY, boxC.MaxY ), MaxZ = Math.Min( boxA.MaxZ, boxC.MaxZ )
		};
		var first = Segments( a, b, shared, samples );
		var second = Segments( c, d, shared, samples );
		foreach ( var one in first )
		{
			foreach ( var other in second )
			{
				// [BIN:TPW-EXE:0x439f35 segment overlap] the segment boxes overlap on all three axes (strict)
				if ( !(one.MinX < other.MaxX && other.MinX < one.MaxX && one.MinY < other.MaxY && other.MinY < one.MaxY && one.MinZ < other.MaxZ && other.MinZ < one.MaxZ) )
					continue;
				if ( PrismsIntersect( samples[one.Index], samples[one.Index + 1], samples[other.Index], samples[other.Index + 1], points ) )
					return true;
			}
		}
		return false;
	}

	/// <summary>
	/// The segments of a link (<c>0x43a060</c>): each pair of consecutive samples from the first node's index up to the second's, kept when either
	/// sample lies in the shared box grown by the link's radius; its own box is the pair's box grown by the same radius.
	/// </summary>
	private static List<Segment> Segments( Node first, Node second, Box shared, IReadOnlyList<CoasterSample> samples )
	{
		var radius = first.Radius ?? second.Radius ?? 0f;
		// [BIN:TPW-EXE:0x0043a0c0 truncated boxes] the shared box grown by the radius (0x43a0c0..0x43a105) and every segment's box (0x43a1f0..0x43a275) are single steps rounded toward zero (the pass's rounding control, Trunc24): min − radius and max + radius
		var (minX, minY, minZ) = (Trunc24.Sub( shared.MinX, radius ), Trunc24.Sub( shared.MinY, radius ), Trunc24.Sub( shared.MinZ, radius ));
		var (maxX, maxY, maxZ) = (Trunc24.Add( shared.MaxX, radius ), Trunc24.Add( shared.MaxY, radius ), Trunc24.Add( shared.MaxZ, radius ));
		bool Inside( SVector3 p ) => p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY && p.Z >= minZ && p.Z <= maxZ;
		var segments = new List<Segment>();
		var last = Math.Min( second.SampleIndex, samples.Count - 1 );
		for ( var index = first.SampleIndex; index < last; index++ )
		{
			var p = samples[index].Position;
			var q = samples[index + 1].Position;
			if ( !Inside( p ) && !Inside( q ) )
				continue;
			segments.Add( new Segment
			{
				Index = index,
				MinX = Trunc24.Sub( Math.Min( p.X, q.X ), radius ), MinY = Trunc24.Sub( Math.Min( p.Y, q.Y ), radius ), MinZ = Trunc24.Sub( Math.Min( p.Z, q.Z ), radius ),
				MaxX = Trunc24.Add( Math.Max( p.X, q.X ), radius ), MaxY = Trunc24.Add( Math.Max( p.Y, q.Y ), radius ), MaxZ = Trunc24.Add( Math.Max( p.Z, q.Z ), radius )
			} );
		}
		return segments;
	}

	/// <summary>
	/// The points of a profile posed at two samples (<c>0x43a2b0</c>): the point (x, y, 0) turned about z by the roll, about x by the pitch and
	/// about y by the yaw with the sine table, then moved to the sample; every step one single rounded toward zero (<see cref="Trunc24"/>). The first sample's points come
	/// first, then the second's.
	/// </summary>
	// [BIN:TPW-EXE:0x43a33b pose points] roll: x' = x·cos − sin·y, y' = cos·y + x·sin; pitch: y' = y·cos − sin·z, z' = cos·z + y·sin; yaw: x' = sin·z + x·cos, z' = cos·z − x·sin (0x43a39d..0x43a4b8); the sin index is fistp(f32(angle · scale)) & mask and the cos index fistp(f32(scale · f32(angle − (−π/2)))) & mask, every product, sum and fistp truncating; the sample's position is added last (0x43a4a1..0x43a4b8)
	internal static float[][] Pose( IReadOnlyList<(float X, float Y)> points, CoasterSample first, CoasterSample second )
	{
		var posed = new float[points.Count * 2][];
		for ( var half = 0; half < 2; half++ )
		{
			// [BIN:TPW-EXE:0x0043a2d7 pose angles] the three angles are read once, from the first sample (0x43a2d7..0x43a2e6); the loop over the two samples jumps back to 0x43a2f6, past them, so the second sample's points are posed with the first sample's yaw, pitch and roll and only its position differs
			var sample = half == 0 ? first : second;
			for ( var index = 0; index < points.Count; index++ )
			{
				float x = points[index].X, y = points[index].Y, z = 0f;
				var (sin, cos) = (CoasterTrig.PcSinTruncated( first.Bank ), CoasterTrig.PcCosTruncated( first.Bank ));
				(x, y) = (Trunc24.Sub( Trunc24.Mul( x, cos ), Trunc24.Mul( sin, y ) ), Trunc24.Add( Trunc24.Mul( cos, y ), Trunc24.Mul( x, sin ) ));
				(sin, cos) = (CoasterTrig.PcSinTruncated( first.Pitch ), CoasterTrig.PcCosTruncated( first.Pitch ));
				(y, z) = (Trunc24.Sub( Trunc24.Mul( y, cos ), Trunc24.Mul( sin, z ) ), Trunc24.Add( Trunc24.Mul( cos, z ), Trunc24.Mul( y, sin ) ));
				(sin, cos) = (CoasterTrig.PcSinTruncated( first.Yaw ), CoasterTrig.PcCosTruncated( first.Yaw ));
				(x, z) = (Trunc24.Add( Trunc24.Mul( sin, z ), Trunc24.Mul( x, cos ) ), Trunc24.Sub( Trunc24.Mul( cos, z ), Trunc24.Mul( x, sin ) ));
				posed[half * points.Count + index] = new[] { Trunc24.Add( sample.Position.X, x ), Trunc24.Add( y, sample.Position.Y ), Trunc24.Add( z, sample.Position.Z ) };
			}
		}
		return posed;
	}

	/// <summary>
	/// The prism test (<c>0x43a2b0</c>): for each side face of the first prism (the quad of profile points i and i + 1 at the two samples, in the plane
	/// of P0, P1 and P2) every profile edge of the second prism at its first sample and every edge from its first to its second sample is cut with the
	/// plane; a cut inside the segment lies in the face when the angles the face's four corners span from it sum to at least 2π − 0.001.
	/// </summary>
	// [BIN:TPW-EXE:0x43a5f4 face plane] with v = P1 − P0 and w = P1 − P2 (singles), n = (w.z·v.y − w.y·v.z, w.x·v.z − w.z·v.x, v.x·w.y − w.x·v.y) over its length sqrt((nx² + ny²) + nz²), each component divided by it, d = −(((P1.x·n.x) + (P1.y·n.y)) + (P1.z·n.z)) (0x43a56a..0x43a64d); for the edge B → B + e, dist = ((n.z·B.z + n.y·B.y) + n.x·B.x) + d and t = −(dist / ((n.x·e.x + n.y·e.y) + n.z·e.z)), accepted for 0 ≤ t ≤ 1 (0x43a675..0x43a72f, 0x43a8d0)
	// [BIN:TPW-EXE:0x43a8b3 face test] the corners P0..P3 give vectors v from the cut point (q = t·e + B), each divided by its length; the angles are acos of the dot products ((y·y + z·z) + x·x) of the pairs (v0, v1), (v1, v2), (v2, v3), (v3, v0), the first stored as a single, each next one an extended acos added to the single sum; the face holds the point when the sum is not below 6.2821855 (0x43a839..0x43a8b3); the longitudinal edge's test 0x43aa10 sums the pairs (v1, v2), (v1, v0), (v2, v3), (v3, v0) with the dot orders (y, z, x), (z, y, x), (y, z, x), (y, z, x)
	internal static bool PrismsIntersect( CoasterSample firstA, CoasterSample secondA, CoasterSample firstB, CoasterSample secondB, IReadOnlyList<(float X, float Y)> points ) =>
		PrismsIntersect( firstA, secondA, firstB, secondB, points, null );

	internal static bool PrismsIntersect( CoasterSample firstA, CoasterSample secondA, CoasterSample firstB, CoasterSample secondB, IReadOnlyList<(float X, float Y)> points, List<float>? sums )
	{
		var a = Pose( points, firstA, secondA );
		var b = Pose( points, firstB, secondB );
		var n = points.Count;
		for ( var i = 0; i < n; i++ )
		{
			var p0 = a[i];
			var p1 = a[(i + 1) % n];
			var p2 = a[i + n];
			var p3 = a[(i + 1) % n + n];
			var v = new[] { Trunc24.Sub( p1[0], p0[0] ), Trunc24.Sub( p1[1], p0[1] ), Trunc24.Sub( p1[2], p0[2] ) };
			var w = new[] { Trunc24.Sub( p1[0], p2[0] ), Trunc24.Sub( p1[1], p2[1] ), Trunc24.Sub( p1[2], p2[2] ) };
			var nx = Trunc24.Sub( Trunc24.Mul( w[2], v[1] ), Trunc24.Mul( w[1], v[2] ) );
			var ny = Trunc24.Sub( Trunc24.Mul( w[0], v[2] ), Trunc24.Mul( w[2], v[0] ) );
			var nz = Trunc24.Sub( Trunc24.Mul( v[0], w[1] ), Trunc24.Mul( w[0], v[1] ) );
			var length = Trunc24.Sqrt( Trunc24.Add( Trunc24.Add( Trunc24.Mul( nx, nx ), Trunc24.Mul( ny, ny ) ), Trunc24.Mul( nz, nz ) ) );
			var normal = new[] { Trunc24.Div( nx, length ), Trunc24.Div( ny, length ), Trunc24.Div( nz, length ) };
			var d = -Trunc24.Add( Trunc24.Add( Trunc24.Mul( p1[0], normal[0] ), Trunc24.Mul( p1[1], normal[1] ) ), Trunc24.Mul( p1[2], normal[2] ) );
			for ( var j = 0; j < n; j++ )
			{
				var start = b[j];
				var profile = b[(j + 1) % n];
				var along = b[j + n];
				var e1 = new[] { Trunc24.Sub( profile[0], start[0] ), Trunc24.Sub( profile[1], start[1] ), Trunc24.Sub( profile[2], start[2] ) };
				var e2 = new[] { Trunc24.Sub( along[0], start[0] ), Trunc24.Sub( along[1], start[1] ), Trunc24.Sub( along[2], start[2] ) };
				var distance = Trunc24.Add( Trunc24.Add( Trunc24.Add( Trunc24.Mul( normal[2], start[2] ), Trunc24.Mul( normal[1], start[1] ) ), Trunc24.Mul( normal[0], start[0] ) ), d );
				var t1 = -Trunc24.Div( distance, Trunc24.Add( Trunc24.Add( Trunc24.Mul( normal[0], e1[0] ), Trunc24.Mul( normal[1], e1[1] ) ), Trunc24.Mul( normal[2], e1[2] ) ) );
				var t2 = -Trunc24.Div( distance, Trunc24.Add( Trunc24.Add( Trunc24.Mul( normal[0], e2[0] ), Trunc24.Mul( normal[1], e2[1] ) ), Trunc24.Mul( normal[2], e2[2] ) ) );
				if ( t1 >= 0 && t1 <= 1 && InFace( start, e1, t1, p0, p1, p2, p3, sums ) )
					return true;
				if ( t2 >= 0 && t2 <= 1 && InFaceAlong( start, e2, t2, p0, p1, p2, p3, sums ) )
					return true;
			}
		}
		return false;
	}

	/// <summary>The unit vector the face test takes from a corner to the cut point: the difference, each component over the length (0x43a7cf..0x43a837, 0x43a9a0).</summary>
	private static float[] Unit( float[] corner, float x, float y, float z )
	{
		float vx = Trunc24.Sub( corner[0], x ), vy = Trunc24.Sub( corner[1], y ), vz = Trunc24.Sub( corner[2], z );
		var length = Trunc24.Sqrt( Trunc24.Add( Trunc24.Add( Trunc24.Mul( vx, vx ), Trunc24.Mul( vy, vy ) ), Trunc24.Mul( vz, vz ) ) );
		return new[] { Trunc24.Div( vx, length ), Trunc24.Div( vy, length ), Trunc24.Div( vz, length ) };
	}

	/// <summary>The dot product 0x43a970 takes: (y·y + z·z) + x·x.</summary>
	private static float DotYzx( float[] a, float[] b ) => Trunc24.Add( Trunc24.Add( Trunc24.Mul( a[1], b[1] ), Trunc24.Mul( a[2], b[2] ) ), Trunc24.Mul( a[0], b[0] ) );

	/// <summary>The (z, y, x) order of the longitudinal edge's second angle (0x43acc7).</summary>
	private static float DotZyx( float[] a, float[] b ) => Trunc24.Add( Trunc24.Add( Trunc24.Mul( a[2], b[2] ), Trunc24.Mul( a[1], b[1] ) ), Trunc24.Mul( a[0], b[0] ) );

	/// <summary>Whether the point start + t·edge, on the face's plane, lies in the face: the angle sum of <see cref="PrismsIntersect"/> for a profile edge.</summary>
	private static bool InFace( float[] start, float[] edge, float t, float[] p0, float[] p1, float[] p2, float[] p3, List<float>? sums )
	{
		var x = Trunc24.Add( Trunc24.Mul( t, edge[0] ), start[0] );
		var y = Trunc24.Add( Trunc24.Mul( t, edge[1] ), start[1] );
		var z = Trunc24.Add( Trunc24.Mul( t, edge[2] ), start[2] );
		var (u0, u1, u2, u3) = (Unit( p0, x, y, z ), Unit( p1, x, y, z ), Unit( p2, x, y, z ), Unit( p3, x, y, z ));
		var sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u0, u1 ) ), 0d );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u1, u2 ) ), sum );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u2, u3 ) ), sum );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u3, u0 ) ), sum );
		sums?.Add( sum );
		return sum >= AngleSumThreshold;
	}

	/// <summary>The face test of the edge from the first sample to the second (<c>0x43aa10</c>): the same corners, the pairs and the dot orders of its own.</summary>
	private static bool InFaceAlong( float[] start, float[] edge, float t, float[] p0, float[] p1, float[] p2, float[] p3, List<float>? sums )
	{
		var x = Trunc24.Add( Trunc24.Mul( t, edge[0] ), start[0] );
		var y = Trunc24.Add( Trunc24.Mul( edge[1], t ), start[1] );
		var z = Trunc24.Add( Trunc24.Mul( edge[2], t ), start[2] );
		var (u0, u1, u2, u3) = (Unit( p0, x, y, z ), Unit( p1, x, y, z ), Unit( p2, x, y, z ), Unit( p3, x, y, z ));
		var sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u1, u2 ) ), 0d );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotZyx( u1, u0 ) ), sum );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u2, u3 ) ), sum );
		sum = Trunc24.Add( CoasterX87.AcosTruncated( DotYzx( u3, u0 ) ), sum );
		sums?.Add( sum );
		return sum >= AngleSumThreshold;
	}
}
