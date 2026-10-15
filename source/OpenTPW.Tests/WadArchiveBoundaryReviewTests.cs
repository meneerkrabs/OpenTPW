using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class WadArchiveBoundaryReviewTests
{
	private static void Word( byte[] bytes, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );

	private static byte[] AliasedArchive()
	{
		var bytes = new byte[177];
		"DWFB"u8.CopyTo( bytes );
		Word( bytes, 4, 1 ); Word( bytes, 72, 2 ); Word( bytes, 76, 88 ); Word( bytes, 80, 80 );
		foreach ( var record in new[] { 88, 128 } )
		{
			Word( bytes, record + 4, 168 ); Word( bytes, record + 8, 6 );
			Word( bytes, record + 12, 174 ); Word( bytes, record + 16, 3 );
		}
		"a.bin\0"u8.CopyTo( bytes.AsSpan( 168 ) );
		new byte[] { 5, 6, 7 }.CopyTo( bytes, 174 );
		return bytes;
	}

	[TestMethod]
	public void AStreamStartingAfterAPrefixReadsOnlyItsRemainingArchive()
	{
		var bytes = AliasedArchive();
		var prefixed = new byte[23 + bytes.Length];
		Array.Fill( prefixed, (byte)0xA5, 0, 23 );
		bytes.CopyTo( prefixed, 23 );
		var source = new MemoryStream( prefixed ) { Position = 23 };
		using var archive = new WadArchive( source );
		CollectionAssert.AreEqual( bytes, archive.Buffer );
		CollectionAssert.AreEqual( new byte[] { 5, 6, 7 }, archive.GetFile( "a.bin" ).GetData() );
		Assert.IsFalse( source.CanRead, "The original consume-and-close ownership also applies at a nonzero position." );
	}

	[TestMethod]
	public void PhysicalAliasesAndPreviouslyIgnoredMetadataRemainAccepted()
	{
		var bytes = AliasedArchive();
		// These fields and the final filename byte were not validated by the existing reader.
		foreach ( var field in new[] { 4, 76, 80, 84, 112, 152 } )
			Word( bytes, field, uint.MaxValue );
		Word( bytes, 108, 3 ); Word( bytes, 148, 3 ); // Only type 4 selects RefPack.
		bytes[173] = (byte)'X';
		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllBytes( path, bytes );
			using var fromPath = new WadArchive( path );
			using var memory = new WadArchive( new MemoryStream( bytes ) );
			foreach ( var archive in new[] { fromPath, memory } )
			{
				var members = archive.Root.Children.OfType<WadArchiveFile>().ToArray();
				CollectionAssert.AreEqual( new[] { "a.bin", "a.bin" }, members.Select( item => item.Name ).ToArray() );
				var first = members[0].GetData();
				var second = members[1].GetData();
				CollectionAssert.AreEqual( new byte[] { 5, 6, 7 }, first );
				CollectionAssert.AreEqual( first, second );
				Assert.AreNotSame( first, second, "Aliased backing ranges retain independent member caches." );
				first[0] = 99;
				Assert.AreEqual( 5, (int)second[0] );
			}
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void OneByteEmptyNamesAndZeroLengthMembersAtEofRemainInRange()
	{
		var bytes = AliasedArchive();
		foreach ( var record in new[] { 88, 128 } )
		{
			Word( bytes, record + 4, (uint)bytes.Length - 1 ); Word( bytes, record + 8, 1 );
			Word( bytes, record + 12, (uint)bytes.Length ); Word( bytes, record + 16, 0 );
		}
		using var archive = new WadArchive( new MemoryStream( bytes ) );
		var members = archive.Root.Children.OfType<WadArchiveFile>().ToArray();
		Assert.AreEqual( 2, members.Length );
		foreach ( var member in members )
		{
			Assert.AreEqual( "", member.Name );
			CollectionAssert.AreEqual( Array.Empty<byte>(), member.GetData() );
		}
	}
}
