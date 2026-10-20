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
	public readonly record struct Node( int SampleIndex, float? Radius, bool IsPylonless, bool CutEnd = false );

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
	// [APPROX:COAST-004] the descriptor-equality skips (0x439c5f..0x439c81) have no case in OpenTPW's tracks; the x87 intermediates are carried in double (COAST-032); the lists (+0xb4, eight slots up to +0xd4) are read only by the drop 0x43ad50 and the save writer 0x437300 as far as traced (the other +0xb4 loads 0x43b159, 0x43b3e5 and 0x43b63f belong to a ring buffer of another object with its capacity at +0xb0 and cursors at +0xc4/+0xc8, 0x44ace9 is a stack slot; the objects of the loads at 0x430867, 0x43420a, 0x443087 and 0x44309c are not decoded); the count (+0xb0) feeds the section builder 0x43f4e0 (material variant 2 for a crossing link)
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
		var (minX, minY, minZ) = (shared.MinX - radius, shared.MinY - radius, shared.MinZ - radius);
		var (maxX, maxY, maxZ) = (shared.MaxX + radius, shared.MaxY + radius, shared.MaxZ + radius);
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
				MinX = Math.Min( p.X, q.X ) - radius, MinY = Math.Min( p.Y, q.Y ) - radius, MinZ = Math.Min( p.Z, q.Z ) - radius,
				MaxX = Math.Max( p.X, q.X ) + radius, MaxY = Math.Max( p.Y, q.Y ) + radius, MaxZ = Math.Max( p.Z, q.Z ) + radius
			} );
		}
		return segments;
	}

	/// <summary>
	/// The points of a profile posed at two samples (<c>0x43a2b0</c>): the point (x, y, 0) turned about z by the roll, about x by the pitch and
	/// about y by the yaw with the sine table under the truncating rounding mode, then moved to the sample. The first sample's points come
	/// first, then the second's.
	/// </summary>
	// [BIN:TPW-EXE:0x43a33b pose points] roll: x' = x·cos − y·sin, y' = x·sin + y·cos; pitch: y' = y·cos − z·sin, z' = y·sin + z·cos; yaw: x' = x·cos + z·sin, z' = z·cos − x·sin; the sin index is fistp(f32(angle · scale)) & mask and the cos index fistp(f32(scale · f32(angle − (−π/2)))) & mask, truncating (the pass sets the control word 0xC00); sample +0x10 yaw, +0x14 pitch, +0x18 roll
	internal static float[][] Pose( IReadOnlyList<(float X, float Y)> points, CoasterSample first, CoasterSample second )
	{
		var posed = new float[points.Count * 2][];
		for ( var half = 0; half < 2; half++ )
		{
			var sample = half == 0 ? first : second;
			for ( var index = 0; index < points.Count; index++ )
			{
				double x = points[index].X, y = points[index].Y, z = 0;
				var (sin, cos) = (CoasterTrig.PcSinTruncated( sample.Bank ), CoasterTrig.PcCosTruncated( sample.Bank ));
				(x, y) = ((float)(x * cos - y * sin), (float)(x * sin + y * cos));
				(sin, cos) = (CoasterTrig.PcSinTruncated( sample.Pitch ), CoasterTrig.PcCosTruncated( sample.Pitch ));
				(y, z) = ((float)(y * cos - z * sin), (float)(y * sin + z * cos));
				(sin, cos) = (CoasterTrig.PcSinTruncated( sample.Yaw ), CoasterTrig.PcCosTruncated( sample.Yaw ));
				(x, z) = ((float)(x * cos + z * sin), (float)(z * cos - x * sin));
				posed[half * points.Count + index] = new[] { (float)(x + sample.Position.X), (float)(y + sample.Position.Y), (float)(z + sample.Position.Z) };
			}
		}
		return posed;
	}

	/// <summary>
	/// The prism test (<c>0x43a2b0</c>): for each side face of the first prism (the quad of profile points i and i + 1 at the two samples, in the plane
	/// of P0, P1 and P2) every profile edge of the second prism at its first sample and every edge from its first to its second sample is cut with the
	/// plane; a cut inside the segment lies in the face when the angles the face's four corners span from it sum to at least 2π − 0.001.
	/// </summary>
	// [BIN:TPW-EXE:0x43a5f4 face plane] n = normalise((P1 − P0) × (P1 − P2)), d = −(P1 · n); t = −(n · B + d) / (n · e) for the edge B → B + e, accepted for 0 ≤ t ≤ 1 (0x43a718, 0x43a8d0)
	// [BIN:TPW-EXE:0x43a8b3 face test] the corners P0, P1, P2, P3 give unit vectors u from the cut point; the sum acos(u0·u1) + acos(u1·u2) + acos(u2·u3) + acos(u3·u0) must not be below 6.2821855; the same pairs, in the same order, are summed by the longitudinal edge's test 0x43aa10
	internal static bool PrismsIntersect( CoasterSample firstA, CoasterSample secondA, CoasterSample firstB, CoasterSample secondB, IReadOnlyList<(float X, float Y)> points )
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
			// v1 = P1 − P0 stays unrounded, w = P1 − P2 is stored as single
			var v1 = new[] { (double)p1[0] - p0[0], (double)p1[1] - p0[1], (double)p1[2] - p0[2] };
			var w = new[] { (float)(p1[0] - (double)p2[0]), (float)(p1[1] - (double)p2[1]), (float)(p1[2] - (double)p2[2]) };
			var nx = (float)(w[2] * v1[1] - w[1] * v1[2]);
			var ny = (float)(w[0] * v1[2] - w[2] * v1[0]);
			var nzExtended = w[1] * v1[0] - w[0] * v1[1];
			var length = Math.Sqrt( (double)nx * nx + (double)ny * ny + nzExtended * nzExtended );
			var normal = new[] { (float)(nx / length), (float)(ny / length), (float)(nzExtended / length) };
			var d = (float)-((double)p1[0] * normal[0] + (double)p1[1] * normal[1] + (double)p1[2] * normal[2]);
			for ( var j = 0; j < n; j++ )
			{
				var start = b[j];
				var profile = b[(j + 1) % n];
				var along = b[j + n];
				var e1 = new[] { (float)(profile[0] - (double)start[0]), (float)(profile[1] - (double)start[1]), (float)(profile[2] - (double)start[2]) };
				var e2 = new[] { (float)(along[0] - (double)start[0]), (float)(along[1] - (double)start[1]), (float)(along[2] - (double)start[2]) };
				var distance = (double)normal[2] * start[2] + (double)normal[1] * start[1] + (double)normal[0] * start[0] + d;
				var t1 = (float)-(distance / ((double)normal[0] * e1[0] + (double)normal[1] * e1[1] + (double)normal[2] * e1[2]));
				var t2 = (float)-(distance / ((double)normal[0] * e2[0] + (double)normal[1] * e2[1] + (double)normal[2] * e2[2]));
				if ( t1 >= 0 && t1 <= 1 && InFace( start, e1, t1, p0, p1, p2, p3 ) )
					return true;
				if ( t2 >= 0 && t2 <= 1 && InFace( start, e2, t2, p0, p1, p2, p3 ) )
					return true;
			}
		}
		return false;
	}

	/// <summary>Whether the point start + t·edge, on the face's plane, lies in the face: the angle sum of <see cref="PrismsIntersect"/>.</summary>
	private static bool InFace( float[] start, float[] edge, float t, float[] p0, float[] p1, float[] p2, float[] p3 )
	{
		var x = t * (double)edge[0] + start[0];
		var y = (float)(t * (double)edge[1] + start[1]);
		var z = (float)(t * (double)edge[2] + start[2]);
		float[] Unit( float[] corner )
		{
			var vx = (float)(corner[0] - x);
			var vy = (float)(corner[1] - (double)y);
			var vz = (float)(corner[2] - (double)z);
			var length = Math.Sqrt( (double)vx * vx + (double)vy * vy + (double)vz * vz );
			return new[] { (float)(vx / length), (float)(vy / length), (float)(vz / length) };
		}
		var (u0, u1, u2, u3) = (Unit( p0 ), Unit( p1 ), Unit( p2 ), Unit( p3 ));
		// [EXT:deterministic-math] the runtime's acos in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		double Angle( float[] one, float[] other ) => DetMath.Acos( (double)one[0] * other[0] + (double)one[1] * other[1] + (double)one[2] * other[2] );
		var sum = (float)Angle( u0, u1 );
		sum = (float)(sum + Angle( u1, u2 ));
		sum = (float)(sum + Angle( u2, u3 ));
		return sum + Angle( u3, u0 ) >= AngleSumThreshold;
	}
}
