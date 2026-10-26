using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

public class Model : Asset
{
	public DeviceBuffer VertexBuffer => vertices!.Buffer;
	public DeviceBuffer? IndexBuffer { get; private set; } = null;

	public Material Material { get; private set; }
	public bool IsIndexed { get; private set; }

	private uint indexCount;
	private ModelVertexBuffer? vertices;
	private bool released;
	private readonly bool dynamicVertices;

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
	/// <remarks>
	/// A model drawn with the park's world shader whose textures include non-opaque ones (<see cref="Texture.AlphaType"/>) adds their alpha test flags to its vertices
	/// (<see cref="WorldTranslucency.ApplyAlphaFlags"/>), and its faces with a translucent texture become <see cref="Companions"/>: models of their own, one per texture, that sort their triangles far to near and
	/// that <see cref="ModelEntity"/> draws in the level's translucent pass (<see cref="WorldPass"/>) instead of with the model. A model without translucent faces is made as it always was.
	/// </remarks>
	public Model( Vertex[] vertices, uint[] indices, Material material, bool dynamicVertices )
	{
		Material = material;
		IsIndexed = true;
		this.dynamicVertices = dynamicVertices;

		var types = material.WorldSlotTypes();
		if ( types == null )
		{
			SetupMesh( vertices, indices, dynamicVertices );
			All.Add( this );
			return;
		}
		// a slot with the runtime flag 0x800 draws opaque, whatever its texture is (WorldTranslucency.OpaqueSlotFlag)
		WorldTranslucency.ForceOpaqueSlots( types, vertices );
		slotTypes = types;
		var whole = BoundsOf( vertices );
		WorldTranslucency.ApplyAlphaFlags( vertices, types );
		var split = WorldTranslucency.SplitFaces( vertices, indices, slot => slot >= 0 && slot < types.Length && WorldTranslucency.IsTranslucent( types[slot] ) );
		if ( split.Translucent.Count == 0 )
		{
			SetupMesh( vertices, indices, dynamicVertices );
			All.Add( this );
			return;
		}
		if ( split.OpaqueIndices.Length > 0 )
			SetupMesh( split.OpaqueVertices, split.OpaqueIndices, dynamicVertices );
		else
			IndexBuffer = null;
		Bounds = whole;
		if ( dynamicVertices )
		{
			mainMap = split.OpaqueMap;
			mainScratch = split.OpaqueIndices.Length > 0 ? new Vertex[split.OpaqueVertices.Length] : null;
		}
		foreach ( var group in split.Translucent )
		{
			var companion = new Model( group.Vertices, group.Indices, material, dynamicVertices, true );
			companions.Add( companion );
			companionSlots.Add( group.Slot );
			if ( dynamicVertices )
				companionMaps.Add( (companion, group.Map, new Vertex[group.Vertices.Length]) );
		}
		All.Add( this );
	}

	public Model( Vertex[] vertices, Material material )
	{
		Material = material;
		IsIndexed = false;

		SetupMesh( vertices );

		All.Add( this );
	}

	/// <summary>A translucent face group of a split model: its own geometry, its triangles sorted far to near.</summary>
	private Model( Vertex[] vertices, uint[] indices, Material material, bool dynamicVertices, bool companion )
	{
		Material = material;
		IsIndexed = true;
		this.dynamicVertices = dynamicVertices;
		SetupMesh( vertices, indices, dynamicVertices, dynamicIndices: true );
		triangleOrder = new TriangleOrder( vertices, indices );
		variants.Add( new OrderVariant( triangleOrder, IndexBuffer! ) );
		All.Add( this );
	}

	private TriangleOrder? triangleOrder;

	/// <summary>
	/// The sorted index sets of a model that sorts its triangles. A model drawn at several places (a track piece laid in many tiles) keeps one set per distinct rotation of its model matrix, each with an index buffer
	/// of its own, sorted once per frame for the first record that asks: the records of one rotation share an order, and no draw has to rewrite a buffer another record of the frame reads.
	/// </summary>
	private sealed class OrderVariant( TriangleOrder order, DeviceBuffer buffer )
	{
		public TriangleOrder Order { get; } = order;
		public DeviceBuffer Buffer { get; } = buffer;
		public (float, float, float, float, float, float, float, float, float) Rotation;
		public bool HasRotation;
		public long Pass = -1;
	}

