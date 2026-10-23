using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The economy digest's canonical form written from the park save's document (<see cref="WorldStateHash.CanonicalEconomy"/>) is the text the digest hashed before, built from a
/// JSON tree (the reference below), byte for byte; and the hasher's fast paths hash a text as hashing each character as a whole number does.
/// </summary>
[TestClass]
public class WorldStateHashCanonicalTests
{
	/// <summary>The digest's canonical text as it was made before: the save serialized to a JSON tree, the parts no simulation reads removed, the keys sorted, each value written alone.</summary>
	private static string Reference( JsonNode? save )
	{
		if ( save is JsonObject root )
		{
			root.Remove( nameof( ParkSaveFile.Data.AnalyserCaches ) );
			root.Remove( nameof( ParkSaveFile.Data.StatisticsHistory ) );
			if ( root[nameof( ParkSaveFile.Data.Research )] is JsonObject research )
				research.Remove( nameof( ParkSaveFile.ResearchData.RecentItems ) );
			if ( root[nameof( ParkSaveFile.Data.Staff )] is JsonObject staff )
				staff.Remove( nameof( ParkSaveFile.StaffData.RoleOrder ) );
			if ( root[nameof( ParkSaveFile.Data.Objectives )] is JsonObject objectives && objectives[nameof( ParkSaveFile.ObjectivesData.Current )] is JsonObject challenge )
				challenge.Remove( nameof( ActiveChallenge.RawValue ) );
		}
		return Canonical( save );
	}

	private static string Canonical( JsonNode? node ) => node switch
	{
		JsonObject item => "{" + string.Join( ",", item.OrderBy( pair => pair.Key, StringComparer.Ordinal ).Select( pair => JsonValue.Create( pair.Key ).ToJsonString() + ":" + Canonical( pair.Value ) ) ) + "}",
		JsonArray list => "[" + string.Join( ",", list.Select( Canonical ) ) + "]",
		null => "null",
		_ => node.ToJsonString()
	};

	private static string Written( byte[] save )
	{
		var text = new ArrayBufferWriter<byte>();
		WorldStateHash.CanonicalEconomy( save, text );
		return Encoding.UTF8.GetString( text.WrittenSpan );
	}

	/// <summary>Hashes as the hasher did before its fast paths: the length, then every character as a whole number.</summary>
	private static ulong ReferenceHash( string text )
	{
		var hash = new StateHasher();
		hash.Add( text.Length );
		foreach ( var character in text )
			hash.Add( (long)character );
		return hash.Value;
	}

	[TestMethod]
	public void AParkSavesCanonicalTextIsTheTreesTextAndHashesAsIt()
	{
		var plain = EconomyTestData.Park();
		var busy = EconomyTestData.Park();
		busy.Ledger.Post( LedgerCategory.OtherIncome, 123_456 );
		busy.QueueCheat( new CheatAction( CheatKind.Mode ) );
		busy.AdvanceDays( 40 );
		foreach ( var (name, economy) in new[] { ("plain", plain), ("after its first turns", busy) } )
		{
			var data = ParkSaveFile.Capture( economy );
			var expected = Reference( JsonSerializer.SerializeToNode( data ) );
			var save = JsonSerializer.SerializeToUtf8Bytes( data );
			Assert.AreEqual( expected, Written( save ), name );
			var text = new ArrayBufferWriter<byte>();
			WorldStateHash.CanonicalEconomy( save, text );
			var hash = new StateHasher();
			hash.AddAscii( text.WrittenSpan );
			Assert.AreEqual( ReferenceHash( expected ), hash.Value, $"{name}: the ASCII text hashes as its characters" );
		}
	}

