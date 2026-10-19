using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic native TPW ring import/export, independent of any original game assets or execution.</summary>
[TestClass]
public class OriginalRecentResearchTests
{
	private static SaveThing Thing( int bytes, int offset = 0 ) => new( 13, SaveThingModel.ParkAnalyser, 0, 0, offset, bytes );
	private static string State( IReadOnlyList<RecentResearchRing> rings ) => JsonSerializer.Serialize( rings );

	[TestMethod]
	public void ConstructorAndCodecAgreeAndOnlyTheExactRingRangeIsWritten()
	{
		var body = SaveNewParkThings.ParkAnalyser( 4, 6 );
		Assert.AreEqual( 62_492, SaveRecentResearch.BodyOffset );
		Assert.AreEqual( 68, SaveRecentResearch.Bytes );
		Assert.AreEqual( State( SaveRecentResearch.Empty() ), State( SaveRecentResearch.Read( body, Thing( body.Length ) ) ) );
		var payload = Enumerable.Repeat( (byte)0xA5, 16 ).Concat( body ).Concat( Enumerable.Repeat( (byte)0x5A, 32 ) ).ToArray();
		var before = payload.ToArray();
		var thing = Thing( body.Length, 16 );
		var rings = RecentResearchTests.RawRings();
		SaveRecentResearch.Write( payload, thing, rings );
		Assert.AreEqual( State( rings ), State( SaveRecentResearch.Read( payload, thing ) ) );
		var first = 16 + SaveRecentResearch.BodyOffset;
		for ( var index = 0; index < payload.Length; index++ )
			if ( index < first || index >= first + SaveRecentResearch.Bytes )
				Assert.AreEqual( before[index], payload[index], $"unrelated byte {index}" );
		var roundtrip = payload.ToArray();
		SaveRecentResearch.Write( roundtrip, thing, SaveRecentResearch.Read( payload, thing ) );
		CollectionAssert.AreEqual( payload, roundtrip, "scratch word and inactive entries round-trip exactly" );
		Assert.AreEqual( (ushort)0xBEEF, BitConverter.ToUInt16( payload, first + 2 * 17 + 9 ) );
	}

