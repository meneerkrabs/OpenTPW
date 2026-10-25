using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The level's translucent pass (<see cref="WorldTranslucency"/>): while a pass is open, <see cref="ModelEntity"/> hands the translucent face groups of its models
/// (<see cref="Model.Companions"/>) to <see cref="Submit"/> instead of drawing them, and <see cref="Flush"/> draws them after every other entity, the farthest record first. A record is one
/// translucent texture of one model, like the original's batched submission of a mesh, a state and a texture, and its key is the mean depth of its vertices.
/// </summary>
// [BIN:TPW-EXE:0x582AD0 deferred records] 0x582AD0, the second half of 0x582170, collects the records 0x565590 kept, sorts them with 0x5829E0 on +0x30 (the depth sum) over +0x20 (the vertex count), largest first, and draws them in that order
internal static class WorldPass
{
	/// <summary>One kept record: the entity and the model to draw, the model matrix of the entity at the time, the record's depth and the order it was handed in.</summary>
	internal readonly record struct Record( ModelEntity Entity, Model Model, Matrix4x4 Matrix, float Depth, int Sequence );

	private static readonly List<Record> records = new();
	private static readonly Comparison<Record> byDepth = ( left, right ) => Compare( left.Depth, left.Sequence, right.Depth, right.Sequence );
	private static Matrix4x4 viewProjection;
	private static long passes;
	private static readonly HashSet<Model> prepared = new( ReferenceEqualityComparer.Instance );

	/// <summary>The number of the open pass (a model sorts once per pass and rotation), or -1 when none is open.</summary>
	internal static long PassId => Active ? passes : -1;

	/// <summary>The translucent records the passes drew since the frame stats were last read (<see cref="FrameStats"/>).</summary>
	internal static int FrameRecords;

	/// <summary>Whether a pass is open: entities hand their translucent groups over instead of drawing them.</summary>
	internal static bool Active { get; private set; }

	/// <summary>The records handed over so far, in the order they were handed over (tests).</summary>
	internal static IReadOnlyList<Record> Pending => records;

	/// <summary>Opens a pass for the view of <paramref name="viewProjection"/> (the camera's view times its projection).</summary>
	internal static void Begin( in Matrix4x4 viewProjection )
	{
		records.Clear();
		WorldPass.viewProjection = viewProjection;
		passes++;
		Active = true;
	}

	/// <summary>Keeps a translucent group for the pass; false (and nothing kept) when no pass is open, and the caller draws it itself.</summary>
	internal static bool Submit( ModelEntity entity, Model model, in Matrix4x4 matrix )
	{
		if ( !Active )
			return false;
		records.Add( new Record( entity, model, matrix, model.MeanDepth( matrix * viewProjection ), records.Count ) );
		return true;
	}

	/// <summary>Keeps a record with a given depth (tests).</summary>
	internal static void Submit( ModelEntity entity, Model model, in Matrix4x4 matrix, float depth )
	{
		if ( Active )
			records.Add( new Record( entity, model, matrix, depth, records.Count ) );
	}

	/// <summary>The order of two records: the larger (farther) depth first, ties in the order they were handed over.</summary>
	internal static int Compare( float leftDepth, int leftSequence, float rightDepth, int rightSequence )
	{
		var byDepth = rightDepth.CompareTo( leftDepth );
		return byDepth != 0 ? byDepth : leftSequence.CompareTo( rightSequence );
	}

	/// <summary>Sorts the kept records far to near (tests; <see cref="Flush"/> does it itself).</summary>
	internal static void SortPending() => records.Sort( byDepth );

	/// <summary>Draws the kept records far to near and closes the pass.</summary>
	internal static void Flush()
	{
		try
		{
			records.Sort( byDepth );
			FrameRecords += records.Count;
			// every re-sorted index set and replaced vertex array goes to the GPU before the first draw of the pass: an upload between two draws ends the render pass on some backends
			prepared.Clear();
			foreach ( var record in records )
			{
				record.Model.OrderTriangles( record.Matrix, record.Matrix * viewProjection, passes );
				if ( prepared.Add( record.Model ) )
					record.Model.FlushUploads( Render.CommandList );
			}
			foreach ( var record in records )
				record.Entity.DrawModel( record.Model, record.Matrix );
		}
		finally
		{
			Abort();
		}
	}

	/// <summary>Closes the pass without drawing what it kept (a pass that failed).</summary>
	internal static void Abort()
	{
		records.Clear();
		prepared.Clear();
		Active = false;
	}
}
