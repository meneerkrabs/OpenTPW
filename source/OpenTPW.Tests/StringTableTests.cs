using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class StringTableTests
{
	internal static byte[] CreateCharacterTable( string characters )
	{
		var data = new byte[8 + characters.Length * 2];
		Encoding.ASCII.GetBytes( "BFMU" ).CopyTo( data, 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), (ushort)characters.Length );
		for ( var i = 0; i < characters.Length; i++ )
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 + i * 2 ), characters[i] );
		return data;
	}

	/// <summary>
	/// Builds a BFST table whose strings are given as 1-based character-table indices.
	/// </summary>
	internal static byte[] CreateStringTable( params byte[][] strings )
	{
		var body = new List<byte>();
		var offsets = new List<int>();
		var start = strings.Length * 4;
		foreach ( var value in strings )
		{
			offsets.Add( start + body.Count );
			body.Add( 1 );
			body.Add( (byte)value.Length );
			body.Add( (byte)(value.Length >> 8) );
			body.Add( (byte)(value.Length >> 16) );
			body.AddRange( value );
			body.AddRange( new byte[4] );
		}
		var data = new byte[12 + start + body.Count];
		Encoding.ASCII.GetBytes( "BFST" ).CopyTo( data, 0 );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), strings.Length );
		for ( var i = 0; i < offsets.Count; i++ )
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 + i * 4 ), offsets[i] );
		body.CopyTo( data, 12 + start );
		return data;
	}

	private static byte[] Encode( string table, string text ) => text.Select( c => (byte)(table.IndexOf( c ) + 1) ).ToArray();

	[TestMethod]
	public void CharacterTableReadsSixteenBitCount()
	{
		var characters = string.Concat( Enumerable.Range( 0, 249 ).Select( i => (char)(0x100 + i) ) );
		var table = new BFMUReader( new MemoryStream( CreateCharacterTable( characters ) ) );
		Assert.AreEqual( 249, table.Characters.Count );
		Assert.AreEqual( (char)0x100, table.GetCharacter( 1 ) );
		Assert.AreEqual( (char)(0x100 + 248), table.GetCharacter( 249 ) );
		Assert.ThrowsException<InvalidDataException>( () => table.GetCharacter( 0 ) );
		Assert.ThrowsException<InvalidDataException>( () => table.GetCharacter( 250 ) );
	}

	[TestMethod]
	public void StringTableDecodesWithItsOwnCharacterTable()
	{
		const string first = "abcdefg\nø";
		const string second = "abcdefgø\n";
		var stringTable = CreateStringTable( Encode( first, "gø\nab" ) );
		Assert.AreEqual( "gø\nab", new StringFile( new MemoryStream( stringTable ), new BFMUReader( new MemoryStream( CreateCharacterTable( first ) ) ) )[0] );
		Assert.AreEqual( "g\nøab", new StringFile( new MemoryStream( stringTable ), new BFMUReader( new MemoryStream( CreateCharacterTable( second ) ) ) )[0] );
	}

	[TestMethod]
	public void StringLengthUsesAllThreeBytes()
	{
		const string characters = "xyz";
		var table = new BFMUReader( new MemoryStream( CreateCharacterTable( characters ) ) );
		var longText = new string( 'x', 300 ) + "z";
		var huge = new string( 'y', 70000 );
		var file = new StringFile( new MemoryStream( CreateStringTable( Encode( characters, longText ), Encode( characters, "" ), Encode( characters, huge ), Encode( characters, "zy" ) ) ), table );
		CollectionAssert.AreEqual( new[] { longText, "", huge, "zy" }, file.Entries );
	}

	[TestMethod]
	public void MalformedStringTablesAreRejected()
	{
		var table = new BFMUReader( new MemoryStream( CreateCharacterTable( "ab" ) ) );
		var outOfTable = CreateStringTable( new byte[] { 1, 3 } );
		Assert.ThrowsException<InvalidDataException>( () => new StringFile( new MemoryStream( outOfTable ), table ) );
		var truncated = CreateStringTable( new byte[] { 1, 2 } );
		truncated[12 + 4 + 1] = 200;
		Assert.ThrowsException<InvalidDataException>( () => new StringFile( new MemoryStream( truncated ), table ) );
		var marker = CreateStringTable( new byte[] { 1 } );
		marker[12 + 4] = 2;
		Assert.ThrowsException<InvalidDataException>( () => new StringFile( new MemoryStream( marker ), table ) );
		Assert.ThrowsException<InvalidDataException>( () => new BFMUReader( new MemoryStream( Encoding.ASCII.GetBytes( "BFMX\0\0\0\0" ) ) ) );
	}

	[TestMethod]
	public void PathConstructorUsesCharacterTableFromSameFolder()
	{
		var root = Path.Combine( Path.GetTempPath(), $"opentpw-strings-{Guid.NewGuid():N}" );
		var originalFileSystem = FileSystem;
		try
		{
			const string english = "abc";
			const string other = "cba";
			Directory.CreateDirectory( Path.Combine( root, "Language", "English" ) );
			Directory.CreateDirectory( Path.Combine( root, "Language", "Other" ) );
			File.WriteAllBytes( Path.Combine( root, "Language", "English", "MBToUni.dat" ), CreateCharacterTable( english ) );
			File.WriteAllBytes( Path.Combine( root, "Language", "Other", "MBToUni.dat" ), CreateCharacterTable( other ) );
			var strings = CreateStringTable( new byte[] { 1, 2, 3 } );
			File.WriteAllBytes( Path.Combine( root, "Language", "English", "T.str" ), strings );
			File.WriteAllBytes( Path.Combine( root, "Language", "Other", "T.str" ), strings );
			FileSystem = new BaseFileSystem( root );
			Assert.AreEqual( "abc", new StringFile( "Language/English/T.str" )[0] );
			Assert.AreEqual( "cba", new StringFile( "/Language/Other/T.str" )[0] );
		}
		finally
		{
			FileSystem = originalFileSystem;
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void OriginalEnglishLongStringsAreNotTruncated()
	{
		var directory = Path.Combine( OriginalDataDirectory(), "Language", "English" );
		if ( !Directory.Exists( directory ) )
			Assert.Inconclusive( "The installed English language folder is missing." );
		var table = new BFMUReader( new MemoryStream( File.ReadAllBytes( Path.Combine( directory, "MBToUni.dat" ) ) ) );
		var data = File.ReadAllBytes( Path.Combine( directory, "UITEXT.str" ) );
		var strings = new StringFile( new MemoryStream( data ), table );
		AssertLengthsMatchLayout( data, strings.Entries );
		// The 474-entry edition (Patch 2, the European CD) has one more message before these.
		if ( strings.Entries.Length == GameLanguage.UiTextEntries + 1 )
			strings.RemoveEntryAt( GameLanguage.UiTextEditionExtraEntry );
		CollectionAssert.AreEqual( new[] { 399, 416 }, Enumerable.Range( 0, strings.Entries.Length ).Where( i => strings[i].Length > 255 ).ToArray() );
		Assert.AreEqual( (262, 615), (strings[399].Length, strings[416].Length) );
		StringAssert.EndsWith( strings[399], "your original setting will be restored." );
	}

	/// <summary>
	/// Every decoded string must end at or before the next string record (records are followed by
	/// zero padding and sometimes extra small integers, so the gap varies).
	/// </summary>
	internal static void AssertLengthsMatchLayout( byte[] data, string[] entries )
	{
		var offsets = Enumerable.Range( 0, entries.Length ).Select( i => 12 + BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 12 + i * 4 ) ) ).ToArray();
		var sorted = offsets.Distinct().OrderBy( offset => offset ).ToArray();
		for ( var i = 0; i < entries.Length; i++ )
		{
			var next = sorted.FirstOrDefault( offset => offset > offsets[i], data.Length );
			var end = offsets[i] + 4 + entries[i].Length;
			Assert.IsTrue( end <= next, $"String {i} ({entries[i].Length} chars) ends at {end}; next record at {next}." );
		}
	}

	internal static string OriginalDataDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original language data." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}
}
