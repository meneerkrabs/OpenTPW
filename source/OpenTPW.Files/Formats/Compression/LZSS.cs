namespace OpenTPW;

internal static class LZSS
{
	public static bool Decompress( BitReader inputBuffer, BinaryWriter outputBuffer )
	{
		while ( true )
		{
			var isDelta = inputBuffer.GetBits( 1 );

			// One byte literal
			if ( isDelta == 0 )
			{
				var literalValue = inputBuffer.GetBits( 8 );
				outputBuffer.Write( (byte)literalValue );
			}
			else
			{
				int offset = (int)inputBuffer.GetBits( 12 );

				if ( offset == 0 )
					break;

				int length = (int)(inputBuffer.GetBits( 7 )) + 1;

				if ( offset > outputBuffer.BaseStream.Length )
					throw new Exception( "Offset > stream length" );

				long initialPosition = outputBuffer.BaseStream.Position;

				outputBuffer.BaseStream.Seek( initialPosition - offset, SeekOrigin.Begin );

				// A short copy needs only this prefix; long copies repeat the whole window.
				var window = new byte[Math.Min( offset, length )];
				for ( int i = 0; i < window.Length; ++i )
				{
					window[i] = (byte)outputBuffer.BaseStream.ReadByte();
				}

				outputBuffer.BaseStream.Seek( initialPosition, SeekOrigin.Begin );

				for ( int i = 0; i < length; ++i )
					outputBuffer.Write( window[i % offset] );
			}
		}

		return true;
	}
}