	private const int MaximumOrderVariants = 8;
	private readonly List<OrderVariant> variants = new();

	private static (float, float, float, float, float, float, float, float, float) RotationOf( in System.Numerics.Matrix4x4 model )
		=> (model.M11, model.M12, model.M13, model.M21, model.M22, model.M23, model.M31, model.M32, model.M33);

	private OrderVariant VariantFor( in System.Numerics.Matrix4x4 model )
	{
		var rotation = RotationOf( model );
		OrderVariant? oldest = null;
		foreach ( var variant in variants )
		{
			if ( variant.HasRotation && variant.Rotation == rotation )
				return variant;
			if ( !variant.HasRotation )
			{
				variant.Rotation = rotation;
				variant.HasRotation = true;
				return variant;
			}
			if ( oldest == null || variant.Pass < oldest.Pass )
				oldest = variant;
		}
		if ( variants.Count >= MaximumOrderVariants )
		{
			// a part that turns every frame: the least recently sorted set is taken over
			oldest!.Rotation = rotation;
			oldest.Order.Reset();
			oldest.Pass = -1;
			return oldest;
		}
		var buffer = Device.ResourceFactory.CreateBuffer( new BufferDescription( indexCount * sizeof( uint ), BufferUsage.IndexBuffer | BufferUsage.Dynamic ) );
		Device.UpdateBuffer( buffer, 0, triangleOrder!.FaceIndices );
		var created = new OrderVariant( triangleOrder.Share(), buffer ) { Rotation = rotation, HasRotation = true };
		variants.Add( created );
		return created;
	}
	private TextureAlphaType[]? slotTypes;
	private int[]? mainMap;
	private Vertex[]? mainScratch;
	private readonly List<Model> companions = new();
	private readonly List<int> companionSlots = new();
	private readonly List<(Model Model, int[] Map, Vertex[] Scratch)> companionMaps = new();

	/// <summary>The translucent face groups of this model, drawn after everything else (<see cref="WorldPass"/>); empty for most models.</summary>
	internal IReadOnlyList<Model> Companions => companions;

	/// <summary>
	/// Whether the translucent group <paramref name="index"/> is translucent now: the texture its slot shows has an alpha plane with an alpha byte. A slot with texture frames of different alpha types
	/// changes pass with its frame; a group that is not translucent now is drawn with the model's other faces.
	/// </summary>
	internal bool CompanionIsTranslucent( int index ) => WorldTranslucency.IsTranslucent( Material.CurrentSlotType( companionSlots[index] ) );

	/// <summary>Whether this model has faces of its own to draw (a model whose faces are all translucent has only <see cref="Companions"/>).</summary>
	internal bool HasGeometry => vertices != null && ( !IsIndexed || indexCount > 0 );

	/// <summary>Whether this model sorts its triangles far to near (a translucent companion).</summary>
	internal bool SortsTriangles => triangleOrder != null;

	/// <summary>
	/// The mean depth of this model's vertices for the view of <paramref name="modelViewProjection"/> (the model matrix times the camera's view and projection): the key the original sorts its
	/// deferred records by (the record's depth sum over its vertex count, 0x582AD0). 0 for a model that does not sort.
	/// </summary>
	internal float MeanDepth( in System.Numerics.Matrix4x4 modelViewProjection ) => triangleOrder?.MeanDepth( modelViewProjection ) ?? 0f;

	/// <summary>
	/// Re-sorts the triangles of a model that sorts them for the view of <paramref name="modelViewProjection"/>; the new order is copied to the GPU when the model is drawn, and only when it differs from the
	/// one the GPU has. Nothing happens when the matrix is the one of the last sort (a still camera), or for a model that does not sort.
	/// </summary>
	internal void OrderTriangles( in System.Numerics.Matrix4x4 modelMatrix, in System.Numerics.Matrix4x4 modelViewProjection, long pass )
	{
		if ( triangleOrder == null )
			return;
		var variant = VariantFor( modelMatrix );
		// one sort per frame and rotation (pass < 0: no pass is open, every draw checks)
		if ( pass >= 0 && variant.Pass == pass )
			return;
		variant.Pass = pass;
		variant.Order.Sort( modelViewProjection );
	}

