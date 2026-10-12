namespace OpenTPW;

internal static class LZSS
{
	public static byte[] Decompress( BitReader inputBuffer )
	{
		var output = new byte[4096];
		var position = 0;
		while ( true )
		{
			var isDelta = inputBuffer.GetBits( 1 );

			// One byte literal
			if ( isDelta == 0 )
			{
				var literalValue = inputBuffer.GetBits( 8 );
				if ( position == output.Length )
					Array.Resize( ref output, output.Length * 2 );
				output[position++] = (byte)literalValue;
			}
			else
			{
				int offset = (int)inputBuffer.GetBits( 12 );

				if ( offset == 0 )
					break;

				int length = (int)(inputBuffer.GetBits( 7 )) + 1;

				if ( offset > position )
					throw new Exception( "Offset > stream length" );

				if ( length > output.Length - position )
					Array.Resize( ref output, Math.Max( output.Length * 2, position + length ) );
				// Forward copying also handles overlap: later bytes repeat those just written.
				for ( int i = 0; i < length; ++i )
				{
					output[position] = output[position - offset];
					position++;
				}
			}
		}

		Array.Resize( ref output, position );
		return output;
	}
}
