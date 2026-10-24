using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

public class Model : Asset
{
	public DeviceBuffer VertexBuffer => vertices.Buffer;
	public DeviceBuffer? IndexBuffer { get; private set; } = null;

	public Material Material { get; private set; }
	public bool IsIndexed { get; private set; }

	private uint indexCount;
	private ModelVertexBuffer vertices = null!;
	private bool released;

	/// <summary>Model-space bounds of the vertex positions (null without vertices); used to skip draws outside the view. Replaced vertices are measured when the bounds are read, not on every replacement.</summary>
	public (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max)? Bounds
	{
		get
		{
			if ( unmeasured != null )
			{
				bounds = BoundsOf( unmeasured );
				unmeasured = null;
			}
			return bounds;
		}
		private set
		{
			bounds = value;
			unmeasured = null;
		}
	}

	private (System.Numerics.Vector3, System.Numerics.Vector3)? bounds;
	private Vertex[]? unmeasured;

	public Model( Vertex[] vertices, uint[] indices, Material material ) : this( vertices, indices, material, false )
	{
	}

	/// <summary>
	/// An indexed model whose vertices can be replaced per frame (<see cref="UpdateVertices"/>) when
	/// <paramref name="dynamicVertices"/> is set, e.g. an MD2 mesh with its own vertex-animated pose.
	/// </summary>
	public Model( Vertex[] vertices, uint[] indices, Material material, bool dynamicVertices ) : this( vertices, indices, material, dynamicVertices, false )
	{
	}

	/// <summary>
	/// An indexed model for the world's translucent pass (<see cref="WorldTranslucency"/>). With <paramref name="sortTriangles"/> its triangles are drawn far to near by the sum of their corner depths
	/// (<see cref="OrderTriangles"/>, before each draw), as the original sorts a deferred batch; a model whose vertices are replaced per frame keeps its triangle order, since its positions move.
	/// </summary>
	public Model( Vertex[] vertices, uint[] indices, Material material, bool dynamicVertices, bool sortTriangles )
	{
		Material = material;
		IsIndexed = true;

		var sorting = sortTriangles && !dynamicVertices && indices.Length >= 3;
		SetupMesh( vertices, indices, dynamicVertices, sorting );
		if ( sorting )
			triangleOrder = new TriangleOrder( vertices, indices );

		All.Add( this );
	}

	private TriangleOrder? triangleOrder;

	/// <summary>Whether this model draws its triangles far to near (<see cref="OrderTriangles"/>).</summary>
	internal bool SortsTriangles => triangleOrder != null;

	/// <summary>
	/// The mean of the vertex positions in model space, the point whose depth stands for the model's average vertex depth (depth is linear in the position, so the mean of the depths is the depth of the mean).
	/// The centre of the bounds for a model that sorts no triangles and so keeps no copy of its vertices; null without vertices.
	/// </summary>
	internal System.Numerics.Vector3? DepthCentre => triangleOrder?.Centroid ?? ( Bounds is { } bounds ? ( bounds.Min + bounds.Max ) * 0.5f : null );

	/// <summary>
	/// Re-sorts the triangles of a model that sorts them for the view of <paramref name="modelViewProjection"/> (the model matrix times the camera's view and projection); the new order is copied to the GPU
	/// when the model is drawn. Nothing happens when the matrix is the one of the last sort (a still camera), or for a model that does not sort.
	/// </summary>
	internal void OrderTriangles( in System.Numerics.Matrix4x4 modelViewProjection ) => triangleOrder?.Sort( modelViewProjection );

	public Model( Vertex[] vertices, Material material )
	{
		Material = material;
		IsIndexed = false;

		SetupMesh( vertices );

		All.Add( this );
	}

	private void SetupMesh( Vertex[] vertices, bool dynamicVertices = false )
	{
		this.vertices = new ModelVertexBuffer( Device, vertices, dynamicVertices );
		Bounds = BoundsOf( vertices );
	}

	private static (System.Numerics.Vector3, System.Numerics.Vector3)? BoundsOf( Vertex[] vertices )
	{
		if ( vertices.Length == 0 )
			return null;
		var min = new System.Numerics.Vector3( float.PositiveInfinity );
		var max = new System.Numerics.Vector3( float.NegativeInfinity );
		foreach ( var vertex in vertices )
		{
			var position = vertex.Position.GetSystemVector3();
			min = System.Numerics.Vector3.Min( min, position );
			max = System.Numerics.Vector3.Max( max, position );
		}
		return (min, max);
	}

	private void SetupMesh( Vertex[] vertices, uint[] indices, bool dynamicVertices, bool dynamicIndices = false )
	{
		SetupMesh( vertices, dynamicVertices );

		var factory = Device.ResourceFactory;
		indexCount = (uint)indices.Length;

		// a model that sorts its triangles rewrites its indices (TriangleOrder)
		IndexBuffer = factory.CreateBuffer(
			new BufferDescription( indexCount * sizeof( uint ), BufferUsage.IndexBuffer | ( dynamicIndices ? BufferUsage.Dynamic : 0 ) )
		);

		Device.UpdateBuffer( IndexBuffer, 0, indices );
	}

	/// <summary>
	/// Replaces the vertices of a dynamic model from the next draw on. The array must keep the original
	/// vertex count and is read when the model is drawn (the frame's command list copies it), so the
	/// caller may keep reusing it.
	/// </summary>
	public bool HasDynamicVertices => vertices?.IsDynamic == true;

