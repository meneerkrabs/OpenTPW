using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StaffSpriteMetadataTests
{
	private static StaffSpriteSet Load( Dictionary<string, byte[]> files ) => StaffSpriteSet.Load( "jungle",
		directory => files.Keys.Where( path => string.Equals( path[..path.LastIndexOf( '/' )], directory, StringComparison.Ordinal ) ),
		path => new MemoryStream( files[path], writable: false ) );

	private static void Add( Dictionary<string, byte[]> files, string directory, string stem )
	{
		foreach ( var pair in StaffSpriteDrawRecordTests.Files( stem ) ) files[$"{directory}/{stem}{pair.Key}"] = pair.Value;
	}

	[TestMethod]
	public void StaffKindMappingIsIndependentOfThingModelNumbering()
	{
		Assert.AreEqual( 4u, StaffSpriteMetadataCatalog.KindOf( StaffType.Entertainer ) );
		Assert.AreEqual( 5u, StaffSpriteMetadataCatalog.KindOf( StaffType.Handyman ) );
		Assert.AreEqual( 6u, StaffSpriteMetadataCatalog.KindOf( StaffType.Mechanic ) );
		Assert.AreEqual( 7u, StaffSpriteMetadataCatalog.KindOf( StaffType.Guard ) );
		Assert.AreEqual( 8u, StaffSpriteMetadataCatalog.KindOf( StaffType.Researcher ) );
	}

	[DataTestMethod]
	[DataRow( "missing-tpc" )]
	[DataRow( "bad-esp" )]
	[DataRow( "bad-tpc" )]
	[DataRow( "invalid-range" )]
	public void IdentifiedInvalidBankKeepsItsOrdinalInsteadOfSelectingTheNextBank( string problem )
	{
		const string directory = "/esprites/Generic/Mechanics";
		var files = new Dictionary<string, byte[]>(); Add( files, directory, "SPR_A" ); Add( files, directory, "SPR_B" );
		switch ( problem )
		{
			case "missing-tpc": files.Remove( $"{directory}/SPR_A.TPC" ); break;
			case "bad-esp": files[$"{directory}/SPR_A.ESP"] = new byte[2]; break;
			case "bad-tpc": files[$"{directory}/SPR_A.TPC"] = new byte[2]; break;
			case "invalid-range":
				var esp = files[$"{directory}/SPR_A.ESP"]; esp[SpriteAnimationFile.SlotOffset] = 255; esp[SpriteAnimationFile.SlotOffset + 1] = 255; break;
		}
		var set = Load( files ); var catalog = new StaffSpriteMetadataCatalog( set );
		var first = catalog.Select( StaffType.Mechanic, 0 ); var second = catalog.Select( StaffType.Mechanic, 1 );
		Assert.IsFalse( first.Available ); StringAssert.EndsWith( first.Identity, "/SPR_A" ); Assert.AreEqual( 0u, first.Variant );
		Assert.IsTrue( second.Available ); StringAssert.EndsWith( second.Identity, "/SPR_B" ); Assert.AreEqual( 1u, second.Variant );
		Assert.AreEqual( "SPR_B", set.Atlases[second.Atlas].Name ); Assert.AreEqual( 1, second.Slot0FrameCount );
		Assert.IsFalse( catalog.Select( StaffType.Handyman, 1 ).Available, "a different role never supplies a bank" );
		Add( files, directory, "SPR_A" );
		Assert.IsFalse( catalog.Resolve( 6, 0 ).Available, "later files do not mutate the already frozen catalog" );
		Assert.IsTrue( new StaffSpriteMetadataCatalog( Load( files ) ).Resolve( 6, 0 ).Available, "only a new catalog sees recovery" );
	}

	[TestMethod]
	public void ExpectedListingAndReadFailuresFreezeUnavailableButProgrammingFailuresPropagate()
	{
		foreach ( var error in new Exception[] { new IOException(), new UnauthorizedAccessException(), new InvalidDataException() } )
		{
			var missing = StaffSpriteSet.Load( "jungle", _ => throw error, _ => throw new InvalidOperationException() );
			Assert.IsFalse( new StaffSpriteMetadataCatalog( missing ).Select( StaffType.Handyman, 0 ).Available );
			var broken = StaffSpriteSet.Load( "jungle", _ => new[] { "SPR_A.ESP", "SPR_A.TPC" }, _ => throw error );
			Assert.IsFalse( new StaffSpriteMetadataCatalog( broken ).Select( StaffType.Handyman, 0 ).Available );
			Assert.AreEqual( 5, new StaffSpriteMetadataCatalog( broken ).Banks.Count );
		}
		Assert.ThrowsException<InvalidOperationException>( () => StaffSpriteSet.Load( "jungle", _ => throw new InvalidOperationException(), _ => Stream.Null ) );
		Assert.ThrowsException<NullReferenceException>( () => StaffSpriteSet.Load( "jungle", _ => new[] { "SPR_A.ESP" }, _ => throw new NullReferenceException() ) );
		Assert.ThrowsException<ArgumentException>( () => StaffSpriteSet.Load( "jungle", _ => new[] { "SPR_A.ESP" }, _ => throw new ArgumentException() ) );
	}

	[TestMethod]
	public void SuccessfulDiscoveryAndLegacyHireVariantOrderRemainSortedAndStable()
	{
		var files = new Dictionary<string, byte[]>();
		Add( files, "/esprites/Generic/Mechanics", "SPR_Z" ); Add( files, "/esprites/Generic/Mechanics", "SPR_A" );
		Add( files, "/esprites/Generic/Handymen", "SPR_H" );
		var set = Load( files ); var catalog = new StaffSpriteMetadataCatalog( set );
		CollectionAssert.AreEqual( new[] { "SPR_H", "SPR_A", "SPR_Z" }, set.Atlases.Select( atlas => atlas.Name ).ToArray() );
		for ( var key = 0; key < 8; key++ )
			Assert.AreEqual( set.AtlasFor( StaffType.Mechanic, key ), catalog.Select( StaffType.Mechanic, key ).Atlas );
	}

	[TestMethod]
	public void MissingMetadataStillConstructsAndSchedulesBothOwnersWithoutReplayOrHotFill()
	{
		var member = new StaffMember { Id = 1, ThingId = 11, Type = StaffType.Handyman, Variant = 0, CellX = 3, CellY = 4 };
		var roster = new List<StaffMember> { member };
		var catalog = new StaffSpriteMetadataCatalog( new[] { StaffSpriteBankMetadata.Unavailable( 5, 0, "SPR_TEST" ) } );
		using var presentation = new StaffSpritePresentation( catalog, () => roster );
		var body = member.BodySprite!; body.CreateSource();
		var firstTicket = body.SourceTicket; var source = body.ReadSourceState()!;
		Assert.AreEqual( 0u, source[StaffSpriteRegister.FrameCount] ); Assert.AreEqual( 0u, source.OutputValid );
		Assert.AreEqual( 62, source.Due ); Assert.AreEqual( 62, source.Interval );
		using var portrait = new EmployeePortraitOwner( presentation ); portrait.FullRefresh( member, -0.5f, 0.25f, 0.975f );
		Assert.AreEqual( 2, presentation.Registry.Count );
		presentation.Registry.Schedule( 63 ); portrait.PartialRefresh();
		Assert.AreEqual( 1u, body.ReadSourceState()!.OutputValid ); Assert.AreEqual( 1u, portrait.ReadCloneState()!.OutputValid );
		Assert.AreEqual( StaffSpriteDiagnostic.MetadataUnavailable, catalog.ResolveFrame( portrait.DrawRecord! ).Diagnostic );
		body.RequestPerformance( uint.MaxValue ); var before = JsonSerializer.Serialize( body.ReadSourceState() ); body.ApplyPending();
		Assert.AreEqual( before, JsonSerializer.Serialize( body.ReadSourceState() ) ); Assert.AreEqual( 0, body.PendingTemplate );
		Assert.AreEqual( StaffSpriteDiagnostic.MetadataUnavailable, body.Diagnostic );
		var appeared = StaffSpriteDrawRecordTests.Atlas();
		for ( var index = 0; index < 10; index++ )
		{
			Assert.IsFalse( catalog.Resolve( 5, 0 ).Matches( appeared ) );
			Assert.AreEqual( StaffSpriteDiagnostic.MetadataUnavailable, catalog.ResolveFrame( portrait.DrawRecord! ).Diagnostic );
		}
		Assert.AreEqual( before, JsonSerializer.Serialize( body.ReadSourceState() ) );
		body.DeleteSource(); body.CreateSource();
		Assert.AreNotEqual( firstTicket, body.SourceTicket ); Assert.AreEqual( 0u, body.ReadSourceState()![StaffSpriteRegister.FrameCount] );
		Assert.AreEqual( 0, body.PendingTemplate );
		presentation.Dispose();
		using var next = new StaffSpritePresentation( StaffSpriteDrawRecordTests.Catalog( appeared ), () => roster );
		member.BodySprite!.CreateSource();
		Assert.AreEqual( 1u, member.BodySprite.ReadSourceState()![StaffSpriteRegister.FrameCount] );
		Assert.AreNotEqual( firstTicket!.Value.Session, member.BodySprite.SourceTicket!.Value.Session );
	}
}
