using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veldrid;

/// <summary>
/// Records commands as integers (and buffer updates as bytes) and replays them in opentpw-gl.js at
/// <see cref="GraphicsDevice.SubmitCommands"/>, so updates keep Veldrid's order relative to draws.
/// </summary>
public class CommandList : IDisposable
{
	private readonly List<int> commands = new();
	private readonly List<byte> data = new();
	private Framebuffer? framebuffer;

	public string? Name { get; set; }
	public bool IsDisposed { get; private set; }

	internal CommandList() { }

	public void Begin()
	{
		commands.Clear();
		data.Clear();
		framebuffer = null;
	}

	public void End() { }

	internal void Execute()
	{
		Gl.Execute( CollectionsMarshal.AsSpan( commands ), CollectionsMarshal.AsSpan( data ) );
		commands.Clear();
		data.Clear();
	}

	private void Emit( Op op, params ReadOnlySpan<int> arguments )
	{
		commands.Add( (int)op );
		commands.AddRange( arguments );
	}

	private static int Bits( float value ) => BitConverter.SingleToInt32Bits( value );

	public void SetFramebuffer( Framebuffer target )
	{
		framebuffer = target;
		Emit( Op.SetFramebuffer, target.Handle, (int)target.Width, (int)target.Height );
	}

	public void SetViewport( uint index, Viewport viewport ) => SetViewport( index, ref viewport );

	public void SetViewport( uint index, ref Viewport viewport )
	{
		if ( index == 0 )
			Emit( Op.Viewport, Bits( viewport.X ), Bits( viewport.Y ), Bits( viewport.Width ), Bits( viewport.Height ), Bits( viewport.MinDepth ), Bits( viewport.MaxDepth ) );
	}

	public void SetFullViewports()
	{
		if ( framebuffer != null )
			SetViewport( 0, new Viewport( 0, 0, framebuffer.Width, framebuffer.Height, 0, 1 ) );
	}

	public void SetFullViewport( uint index ) => SetFullViewports();

	public void SetScissorRect( uint index, uint x, uint y, uint width, uint height )
	{
		if ( index == 0 )
			Emit( Op.Scissor, (int)x, (int)y, (int)width, (int)height );
	}

	public void SetFullScissorRects()
	{
		if ( framebuffer != null )
			SetScissorRect( 0, 0, 0, framebuffer.Width, framebuffer.Height );
	}

	public void SetFullScissorRect( uint index ) => SetFullScissorRects();

	public void ClearColorTarget( uint index, RgbaFloat clearColor )
	{
		if ( index == 0 )
			Emit( Op.ClearColor, Bits( clearColor.R ), Bits( clearColor.G ), Bits( clearColor.B ), Bits( clearColor.A ) );
	}

	public void ClearDepthStencil( float depth ) => ClearDepthStencil( depth, 0 );
	public void ClearDepthStencil( float depth, byte stencil ) => Emit( Op.ClearDepth, Bits( depth ), stencil );

	public void SetPipeline( Pipeline pipeline ) => Emit( Op.SetPipeline, pipeline.Handle );

	public void SetVertexBuffer( uint index, DeviceBuffer buffer ) => SetVertexBuffer( index, buffer, 0 );
	public void SetVertexBuffer( uint index, DeviceBuffer buffer, uint offset ) => Emit( Op.SetVertexBuffer, (int)index, buffer.Handle, (int)offset );

	public void SetIndexBuffer( DeviceBuffer buffer, IndexFormat format ) => SetIndexBuffer( buffer, format, 0 );
	public void SetIndexBuffer( DeviceBuffer buffer, IndexFormat format, uint offset )
		=> Emit( Op.SetIndexBuffer, buffer.Handle, format == IndexFormat.UInt16 ? Gl.UnsignedShort : Gl.UnsignedInt, (int)offset );

	public void SetGraphicsResourceSet( uint slot, ResourceSet resourceSet ) => Emit( Op.SetResourceSet, (int)slot, resourceSet.Handle );
	public void SetGraphicsResourceSet( uint slot, ResourceSet resourceSet, uint[] dynamicOffsets ) => SetGraphicsResourceSet( slot, resourceSet );

	public void Draw( uint vertexCount ) => Draw( vertexCount, 1, 0, 0 );
	public void Draw( uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart )
		=> Emit( Op.Draw, (int)vertexCount, (int)instanceCount, (int)vertexStart, (int)instanceStart );

	public void DrawIndexed( uint indexCount ) => DrawIndexed( indexCount, 1, 0, 0, 0 );
	public void DrawIndexed( uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart )
		=> Emit( Op.DrawIndexed, (int)indexCount, (int)instanceCount, (int)indexStart, vertexOffset, (int)instanceStart );

	private void Update( DeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<byte> bytes )
	{
		Emit( Op.UpdateBuffer, buffer.Handle, (int)bufferOffsetInBytes, data.Count, bytes.Length );
		data.AddRange( bytes );
	}

	public void UpdateBuffer( DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes )
	{
		unsafe
		{
			Update( buffer, bufferOffsetInBytes, new ReadOnlySpan<byte>( (void*)source, (int)sizeInBytes ) );
		}
	}

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, T source ) where T : unmanaged
		=> Update( buffer, bufferOffsetInBytes, MemoryMarshal.AsBytes( new ReadOnlySpan<T>( ref source ) ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ref T source ) where T : unmanaged
		=> Update( buffer, bufferOffsetInBytes, MemoryMarshal.AsBytes( new ReadOnlySpan<T>( ref source ) ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ref T source, uint sizeInBytes ) where T : unmanaged
		=> Update( buffer, bufferOffsetInBytes, MemoryMarshal.CreateReadOnlySpan( ref Unsafe.As<T, byte>( ref source ), (int)sizeInBytes ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, T[] source ) where T : unmanaged
		=> Update( buffer, bufferOffsetInBytes, MemoryMarshal.AsBytes( source.AsSpan() ) );

	public void UpdateBuffer<T>( DeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<T> source ) where T : unmanaged
		=> Update( buffer, bufferOffsetInBytes, MemoryMarshal.AsBytes( source ) );

	/// <summary>Resolves a multisampled colour target into a single-sampled texture.</summary>
	public void ResolveTexture( Texture source, Texture destination )
		=> Emit( Op.Resolve, source.Handle, destination.Handle, (int)destination.Width, (int)destination.Height );

	public void GenerateMipmaps( Texture texture ) => Emit( Op.GenerateMipmaps, texture.Handle );

	/// <summary>Copies into staging textures are only used for desktop readback.</summary>
	public void CopyTexture( Texture source, Texture destination ) => throw new NotSupportedException( "The browser renderer cannot copy textures for readback." );

	public void PushDebugGroup( string name ) { }
	public void PopDebugGroup() { }
	public void InsertDebugMarker( string name ) { }

	public void Dispose()
	{
		IsDisposed = true;
		commands.Clear();
		data.Clear();
	}
}
