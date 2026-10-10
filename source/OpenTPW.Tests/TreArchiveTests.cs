using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>TREE archives of the original autorun launcher (docs/AUTORUN.md): synthetic layout, hashing and codecs; private CD data behind OPENTPW_GAME_PATH.</summary>
[TestClass]
public class TreArchiveTests
{
	private enum Mode { Stored, Refpack, Pkware }

	/// <summary>Minimal TREE writer: heap-ordered search tree of hashes, descriptors, then the data.</summary>
	private static byte[] Build( IReadOnlyList<(uint Hash, byte[] Stored, uint Full, Mode Mode)> entries, uint version = 1, byte[]? collisions = null, uint? collidingHash = null )
	{
		var sorted = entries.Select( ( entry, index ) => (entry.Hash, Index: (uint)index) ).OrderBy( pair => pair.Hash ).ToList();
		if ( collidingHash != null )
			sorted.Add( (collidingHash.Value, 0xFFFFFFFF) );
		sorted = sorted.OrderBy( pair => pair.Hash ).ToList();
		var nodes = new List<(uint Hash, uint Index)>();
		void Place( int low, int high, int node )
		{
			if ( low >= high )
				return;
			while ( nodes.Count <= node )
				nodes.Add( (0xFFFFFFFF, 0xFFFFFFFF) );
			var middle = (low + high) / 2;
			nodes[node] = sorted[middle];
			Place( low, middle, node * 2 + 1 );
			Place( middle + 1, high, node * 2 + 2 );
		}
		Place( 0, sorted.Count, 0 );
		// A complete tree: 2^depth - 1 slots.
		var slots = 1;
		while ( slots < nodes.Count )
			slots = slots * 2 + 1;
		while ( nodes.Count < slots )
			nodes.Add( (0xFFFFFFFF, 0xFFFFFFFF) );
		collisions ??= [];
		const int headerSize = 32;
		var hashOffset = headerSize;
		var descriptorOffset = hashOffset + (nodes.Count + 1) * 8;
		var collisionOffset = descriptorOffset + entries.Count * 12;
		var dataOffset = collisionOffset + collisions.Length;
		using var memory = new MemoryStream();
		using var writer = new BinaryWriter( memory );
		writer.Write( 0x45455254u );
		writer.Write( version );
		writer.Write( (uint)entries.Count );
		writer.Write( (uint)descriptorOffset );
		writer.Write( (uint)nodes.Count );
		writer.Write( (uint)hashOffset );
		writer.Write( (uint)collisions.Length );
		writer.Write( (uint)collisionOffset );
		foreach ( var (hash, index) in nodes )
		{
			writer.Write( hash );
			writer.Write( index );
		}
		writer.Write( 0u );
		writer.Write( 0u );
		var position = dataOffset;
		foreach ( var entry in entries )
		{
			writer.Write( (uint)position );
			writer.Write( entry.Mode switch { Mode.Stored => entry.Full, Mode.Refpack => (uint)entry.Stored.Length | 0xC0000000u, _ => (uint)entry.Stored.Length } );
			writer.Write( entry.Full );
			position += entry.Stored.Length;
		}
		writer.Write( collisions );
		foreach ( var entry in entries )
			writer.Write( entry.Stored );
		return memory.ToArray();
	}

	private static byte[] Ascii( string text ) => Encoding.ASCII.GetBytes( text );

	[TestMethod]
	public void HashMatchesTheOriginalNames()
	{
		// Values stored in the original CD's English.tre / general.tre / autorun.tre (names are not stored there).
		Assert.AreEqual( 0xA9A487B1u, TreArchive.Hash( @".\autorun\play.bmp" ) );
		Assert.AreEqual( 0xBF3C537Au, TreArchive.Hash( @".\autorun\install.bmp" ) );
		Assert.AreEqual( 0x40B9B897u, TreArchive.Hash( @".\AUTORUN\BACK.BMP" ) );
		Assert.AreEqual( 0xBA07B3D9u, TreArchive.Hash( @".\general\backgrnd.bmp" ) );
		Assert.AreEqual( 0x358A7540u, TreArchive.Hash( "autorun.cfg" ) );
	}

	[TestMethod]
	public void FindsStoredEntriesCaseInsensitively()
	{
		var names = new[] { @".\autorun\a.bmp", @".\autorun\b.bmp", @".\general\c.bmp", "autorun.cfg", @".\autorun\d.bmp" };
		var entries = names.Select( ( name, index ) => (TreArchive.Hash( name ), Ascii( $"payload {index}" ), (uint)$"payload {index}".Length, Mode.Stored) ).ToList();
		var archive = new TreArchive( Build( entries ) );
		Assert.AreEqual( names.Length, archive.Entries.Count );
		for ( var index = 0; index < names.Length; index++ )
		{
			Assert.IsTrue( archive.TryFind( names[index].ToUpperInvariant(), out var entry ), names[index] );
			Assert.AreEqual( index, entry.Index );
			Assert.AreEqual( TreCompressionKind.Stored, entry.Compression );
			CollectionAssert.AreEqual( Ascii( $"payload {index}" ), archive.Read( entry ) );
		}
		Assert.IsFalse( archive.TryFind( @".\autorun\missing.bmp", out _ ) );
		Assert.IsNull( archive.Read( "nothing.txt" ) );
		Assert.AreEqual( names.Length, archive.HashNodes().Count() );
	}