	/// <summary>Copies what changed since the last frame (replaced vertices, re-sorted indices) to the GPU. The translucent pass calls it for every record before its first draw, so no upload interrupts the draws.</summary>
	internal void FlushUploads( CommandList commandList )
	{
		vertices?.Flush( commandList );
		foreach ( var variant in variants )
			variant.Order.Flush( commandList, variant.Buffer );
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
	public bool HasDynamicVertices => dynamicVertices && IsIndexed && ( vertices?.IsDynamic == true || companions.Count > 0 );

	public void UpdateVertices( Vertex[] updated )
	{
		if ( !dynamicVertices )
			throw new InvalidOperationException( "Only a model created with dynamic vertices can replace them." );
		if ( slotTypes == null )
		{
			vertices!.Set( updated );
		}
		else if ( mainMap == null && companions.Count == 0 )
		{
			// flagged, not split: the caller's array goes to the GPU with the alpha test flags added
			WorldTranslucency.ApplyAlphaFlags( updated, slotTypes );
			vertices!.Set( updated );
		}
		else
		{
			if ( mainScratch != null )
			{
				ProjectVertices( updated, mainMap!, mainScratch );
				vertices!.Set( mainScratch );
			}
			foreach ( var (companion, map, scratch) in companionMaps )
			{
				ProjectVertices( updated, map, scratch );
				companion.ReplaceCompanionVertices( scratch );
			}
		}
		unmeasured = updated;
	}

	/// <summary>The vertices a part of a split model uses, taken from the replaced array by its map, with the alpha test flags of their textures.</summary>
	private void ProjectVertices( Vertex[] updated, int[] map, Vertex[] into )
	{
		for ( var index = 0; index < map.Length; index++ )
			into[index] = updated[map[index]];
		WorldTranslucency.ApplyAlphaFlags( into, slotTypes! );
	}

	private void ReplaceCompanionVertices( Vertex[] replaced )
	{
		vertices!.Set( replaced );
		triangleOrder?.SetPositions( replaced );
	}

	internal void Draw() => Draw( IndexBuffer, null );

	/// <summary>Draws a model placed at <paramref name="modelMatrix"/>: a model that sorts its triangles draws the index set of that rotation.</summary>
	internal void Draw( in System.Numerics.Matrix4x4 modelMatrix )
	{
		if ( triangleOrder == null )
		{
			Draw( IndexBuffer, null );
			return;
		}
		var variant = VariantFor( modelMatrix );
		Draw( variant.Buffer, variant.Order );
	}

	private void Draw( DeviceBuffer? indexBuffer, TriangleOrder? order )
	{
		if ( !HasGeometry )
			return;
		var commandList = Render.CommandList;

		vertices!.Flush( commandList );
		commandList.SetVertexBuffer( 0, VertexBuffer );
		commandList.SetPipeline( Material.Pipeline );

		var resourceSets = Material.GetResourceSets();

		for ( uint i = 0; i < resourceSets.Length; ++i )
			commandList.SetGraphicsResourceSet( i, resourceSets[i] );

		if ( IsIndexed )
		{
			order?.Flush( commandList, indexBuffer! );
			commandList.SetIndexBuffer( indexBuffer, IndexFormat.UInt32 );

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

	/// <summary>Retires this model (and its translucent companions) and its binding cache without taking ownership of a shared material.</summary>
	internal void ReleaseBuffers()
	{
		if ( released )
			return;
		released = true;
		All.Remove( this );
		foreach ( var companion in companions )
			companion.ReleaseBuffers();
		Material.ClearResourceSets();
		var (vertexBuffer, indexBuffer) = (vertices?.Buffer, IndexBuffer);
		var extra = variants.Select( variant => variant.Buffer ).Where( buffer => buffer != indexBuffer ).ToArray();
		if ( vertexBuffer == null && indexBuffer == null )
			return;
		Render.ScheduleDelete( () =>
		{
			vertexBuffer?.Dispose();
			indexBuffer?.Dispose();
			foreach ( var buffer in extra )
				buffer.Dispose();
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
	private readonly int[] positionVersion;
	private int seenVersion;
	private readonly uint[] original;
	private uint[] sorted;
	private uint[] next;
	private readonly int[] order;
	private readonly ulong[] keys;
	private System.Numerics.Matrix4x4 sortedFor;
	private bool sortedOnce;
	private System.Numerics.Matrix4x4 depthFor;
	private bool depthKnown;
	private float meanDepth;
	private bool dirty;

	public TriangleOrder( Vertex[] vertices, uint[] indices )
	{
		positions = new System.Numerics.Vector3[vertices.Length];
		positionVersion = new int[1];
		SetPositionsFrom( vertices );
		original = indices.ToArray();
		sorted = indices.ToArray();
		next = new uint[indices.Length];
		order = new int[indices.Length / 3];
		keys = new ulong[indices.Length / 3];
	}

	/// <summary>An order over the same faces and vertex positions with a sorted state of its own (another rotation of the same model); its buffer starts as the faces' own order.</summary>
	private TriangleOrder( TriangleOrder source )
	{
		positions = source.positions;
		positionVersion = source.positionVersion;
		seenVersion = positionVersion[0];
		original = source.original;
		sorted = source.original.ToArray();
		next = new uint[original.Length];
		order = new int[original.Length / 3];
		keys = new ulong[original.Length / 3];
	}

	/// <summary>A second sorted state over the same faces and positions; its GPU buffer is expected to hold <see cref="FaceIndices"/>.</summary>
	public TriangleOrder Share() => new TriangleOrder( this );

	/// <summary>The faces' own index order (the content of a new buffer).</summary>
	public uint[] FaceIndices => original;

	/// <summary>Forgets the sort: the next one runs even for the matrix of the last (a state taken over by another rotation).</summary>
	public void Reset() => sortedOnce = false;

	private void CheckVersion()
	{
		if ( seenVersion == positionVersion[0] )
			return;
		seenVersion = positionVersion[0];
		sortedOnce = false;
		depthKnown = false;
	}

	private void SetPositionsFrom( Vertex[] vertices )
	{
		for ( var index = 0; index < vertices.Length; index++ )
			positions[index] = vertices[index].Position.GetSystemVector3();
	}

	/// <summary>The vertices moved (a vertex-animated part): the next sort and depth start from the new positions.</summary>
	public void SetPositions( Vertex[] vertices )
	{
		// a texture-coordinate-only update (a scrolling water mesh) leaves the positions as they were: no new sort
		var moved = false;
		for ( var index = 0; index < vertices.Length && !moved; index++ )
			moved = positions[index] != vertices[index].Position.GetSystemVector3();
		if ( !moved )
			return;
		SetPositionsFrom( vertices );
		positionVersion[0]++;
		CheckVersion();
	}

	public float MeanDepth( in System.Numerics.Matrix4x4 modelViewProjection )
	{
		CheckVersion();
		if ( depthKnown && depthFor == modelViewProjection )
			return meanDepth;
		depthFor = modelViewProjection;
		depthKnown = true;
		return meanDepth = WorldTranslucency.MeanDepth( positions, modelViewProjection );
	}

	public void Sort( in System.Numerics.Matrix4x4 modelViewProjection )
	{
		CheckVersion();
		if ( sortedOnce && sortedFor == modelViewProjection )
			return;
		sortedFor = modelViewProjection;
		sortedOnce = true;
		WorldTranslucency.SortTriangles( positions, original, modelViewProjection, order, keys );
		for ( var triangle = 0; triangle < order.Length; triangle++ )
		{
			var from = order[triangle] * 3;
			next[triangle * 3] = original[from];
			next[triangle * 3 + 1] = original[from + 1];
			next[triangle * 3 + 2] = original[from + 2];
		}
		// the GPU copy changes only when the order does: a camera that moved a little leaves most models as they were
		if ( next.AsSpan().SequenceEqual( sorted ) && !dirty )
			return;
		(sorted, next) = (next, sorted);
		dirty = true;
	}

	/// <summary>Whether the sorted order differs from the one the GPU has (tests).</summary>
	public bool NeedsUpload => dirty;

	/// <summary>Takes the sorted order as uploaded (tests).</summary>
	public void MarkUploaded() => dirty = false;

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
