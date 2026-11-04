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
	/// <summary>
	/// One kept record: the entity and the model to draw, the model matrix of the entity at the time, the record's depth and the order it was handed in; a particle quad (<see cref="SubmitParticle"/>) names its renderer and
	/// the quad of its last frame instead.
	/// </summary>
	internal readonly record struct Record( ModelEntity? Entity, Model? Model, Matrix4x4 Matrix, float Depth, int Sequence, ParticleRenderer? Particles = null, int Quad = 0 );

	private static readonly List<Record> records = new();
	private static readonly Comparison<Record> byDepth = ( left, right ) => Compare( left.Depth, left.Sequence, right.Depth, right.Sequence );
	private static Matrix4x4 viewProjection;
	private static long passes;
	private static readonly List<ParticleRenderer> renderers = new();
	private static readonly HashSet<Model> prepared = new( ReferenceEqualityComparer.Instance );

	/// <summary>The number of the open pass (a model sorts once per pass and rotation), or -1 when none is open.</summary>
	internal static long PassId => Active ? passes : -1;

	/// <summary>The translucent records the passes drew since the frame stats were last read (<see cref="FrameStats"/>).</summary>
	internal static int FrameRecords;

	/// <summary>The deferred particle quads the passes drew, and the draws the earlier rule (merge only neighbours that are ascending in the renderer's own buffer) would have made for them, since the frame stats were last read (<c>--frame-stats</c>).</summary>
	internal static int FrameParticleQuads, FrameParticleDrawsUnsorted;

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

	/// <summary>
	/// Keeps one particle quad of <paramref name="particles"/> for the pass, with the mean depth of its corners as its key: the original keeps every particle batch whose state has neither bit 0x1000 nor 0x2000 (0x556, 0x200552,
	/// 0x200522) in the same list as the translucent model faces (0x565590) and draws them by the same sort (0x582AD0).
	/// </summary>
	// [BIN:TPW-EXE:0x57C620 particle batches] each particle is submitted to 0x565F90 as a batch of its own (one record of two triangles), whose state decides between the immediate draw and this list
	internal static void SubmitParticle( ParticleRenderer particles, int quad, float depth )
	{
		if ( Active )
			records.Add( new Record( null, null, Matrix4x4.Identity, depth, records.Count, particles, quad ) );
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
				if ( record.Model is not { } model )
					continue;
				model.OrderTriangles( record.Matrix, record.Matrix * viewProjection, passes );
				if ( prepared.Add( model ) )
					model.FlushUploads( Render.CommandList );
			}
			// the deferred particle quads are copied into their renderer's second buffer in the sorted order (one upload per renderer, before the first draw, like the models' uploads above), so
			// consecutive quads are neighbours in it whatever the camera direction and a run of one kind is one draw
			renderers.Clear();
			ParticleRenderer? previous = null;
			int previousKind = 0, previousQuad = 0;
			for ( var index = 0; index < records.Count; index++ )
			{
				var record = records[index];
				if ( record.Particles is not { } particles )
				{
					previous = null;
					continue;
				}
				if ( FrameStats.Active )
				{
					var kind = particles.QuadKind( record.Quad );
					FrameParticleQuads++;
					if ( !(previous == particles && kind == previousKind && record.Quad == previousQuad + 1) )
						FrameParticleDrawsUnsorted++;
					(previous, previousKind, previousQuad) = (particles, kind, record.Quad);
				}
				records[index] = record with { Quad = particles.AppendSorted( passes, record.Quad ) };
				if ( !renderers.Contains( particles ) )
					renderers.Add( particles );
			}
			foreach ( var particles in renderers )
				particles.UploadSorted( passes, Render.CommandList );
			ParticleRenderer? run = null;
			int runFirst = 0, runCount = 0;
			foreach ( var record in records )
			{
				if ( record.Particles is { } particles )
				{
					if ( run == particles && record.Quad == runFirst + runCount )
					{
						runCount++;
						continue;
					}
					run?.DrawSorted( runFirst, runCount );
					run = particles;
					runFirst = record.Quad;
					runCount = 1;
					continue;
				}
				run?.DrawSorted( runFirst, runCount );
				run = null;
				record.Entity!.DrawModel( record.Model!, record.Matrix );
			}
			run?.DrawSorted( runFirst, runCount );
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
		renderers.Clear();
		Active = false;
	}
}