	[TestMethod]
	public void RefpackCommandForms()
	{
		// 4 literals, 1-byte form (dist 4, len 3), 3-byte form (dist 7, len 5), 4-byte form (dist 12, len 5),
		// then an overlapping copy (dist 1, len 6), a 40-literal run and a stop with one trailing literal.
		var stream = new List<byte> { 0xE0, (byte)'A', (byte)'B', (byte)'C', (byte)'D' };
		var expected = new List<byte>( Ascii( "ABCD" ) );
		void Copy( int distance, int length )
		{
			for ( var index = 0; index < length; index++ )
				expected.Add( expected[^distance] );
		}
		stream.AddRange( [0x00, 3] );
		Copy( 4, 3 );
		stream.AddRange( [0x81, 0x00, 6] );
		Copy( 7, 5 );
		stream.AddRange( [0xC0, 0x00, 11, 0x00] );
		Copy( 12, 5 );
		stream.AddRange( [0x01, 0x00, (byte)'Q'] ); // one literal, then distance 1, length 3
		expected.Add( (byte)'Q' );
		Copy( 1, 3 );
		var forty = Ascii( new string( 'z', 40 ) );
		stream.Add( (byte)(0xE0 + (40 - 4) / 4 ) );
		stream.AddRange( forty );
		expected.AddRange( forty );
		stream.AddRange( [0xFD, (byte)'!'] );
		expected.Add( (byte)'!' );
		CollectionAssert.AreEqual( expected.ToArray(), TreCompression.Refpack( stream.ToArray(), expected.Count ) );
		Assert.ThrowsException<InvalidDataException>( () => TreCompression.Refpack( stream.ToArray(), expected.Count + 1 ) );
		Assert.ThrowsException<InvalidDataException>( () => TreCompression.Refpack( stream.Take( 8 ).ToArray(), expected.Count ) );
		Assert.ThrowsException<InvalidDataException>( () => TreCompression.Refpack( new byte[] { 0x00, 0x05, 0xFC }, 3 ) ); // distance before the start
	}

	[TestMethod]
	public void PkwareExplodesTheReferenceVector()
	{
		// The test vector of PKWARE DCL "blast" reference decoders.
		var packed = new byte[] { 0x00, 0x04, 0x82, 0x24, 0x25, 0x8F, 0x80, 0x7F };
		CollectionAssert.AreEqual( Ascii( "AIAIAIAIAIAIA" ), TreCompression.Explode( packed, 13 ) );
		Assert.ThrowsException<InvalidDataException>( () => TreCompression.Explode( packed, 12 ) );
		Assert.ThrowsException<InvalidDataException>( () => TreCompression.Explode( new byte[] { 0x07, 0x04, 0 }, 1 ) );
	}

	[TestMethod]
	public void ReadsEveryCompressionKind()
	{
		var text = Ascii( "ABCDABC" );
		var refpack = new byte[] { 0xE0, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0x00, 3, 0xFC };
		var pkware = new byte[] { 0x00, 0x04, 0x82, 0x24, 0x25, 0x8F, 0x80, 0x7F };
		var entries = new List<(uint, byte[], uint, Mode)>
		{
			(TreArchive.Hash( "one" ), text, (uint)text.Length, Mode.Stored),
			(TreArchive.Hash( "two" ), refpack, 7u, Mode.Refpack),
			(TreArchive.Hash( "three" ), pkware, 13u, Mode.Pkware),
		};
		var archive = new TreArchive( Build( entries ) );
		CollectionAssert.AreEqual( text, archive.Read( "ONE" ) );
		CollectionAssert.AreEqual( text, archive.Read( "Two" ) );
		CollectionAssert.AreEqual( Ascii( "AIAIAIAIAIAIA" ), archive.Read( "three" ) );
		Assert.AreEqual( TreCompressionKind.Refpack, archive.Entries[1].Compression );
		Assert.AreEqual( TreCompressionKind.Pkware, archive.Entries[2].Compression );
		Assert.AreEqual( 0xC0000000u, archive.Entries[1].Flags );
	}

	[TestMethod]
	public void CollidingNamesUseTheCollisionList()
	{
		var hash = TreArchive.Hash( "first" );
		var entries = new List<(uint, byte[], uint, Mode)>
		{
			(hash, Ascii( "1" ), 1u, Mode.Stored),
			(TreArchive.Hash( "other" ), Ascii( "2" ), 1u, Mode.Stored),
		};
		var list = new List<byte>();
		foreach ( var (name, index) in new[] { ("FIRST", 0u), ("SECOND", 1u) } )
		{
			list.Add( (byte)name.Length );
			list.AddRange( Ascii( name ) );
			list.AddRange( BitConverter.GetBytes( index ) );
		}
		var archive = new TreArchive( Build( entries, collisions: list.ToArray(), collidingHash: TreArchive.Hash( "second" ) ) );
		Assert.IsTrue( archive.TryFind( "second", out var entry ) );
		Assert.AreEqual( 1, entry.Index );
	}

