using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's attraction constructor writes a new litter bin's VAR_CAPACITY, VAR_DIRTY and VAR_FIRERISK (and a plant's VAR_TIMEOUT, VAR_TEST, VAR_SCALE) from Standard.sam (<c>0x5DB7D5</c>); Theme Park
/// World's constructor writes none of them (RIDES-045). The retail tests need OPENTPW_TPI_PATH; the placement test runs on a synthetic dummy-only model (no render parts).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncConstructionVariableTests
{
	private BaseFileSystem? previous;
	private GameEdition previousEdition;
	private List<Entity> previousEntities = null!;
	private string? root;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		previousEdition = GameEditions.Current;
		previousEntities = Entity.All;
		Entity.All = new();
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Entity.All = previousEntities;
		FileSystem = previous!;
		GameEditions.Select( previousEdition );
		if ( root != null )
			Directory.Delete( root, true );
	}

	[TestMethod]
	public void TheStandardWordsAreTheShippedLitterBinAndVegetationValues()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		var standard = ThemeParkIncStandard.Load( ParkEconomy.GameCareerFiles.Instance.Read, "science" )!;
		// data\levels\Standard.sam: LitterBin.Capacity 20, Dirty 15, FireRisk 5; Vegetation.TimeBetweenWaterings 400, TestAnimation 0 (0xA02990..0xA029A0)
		Assert.AreEqual( new ThemeParkIncConstructionWords( 20, 15, 5, 400, 0 ), ThemeParkIncStandard.ConstructionWords( standard ) );
	}

	[TestMethod]
	public void TheScienceLitterBinsScriptTakesItsCapacityDirtAndFireRisk()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		// the litter bin is a facility (levels/science/Facilities/litbin), a folder the object catalogue does not read yet: its own .sam and script are read here
		var files = FileSystem.GetFiles( "/levels/science/Facilities/litbin" ).ToArray();
		var sam = files.Single( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) );
		var settings = new ObjectSettings( new[] { ObjectSettingsFile.Load( FileSystem, sam ) } );
		Assert.IsTrue( settings.GetBool( "UsageInfo.HoldsLitter" ), "the Science litter bin holds litter (thing flag 0x40)" );
		using var stream = FileSystem.OpenRead( files.Single( file => file.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) ) );
		var script = new RideVM( stream );
		var names = script.Script.VariableNames;
		Assert.AreEqual( ("VAR_CAPACITY", "VAR_DIRTY", "VAR_FIRERISK"), (names[3], names[11], names[12]) );
		var before = script.Variables.ToArray();
		ThemeParkIncStandard.ConstructionWords( ThemeParkIncStandard.Load( ParkEconomy.GameCareerFiles.Instance.Read, "science" )! ).Apply( settings, script );
		var after = script.Variables;
		Assert.AreEqual( (20, 15, 5), (after[3], after[11], after[12]), "0x5DB7F1, 0x5DB80F, 0x5DB82C" );
		Assert.IsTrue( after.Select( ( value, index ) => index is 3 or 11 or 12 || value == before[index] ).All( same => same ), "no other variable is written" );
		// the plants: the original loads data\levels\vegetation.rse for a thing with flag 0x1000 (0x5DE61A); OpenTPW does not, so a plant has no script and its writes are dropped, as 0x6B0780 drops them
		var plant = ObjectCatalog.Load( "science" ).Entries.First( entry => entry.Settings.GetBool( "UsageInfo.RequiresWatering" ) );
		Assert.IsNull( new OriginalObjectRuntime( plant ).Script );
	}

	[TestMethod]
	public void ANewBinTakesTheWordsOnlyInThemeParkInc()
	{
		var entry = SyntheticBin();
		GameEditions.Select( GameEdition.ThemeParkInc );
		var placed = Objects( entry ).Add( entry, 3, 3, 0 );
		var variables = placed.Runtime.Script!.Variables;
		Assert.AreEqual( (20, 15, 5), (variables[3], variables[11], variables[12]), "a new Theme Park Inc object takes the Standard words at its construction" );
		// Theme Park World's constructor (tp.exe 0x4DB090) sets the flag (0x4DB3FB) but writes nothing for it: the same bin in a Theme Park World park keeps 0
		GameEditions.Select( GameEdition.ThemeParkWorld );
		var world = Objects( entry ).Add( entry, 3, 3, 0 );
		Assert.AreEqual( (0, 0, 0), (world.Runtime.Script!.Variables[3], world.Runtime.Script.Variables[11], world.Runtime.Script.Variables[12]) );
	}

	private static ParkObjects Objects( ObjectCatalogEntry entry ) => new( (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
		new object[] { "science", new[] { entry } }, null )!, new SandboxParkGrid(), new WorldSeed( 1 ) );

	/// <summary>A litter bin on a valid dummy-only model (as <see cref="ThemeParkIncLayoutCheckpointTests"/> builds it) with a script of the standard variables, and a Standard.sam with the bin's words.</summary>
	private ObjectCatalogEntry SyntheticBin()
	{
		root = Path.Combine( Path.GetTempPath(), "opentpw-tpi-bin-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "synthetic" ) );
		Directory.CreateDirectory( Path.Combine( root, "levels" ) );
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } )
			BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "synthetic/model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "synthetic/script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build( Enumerable.Range( 0, 14 ).Select( index => $"VAR_{index:D2}" ).ToArray() ) );
		File.WriteAllText( Path.Combine( root, "levels/Standard.sam" ), "LitterBin.Capacity 20\nLitterBin.Dirty 15\nLitterBin.FireRisk 5\n" );
		FileSystem = new BaseFileSystem( root );
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1422\nInfo.Name Bin\nUsageInfo.HoldsLitter 1\n", "bin.sam" ) } );
		return new ObjectCatalogEntry( "science", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( new[] { "*" } ),
			"/synthetic/model.MD2", null, "/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null );
	}
}
