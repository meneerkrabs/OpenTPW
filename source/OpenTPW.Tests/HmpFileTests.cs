using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The bounded strict <c>.hmp</c> height profile reader (docs/TRACKS.md, Q6 of the trace).</summary>
[TestClass]
public class HmpFileTests
{
	/// <summary>A well-formed file: width × height cells of 5 samples, the per-cell maxima and flags after the profile.</summary>
	private static byte[] Build( int width = 2, int height = 2, Action<byte[]>? mutate = null )
	{
		var profile = (width * 5) * (height * 5);
		var cells = width * height;
		var data = new byte[48 + profile + cells * 2];
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0 ), 3 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 2 ), 0xAB1E );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 100 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 ), (ushort)width );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), (ushort)height );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), 48 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), (uint)(48 + profile) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), (uint)(48 + profile + cells) );
		for ( var index = 0; index < 6; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 24 + index * 4 ), index < 3 ? index : 20f );
		for ( var index = 0; index < profile; index++ )
			data[48 + index] = (byte)(index % 23);
		for ( var index = 0; index < cells; index++ )
			data[48 + profile + index] = (byte)(40 + index);
		mutate?.Invoke( data );
		return data;
	}

	private static HmpFile Parse( byte[] data ) => new( new MemoryStream( data ) );

	[TestMethod]
	public void AWellFormedFileExposesItsHeaderAndSections()
	{
		var file = Parse( Build() );
		Assert.AreEqual( (3, 5, 100, 2, 2), (file.Version, file.SamplesPerCell, file.Scale, file.Width, file.Height) );
		Assert.AreEqual( (10, 10), (file.ProfileWidth, file.ProfileHeight) );
		Assert.AreEqual( 100, file.Profile.Length );
		Assert.AreEqual( 4, file.CellMaxima.Length );
		Assert.AreEqual( 4, file.CellFlags.Length );
		CollectionAssert.AreEqual( new[] { 0f, 1f, 2f, 20f, 20f, 20f }, file.BoundingBox.ToArray() );
		Assert.AreEqual( 0, file.Sample( 0, 0 ) );
		Assert.AreEqual( 13, file.Sample( 3, 1 ), "row-major: sample (3, 1) is index 13" );
		Assert.AreEqual( 43, file.CellMaximum( 1, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => file.Sample( 10, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => file.CellMaximum( 2, 0 ) );
		var station = Parse( Build( 4, 4 ) );
		Assert.AreEqual( (4, 4, 400, 16, 16), (station.Width, station.Height, station.Profile.Length, station.CellMaxima.Length, station.CellFlags.Length), "a 4 × 4 station profile" );
	}

	[TestMethod]
	public void MalformedFilesAreRejectedWithBoundedReads()
	{
		Assert.ThrowsException<InvalidDataException>( () => Parse( new byte[10] ), "shorter than the header" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Array.Empty<byte>() ) );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[2] = 0 ) ), "bad magic" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[0] = 4 ) ), "bad version" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[4] = 0 ) ), "zero samples per cell" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[4] = 17 ) ), "too many samples per cell" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[8] = 0 ) ), "zero width" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), 4000 ) ) ), "a huge height" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[12] = 49 ) ), "the body does not start after the header" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[16]++ ) ), "the maxima offset does not follow the profile" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => data[20]-- ) ), "the flags offset does not follow the maxima" );
		var truncated = Build();
		Assert.ThrowsException<InvalidDataException>( () => Parse( truncated[..^1] ), "a missing flag byte" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( truncated.Concat( new byte[] { 0 } ).ToArray() ), "a trailing byte" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( mutate: data => BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 24 ), float.NaN ) ) ), "a non-finite bounding box" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( new byte[HmpFile.MaximumFileBytes + 1] ), "a file over the byte limit is refused before it is parsed" );
		Assert.ThrowsException<ArgumentNullException>( () => new HmpFile( (Stream)null! ) );
	}
}