	[TestMethod]
	public void TruncatedCollisionRecordDoesNotReadFollowingData()
	{
		var hash = TreArchive.Hash( "second" );
		var entries = new List<(uint, byte[], uint, Mode)> { (TreArchive.Hash( "first" ), Ascii( "1" ), 1u, Mode.Stored) };
		// The declared list holds only "SECOND" and a 2-byte stub of the index; the payload after it must not complete it.
		var list = new List<byte> { 6 };
		list.AddRange( Ascii( "SECOND" ) );
		list.AddRange( [0, 0] );
		var archive = new TreArchive( Build( entries, collisions: list.ToArray(), collidingHash: hash ) );
		Assert.IsFalse( archive.TryFind( "second", out _ ) );
	}

	[TestMethod]
	public void RejectsMalformedArchives()
	{
		var good = Build( [(TreArchive.Hash( "x" ), Ascii( "abc" ), 3u, Mode.Stored)] );
		Assert.ThrowsException<InvalidDataException>( () => new TreArchive( new byte[10] ) );
		var wrongMagic = (byte[])good.Clone();
		wrongMagic[0] = (byte)'X';
		Assert.ThrowsException<InvalidDataException>( () => new TreArchive( wrongMagic ) );
		Assert.ThrowsException<InvalidDataException>( () => new TreArchive( Build( [(TreArchive.Hash( "x" ), Ascii( "abc" ), 3u, Mode.Stored)], version: 2 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => new TreArchive( good.Take( good.Length - 1 ).ToArray() ) ); // the entry now runs past the end
		var hugeCount = (byte[])good.Clone();
		BitConverter.GetBytes( 0xFFFFFFu ).CopyTo( hugeCount, 8 );
		Assert.ThrowsException<InvalidDataException>( () => new TreArchive( hugeCount ) );
	}

	private static string? FindAutorunFolder()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		foreach ( var root in new[] { gamePath, Path.GetDirectoryName( gamePath.TrimEnd( Path.DirectorySeparatorChar ) ) ?? gamePath } )
		{
			var folder = Directory.EnumerateDirectories( root ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "autorun", StringComparison.OrdinalIgnoreCase ) );
			if ( folder != null )
				return folder;
		}
		return null;
	}

	private static string FileIgnoringCase( string folder, string name ) =>
		Directory.EnumerateFiles( folder ).First( path => string.Equals( Path.GetFileName( path ), name, StringComparison.OrdinalIgnoreCase ) );

	[TestMethod]
	public void ReadsTheOriginalCdArchives()
	{
		var folder = FindAutorunFolder();
		if ( folder == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to a game or CD folder with an Autorun folder to run the original autorun archive tests." );
		var script = new TreArchive( File.ReadAllBytes( FileIgnoringCase( folder!, "autorun.tre" ) ) );
		var cfg = script.Read( "autorun.cfg" );
		Assert.IsNotNull( cfg );
		Assert.AreEqual( TreCompressionKind.Refpack, script.Entries.Single().Compression );
		StringAssert.StartsWith( Encoding.ASCII.GetString( cfg!, 0, 19 ), "// Autorun for DKII" );
		StringAssert.Contains( Encoding.ASCII.GetString( cfg ), "nvPlayY.value" );

		var back = new TreArchive( File.ReadAllBytes( FileIgnoringCase( folder!, "general.tre" ) ) ).Read( @".\general\backgrnd.bmp" );
		Assert.IsNotNull( back );
		Assert.AreEqual( 'B', (char)back![0] );
		Assert.AreEqual( 640, BitConverter.ToInt32( back, 18 ) );
		Assert.AreEqual( 480, BitConverter.ToInt32( back, 22 ) );

		foreach ( var languagePath in Directory.EnumerateFiles( folder ).Where( path => path.EndsWith( ".tre", StringComparison.OrdinalIgnoreCase ) && !path.EndsWith( "general.tre", StringComparison.OrdinalIgnoreCase ) && !path.EndsWith( "autorun.tre", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var language = new TreArchive( File.ReadAllBytes( languagePath ) );
			foreach ( var name in new[] { "play", "install", "uninst", "reinst", "readme", "tech", "quit", "back" } )
				Assert.IsTrue( language.TryFind( $@".\autorun\{name}.bmp", out _ ), $"{Path.GetFileName( languagePath )} {name}" );
			Assert.AreEqual( 640, BitConverter.ToInt32( language.Read( @".\autorun\back.bmp" )!, 18 ) );
		}
	}
}
