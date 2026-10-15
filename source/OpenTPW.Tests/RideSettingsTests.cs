using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The ride settings setters of tp.exe (SetSpeed 0x4dd6e0, SetCapacity 0x4dd7f0, SetDuration 0x4dd720) and the ride window's apply 0x4af440.</summary>
[TestClass, DoNotParallelize]
public class RideSettingsTests
{
	private string root = null!;
	private List<Entity> previous = null!;
	private Logger logger = null!;

	[TestInitialize]
	public void Setup()
	{
		previous = Entity.All; Entity.All = new(); logger = Log; Log = new();
		root = Path.Combine( Path.GetTempPath(), "opentpw-ride-settings-" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( root );
		var bytes = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
		foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
	}

	[TestCleanup]
	public void Cleanup() { foreach ( var item in Entity.All.ToArray() ) item.Delete(); Entity.All = previous; Log = logger; Directory.Delete( root, true ); }

	private ObjectCatalogEntry Entry( string extra = "", int capacity = 5 )
	{
		var sam = "Info.Id 1100\nInfo.Name settings-test\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nInfo.HasQueue 1\n"
			+ "UsageInfo.MinCapacity 2\nUsageInfo.MaxCapacity 10\nUsageInfo.MinDuration 10\nUsageInfo.MaxDuration 60\nUsageInfo.MinSpeed 1\nUsageInfo.MaxSpeed 100\n"
			+ $"Upgrades[0].InitCapacity {capacity}\nUpgrades[0].InitDuration 30\nUpgrades[0].InitSpeed 60\nUpgrades[0].QueueWaitTimeConstant 130\n" + extra;
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/settings-test", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "settings-test.sam" ) } ), new ObjectShape( new[] { "*" } ),
			"/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
	}

	private static ObjectSettings Settings( string sam ) => new( new[] { new ObjectSettingsFile( sam, "clamp.sam" ) } );

	[TestMethod]
	public void TheCapacityClampTakesTheLowByteAndOnlyClampsWhenTheLimitsSumAboveZero()
	{
		var limits = Settings( "UsageInfo.MinCapacity 2\nUsageInfo.MaxCapacity 10\n" );
		Assert.AreEqual( 2, OriginalObjectRuntime.ClampCapacity( limits, 1 ) );
		Assert.AreEqual( 10, OriginalObjectRuntime.ClampCapacity( limits, 11 ) );
		Assert.AreEqual( 7, OriginalObjectRuntime.ClampCapacity( limits, 7 ) );
		Assert.AreEqual( 2, OriginalObjectRuntime.ClampCapacity( limits, 256 + 1 ), "the byte of 257 is 1, below the minimum" );
		var none = Settings( "" );
		Assert.AreEqual( 200, OriginalObjectRuntime.ClampCapacity( none, 200 ), "Min + Max = 0: no clamp (0x4dd7f0)" );
		Assert.AreEqual( 44, OriginalObjectRuntime.ClampCapacity( none, 300 ), "only the low byte" );
	}

	[TestMethod]
	public void TheDurationClampHasNoSumTest()
	{
		var none = Settings( "" );
		Assert.AreEqual( 0, OriginalObjectRuntime.ClampDuration( none, 30 ), "MaxDuration 0 lowers every byte to 0 (0x4dd720)" );
		var limits = Settings( "UsageInfo.MinDuration 10\nUsageInfo.MaxDuration 60\n" );
		Assert.AreEqual( 10, OriginalObjectRuntime.ClampDuration( limits, 3 ) );
		Assert.AreEqual( 60, OriginalObjectRuntime.ClampDuration( limits, 61 ) );
		Assert.AreEqual( 45, OriginalObjectRuntime.ClampDuration( limits, 45 ) );
	}

	[TestMethod]
	public void SetSpeedWritesTheScriptBiasAndTheSpeedWord()
	{
		var ride = new OriginalObjectRuntime( Entry(), seed: 1 );
		Assert.AreEqual( (60, 60), (ride.Script!.SpeedBias, ride.Settings.Speed) );
		ride.SetSpeed( 90 );
		Assert.AreEqual( (90, 1.4f), (ride.Script.SpeedBias, ride.Script.ScriptSpeed) );
		Assert.AreEqual( 90, ride.Settings.Speed );
		Assert.AreEqual( 60, ride.Visitors.Parameters.InitSpeed, "the type's InitSpeed stays the queue limit's denominator" );
		// G = 90 / 60 = 1.5: trunc(130 x 1.5 x 5 / 30) = 32
		Assert.AreEqual( 32u, RideVisitorBridge.ComputeQueueLimit( ride.Visitors.Parameters ) );
		ride.SetSpeed( 70000 );
		Assert.AreEqual( unchecked( (short)70000 ), ride.Script.SpeedBias, "the bias is a signed 16-bit word (0x55a300)" );
	}

	[TestMethod]
	public void SetCapacityAndSetDurationWriteScriptVariablesTwoAndThreeClamped()
	{
		var ride = new OriginalObjectRuntime( Entry(), seed: 1 );
		ride.SetCapacity( 50 );
		Assert.AreEqual( (10, 10), (ride.GetVariable( RideVariables.VAR_CAPACITY ), ride.Settings.Capacity) );
		ride.SetDuration( 5 );
		Assert.AreEqual( (10, 10), (ride.GetVariable( RideVariables.VAR_DURATION ), ride.Settings.Duration) );
		Assert.AreEqual( 30, ride.Visitors.Parameters.InitDuration, "the type's InitDuration stays the join excitement's denominator" );
	}

	[TestMethod]
	public void TheRideWindowAppliesTheDurationOnlyForATypeWithADurationUnit()
	{
		var plain = new OriginalObjectRuntime( Entry(), seed: 1 );
		plain.ApplyRideWindow( new RideSettingWords( 40, 3, 50 ) );
		Assert.AreEqual( new RideSettingWords( 40, 3, 30 ), plain.Settings, "Info.DurationUnit 0: 0x4af440 skips SetDuration" );
		Assert.AreEqual( 30, plain.GetVariable( RideVariables.VAR_DURATION ) );
		var timed = new OriginalObjectRuntime( Entry( "Info.DurationUnit 1\n" ), seed: 1 );
		timed.ApplyRideWindow( new RideSettingWords( 40, 3, 50 ) );
		Assert.AreEqual( new RideSettingWords( 40, 3, 50 ), timed.Settings );
		Assert.AreEqual( 50, timed.GetVariable( RideVariables.VAR_DURATION ) );
	}

	[TestMethod]
	public void RideInitWritesTheClampedCapacityOnlyWhenAboveZero()
	{
		var clamped = new OriginalObjectRuntime( Entry( capacity: 12 ), seed: 1 );
		Assert.AreEqual( 10, clamped.GetVariable( RideVariables.VAR_CAPACITY ), "0x4db54f calls the capacity setter 0x4dd7f0" );
		var none = new OriginalObjectRuntime( Entry( capacity: 0 ), seed: 1 );
		Assert.AreEqual( 0, none.GetVariable( RideVariables.VAR_CAPACITY ), "not written when InitCapacity is not above 0" );
	}

	[TestMethod]
	public void TheSettingWordsSurviveTheParkResumeSnapshot()
	{
		var entry = Entry( "Info.DurationUnit 1\n" );
		var world = new RideScriptWorld();
		var source = new OriginalObjectRuntime( entry, world, seed: 7 );
		source.ApplyRideWindow( new RideSettingWords( 80, 4, 20 ) );
		source.Simulate( .001 );
		var snapshot = source.CaptureSnapshot();
		Assert.AreEqual( (80, 4, 20), (snapshot.RideSpeed, snapshot.RideCapacity, snapshot.RideDuration) );
		var scripts = world.CaptureSnapshot();
		var restoredWorld = new RideScriptWorld();
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, restoredWorld, 7, source.IsOpen, snapshot.AttractionId );
		candidate.RestoreSnapshot( snapshot );
		restoredWorld.RestoreSnapshot( scripts, new Dictionary<int, RideVM> { { source.Script!.ScriptId, candidate.Script! } } );
		Assert.AreEqual( new RideSettingWords( 80, 4, 20 ), candidate.Settings );
		Assert.AreEqual( (80, 4, 20), (candidate.Script!.SpeedBias, candidate.GetVariable( RideVariables.VAR_CAPACITY ), candidate.GetVariable( RideVariables.VAR_DURATION )) );
	}

	[TestMethod]
	public void AnOlderSnapshotWithoutSettingWordsRestoresTheCatalogueValues()
	{
		var entry = Entry();
		var source = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 7 );
		var snapshot = source.CaptureSnapshot();
		Assert.IsNull( snapshot.RideSpeed );
		var json = System.Text.Json.JsonSerializer.Serialize( snapshot );
		StringAssert.DoesNotMatch( json, new System.Text.RegularExpressions.Regex( "RideSpeed" ), "an untouched ride writes no setting words" );
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), 7, true, snapshot.AttractionId );
		candidate.RestoreSnapshot( snapshot );
		Assert.AreEqual( new RideSettingWords( 60, 5, 30 ), candidate.Settings );
		Assert.ThrowsException<InvalidDataException>( () => (snapshot with { RideCapacity = 256 }).ValidateSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => (snapshot with { RideSpeed = -1 }).ValidateSnapshot() );
	}
}
