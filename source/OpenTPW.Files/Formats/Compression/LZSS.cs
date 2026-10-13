namespace OpenTPW;

internal static class LZSS
{
	public static byte[] Decompress( BitReader inputBuffer )
	{
		var output = new byte[4096];
		var position = 0;
		while ( true )
		{
			var isDelta = inputBuffer.GetBitsChecked( 1 );

			// One byte literal
			if ( isDelta == 0 )
			{
				var literalValue = inputBuffer.GetBitsChecked( 8 );
				if ( position == Array.MaxLength )
					throw new InvalidDataException( "LZSS output exceeds the supported array length." );
				if ( position == output.Length )
					Array.Resize( ref output, (int)Math.Min( (long)output.Length * 2, Array.MaxLength ) );
				output[position++] = (byte)literalValue;
			}
			else
			{
				int offset = (int)inputBuffer.GetBitsChecked( 12 );

				if ( offset == 0 )
					break;

				int length = (int)(inputBuffer.GetBitsChecked( 7 )) + 1;

				if ( offset > position )
					throw new Exception( "Offset > stream length" );

				if ( length > Array.MaxLength - position )
					throw new InvalidDataException( "LZSS output exceeds the supported array length." );
				if ( length > output.Length - position )
					Array.Resize( ref output, (int)Math.Min( Array.MaxLength, Math.Max( (long)output.Length * 2, (long)position + length ) ) );
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
