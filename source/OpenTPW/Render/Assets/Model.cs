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

	public Model( Vertex[] vertices, uint[] indices, Material material ) : this( vertices, indices, material, false )
	{
	}

	/// <summary>
	/// An indexed model whose vertices can be replaced per frame (<see cref="UpdateVertices"/>) when
	/// <paramref name="dynamicVertices"/> is set, e.g. an MD2 mesh with its own vertex-animated pose.
	/// </summary>
	public Model( Vertex[] vertices, uint[] indices, Material material, bool dynamicVertices )
	{
		Material = material;
		IsIndexed = true;

		SetupMesh( vertices, indices, dynamicVertices );

		All.Add( this );
	}

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
	}

	private void SetupMesh( Vertex[] vertices, uint[] indices, bool dynamicVertices )
	{
		SetupMesh( vertices, dynamicVertices );

		var factory = Device.ResourceFactory;
		indexCount = (uint)indices.Length;

		IndexBuffer = factory.CreateBuffer(
			new BufferDescription( indexCount * sizeof( uint ), BufferUsage.IndexBuffer )
		);

		Device.UpdateBuffer( IndexBuffer, 0, indices );
	}

	/// <summary>
	/// Replaces the vertices of a dynamic model from the next draw on. The array must keep the original
	/// vertex count and is read when the model is drawn (the frame's command list copies it), so the
	/// caller may keep reusing it.
	/// </summary>
	public void UpdateVertices( Vertex[] vertices ) => this.vertices.Set( vertices );

	internal void Draw()
	{
		var commandList = Render.CommandList;

		vertices.Flush( commandList );
		commandList.SetVertexBuffer( 0, VertexBuffer );
		commandList.SetPipeline( Material.Pipeline );

		Material.CreateEphemeralResourceSet( out var resourceSets );

		for ( uint i = 0; i < resourceSets.Length; ++i )
			commandList.SetGraphicsResourceSet( i, resourceSets[i] );

		if ( IsIndexed )
		{
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