	[TestMethod]
	public void EscapesNumbersAndLeftOutPartsAreWrittenAsTheTreeWroteThem()
	{
		// names and values the default encoder escapes (outside ASCII, HTML-sensitive, quotes, control characters, a surrogate pair), raw numbers, nesting, and every part the
		// digest leaves out, at its place and elsewhere
		var json = """
			{"zé":"café <b>&'+\"\\/ \u0001","a":1.50E+2,"Ångström":[true,false,null,{"k":"🎢"}],
			 "AnalyserCaches":{"x":1},"StatisticsHistory":[1,2],"Research":{"RecentItems":[1],"Done":2},"Staff":{"RoleOrder":[3],"Members":[]},
			 "Objectives":{"Current":{"RawValue":5,"Baseline":4},"RawValue":6},"Nested":{"RecentItems":7,"RoleOrder":8,"AnalyserCaches":9},"":0,"B":-0.0}
			""";
		var bytes = Encoding.UTF8.GetBytes( json );
		Assert.AreEqual( Reference( JsonNode.Parse( bytes ) ), Written( bytes ) );
		StringAssert.Contains( Written( bytes ), "\"Nested\":{\"AnalyserCaches\":9,\"RecentItems\":7,\"RoleOrder\":8}", "a left-out name elsewhere is kept" );
	}

	/// <summary>What the old tree-built text did with <paramref name="save"/>, and what the canonical writer does: the same text, or both refusing it.</summary>
	private static void SameOrBothRefused( byte[] save, string name, bool refused )
	{
		string? expected = null, written = null;
		Exception? treeError = null, writerError = null;
		try { expected = Reference( JsonNode.Parse( save ) ); }
		catch ( Exception error ) { treeError = error; }
		try { written = Written( save ); }
		catch ( Exception error ) { writerError = error; }
		Assert.AreEqual( refused, treeError != null, $"{name}: the tree {(treeError == null ? "took it" : $"refused it: {treeError.Message}")}" );
		Assert.AreEqual( treeError == null, writerError == null, $"{name}: the writer {(writerError == null ? "took it" : $"refused it: {writerError.Message}")}" );
		if ( treeError != null )
			Assert.IsInstanceOfType( writerError, treeError.GetType(), name );
		else
			Assert.AreEqual( expected, written, name );
	}

	[TestMethod]
	public void ANameTwiceInAnObjectIsRefusedWhereTheTreeRefusedIt()
	{
		// two counters whose keys differ only in an unpaired surrogate: the default encoder writes both as U+FFFD, so the save names one key twice
		var economy = EconomyTestData.Park();
		economy.Counters.Max( "\uD800", 1 );
		economy.Counters.Max( "\uFFFD", 2 );
		var save = JsonSerializer.SerializeToUtf8Bytes( ParkSaveFile.Capture( economy ) );
		SameOrBothRefused( save, "a park's counters", refused: true );
		Assert.ThrowsException<ArgumentException>( () => WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
		// a name twice anywhere the tree reads, a left-out name among them; and inside a left-out part, which the tree never read
		SameOrBothRefused( Encoding.UTF8.GetBytes( "{\"a\":{\"x\":1,\"x\":2}}" ), "nested", refused: true );
		SameOrBothRefused( Encoding.UTF8.GetBytes( "{\"a\":[{\"x\":1},{\"y\":1,\"y\":1}]}" ), "in an array", refused: true );
		SameOrBothRefused( Encoding.UTF8.GetBytes( "{\"Research\":{\"RecentItems\":1,\"RecentItems\":2}}" ), "a left-out name twice", refused: true );
		SameOrBothRefused( Encoding.UTF8.GetBytes( "{\"AnalyserCaches\":{\"x\":1,\"x\":2},\"a\":1}" ), "inside a left-out part", refused: false );
		SameOrBothRefused( Encoding.UTF8.GetBytes( "{\"Staff\":{\"RoleOrder\":{\"y\":1,\"y\":2},\"Members\":[]}}" ), "inside a left-out member", refused: false );
	}

	[TestMethod]
	public void TheHashersFastPathsHashTextAsItsCharactersDo()
	{
		foreach ( var text in new[] { "", "a", "Theme Park", "café Å 中 🎢 \u0000￿", new string( (char)0x7f, 3 ) + "Āÿ" } )
		{
			var hash = new StateHasher();
			hash.Add( text );
			Assert.AreEqual( ReferenceHash( text ), hash.Value, text );
		}
		var ascii = "{\"Cash\":12,\"Name\":\"Jungle\"}";
		var bytes = new StateHasher();
		bytes.AddAscii( Encoding.ASCII.GetBytes( ascii ) );
		Assert.AreEqual( ReferenceHash( ascii ), bytes.Value );
	}
}
