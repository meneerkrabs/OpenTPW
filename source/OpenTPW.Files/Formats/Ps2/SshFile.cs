namespace OpenTPW;

/// <summary>One image record of an <see cref="SshFile"/>.</summary>
/// <param name="Tag">Four-character directory tag.</param>
/// <param name="Type">Record type byte; 0x80 marks the PS2 'GM' compressed payload, the low bits the pixel kind (4 = 24-bit, 5 = 32-bit).</param>
/// <param name="Name">Name from the attached 0x70 block, or empty.</param>
/// <param name="PayloadSize">Bytes of pixel payload after the 16-byte record header.</param>
/// <param name="GmBlocksX">For 'GM' payloads: horizontal count of 16×16 blocks.</param>
/// <param name="GmBlocksY">For 'GM' payloads: vertical count of 16×16 blocks.</param>
public sealed record SshImage( string Tag, int Type, int Width, int Height, string Name, int PayloadSize, bool IsGmCompressed, int GmBlocksX, int GmBlocksY );

/// <summary>
/// EA 'SHPS' shape container as used by the PS2 version (docs/PS2.md): header <c>SHPS</c>, total size,
/// image count, four-character id (<c>GIMX</c> on the PAL disc), then (tag, offset) pairs. Each image
/// record is a type byte, a 24-bit offset to the next attached block (0 = last), width and height; a
/// 0x70 block carries the image name. This reads the container only: the PS2 'GM' pixel payload is a
/// lossy block format that is not decoded yet; every PS2 texture also ships as a truecolor TGA of the
/// same size, which is used for viewing.
/// </summary>
public sealed class SshFile
{
	public const int MaximumImages = 4096;

	public string Id { get; }
	public IReadOnlyList<SshImage> Images { get; }

	public SshFile( byte[] data )
	{
		if ( data.Length < 16 || data[0] != (byte)'S' || data[1] != (byte)'H' || data[2] != (byte)'P' || data[3] != (byte)'S' )
			throw new InvalidDataException( "Not an SSH (SHPS) file." );
		var count = BitConverter.ToInt32( data, 8 );
		if ( count < 0 || count > MaximumImages || 16 + 8L * count > data.Length )
			throw new InvalidDataException( $"SSH image count {count} is out of range." );
		Id = System.Text.Encoding.ASCII.GetString( data, 12, 4 );
		var tags = new string[count];
		var offsets = new int[count];
		for ( var i = 0; i < count; i++ )
		{
			tags[i] = System.Text.Encoding.Latin1.GetString( data, 16 + 8 * i, 4 ).TrimEnd( '\0' );
			var offset = BitConverter.ToInt32( data, 16 + 8 * i + 4 );
			if ( offset < 0 || (long)offset + 16 > data.Length )
				throw new InvalidDataException( $"SSH image {i} offset {offset} is outside the file." );
			offsets[i] = offset;
		}

		var sorted = (int[])offsets.Clone();
		Array.Sort( sorted );
		var starts = new List<int>();
		foreach ( var start in sorted )
			if ( starts.Count == 0 || starts[starts.Count - 1] != start )
				starts.Add( start );

		var images = new List<SshImage>();
		for ( var i = 0; i < count; i++ )
		{
			var offset = offsets[i];
			var limit = data.Length;
			foreach ( var start in starts )
			{
				if ( start > offset )
				{
					limit = start;
					break;
				}
			}

			var type = data[offset];
			var next = data[offset + 1] | data[offset + 2] << 8 | data[offset + 3] << 16;
			var width = BitConverter.ToUInt16( data, offset + 4 );
			var height = BitConverter.ToUInt16( data, offset + 6 );
			var payloadEnd = next == 0 ? limit : (long)offset + next;
			if ( payloadEnd > limit || payloadEnd < (long)offset + 16 )
				throw new InvalidDataException( $"SSH image {i} block runs past its image." );
			var payloadStart = (int)((long)offset + 16);
			var payload = (int)(payloadEnd - offset - 16);
			var gm = payload >= 8 && data[payloadStart] == (byte)'G' && data[payloadStart + 1] == (byte)'M';
			var name = "";
			long block = offset;
			var guard = 0;
			while ( next != 0 && guard++ < 16 )
			{
				block += next;
				if ( block + 4 > limit )
					throw new InvalidDataException( $"SSH image {i} attached block runs past its image." );
				var blockIndex = (int)block;
				var blockType = data[blockIndex];
				next = data[blockIndex + 1] | data[blockIndex + 2] << 8 | data[blockIndex + 3] << 16;
				if ( blockType == 0x70 )
				{
					var nameStart = blockIndex + 4;
					var end = Array.IndexOf( data, (byte)0, nameStart, limit - nameStart );
					name = System.Text.Encoding.Latin1.GetString( data, nameStart, (end < 0 ? limit : end) - nameStart );
				}
			}
			images.Add( new SshImage( tags[i], type, width, height, name, payload, gm, gm ? data[payloadStart + 2] : 0, gm ? data[payloadStart + 3] : 0 ) );
		}
		Images = images;
	}
}
