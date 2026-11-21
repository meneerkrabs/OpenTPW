using System.Runtime.CompilerServices;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// Writes into <see cref="BufferUsage.Dynamic"/> buffers the way every backend supports.
/// </summary>
/// <remarks>
/// On Direct3D 11 a dynamic buffer is GPU read-only: its contents may only be written through <c>Map</c> with <c>WRITE_DISCARD</c>
/// (D3D11_USAGE, "Resource Usage Restrictions": no GPU write for D3D11_USAGE_DYNAMIC, copies only between resources of the same usage).
/// Veldrid 4.9 maps only when an update covers the whole buffer; an update of part of a dynamic buffer goes through a staging buffer and
/// <c>CopySubresourceRegion</c> into the dynamic buffer (D3D11GraphicsDevice/D3D11CommandList.UpdateBufferCore), a GPU write the usage does not
/// allow. What a driver then draws is undefined: after an NVIDIA driver update the interface's help box came and went from frame to frame
/// (issue #829). So on Direct3D 11 a partial update of a dynamic buffer is widened to the whole buffer, which Veldrid writes
/// with <c>WRITE_DISCARD</c>; the bytes past the caller's are left as they were in the scratch copy and are never drawn. The other backends keep
/// the caller's byte count.
/// </remarks>
internal static class DynamicBufferUpload
{
	[ThreadStatic] private static byte[]? scratch;

	/// <summary>Set by tests; null follows the device.</summary>
	internal static bool? WholeBufferOverride { get; set; }

	/// <summary>Whether partial writes into dynamic buffers must become whole-buffer writes on this device.</summary>
	internal static bool WholeBufferWrites => WholeBufferOverride ?? Device.BackendType == GraphicsBackend.Direct3D11;

	/// <summary>
	/// How many bytes an update of <paramref name="bytes"/> from offset 0 uploads: the buffer's whole size when the buffer is dynamic, the write is
	/// partial and <paramref name="wholeBuffer"/> holds; otherwise the caller's count.
	/// </summary>
	internal static uint UploadBytes( BufferUsage usage, uint bufferSize, uint bytes, bool wholeBuffer ) =>
		wholeBuffer && (usage & BufferUsage.Dynamic) != 0 && bytes < bufferSize ? bufferSize : bytes;

	/// <summary>Records an update of the buffer's first <paramref name="bytes"/> bytes from <paramref name="source"/>.</summary>
	public static void Update<T>( CommandList commands, DeviceBuffer buffer, ref T source, uint bytes ) where T : unmanaged
	{
		if ( bytes == 0 )
			return;
		var upload = UploadBytes( buffer.Usage, buffer.SizeInBytes, bytes, WholeBufferWrites );
		if ( upload == bytes )
		{
			commands.UpdateBuffer( buffer, 0, ref source, bytes );
			return;
		}
		var widened = Widen( ref source, bytes, upload );
		commands.UpdateBuffer( buffer, 0, ref widened[0], upload );
	}

	/// <summary>Records an update of the buffer's first bytes with one value.</summary>
	public static void Update<T>( CommandList commands, DeviceBuffer buffer, ref T source ) where T : unmanaged =>
		Update( commands, buffer, ref source, (uint)Unsafe.SizeOf<T>() );

	/// <summary>Records an update of the buffer's first bytes with an array's elements.</summary>
	public static void Update<T>( CommandList commands, DeviceBuffer buffer, T[] source ) where T : unmanaged
	{
		if ( source.Length > 0 )
			Update( commands, buffer, ref source[0], (uint)(source.Length * Unsafe.SizeOf<T>()) );
	}

	/// <summary>Writes the buffer's first <paramref name="bytes"/> bytes from <paramref name="source"/> straight away (outside a command list).</summary>
	public static void Update<T>( GraphicsDevice device, DeviceBuffer buffer, ref T source, uint bytes ) where T : unmanaged
	{
		if ( bytes == 0 )
			return;
		var upload = UploadBytes( buffer.Usage, buffer.SizeInBytes, bytes, WholeBufferWrites );
		if ( upload == bytes )
		{
			device.UpdateBuffer( buffer, 0, ref source, bytes );
			return;
		}
		var widened = Widen( ref source, bytes, upload );
		device.UpdateBuffer( buffer, 0, ref widened[0], upload );
	}

	/// <summary>Writes the buffer's first bytes with an array's elements straight away.</summary>
	public static void Update<T>( GraphicsDevice device, DeviceBuffer buffer, T[] source ) where T : unmanaged
	{
		if ( source.Length > 0 )
			Update( device, buffer, ref source[0], (uint)(source.Length * Unsafe.SizeOf<T>()) );
	}

	/// <summary>The caller's bytes at the start of a scratch array at least <paramref name="upload"/> bytes long (the update copies it before returning).</summary>
	internal static byte[] Widen<T>( ref T source, uint bytes, uint upload ) where T : unmanaged
	{
		var buffer = scratch;
		if ( buffer == null || buffer.Length < upload )
			scratch = buffer = new byte[Math.Max( upload, (uint)(buffer?.Length ?? 0) * 2 )];
		Unsafe.CopyBlockUnaligned( ref buffer[0], ref Unsafe.As<T, byte>( ref source ), bytes );
		return buffer;
	}
}
