using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// tp.exe's validator 0x535670 lets an object stand on another object's cells when its Info.OverwritePriority is higher
/// (0x535D66), and the commit demolishes the lower object (cell op 0x87, 0x527EE0). Synthetic dummy-only models: no GPU.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ObjectOverwritePriorityTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private List<Entity> previousEntities = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousEntities = Entity.All; previousLog = Log;
		Entity.All = new(); Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-overwrite-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		// A valid single dummy root with no mesh, as in ThemeParkIncLayoutCheckpointTests.
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enum.GetNames<RideVariables>() ) );
		FileSystem = new BaseFileSystem( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() ) entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; Log = previousLog;
		Directory.Delete( root, true );
	}

	private ObjectCatalogEntry Entry( int id, int priority, params string[] shape ) => EntryWithModel( id, priority, "/synthetic/model.MD2", shape );

	private ObjectCatalogEntry EntryWithModel( int id, int priority, string model, params string[] shape )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\nInfo.IsChoosable 1\nInfo.OverwritePriority {priority}\n", $"synthetic{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( shape ), model, null, "/synthetic/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );
	}

	private static ParkObjects Objects( params ObjectCatalogEntry[] entries )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { "jungle", entries }, null )!;
		return new ParkObjects( catalog, new SandboxParkGrid() );
	}

	[TestMethod]
	public void AHigherPriorityObjectReplacesTheLowerOnesItCovers()
	{
		var bush = Entry( 1401, 1, "*" );
		var bin = Entry( 1406, 0, "*" );
		var ride = Entry( 1100, 5, "**", "**" );
		var objects = Objects( bush, bin, ride );
		var first = objects.Add( bush, 4, 4, 0 );
		var second = objects.Add( bin, 5, 5, 0 );
		var removed = new List<OriginalObject>();
		objects.ObjectRemoved += removed.Add;

		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( ride, 4, 4, 0, out var overwritten ) );
		CollectionAssert.AreEquivalent( new[] { first, second }, overwritten.ToArray() );
		Assert.AreEqual( 2, objects.Objects.Count, "checking demolishes nothing" );

		var placed = objects.TryPlace( ride, 4, 4, 0, out var result );
		Assert.AreEqual( OriginalPlacementResult.Allowed, result );
		CollectionAssert.AreEquivalent( new[] { first, second }, removed );
		CollectionAssert.AreEqual( new[] { placed }, objects.Objects.ToArray() );
		Assert.AreSame( placed, objects.FindAt( 5, 5 ) );
	}

	[TestMethod]
	public void AnEqualOrLowerPriorityRefusesAndRemovesNothing()
	{
		var bush = Entry( 1401, 1, "*" );
		var otherBush = Entry( 1402, 1, "*" );
		var bin = Entry( 1406, 0, "*" );
		var ride = Entry( 1100, 5, "**" );
		var objects = Objects( bush, otherBush, bin, ride );
		var first = objects.Add( bush, 4, 4, 0 );
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( otherBush, 4, 4, 0, out var equal ) );
		Assert.AreEqual( 0, equal.Count );
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( bin, 4, 4, 0 ) );
		// a ride over the bush and another ride: the ride refuses, so the bush stays as well
		objects.Add( ride, 5, 4, 0 );
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( ride, 4, 4, 0, out var none ) );
		Assert.AreEqual( 0, none.Count );
		Assert.IsNull( objects.TryPlace( ride, 4, 4, 0, out _ ) );
		Assert.IsTrue( objects.Objects.Contains( first ) );
		// reserved cells (paths, queues and tracks in a level) refuse a higher priority, also where a lower object stands
		objects.IsReserved = ( x, y ) => x == 4 && y == 4;
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( Entry( 1101, 5, "*" ), 4, 4, 0, out var reserved ) );
		Assert.AreEqual( 0, reserved.Count );
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.CheckFree( ride, 3, 4, 0 ) );
	}

	[TestMethod]
	public void CheckFreeRefusesASiteThatWouldDemolish()
	{
		var bush = Entry( 1401, 1, "*" );
		var ride = Entry( 1100, 5, "*" );
		var objects = Objects( bush, ride );
		objects.Add( bush, 4, 4, 0 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( ride, 4, 4, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.CheckFree( ride, 4, 4, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.CheckFree( ride, 6, 6, 0 ) );
	}

	[TestMethod]
	public void AFailedBuildDemolishesNothing()
	{
		var bush = Entry( 1401, 1, "*" );
		var broken = EntryWithModel( 1100, 5, "/synthetic/missing.MD2", "*" );
		var objects = Objects( bush, broken );
		var first = objects.Add( bush, 4, 4, 0 );
		Assert.ThrowsException<FileNotFoundException>( () => objects.TryPlace( broken, 4, 4, 0, out _ ) );
		CollectionAssert.AreEqual( new[] { first }, objects.Objects.ToArray() );
		Assert.AreSame( first, objects.FindAt( 4, 4 ) );
	}

	[TestMethod]
	public void TheShippedPrioritiesPutRidesAboveScenery()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the shipped catalogues." );
		try
		{
			foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
			{
				var catalog = ObjectCatalog.Load( theme );
				Assert.IsTrue( catalog.Entries.All( entry => entry.OverwritePriority is 0 or 1 or 5 ), theme );
				Assert.IsTrue( catalog.Entries.Where( entry => entry.OverwritePriority < 5 ).All( entry => entry.Category == ObjectCategory.Feature ), theme );
				Assert.IsTrue( catalog.Entries.Where( entry => entry.Category is ObjectCategory.Ride or ObjectCategory.Shop or ObjectCategory.Sideshow ).All( entry => entry.OverwritePriority == 5 ), theme );
				Assert.IsTrue( catalog.Entries.Any( entry => entry.OverwritePriority == 1 ) && catalog.Entries.Any( entry => entry.OverwritePriority == 0 ), theme );
			}
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
