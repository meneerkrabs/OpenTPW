using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SaveAttractionListTests
{
	private sealed record Fields
	{
		public uint Handle { get; init; } = 12;
		public uint Class { get; init; } = SaveAttractionList.AttractionClass;
		public ushort PositionX { get; init; } = 51 * 256 + 128;
		public ushort PositionY { get; init; } = 23 * 256 + 128;
		public uint Angle { get; init; } = 90;
		public ushort InfoId { get; init; } = 1100;
		public uint[] Stamp { get; init; } = { 2000, 1, 1, 6, 15, 37, 30, 0 };
		public uint MeshInstance { get; init; } = 110;
		public string Line1 { get; init; } = "Belly";
		public string Line2 { get; init; } = "Bounce";
		public uint State { get; init; }
		public uint RingCount { get; init; } = 30;
		public byte RingFlag { get; init; } = 1;
		public uint Customers { get; init; } = 7;
		public uint WalkAways { get; init; } = 2;
		public byte Capacity { get; init; } = 5;
		public byte Duration { get; init; } = 30;
		public uint Speed { get; init; } = 60;
		public uint Price { get; init; } = 20;
		public uint QueueCells { get; init; } = 4;
		public float Gauge48 { get; init; } = 100f;
		public float LifeGauge { get; init; } = 97f;
		public float Repair { get; init; } = 64f;
		public uint Costs { get; init; } = 150;
		public uint Takings { get; init; } = 900;
		public byte Level { get; init; } = 1;
	}

	/// <summary>Envelope (handle, class) and a body in the original serializer's order.</summary>
	private static byte[] CreateRecord( Fields fields )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( fields.Handle );
		writer.Write( fields.Class );
		writer.Write( fields.PositionX );
		writer.Write( fields.PositionY );
		writer.Write( new byte[4] );
		writer.Write( fields.Angle );
		writer.Write( fields.InfoId );
		foreach ( var value in fields.Stamp )
			writer.Write( value );
		writer.Write( fields.MeshInstance );
		writer.Write( (ushort)0 );
		for ( var index = 0; index < SaveAttractionList.NameCharacters; index++ )
		{
			writer.Write( (ushort)(index < fields.Line1.Length ? fields.Line1[index] : 0) );
			writer.Write( (ushort)(index < fields.Line2.Length ? fields.Line2[index] : 0) );
		}
		writer.Write( new byte[8] );
		writer.Write( fields.State );
		writer.Write( new byte[24] );
		void Ring()
		{
			writer.Write( 1u );
			writer.Write( fields.RingCount );
			writer.Write( fields.RingFlag );
			writer.Write( 0u );
			writer.Write( new byte[(int)Math.Min( fields.RingCount, 64 ) * 4] );
		}
		Ring();
		Ring();
		writer.Write( fields.Customers );
		Ring();
		writer.Write( fields.WalkAways );
		Ring();
		Ring();
		Ring();
		writer.Write( fields.Capacity );
		writer.Write( fields.Duration );
		writer.Write( fields.Speed );
		writer.Write( (ushort)0 );
		writer.Write( new byte[12] );
		writer.Write( fields.Price );
		writer.Write( 0u );
		writer.Write( fields.QueueCells );
		writer.Write( fields.Gauge48 );
		writer.Write( fields.LifeGauge );
		writer.Write( fields.Repair );
		writer.Write( new byte[8] );
		writer.Write( fields.Costs );
		writer.Write( fields.Takings );
		writer.Write( 0u );
		writer.Write( fields.Level );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Opaque prefix holding <paramref name="prefixRecords"/>, then every known marker.</summary>
	private static byte[] CreatePayload( params byte[][] prefixRecords )
	{
		using var output = new MemoryStream();
		output.Write( Enumerable.Repeat( (byte)0xCD, 37 ).ToArray() );
		foreach ( var record in prefixRecords )
		{
			output.Write( record );
			output.Write( new byte[5] );
		}
		foreach ( var tag in SavePayloadLayout.ObservedEasymodeOrder )
		{
			output.Write( Encoding.ASCII.GetBytes( tag ) );
			output.Write( new byte[8] );
		}
		return output.ToArray();
	}

	private static System.Collections.Generic.IReadOnlyList<SaveAttraction> Parse( byte[] payload )
		=> SaveAttractionList.Parse( payload, SavePayloadLayout.Parse( payload ) );

	[TestMethod]
	public void DecodesAttractionFieldsInSerializerOrder()
	{
		var payload = CreatePayload( CreateRecord( new Fields() ) );
		var attraction = Parse( payload ).Single();
		Assert.AreEqual( 37 + SaveAttractionList.EnvelopeBytes, attraction.Offset );
		Assert.AreEqual( 12, attraction.Handle );
		Assert.AreEqual( 1100, attraction.InfoId );
		Assert.AreEqual( (51, 23), (attraction.CellX, attraction.CellY) );
		Assert.AreEqual( 90, attraction.Angle );
		Assert.AreEqual( new SaveTimestamp( 2000, 1, 1, 6, 15, 37 ), attraction.Timestamp );
		Assert.AreEqual( 110u, attraction.MeshInstanceId );
		Assert.AreEqual( ("Belly", "Bounce"), (attraction.NameLine1, attraction.NameLine2) );
		Assert.AreEqual( (5, 30, 60u), (attraction.OperatingCapacity, attraction.OperatingDuration, attraction.OperatingSpeed) );
		Assert.AreEqual( (20, 4), (attraction.PricePerUse, attraction.QueueSizeInCells) );
		Assert.AreEqual( (7u, 2u, 150, 900), (attraction.CustomerCount, attraction.WalkAwayCount, attraction.TotalCosts, attraction.TotalTakings) );
		Assert.AreEqual( (100f, 97f, 64f), (attraction.Gauge48, attraction.LifeGauge, attraction.StateOfRepair) );
		Assert.AreEqual( 1, attraction.UpgradeLevel );
		Assert.AreEqual( CreateRecord( new Fields() ).Length - SaveAttractionList.EnvelopeBytes, attraction.Length );
	}

	[TestMethod]
	public void ReadsConsecutiveRecordsAndSkipsOtherClasses()
	{
		var payload = CreatePayload(
			CreateRecord( new Fields { Handle = 14, InfoId = 1203 } ),
			CreateRecord( new Fields { Handle = 13, Class = 7, InfoId = 1303 } ),
			CreateRecord( new Fields { Handle = 12, InfoId = 1100, RingCount = 0 } ) );
		CollectionAssert.AreEqual( new[] { 1203, 1100 }, Parse( payload ).Select( attraction => attraction.InfoId ).ToArray() );
	}

	[TestMethod]
	public void RejectsRecordsOutsideTheOriginalBounds()
	{
		var invalid = new[]
		{
			new Fields { Repair = float.NaN },
			new Fields { LifeGauge = -1f },
			new Fields { Gauge48 = 256f },
			new Fields { Stamp = new uint[] { 2000, 13, 1, 6, 15, 37, 30, 0 } },
			new Fields { Stamp = new uint[] { 1899, 1, 1, 6, 15, 37, 30, 0 } },
			new Fields { Stamp = new uint[] { 2000, 1, 0, 6, 15, 37, 30, 0 } },
			new Fields { Stamp = new uint[] { 2000, 1, 1, 24, 15, 37, 30, 0 } },
			new Fields { Stamp = new uint[] { 2000, 1, 1, 6, 60, 37, 30, 0 } },
			new Fields { Stamp = new uint[] { 2000, 1, 1, 6, 15, 60, 30, 0 } },
			new Fields { State = ushort.MaxValue + 1u },
			new Fields { QueueCells = ushort.MaxValue + 1u },
			new Fields { RingFlag = 2 },
			new Fields { Handle = ushort.MaxValue + 1u },
			new Fields { Angle = 45 },
			new Fields { InfoId = 0 },
			new Fields { Price = SaveAttractionList.MaximumPricePerUse + 1 },
			new Fields { Level = SaveAttractionList.MaximumUpgradeLevel + 1 },
			new Fields { RingCount = SaveAttractionList.MaximumRingEntries + 1 },
			new Fields { Handle = 0 },
		};
		foreach ( var fields in invalid )
			Assert.AreEqual( 0, Parse( CreatePayload( CreateRecord( fields ) ) ).Count, fields.ToString() );
	}

	[TestMethod]
	public void KeepsOpaqueTimestampWordsAndLargeCounters()
	{
		var attraction = Parse( CreatePayload( CreateRecord( new Fields
		{
			Stamp = new uint[] { 2000, 1, 1, 6, 15, 37, 0x80000000, uint.MaxValue },
			Customers = 0x80000000,
			WalkAways = uint.MaxValue,
		} ) ) ).Single();
		Assert.AreEqual( (0x80000000u, uint.MaxValue), (attraction.CustomerCount, attraction.WalkAwayCount) );
	}

	/// <summary>
	/// Cuts mUpgradeBalloonSprite and mUpgradeLevel so that, read past the prefix, they would come from the
	/// first marker (unbounded) and its zero padding (level 0): only the prefix bound can reject the record.
	/// </summary>
	[TestMethod]
	public void RejectsRecordTruncatedByTheFirstMarker()
	{
		var record = CreateRecord( new Fields { Level = 0 } );
		using var output = new MemoryStream();
		output.Write( record.AsSpan( 0, record.Length - 5 ) );
		foreach ( var tag in SavePayloadLayout.ObservedEasymodeOrder )
		{
			output.Write( Encoding.ASCII.GetBytes( tag ) );
			output.Write( new byte[8] );
		}
		Assert.AreEqual( 0, Parse( output.ToArray() ).Count );
	}

	[TestMethod]
	public void RejectsMismatchedLayout()
	{
		var payload = CreatePayload( CreateRecord( new Fields() ) );
		var layout = SavePayloadLayout.Parse( payload );
		Assert.ThrowsException<ArgumentException>( () => SaveAttractionList.Parse( payload.AsSpan( 1 ), layout ) );
	}

	[TestMethod]
	public void OriginalEasymodeAttractionsMatchPlacedObjects()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle Easymode fixture." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var path = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		var payload = reader.ReadFile();
		var layout = SavePayloadLayout.Parse( payload );
		var attractions = SaveAttractionList.Parse( payload, layout );
		CollectionAssert.AreEqual( new[] { 28, 23, 22, 21, 20, 19, 18, 17, 16, 14, 13, 12, 11, 10 }, attractions.Select( attraction => attraction.Handle ).ToArray() );
		Assert.IsTrue( attractions.All( attraction => attraction.StateOfRepair == 100f && attraction.LifeGauge == 100f && attraction.Gauge48 == 100f ) );
		Assert.IsTrue( attractions.All( attraction => attraction.UpgradeLevel == 0 && attraction.CustomerCount == 0u && attraction.TotalTakings == 0 ) );
		var bouncy = attractions.Single( attraction => attraction.InfoId == 1100 );
		Assert.AreEqual( ("Belly", "Bounce", 51, 23), (bouncy.NameLine1, bouncy.NameLine2, bouncy.CellX, bouncy.CellY) );
		Assert.AreEqual( (5, 30, 60u), (bouncy.OperatingCapacity, bouncy.OperatingDuration, bouncy.OperatingSpeed) );
		// Every buildable SYSG record has an attraction with the same Info.Id in the same cell.
		var placed = SaveObjectList.Parse( payload, layout, 128, 128 ).Where( record => record.Kind == SaveObject.PlacedObjectKind ).ToList();
		Assert.AreEqual( 11, placed.Count );
		foreach ( var record in placed )
			Assert.IsTrue( attractions.Any( attraction => attraction.InfoId == record.InfoId && attraction.CellX == record.X && attraction.CellY == record.Y ), $"SYSG index {record.Index}" );
	}
}