	public void UpdateVertices( Vertex[] vertices )
	{
		this.vertices.Set( vertices );
		unmeasured = vertices;
	}

	internal void Draw()
	{
		var commandList = Render.CommandList;

		vertices.Flush( commandList );
		commandList.SetVertexBuffer( 0, VertexBuffer );
		commandList.SetPipeline( Material.Pipeline );

		var resourceSets = Material.GetResourceSets();

		for ( uint i = 0; i < resourceSets.Length; ++i )
			commandList.SetGraphicsResourceSet( i, resourceSets[i] );

		if ( IsIndexed )
		{
			triangleOrder?.Flush( commandList, IndexBuffer! );
			commandList.SetIndexBuffer( IndexBuffer, IndexFormat.UInt32 );

			commandList.DrawIndexed(
				indexCount: indexCount,
				instanceCount: 1,
				indexStart: 0,
				vertexOffset: 0,
				instanceStart: 0
			);
		}
		else
		{
			commandList.Draw( vertices.Count );
		}
	}

	/// <summary>Retires this model and its binding cache without taking ownership of a shared material.</summary>
	internal void ReleaseBuffers()
	{
		if ( released )
			return;
		released = true;
		All.Remove( this );
		Material.ClearResourceSets();
		Render.ScheduleDelete( () =>
		{
			VertexBuffer.Dispose();
			IndexBuffer?.Dispose();
		} );
	}
}

/// <summary>
/// The CPU side of a model that sorts its triangles: the vertex positions and the faces' own order, the sorted copy and the matrix it was sorted for. The sort is the original's: triangles by the sum of
/// their three vertex depths, farthest first (<see cref="WorldTranslucency.SortTriangles"/>).
/// </summary>
internal sealed class TriangleOrder
{
	private readonly System.Numerics.Vector3[] positions;
	private readonly uint[] original;
	private readonly uint[] sorted;
	private readonly int[] order;
	private readonly ulong[] keys;
	private System.Numerics.Matrix4x4 sortedFor;
	private bool sortedOnce;
	private bool dirty;

	public TriangleOrder( Vertex[] vertices, uint[] indices )
	{
		positions = new System.Numerics.Vector3[vertices.Length];
		double x = 0, y = 0, z = 0;
		for ( var index = 0; index < vertices.Length; index++ )
		{
			var position = vertices[index].Position.GetSystemVector3();
			positions[index] = position;
			x += position.X;
			y += position.Y;
			z += position.Z;
		}
		Centroid = vertices.Length == 0 ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3( (float)(x / vertices.Length), (float)(y / vertices.Length), (float)(z / vertices.Length) );
		original = indices.ToArray();
		sorted = indices.ToArray();
		order = new int[indices.Length / 3];
		keys = new ulong[indices.Length / 3];
	}

	public System.Numerics.Vector3 Centroid { get; }

	public void Sort( in System.Numerics.Matrix4x4 modelViewProjection )
	{
		if ( sortedOnce && sortedFor == modelViewProjection )
			return;
		sortedFor = modelViewProjection;
		sortedOnce = true;
		WorldTranslucency.SortTriangles( positions, original, modelViewProjection, order, keys );
		for ( var triangle = 0; triangle < order.Length; triangle++ )
		{
			var from = order[triangle] * 3;
			sorted[triangle * 3] = original[from];
			sorted[triangle * 3 + 1] = original[from + 1];
			sorted[triangle * 3 + 2] = original[from + 2];
		}
		dirty = true;
	}

	public void Flush( CommandList commandList, DeviceBuffer buffer )
	{
		if ( !dirty )
			return;
		commandList.UpdateBuffer( buffer, 0, sorted );
		dirty = false;
	}
}

/// <summary>
/// The vertex buffer of a <see cref="Model"/>. A dynamic one keeps its size: replacements are queued by
/// <see cref="Set"/> and recorded into the frame's command list by <see cref="Flush"/>, which Veldrid
/// orders before the draws that read the buffer, so frames still in flight keep their data.
/// </summary>
internal sealed class ModelVertexBuffer
{
	private static readonly uint Stride = (uint)Marshal.SizeOf<Vertex>();
	private Vertex[]? pending;

	public ModelVertexBuffer( GraphicsDevice device, Vertex[] vertices, bool dynamic )
	{
		ArgumentNullException.ThrowIfNull( vertices );
		IsDynamic = dynamic;
		Count = (uint)vertices.Length;
		Buffer = device.ResourceFactory.CreateBuffer( new BufferDescription( Count * Stride, BufferUsage.VertexBuffer | (dynamic ? BufferUsage.Dynamic : 0) ) );
		device.UpdateBuffer( Buffer, 0, vertices );
	}

	public DeviceBuffer Buffer { get; }
	public uint Count { get; }
	public bool IsDynamic { get; }
	public bool HasPendingUpdate => pending != null;

	public void Set( Vertex[] vertices )
	{
		ArgumentNullException.ThrowIfNull( vertices );
		if ( !IsDynamic )
			throw new InvalidOperationException( "Only a model created with dynamic vertices can replace them." );
		if ( vertices.Length != Count )
			throw new ArgumentException( $"A dynamic model keeps its {Count} vertices.", nameof( vertices ) );
		pending = vertices;
	}

	public void Flush( CommandList commandList )
	{
		if ( pending == null )
			return;
		commandList.UpdateBuffer( Buffer, 0, pending );
		pending = null;
	}
}
