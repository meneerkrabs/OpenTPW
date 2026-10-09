using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TextCodecTests
{
	private static byte[] Bfmu( params char[] characters )
	{
		var data = new List<byte> { (byte)'B', (byte)'F', (byte)'M', (byte)'U', 0, 0, (byte)characters.Length, 0 };
		foreach ( var character in characters )
			data.AddRange( BitConverter.GetBytes( (ushort)character ) );
		return data.ToArray();
	}

	private static byte[] Bfum( params (char First, ushort[] Indices, ushort Filler)[] ranges )
	{
		var data = new List<byte> { (byte)'B', (byte)'F', (byte)'U', (byte)'M' };
		foreach ( var (first, indices, filler) in ranges )
		{
			data.AddRange( BitConverter.GetBytes( (ushort)first ) );
			data.AddRange( BitConverter.GetBytes( filler ) );
			data.AddRange( BitConverter.GetBytes( (ushort)(first + indices.Length - 1) ) );
			data.AddRange( BitConverter.GetBytes( filler ) );
			foreach ( var index in indices )
				data.AddRange( BitConverter.GetBytes( index ) );
		}
		return data.ToArray();
	}

	[TestMethod]
	public void ReadsRangesAndKeepsOnlyRoundTrippingMappings()
	{
		// Codepage: 1 'A', 2 'B', 3 '?', 4 'é'; 'C' maps to filler index 2 (which decodes to 'B').
		var decode = new BFMUReader( new MemoryStream( Bfmu( 'A', 'B', '?', 'é' ) ) );
		var encode = new BFUMReader( new MemoryStream( Bfum( ('?', new ushort[] { 3 }, 0), ('A', new ushort[] { 1, 2, 2 }, 0x1F6), ('é', new ushort[] { 4 }, 0) ) ) );
		CollectionAssert.AreEqual( new[] { ('?', '?'), ('A', 'C'), ('é', 'é') }, encode.Ranges.ToArray() );
		Assert.AreEqual( 5, encode.Entries.Count );
		var codec = new GameTextCodec( decode, encode );
		Assert.IsTrue( codec.CanEncode( 'é' ) );
		Assert.IsFalse( codec.CanEncode( 'C' ), "filler entries do not round-trip" );
		CollectionAssert.AreEqual( new[] { 'A', 'B', '?', 'é' }, codec.Repertoire.ToArray() );
		var unmappable = new List<char>();
		CollectionAssert.AreEqual( new byte[] { 1, 4, 3, 2 }, codec.Encode( "AéCB", unmappable ) );
		CollectionAssert.AreEqual( new[] { 'C' }, unmappable );
		Assert.AreEqual( "AéB", codec.Decode( new byte[] { 1, 4, 2 } ) );
	}

	[TestMethod]
	public void RejectsMalformedTables()
	{
		Assert.ThrowsException<InvalidDataException>( () => new BFUMReader( new MemoryStream( "BFMU\0\0\0\0"u8.ToArray() ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new BFUMReader( new MemoryStream( "BFUM"u8.ToArray() ) ) );
		var truncated = Bfum( ('A', new ushort[] { 1, 2, 3 }, 0) );
		Assert.ThrowsException<InvalidDataException>( () => new BFUMReader( new MemoryStream( truncated[..^2] ) ) );
		var reversed = Bfum( ('B', new ushort[] { 1 }, 0) );
		reversed[8] = (byte)'A';
		Assert.ThrowsException<InvalidDataException>( () => new BFUMReader( new MemoryStream( reversed ) ) );
	}

	[DataTestMethod]
	[DynamicData( nameof( LanguageTests.ShippedLanguages ), typeof( LanguageTests ) )]
	public void OriginalTablesRoundTripEveryCharacterAndString( string name )
	{
		var language = LanguageTests.OriginalLanguagePublic( name );
		var codec = GameTextCodec.Load( language );
		var table = language.CharacterTable.Characters;
		// Every codepage character encodes back to its own index (the tables are inverses).
		for ( var index = 1; index <= table.Count; index++ )
		{
			Assert.IsTrue( codec.CanEncode( table[index - 1] ), $"{name}: U+{(int)table[index - 1]:X4} (index {index})" );
			CollectionAssert.AreEqual( new[] { (byte)index }, codec.Encode( table[index - 1].ToString() ) );
		}
		Assert.AreEqual( table.Count, codec.Repertoire.Count );
		// Every string of every table survives Unicode -> codepage -> Unicode.
		foreach ( var file in language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) ) )
		{
			foreach ( var entry in language.LoadStrings( Path.GetFileName( file ) ).Entries )
			{
				var unmappable = new List<char>();
				Assert.AreEqual( entry, codec.Decode( codec.Encode( entry, unmappable ) ), $"{name} {Path.GetFileName( file )}" );
				Assert.AreEqual( 0, unmappable.Count );
			}
		}
		var euro = new List<char>();
		Assert.AreEqual( "Park ?", codec.Decode( codec.Encode( "Park €", euro ) ), "unrepresentable characters become '?'" );
		CollectionAssert.AreEqual( new[] { '€' }, euro );
	}
}