	[TestMethod]
	public void MalformedRingsAreRejectedBeforeAnyByteIsChanged()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		var before = body.ToArray();
		var invalid = new[]
		{
			new RecentResearchRing( -2, 3, 0, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 3, 3, 0, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, int.MaxValue, 0, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, 0, 0, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, 3, 2, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( -1, 3, 1, 0, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, -1, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, 65536, new[] { 0, 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, 0, new[] { -1, 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, 0, new[] { 65536, 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, 0, new[] { 0, 0 } ),
			new RecentResearchRing( 0, 3, 0, 0, null! ),
			null!,
		};
		foreach ( var bad in invalid )
		{
			var rings = RecentResearchTests.RawRings(); rings[3] = bad;
			Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Write( body, Thing( body.Length ), rings ) );
			CollectionAssert.AreEqual( before, body, "even an invalid final ring must not partially patch earlier rings" );
			Assert.ThrowsException<InvalidDataException>( () => RecentResearchTests.Park().Research.RestoreRecentItems( rings ) );
		}
		Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Validate( null! ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Validate( Array.Empty<RecentResearchRing>() ) );
	}

	[TestMethod]
	public void NativeReaderValidatesCapacityFlagsHeadModelAndBounds()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		foreach ( var (offset, value) in new[] { (0, -2), (0, 3), (4, 0), (4, int.MaxValue), (8, 2), (8, 1) } )
		{
			var bad = body.ToArray();
			if ( offset == 8 ) bad[SaveRecentResearch.BodyOffset + offset] = (byte)value;
			else BitConverter.GetBytes( value ).CopyTo( bad, SaveRecentResearch.BodyOffset + offset );
			Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Read( bad, Thing( bad.Length ) ) );
		}
		foreach ( var thing in new[]
		{
			Thing( body.Length ) with { Model = SaveThingModel.Bank },
			Thing( SaveRecentResearch.BodyOffset + SaveRecentResearch.Bytes - 1 ),
			Thing( body.Length, -1 ), Thing( body.Length, int.MaxValue ), Thing( int.MaxValue ),
		} )
		{
			Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Read( body, thing ) );
			Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Write( body, thing, SaveRecentResearch.Empty() ) );
		}
		Assert.ThrowsException<InvalidDataException>( () => SaveRecentResearch.Read( body[..^1], Thing( body.Length ) ) );
	}

	private static OriginalSavePayload Template( BalanceSettings settings )
	{
		var template = OriginalSavePayloadFixtures.Blank( settings );
		// The payload model stores the used/free/first-free counters after the length word.
		template.RideSystem = SaveTestPayloads.EmptyRideSystem[4..];
		var things = template.World.Things;
		things[0].Next = 2;
		things.Add( new SaveThingEntry { Id = 2, Next = 3, Model = SaveThingModel.ResearchLab, Body = new byte[SaveResearchLab.Bytes] } );
		things.Add( new SaveThingEntry { Id = 3, Next = 4, Model = SaveThingModel.ParkAnalyser, Body = SaveNewParkThings.ParkAnalyser( 0, 0 ) } );
		things.Add( new SaveThingEntry { Id = 4, Next = 5, Model = SaveThingModel.ChallengeSystem, Body = OriginalSaveImportTests.Challenges( settings ) } );
		// The first analyser is deliberately a decoy: the World variable names id 5.
		var analyser = SaveNewParkThings.ParkAnalyser( 0, 0 ); analyser[28] = 99; analyser[^1] = 77;
		things.Add( new SaveThingEntry { Id = 5, Next = 0, Model = SaveThingModel.ParkAnalyser, Body = analyser } );
		template.World.Variables = template.World.Variables with { ResearchLab = 2, ParkAnalyser = 5 };
		return template;
	}

	[TestMethod]
	public void ExportAndImportUseTheReferencedAnalyserAndPreserveTheRemainder()
	{
		var settings = OriginalSaveImportTests.Settings();
		var park = RecentResearchTests.Park( settings: settings );
		park.Research.RestoreRecentItems( RecentResearchTests.RawRings() );
		var template = Template( settings ); var bytes = template.Write();
		var originalAnalyser = template.World.Things.Single( thing => thing.Id == 5 ).Body;
		var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( park ), out var exportEvidence, new OriginalParkExportOptions( Research: park.Research ) );
		CollectionAssert.AreEqual( bytes, template.Write(), "the input template is never modified" );
		var analyser = exported.World.Things.Single( thing => thing.Id == 5 ).Body;
		CollectionAssert.AreEqual( template.World.Things.Single( thing => thing.Id == 3 ).Body, exported.World.Things.Single( thing => thing.Id == 3 ).Body, "the unreferenced analyser is untouched" );
		for ( var index = 0; index < analyser.Length; index++ )
			if ( index < SaveRecentResearch.BodyOffset || index >= SaveRecentResearch.BodyOffset + SaveRecentResearch.Bytes )
				Assert.AreEqual( originalAnalyser[index], analyser[index], $"unrelated analyser byte {index}" );
		var map = OriginalParkImportTests.CreateMap( 128, 128, ( _, _ ) => 0 );
		var imported = OriginalParkImport.Import( exported.Write(), map );
		Assert.AreEqual( State( park.Research.RecentItems ), State( imported.RecentResearch! ) );
		var restored = RecentResearchTests.Park( settings: settings );
		var report = OriginalEconomyImport.Apply( restored, imported );
		Assert.AreEqual( RecentResearchTests.State( park.Research ), RecentResearchTests.State( restored.Research ) );
		Assert.IsTrue( exportEvidence.Any( line => line.StartsWith( "Park analyser: four recent-research rings written", StringComparison.Ordinal ) ) );
		Assert.IsTrue( report.Evidence.Any( line => line.StartsWith( "Park analyser: four recent-research rings imported", StringComparison.Ordinal ) ) );
		var again = OriginalParkExport.Export( exported, ParkSaveFile.Capture( restored ), out _, new OriginalParkExportOptions( Research: restored.Research ) );
		CollectionAssert.AreEqual( analyser, again.World.Things.Single( thing => thing.Id == 5 ).Body );
	}

	[TestMethod]
	public void NoResearchOptionPreservesTemplateHistoryAndExplicitEmptyHistoryClearsIt()
	{
		var park = RecentResearchTests.Park( settings: OriginalSaveImportTests.Settings() );
		var template = Template( park.Settings );
		var analyser = template.World.Things.Single( thing => thing.Id == 5 );
		SaveRecentResearch.Write( analyser.Body, Thing( analyser.Body.Length ), RecentResearchTests.RawRings() );
		var unchanged = OriginalParkExport.Export( template, ParkSaveFile.Capture( park ), out _ );
		CollectionAssert.AreEqual( analyser.Body, unchanged.World.Things.Single( thing => thing.Id == 5 ).Body );
		var cleared = OriginalParkExport.Export( template, ParkSaveFile.Capture( park ), out _, new OriginalParkExportOptions( Research: park.Research ) );
		var body = cleared.World.Things.Single( thing => thing.Id == 5 ).Body;
		Assert.AreEqual( State( SaveRecentResearch.Empty() ), State( SaveRecentResearch.Read( body, Thing( body.Length ) ) ) );
		foreach ( var id in new ushort[] { 0, 1, 99 } )
		{
			var bad = Template( park.Settings ); bad.World.Variables = bad.World.Variables with { ParkAnalyser = id };
			var exportError = Assert.ThrowsException<InvalidDataException>( () => OriginalParkExport.Export( bad, ParkSaveFile.Capture( park ), out _, new OriginalParkExportOptions( Research: park.Research ) ) );
			// Both the typed World guard (ParkAnalyser) and ring codec (park analyser) name this boundary.
			StringAssert.Contains( exportError.Message.Replace( " ", "" ).ToLowerInvariant(), "parkanalyser" );
			if ( id != 0 )
			{
				var importError = Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.Import( bad.Write(), OriginalParkImportTests.CreateMap( 128, 128, ( _, _ ) => 0 ) ) );
				StringAssert.Contains( importError.Message.Replace( " ", "" ).ToLowerInvariant(), "parkanalyser" );
			}
		}
	}
}
