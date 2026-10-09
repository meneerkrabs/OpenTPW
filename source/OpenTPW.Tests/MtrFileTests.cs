using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MtrFileTests
{
	private static byte[] CreateMtr( uint[] table, string name = "s_test" )
	{
		var trailerOffset = MtrFile.HeaderBytes + table.Length * 4;
		var data = new byte[trailerOffset + MtrFile.TrailerBytes];
		MtrFile.Magic.CopyTo( data );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), 6 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), (uint)trailerOffset );
		for ( var index = 0; index < table.Length; index++ )
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( MtrFile.HeaderBytes + index * 4 ), table[index] );
		System.Text.Encoding.ASCII.GetBytes( name ).CopyTo( data, trailerOffset );
		var cursor = trailerOffset + MtrFile.NameBytes;
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( cursor ), 1 );
		cursor += 4;
		for ( var index = 0; index < MtrFile.TrailerFloatCount; index++, cursor += 4 )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( cursor ), index * 0.5f - 3f );
		var footer = new uint[] { 9, 24, 10, 7, 4 };
		foreach ( var word in footer )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( cursor ), word );
			cursor += 4;
		}
		return data;
	}

	[TestMethod]
	public void ReadsHeaderTableNameTrailerAndFooterWithoutClosingInput()
	{
		using var stream = new MemoryStream( CreateMtr( new uint[] { 1, 2, 2, 0 } ) );
		var mtr = new MtrFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( "s_test", mtr.Name );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 2, 0 }, mtr.Table.ToArray() );
		Assert.AreEqual( 144, mtr.TrailerFloats.Count );
		Assert.AreEqual( -3f, mtr.TrailerFloats[0] );
		Assert.AreEqual( 68.5f, mtr.TrailerFloats[^1] );
		CollectionAssert.AreEqual( new uint[] { 9, 24, 10, 7, 4 }, mtr.Footer.ToArray() );
	}

	[TestMethod]
	public void EmptyTableIsValid()
	{
		Assert.AreEqual( 0, new MtrFile( new MemoryStream( CreateMtr( Array.Empty<uint>() ) ) ).Table.Count );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 4 )]
	[DataRow( 891 )]
	public void RejectsTruncatedFile( int length )
	{
		var data = CreateMtr( Array.Empty<uint>() );
		Array.Resize( ref data, length );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsBadMagicAndTrailingBytes()
	{
		var data = CreateMtr( new uint[] { 1 } );
		data[0] = 0;
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( data ) ) );
		var extended = CreateMtr( new uint[] { 1 } );
		Array.Resize( ref extended, extended.Length + 4 );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( extended ) ) );
	}

	[DataTestMethod]
	[DataRow( 4, 7u )]
	[DataRow( 8, 0u )]
	[DataRow( 12, 2u )]
	[DataRow( 16, 1u )]
	public void RejectsUnobservedHeaderVariants( int offset, uint value )
	{
		var data = CreateMtr( new uint[] { 1 } );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), value );
		Assert.ThrowsException<NotSupportedException>( () => new MtrFile( new MemoryStream( data ) ) );
	}

	[DataTestMethod]
	[DataRow( 24 )]
	[DataRow( 28 )]
	[DataRow( 32 )]
	public void RejectsNonzeroReservedHeaderWords( int offset )
	{
		var data = CreateMtr( new uint[] { 1 } );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), 1 );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( data ) ) );
	}

	[DataTestMethod]
	[DataRow( 0u )]
	[DataRow( 35u )]
	[DataRow( 38u )]
	[DataRow( 40u )]
	[DataRow( uint.MaxValue )]
	public void RejectsInvalidTrailerOffsets( uint offset )
	{
		var data = CreateMtr( new uint[] { 1, 2 } );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), offset );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsEmptyUnterminatedPaddedOrNonPrintableNames()
	{
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( CreateMtr( new uint[] { 1 }, "" ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( CreateMtr( new uint[] { 1 }, new string( 'a', MtrFile.NameBytes ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( CreateMtr( new uint[] { 1 }, "ab\0c" ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( CreateMtr( new uint[] { 1 }, "a\tb" ) ) ) );
	}

	[TestMethod]
	public void RejectsUnobservedTrailerWordAndFooterWord()
	{
		var trailerWord = CreateMtr( new uint[] { 1 } );
		BinaryPrimitives.WriteUInt32LittleEndian( trailerWord.AsSpan( 40 + MtrFile.NameBytes ), 2 );
		Assert.ThrowsException<NotSupportedException>( () => new MtrFile( new MemoryStream( trailerWord ) ) );
		var footerWord = CreateMtr( new uint[] { 1 } );
		BinaryPrimitives.WriteUInt32LittleEndian( footerWord.AsSpan( footerWord.Length - 16 ), 25 );
		Assert.ThrowsException<NotSupportedException>( () => new MtrFile( new MemoryStream( footerWord ) ) );
	}

	[DataTestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void RejectsNonFiniteTrailerFloats( float value )
	{
		var data = CreateMtr( new uint[] { 1 } );
		BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 40 + MtrFile.NameBytes + 4 + 10 * 4 ), value );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void ReadsNonseekableShortReads()
	{
		using var stream = new ShortReadStream( CreateMtr( new uint[] { 5, 6 } ) );
		CollectionAssert.AreEqual( new uint[] { 5, 6 }, new MtrFile( stream ).Table.ToArray() );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[MtrFile.MaximumFileBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => new MtrFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 3 ) );
	}

	private sealed record Pin( string Name, int TableCount, uint[] Footer, long TableSum );

	// The nine distinct MTR files among the eleven ISO copies (Danish/Swedish bankrupt and
	// Danish/German paused are byte-identical).
	private static readonly Dictionary<string, Pin> Pins = new()
	{
		["ECC7F7EEDD85206041AFA3BA51E1ADDAE5E62EC1C206E035F64F4F2B4670A5B2"] = new( "s_bkrupt", 2508, new uint[] { 639, 24, 2580, 411, 5136 }, 289897 ),
		["6952FFCA0E32CF2CF5738E31C47B63AF4A3CE6DEED4686BAB2F612ADC644738B"] = new( "s_congrats", 4067, new uint[] { 1012, 24, 4072, 682, 8120 }, 800371 ),
		["6A27AE4733B6A760594DB657239F92BEF3E7B8C6F0B8B662094ECEB0C18C3E7B"] = new( "s_bkrupt", 2296, new uint[] { 575, 24, 2324, 383, 4624 }, 252277 ),
		["DE0773F79772BF301D0277BE31ED8C82707F0B2FC7C123587009E35094D638DE"] = new( "s_congrats", 1472, new uint[] { 397, 24, 1612, 227, 3200 }, 98284 ),
		["17EA1D42FF22F87ED63ECE9CC84BE6F866D78DCFA1A398ACCE4823FC6B9D48C1"] = new( "s_paused", 1426, new uint[] { 359, 24, 1460, 237, 2896 }, 97117 ),
		["7281EAE25EDBB8BB2B4C21528A14CB3EE3772CB40189FB0890AC1EEFBAF7DF2A"] = new( "s_bkrupt", 1178, new uint[] { 313, 24, 1276, 185, 2528 }, 63299 ),
		["93FBF87CF301DF996FDBE7C9816B6308E18C62EB58089F81340F465B403DB896"] = new( "s_congrats", 3304, new uint[] { 833, 24, 3356, 547, 6688 }, 530984 ),
		["715608A5046C11C767763F489444F3A9C310041F2C5B13233FF3C7C67F8503C3"] = new( "s_congrats", 3446, new uint[] { 871, 24, 3508, 569, 6992 }, 561621 ),
		["7273E32DBE6B8C4D8F99FADDB92BFEDFA04FDA72F9F2EDACB307427C47DCCD30"] = new( "s_paused", 1731, new uint[] { 432, 24, 1752, 290, 3480 }, 143148 ),
	};

	[TestMethod]
	public void OriginalIsoMtrFilesMatchPinnedStructure()
	{
		var directory = Environment.GetEnvironmentVariable( "OPENTPW_MTR_PATH" );
		if ( string.IsNullOrWhiteSpace( directory ) || !Directory.Exists( directory ) )
			Assert.Inconclusive( "Set OPENTPW_MTR_PATH to a directory with .mtr files extracted from the original ISO." );
		var files = Directory.EnumerateFiles( directory!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).Equals( ".mtr", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		if ( files.Length == 0 )
			Assert.Inconclusive( "OPENTPW_MTR_PATH contains no .mtr files." );
		foreach ( var file in files )
		{
			var data = File.ReadAllBytes( file );
			var hash = Convert.ToHexString( SHA256.HashData( data ) );
			Assert.IsTrue( Pins.TryGetValue( hash, out var pin ), $"Unpinned MTR {file} {hash}" );
			var mtr = new MtrFile( new MemoryStream( data ) );
			Assert.AreEqual( pin!.Name, mtr.Name, file );
			Assert.AreEqual( pin.TableCount, mtr.Table.Count, file );
			Assert.AreEqual( pin.TableSum, mtr.Table.Sum( value => (long)value ), file );
			CollectionAssert.AreEqual( pin.Footer, mtr.Footer.ToArray(), file );
			// Observed relations only; no meaning is assigned to them.
			Assert.IsTrue( mtr.Table.All( value => value < mtr.Footer[3] ), file );
			Assert.AreEqual( 2 * mtr.Footer[2] - 24, mtr.Footer[4], file );
		}
	}

	[DataTestMethod]
	[DataRow( "bankrupt", 411 )]
	[DataRow( "congrats", 682 )]
	public void InstalledEnglishMd2SharesMtrFooterCount( string name, int expected )
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the installed English MD2 files." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "Language", "English", name + ".MD2" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( $"Installed {name}.MD2 is missing." );
		var data = File.ReadAllBytes( path );
		// MD2 header u16 at 0x3E equals the English ISO MTR footer word 3 for the same mesh name.
		Assert.AreEqual( expected, BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 0x3e ) ) );
	}
}
