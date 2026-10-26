using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>A shape as the skirt routines read it: its width and height and the type dword of every cell at <c>row × 20 + column</c> (the original's 20-wide cell array).</summary>
internal sealed record SkirtShape( int Width, int Height, int[] Cells )
{
	public const int Stride = 20;

	/// <summary>The type dword at the flat index <c>row × 20 + column</c>; a column of 20 reads the next row's first cell, an index outside the array reads 0.</summary>
	public int TypeAt( int column, int row )
	{
		var index = row * Stride + column;
		return index >= 0 && index < Cells.Length ? Cells[index] : 0;
	}
}

/// <summary>One material group of the ground node: its texture slot (-1 without one) and its run of faces.</summary>
internal readonly record struct SkirtGroup( int Slot, int First, int Count );

/// <summary>The ground node as the skirt reader sees it: the node's matrix and its parents' (nearest first), its stored vertices, the texture coordinates per corner, the faces' corners and the material groups.</summary>
internal sealed record SkirtNode( float[] Matrix, IReadOnlyList<float[]> Parents, NVector3[] Vertices, NVector2[] Uvs, int[] CornerVertex, int[][] Faces, SkirtGroup[] Groups );

/// <summary>What the skirt reader leaves behind: the entries, the slot flag words, the node's normalised texture coordinates and the counts it prints.</summary>
internal sealed record SkirtReadResult( ObjectSkirtEntry[] Entries, uint[] Slots, NVector2[] Uvs, int Flipped, int NotFlipped, int Errors, bool UvLogged );

/// <summary>
/// The skirt reader and its helpers as tp.exe runs them (<c>0x0046AA10</c>, <c>0x0052C420</c>, <c>0x00467310</c>), on plain arrays, with the original's float and double steps: sums in double, a float
/// store where the original stores one (<see cref="ObjectFloorMath"/>).
/// </summary>
internal static class SkirtReading
{
	private static readonly float OneThird = (float)(1.0 / 3.0);
	private static readonly float ZeroPointNine = 0.9f;
	/// <summary>The epsilon 0x0046AA10 hands the coordinate pass (0x3CA3D70A).</summary>
	internal static readonly float Snap = 0.02f;
	private static readonly float Tenth = 0.1f;

	private static bool IsFloorType( int type ) => type is 4 or 9 or 10 or 0x17;

	/// <summary>The bytes the edge mask routine leaves at 0x008186F0: 256 mask bytes (column × 16 + row), the shape's width and height words behind them, and what a 20-wide or 20-high shape writes further.</summary>
	internal const int MaskSpan = 324;

	/// <summary>
	/// The footprint edge mask (tp.exe <c>0x0052C420</c>): <c>mask[column × 16 + row]</c> is 1 for a cell of type 4, 9, 10 or 0x17 that lacks a neighbour of those types on any side. The neighbour tests are
	/// row − 1 ≥ 0, column − 1 ≥ 0, column + 1 ≤ width and row + 1 ≤ height (one step past the last column and row is read).
	/// </summary>
	// [BIN:TPW-EXE:0x0052C420 skirt edge mask] the routine stores the shape's width and height behind the 256 mask bytes first, clears only the 256 bytes, and sets byte column × 16 + row to 1 for a footprint cell (type dword 4, 9, 10 or 0x17 at shape + 8 + (row × 20 + column) × 8) with fewer than four footprint neighbours
	public static byte[] Mask( SkirtShape shape )
	{
		var memory = new byte[MaskSpan];
		BitConverter.TryWriteBytes( memory.AsSpan( 256, 4 ), shape.Width );
		BitConverter.TryWriteBytes( memory.AsSpan( 260, 4 ), shape.Height );
		for ( var row = 0; row < shape.Height; row++ )
		{
			for ( var column = 0; column < shape.Width; column++ )
			{
				if ( !IsFloorType( shape.TypeAt( column, row ) ) )
					continue;
				var count = 0;
				if ( row - 1 >= 0 && IsFloorType( shape.TypeAt( column, row - 1 ) ) )
					count++;
				if ( column - 1 >= 0 && IsFloorType( shape.TypeAt( column - 1, row ) ) )
					count++;
				if ( column + 1 <= shape.Width && IsFloorType( shape.TypeAt( column + 1, row ) ) )
					count++;
				if ( row + 1 <= shape.Height && IsFloorType( shape.TypeAt( column, row + 1 ) ) )
					count++;
				if ( count != 4 )
				{
					var position = column * 16 + row;
					if ( position < MaskSpan )
						memory[position] = 1;
				}
			}
		}
		return memory;
	}

	/// <summary>A byte of the mask region at a flat index; an index outside it reads 0 (the neighbouring static data is zero).</summary>
	public static byte MaskValue( byte[] mask, int position ) => position >= 0 && position < mask.Length ? mask[position] : (byte)0;

	/// <summary>One texture coordinate: the whole part plus the fraction snapped to -1, 0 or 1 within <paramref name="epsilon"/> (tp.exe <c>0x00467310</c>).</summary>
	public static float SnapComponent( float value, float epsilon )
	{
		double whole = Math.Truncate( (double)value );
		var fraction = value - whole;
		var nearOne = (float)(1.0 - epsilon);
		double snapped;
		if ( fraction < -(double)nearOne )
			snapped = -1.0;
		else
		{
			var fraction32 = (float)fraction;
			if ( fraction32 <= -epsilon || epsilon <= fraction32 )
				snapped = fraction32 > nearOne ? 1.0 : fraction;
			else
				snapped = 0.0;
		}
		return (float)(whole + snapped);
	}

	/// <summary>
	/// The coordinate pass (tp.exe <c>0x00467310</c>): every coordinate snapped, the corners of each face shifted (once, in face order) by the floor of the smallest u and v of the face's snapped corners, and
	/// the wrap bits 0x30 cleared on the slot of every group whose faces all lie flat (|centre height| &lt; 0.1) in a cell the mask marks, unless the slot has bit 0x8000. Returns the coordinates and the slots.
	/// </summary>
	// [BIN:TPW-EXE:0x00467310 texture coordinate pass] the minimum of every face is taken over the snapped coordinates before any face shifts its corners; the shift is the truncated minimum, one less when the truncation left a negative fraction; a corner takes the shift of the first face that reaches it; a face's centre is the sum of its corners times 0.33333334 (the sums in the orders (c + a) + b, (b + a) + c, (c + b) + a), its cell index (ftol(x × 0.1) << 4) − ftol(z × −0.1)
	public static (NVector2[] Uvs, uint[] Slots, bool Logged) NormaliseCoordinates( SkirtNode node, uint[] slotFlags, byte[]? mask, NVector3[]? vertices, float epsilon )
	{
		var snap = epsilon;
		var haveMask = mask != null && vertices != null;
		var uvs = node.Uvs.Select( uv => new NVector2( SnapComponent( uv.X, snap ), SnapComponent( uv.Y, snap ) ) ).ToArray();
		var faces = node.Faces;
		var minimum = new (float U, float V)[faces.Length];
		for ( var f = 0; f < faces.Length; f++ )
		{
			float minU = float.MaxValue, minV = float.MaxValue;
			foreach ( var corner in faces[f] )
			{
				if ( uvs[corner].X < minU )
					minU = uvs[corner].X;
				if ( uvs[corner].Y < minV )
					minV = uvs[corner].Y;
			}
			minimum[f] = (minU, minV);
		}
		var visited = new bool[uvs.Length];
		for ( var f = 0; f < faces.Length; f++ )
		{
			double shiftU = Math.Truncate( (double)minimum[f].U );
			double shiftV = Math.Truncate( (double)minimum[f].V );
			if ( minimum[f].U - shiftU < 0 )
				shiftU = (float)(shiftU - 1.0);
			if ( minimum[f].V - shiftV < 0 )
				shiftV = (float)(shiftV - 1.0);
			foreach ( var corner in faces[f] )
			{
				if ( visited[corner] )
					continue;
				uvs[corner] = new NVector2( (float)(uvs[corner].X - shiftU), (float)(uvs[corner].Y - shiftV) );
				visited[corner] = true;
			}
		}
		var slots = (uint[])slotFlags.Clone();
		var logged = false;
		foreach ( var group in node.Groups )
		{
			var bad = 0;
			for ( var index = group.First; index < group.First + group.Count && index < faces.Length; index++ )
			{
				var face = faces[index];
				float cx = 0, cy = 0, cz = 0;
				if ( haveMask )
				{
					var a = vertices![node.CornerVertex[face[0]]];
					var b = vertices[node.CornerVertex[face[1]]];
					var c = vertices[node.CornerVertex[face[2]]];
					cx = (float)(((double)c.X + a.X + b.X) * OneThird);
					cy = (float)(((double)b.Y + a.Y + c.Y) * OneThird);
					cz = (float)(((double)c.Z + b.Z + a.Z) * OneThird);
				}
				foreach ( var corner in face )
				{
					var uv = uvs[corner];
					if ( haveMask )
					{
						if ( cy <= -Tenth || !(cy < Tenth) )
						{
							bad++;
							continue;
						}
						var zIndex = FloatToInt( (double)cz * -Tenth );
						var xIndex = FloatToInt( (double)cx * Tenth );
						var position = unchecked(xIndex << 4) - zIndex;
						if ( MaskValue( mask!, position ) == 0 )
						{
							bad++;
							continue;
						}
					}
					if ( !logged && (uv.X < 0f || !(uv.X <= 1f) || uv.Y < 0f || !(uv.Y <= 1f)) )
						logged = true;
				}
			}
			if ( bad == 0 && group.Slot >= 0 && (slots[group.Slot] & 0x8000) == 0 )
				slots[group.Slot] &= 0xFFCF;
		}
		return (uvs, slots, logged);
	}

	/// <summary>The float-to-integer helper <c>_ftol</c> (<c>0x0067A830</c>): truncation toward zero, 0 for a value the 64-bit result cannot hold (the "indefinite" integer's low dword).</summary>
	public static int FloatToInt( double value )
	{
		if ( !double.IsFinite( value ) || Math.Abs( value ) >= 9.2233720368547758E+18 )
			return 0;
		return unchecked((int)(long)Math.Truncate( value ));
	}

	/// <summary>The best axis-aligned floor face of a cell: the largest |x| and |z| components its edges have, the edges that have them and their directions.</summary>
	internal sealed class BestFace
	{
		public float MaxX, MaxZ;
		public int Group = -1, Face = -1, EdgeX = -1, EdgeZ = -1;
		public NVector3 DirX, DirZ;
	}

	/// <summary>
	/// The three edges of a face, normalised, and the edge with the largest |x| and the one with the largest |z| (the first wins a tie; both start at 0). <c>dx = x1 − x0</c> in double,
	/// <c>length = float(sqrt((dx·dx + dy·float(dy)) + dz·dz))</c>, the direction is the float of each difference over the length (<c>dy</c> and <c>dx</c> through a float first).
	/// </summary>
	public static (float MaxX, float MaxZ, int EdgeX, int EdgeZ, NVector3 DirX, NVector3 DirZ) EdgeAxes( NVector3 a, NVector3 b, NVector3 c )
	{
		var corners = new[] { a, b, c };
		float bestX = 0f, bestZ = 0f;
		int edgeX = -1, edgeZ = -1;
		NVector3 directionX = default, directionZ = default;
		for ( var e = 0; e < 3; e++ )
		{
			var start = corners[e];
			var end = corners[(e + 1) % 3];
			double dx = (double)end.X - start.X, dy = (double)end.Y - start.Y, dz = (double)end.Z - start.Z;
			var length = (float)Math.Sqrt( dx * dx + dy * (float)dy + dz * dz );
			var nx = (float)dx / (double)length;
			var ny = (float)dy / (double)length;
			var nz = dz / length;
			var direction = new NVector3( (float)nx, (float)ny, (float)nz );
			if ( Math.Abs( nx ) > bestX )
			{
				bestX = (float)Math.Abs( nx );
				edgeX = e;
				directionX = direction;
			}
			if ( (float)Math.Abs( nz ) > bestZ )
			{
				bestZ = (float)Math.Abs( nz );
				edgeZ = e;
				directionZ = direction;
			}
		}
		return (bestX, bestZ, edgeX, edgeZ, directionX, directionZ);
	}

	/// <summary>The terrain cell of a face's centre (tp.exe <c>0x0046AA10</c>): <c>x = ftol(((x0 + x1) + x2)·0.33333334 / sizeX)</c> with the sum and product in double, <c>z = ftol(float((z2 + float(z0 + z1))·0.33333334) / sizeZ)</c>.</summary>
	public static (int X, int Z) FaceCell( NVector3 a, NVector3 b, NVector3 c, float sizeX, float sizeZ )
	{
		var cx = ((double)a.X + b.X + c.X) * OneThird;
		var cz = (float)(((double)c.Z + (float)((double)a.Z + b.Z)) * OneThird);
		return (FloatToInt( cx / sizeX ), FloatToInt( cz / (double)sizeZ ));
	}

	/// <summary>
	/// The skirt reader (tp.exe <c>0x0046AA10</c>) on a ground node: the opaque flag of the slots of its faces (set in an edge cell, cleared elsewhere, the last face of a slot deciding; set everywhere with
	/// <paramref name="drape"/>), the best floor face of every cell, the coordinate pass, and per cell of the skirt grid its entry.
	/// </summary>
	public static SkirtReadResult Read( SkirtNode node, SkirtShape shape, uint[] slotFlags, bool drape, float cellSizeX, float cellSizeZ )
	{
		var mask = Mask( shape );
		var vertices = node.Vertices.Select( vertex => vertex ).ToArray();
		var matrix = ObjectFloorMath.WorldMatrix( node.Matrix, node.Parents );
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index] = ObjectFloorMath.TransformVertex( matrix, node.Vertices[index] );
		var slots = (uint[])slotFlags.Clone();
		var records = new Dictionary<int, BestFace>();
		BestFace Record( int index )
		{
			if ( !records.TryGetValue( index, out var record ) )
			{
				var column = ((index % SkirtShape.Stride) + SkirtShape.Stride) % SkirtShape.Stride;
				var row = (index - column) / SkirtShape.Stride;
				var inside = row >= 0 && row < shape.Height && column < shape.Width;
				records[index] = record = new BestFace { Group = inside ? -1 : 0, Face = inside ? -1 : 0 };
			}
			return record;
		}
		for ( var groupIndex = 0; groupIndex < node.Groups.Length; groupIndex++ )
		{
			var group = node.Groups[groupIndex];
			for ( var faceIndex = 0; faceIndex < group.Count; faceIndex++ )
			{
				var face = node.Faces[group.First + faceIndex];
				var a = vertices[node.CornerVertex[face[0]]];
				var b = vertices[node.CornerVertex[face[1]]];
				var c = vertices[node.CornerVertex[face[2]]];
				var (cellX, cellZ) = FaceCell( a, b, c, cellSizeX, cellSizeZ );
				if ( group.Slot >= 0 )
				{
					if ( !drape && MaskValue( mask, unchecked((cellX << 4) + cellZ) ) == 0 )
						slots[group.Slot] &= 0xF7FF;
					else
						slots[group.Slot] |= 0x0800;
				}
				var axes = EdgeAxes( a, b, c );
				var index = unchecked(cellX + cellZ * SkirtShape.Stride);
				var current = Record( index );
				if ( ZeroPointNine < axes.MaxX && ZeroPointNine < axes.MaxZ && (current.MaxX < axes.MaxX || current.MaxZ < axes.MaxZ) )
					records[index] = new BestFace { MaxX = axes.MaxX, MaxZ = axes.MaxZ, EdgeX = axes.EdgeX, EdgeZ = axes.EdgeZ, DirX = axes.DirX, DirZ = axes.DirZ, Group = groupIndex, Face = faceIndex };
			}
		}
		var (uvs, finalSlots, logged) = NormaliseCoordinates( node, slots, mask, vertices, Snap );
		var entries = new ObjectSkirtEntry[shape.Width * shape.Height];
		var flipped = 0;
		var notFlipped = 0;
		var errors = 0;
		for ( var row = 0; row < shape.Height; row++ )
		{
			for ( var column = 0; column < shape.Width; column++ )
			{
				var entry = ObjectSkirtEntry.None;
				var wanted = drape || (MaskValue( mask, column * 16 + row ) != 0 && shape.TypeAt( column, row ) != 0);
				if ( wanted )
				{
					var current = Record( column + row * SkirtShape.Stride );
					if ( current.Group == -1 )
					{
						if ( errors == 0 )
							errors = 1;
					}
					else if ( ZeroPointNine < current.MaxX && ZeroPointNine < current.MaxZ )
					{
						var group = node.Groups[current.Group];
						var face = node.Faces[group.First + current.Face];
						(entry, var isFlipped) = EntryOf( current, face, uvs, group.Slot );
						if ( isFlipped )
							flipped++;
						else
							notFlipped++;
					}
				}
				entries[row * shape.Width + column] = entry;
			}
		}
		return new SkirtReadResult( entries, finalSlots, uvs, flipped, notFlipped, errors, logged );
	}

	/// <summary>
	/// The entry of one cell from its best face (tp.exe <c>0x0046AF02</c>): the right-angle vertex picks the quad's missing corner, which the other three complete (a parallelogram); the corner nearest the
	/// smallest u and v names the orientation (0x80, 0x88, 0x90, 0xA0) and an edge that changes u more than v flips it (0x40).
	/// </summary>
	private static (ObjectSkirtEntry Entry, bool Flipped) EntryOf( BestFace current, int[] face, NVector2[] uvs, int slot )
	{
		int lowX, highX, lowZ, highZ;
		if ( current.DirX.X > 0 )
		{
			lowX = current.EdgeX;
			highX = (current.EdgeX + 1) % 3;
		}
		else
		{
			highX = current.EdgeX;
			lowX = (current.EdgeX + 1) % 3;
		}
		if ( current.DirZ.Z > 0 )
		{
			lowZ = current.EdgeZ;
			highZ = (current.EdgeZ + 1) % 3;
		}
		else
		{
			highZ = current.EdgeZ;
			lowZ = (current.EdgeZ + 1) % 3;
		}
		var order = new int[4];
		int missing;
		if ( lowX == lowZ )
		{
			missing = 2;
			order[0] = lowX;
			order[1] = highZ;
			order[3] = highX;
		}
		else if ( highZ == lowX )
		{
			missing = 3;
			order[0] = lowZ;
			order[1] = highZ;
			order[2] = highX;
		}
		else if ( highX == highZ )
		{
			missing = 0;
			order[1] = lowX;
			order[2] = highX;
			order[3] = lowZ;
		}
		else
		{
			missing = 1;
			order[0] = lowX;
			order[2] = highZ;
			order[3] = highX;
		}
		var u = new float[4];
		var v = new float[4];
		for ( var k = 0; k < 4; k++ )
		{
			if ( k == missing )
				continue;
			var uv = uvs[face[order[k]]];
			(u[k], v[k]) = (uv.X, uv.Y);
		}
		switch ( missing )
		{
			case 0:
				u[0] = (float)((double)u[3] - u[2] + u[1]);
				v[0] = (float)((double)v[3] - v[2] + v[1]);
				break;
			case 1:
				u[1] = (float)((double)u[2] - u[3] + u[0]);
				v[1] = (float)((double)v[2] - v[3] + v[0]);
				break;
			case 2:
				u[2] = (float)((double)u[3] - u[0] + u[1]);
				v[2] = (float)((double)v[3] - v[0] + v[1]);
				break;
			default:
				u[3] = (float)((double)u[2] - u[1] + u[0]);
				v[3] = (float)((double)v[2] - v[1] + v[0]);
				break;
		}
		var minU = u[0];
		var minV = v[0];
		for ( var k = 1; k < 4; k++ )
		{
			if ( u[k] < minU )
				minU = u[k];
			if ( minV > v[k] )
				minV = v[k];
		}
		var best = 0;
		var bestDistance = float.MaxValue;
		for ( var k = 0; k < 4; k++ )
		{
			var du = (double)u[k] - minU;
			var dv = (double)v[k] - minV;
			var distance = du * (float)du + dv * dv;
			if ( distance < bestDistance )
			{
				best = k;
				bestDistance = (float)distance;
			}
		}
		var code = new[] { 0x80, 0x88, 0x90, 0xA0 }[best];
		var following = (best + 1) & 3;
		if ( Math.Abs( (double)u[best] - u[following] ) > Math.Abs( (double)v[best] - v[following] ) )
			return (new ObjectSkirtEntry( code | 0x40, slot ), true);
		return (new ObjectSkirtEntry( code, slot ), false);
	}
}
