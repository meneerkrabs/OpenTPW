using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.ParticleSystemTests;
using static OpenTPW.Tests.SaveEngineBlocksTests;

namespace OpenTPW.Tests;

/// <summary>Synthetic saved-pool configuration continuation, not an original-game visual oracle (SAVE-013).</summary>
[TestClass]
public class ParticlePoolConfigurationTests
{
	private static ParticleSystemState RoundTrip( ParticleSystemState state ) =>
		ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Particles = state } ) ).Particles!;

	private static byte[] Sprites( ParticleSystem system )
	{
		var sprites = new List<ParticleSprite>();
		system.Snapshot( sprites );
		return ParkResumeJson.Serialize( sprites );
	}

	private static void Same( ParticleSystem expected, ParticleSystem actual )
	{
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( expected.Capture() ), ParkResumeJson.Serialize( actual.Capture() ) );
		CollectionAssert.AreEqual( Sprites( expected ), Sprites( actual ) );
		Assert.AreEqual( expected.LiveParticles, actual.LiveParticles );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OwnSaveRestoresSettingsBeforeTheNextNewEmitterEvenWhenEmpty( bool live )
	{
		var templates = Pool( (1, Template( Life( 0 ), Immortal(), (0x12, 2, 5) )),
			(2, Template( Life( 100 ), Burst( 20 ), Cap( 200 ), Rates( 9, 8, 7, 6 ), ParticleLife( 1000 ) )),
			(3, Template( Life( 1 ), (0x12, 2, 5) )) );
		var control = New( templates, 1500, 100, lowDetail: true );
		var old = control.Create( 3, Origin );
		Run( control, 4 );
		Assert.IsFalse( control.IsAlive( old ) );
		var liveHandle = live ? control.Create( 1, Origin ) : -1;
		Assert.AreEqual( 0, control.LiveParticles, "the continuation control must start with an empty particle array too" );
		var checkpoint = RoundTrip( control.Capture() );
		Assert.AreNotEqual( 1u, checkpoint.Random, "the empty case carries history, not only constructor defaults" );
		Assert.AreEqual( live ? 1 : 0, checkpoint.Emitters.Count );
		var restored = New( templates, 500, 2048 );
		restored.Restore( checkpoint );
		Assert.AreEqual( (1500, 100, true), (restored.Density, restored.Capacity, restored.LowDetail) );
		Assert.AreEqual( live, restored.IsAlive( liveHandle ) );
		Same( control, restored );
		Assert.AreEqual( control.Create( 2, Origin ), restored.Create( 2, Origin ), "slot and generation continue" );
		Assert.AreEqual( 29, restored.LiveParticles, "the new emitter uses saved density1500, not host500 (9 particles)" );
		var created = restored.Capture().Emitters[0];
		CollectionAssert.AreEqual( new[] { 13, 11, 10, 8 }, created.Rates.ToArray() );
		Assert.AreEqual( (29, 292), (created.Burst, created.Cap) );
		for ( var tick = 0; tick < 12; tick++ )
		{
			control.Tick(); restored.Tick(); Same( control, restored );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OriginalImportThenOwnSaveRetainsEmptyAndLivePoolConfiguration( bool live )
	{
		var templates = Pool();
		var block = SaveParticlesBlock.Parse( ParticlesBlock( PoolTable( live ? new[] { 3 } : Array.Empty<int>(), table =>
		{
			BitConverter.TryWriteBytes( table.AsSpan( 8 ), 0x80000000u );
			BitConverter.TryWriteBytes( table.AsSpan( 0xC ), 100u );
		} ), TemplateTable() ), 0 );
		var converted = block.ToSystemState( templates )!;
		Assert.AreEqual( new ParticlePoolConfiguration { Density = 1500, PoolCapacity = 100, LowDetail = true }, converted.Configuration );
		var imported = New( templates, 500, 2048 );
		imported.Restore( converted );
		var resumed = New( templates, 2000, 32767 );
		resumed.Restore( RoundTrip( imported.Capture() ) );
		Same( imported, resumed );
		Assert.AreEqual( live ? 1 : 0, resumed.LiveEmitters );
		Assert.AreEqual( converted.Random, resumed.RandomState );
		CollectionAssert.AreEqual( converted.FreeEmitters.ToArray(), resumed.Capture().FreeEmitters.ToArray() );
	}

	[TestMethod]
	public void SavedLowDetailRefusesOrdinaryDirectedAndEffectorCreationBeforeChangingHistory()
	{
		var templates = Pool( (1, Template( Life( 10 ), Burst( 5 ), (0xC1, 1, 1) )) );
		var bytes = new byte[ParticleEffectorTemplate.Size];
		bytes[0x44] = 1;
		var effectors = new[] { new ParticleEffectorTemplate( bytes ) };
		var source = New( templates, lowDetail: true, effectors: effectors );
		var target = New( templates, lowDetail: false, effectors: effectors );
		target.Restore( RoundTrip( source.Capture() ) );
		var before = ParkResumeJson.Serialize( target.Capture() );
		Assert.AreEqual( -1, target.Create( 1, Origin ) );
		Assert.AreEqual( -1, target.CreateDirected( 1, Origin, (0, 1024, 0) ) );
		Assert.AreEqual( -1, target.CreateEffector( 0, Origin ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( target.Capture() ), "refusals consume no RNG, handle or list slot" );
	}

	[DataTestMethod]
	[DataRow( 100, 32767 )]
	[DataRow( 32767, 100 )]
	public void SavedCapacityResizesTheEmptyPoolAndBoundsBurstExhaustion( int savedCapacity, int hostCapacity )
	{
		var templates = Pool( (1, Template( Life( 100 ), Burst( 40000 ), ParticleLife( 1000 ) )) );
		var control = New( templates, particleCount: savedCapacity );
		var restored = New( templates, particleCount: hostCapacity );
		restored.Create( 1, Origin );
		Assert.AreEqual( hostCapacity, restored.LiveParticles );
		restored.Restore( RoundTrip( control.Capture() ) );
		Assert.AreEqual( 0, restored.LiveParticles );
		Assert.AreEqual( savedCapacity, restored.Capacity );
		Assert.AreEqual( control.Create( 1, Origin ), restored.Create( 1, Origin ) );
		Assert.AreEqual( savedCapacity, restored.LiveParticles );
		Same( control, restored );
		var random = restored.RandomState;
		restored.Create( 1, Origin );
		Assert.AreEqual( random, restored.RandomState, "a burst stopped by the rebuilt full pool draws nothing" );
		Assert.AreEqual( savedCapacity, restored.LiveParticles );
	}

	[DataTestMethod]
	[DataRow( 0, 1, 1, 1, 1 )]
	[DataRow( -1, -1, -1, -2, 0 )]
	[DataRow( int.MinValue, 1, 1, -2097152, 0 )]
	[DataRow( int.MaxValue, -1, -1, 2097150, 100 )]
	public void SavedDensityPreservesSignedNativeArithmeticWithoutUnboundedEmission( int density, int rate, int cap, int burst, int count )
	{
		var templates = Pool( (1, Template( Life( 0 ), Immortal(), Burst( 1025 ), Cap( 200 ), Rates( 8, 8, 8, 8 ), ParticleLife( 1000 ) )) );
		var block = SaveParticlesBlock.Parse( ParticlesBlock( PoolTable( Array.Empty<int>(), table =>
		{
			BitConverter.TryWriteBytes( table.AsSpan( 0x20 ), unchecked( (uint)density ) );
			BitConverter.TryWriteBytes( table.AsSpan( 0xC ), 100u );
		} ), TemplateTable() ), 0 );
		var target = New( templates, 500 );
		target.Restore( RoundTrip( block.ToSystemState( templates )! ) );
		Assert.AreEqual( density, target.Density, "load does not apply init's zero-to-one normalization" );
		target.Create( 1, Origin );
		var emitter = target.Capture().Emitters.Single();
		CollectionAssert.AreEqual( Enumerable.Repeat( rate, 4 ).ToArray(), emitter.Rates.ToArray() );
		Assert.AreEqual( (cap, burst, count), (emitter.Cap, emitter.Burst, target.LiveParticles) );
		Run( target, 8 );
		Assert.IsTrue( target.LiveParticles <= 100 );
		Assert.IsNull( ParticleSystem.ValidateState( target.Capture() ) );
		if ( density == int.MaxValue )
		{
			var random = target.RandomState;
			target.Create( 1, Origin );
			Assert.AreEqual( random, target.RandomState, "the huge positive burst stops at the first refused allocation" );
		}
	}

	[TestMethod]
	public void InvalidSavedCountsListsAndTemplatesLeaveTheLiveHostEntirelyUnchanged()
	{
		var templates = new[] { Template( Life( 100 ), Burst( 8 ), Cap( 100 ), ParticleLife( 1000 ) ) };
		var target = New( templates, 500, 2048, lowDetail: true );
		var handle = target.Create( 0, Origin );
		var state = target.Capture();
		var validDifferent = state with { Configuration = new ParticlePoolConfiguration { Density = 1500, PoolCapacity = 100, LowDetail = false } };
		var before = ParkResumeJson.Serialize( state );
		var beforeSprites = Sprites( target );
		var invalid = new List<ParticleSystemState>();
		foreach ( var capacity in new[] { int.MinValue, -1, 0, 99, 32768, int.MaxValue } )
		{
			var bad = state with { Configuration = validDifferent.Configuration! with { PoolCapacity = capacity } };
			invalid.Add( bad );
			StringAssert.Contains( ParticleSystem.ValidateState( bad )!, "pool capacity" );
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Particles = bad } ) );
			if ( capacity != 0 ) // the original block parser already rejects zero allocation
			{
				var block = SaveParticlesBlock.Parse( ParticlesBlock( PoolTable( Array.Empty<int>(), table => BitConverter.TryWriteBytes( table.AsSpan( 0xC ), unchecked( (uint)capacity ) ) ), TemplateTable() ), 0 );
				Assert.ThrowsException<InvalidDataException>( () => block.ToSystemState( templates ) );
			}
		}
		invalid.Add( validDifferent with { FreeEmitters = Array.Empty<int>() } );
		invalid.Add( validDifferent with { Emitters = new[] { state.Emitters[0] with { EffectId = 1 } } } );
		invalid.Add( validDifferent with { Effectors = new[] { new ParticleEffectorState { Slot = 0, EffectorId = 0 } }, FreeEffectors = state.FreeEffectors.Skip( 1 ).ToArray() } );
		foreach ( var bad in invalid )
		{
			Assert.ThrowsException<InvalidDataException>( () => target.Restore( bad ) );
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( target.Capture() ) );
			CollectionAssert.AreEqual( beforeSprites, Sprites( target ) );
			Assert.IsTrue( target.IsAlive( handle ) );
		}
	}

	[TestMethod]
	public void LegacyAbsentOrNullConfigurationKeepsTheHostsCurrentSettingsWithoutRescalingSavedEmitters()
	{
		var templates = Pool( (1, Template( Life( 100 ), Burst( 20 ), Cap( 200 ), Rates( 9, 8, 7, 6 ), ParticleLife( 1000 ) )) );
		var source = New( templates, 1500, 100, lowDetail: true );
		source.Create( 1, Origin );
		var saved = source.Capture();
		foreach ( var explicitNull in new[] { false, true } )
		{
			var json = JsonNode.Parse( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Particles = saved } ) )!;
			var particle = json["Particles"]!.AsObject();
			if ( explicitNull ) particle["Configuration"] = null;
			else particle.Remove( "Configuration" );
			var older = ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ) ).Particles!;
			Assert.IsNull( older.Configuration );
			var target = New( templates, 500, 2048 );
			target.Restore( older );
			Assert.AreEqual( (500, 2048, false), (target.Density, target.Capacity, target.LowDetail) );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( saved.Emitters ), ParkResumeJson.Serialize( target.Capture().Emitters ), "live emitters' saved rates, burst and cap are not rescaled" );
			target.Restore( saved );
			target.Restore( older );
			Assert.AreEqual( (1500, 100, true), (target.Density, target.Capacity, target.LowDetail), "missing configuration keeps the current, not constructor, settings" );
		}
	}

	[DataTestMethod]
	[DataRow( "Density" )]
	[DataRow( "PoolCapacity" )]
	[DataRow( "LowDetail" )]
	public void PresentConfigurationRequiresEveryMember( string missing )
	{
		var state = New( Pool(), 1500, 100, lowDetail: true ).Capture();
		var json = JsonNode.Parse( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() with { Particles = state } ) )!;
		json["Particles"]!["Configuration"]!.AsObject().Remove( missing );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SlicedSaveMatchesNormalSerializationWithPresentOrLegacyConfiguration( bool legacy )
	{
		var state = New( Pool(), 1500, 100, lowDetail: true ).Capture();
		if ( legacy ) state = state with { Configuration = null };
		var snapshot = ParkResumeSaveFileTests.Envelope() with { Particles = state };
		var expected = ParkResumeSaveFile.Serialize( snapshot );
		var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot );
		while ( !sliced.Run( TimeSpan.Zero ) ) { }
		CollectionAssert.AreEqual( expected, sliced.Bytes );
		Assert.AreEqual( state.Configuration, ParkResumeSaveFile.Deserialize( sliced.Bytes ).Particles!.Configuration );
	}

	[TestMethod]
	public void RestoreRetainsTheHostsGroundCallback()
	{
		var templates = Pool( (1, Template( Life( 100 ), Cap( 500 ), Rates( 1, 1, 1, 1 ), ParticleLife( 1000 ), (0x28, 4, 2), (0x76, 2, 1) )) );
		var sourceCalls = 0;
		var hostCalls = 0;
		var source = New( templates, 1500, 100, ground: ( x, z ) => { sourceCalls++; return 0; } );
		var target = New( templates, 500, 2048, ground: ( x, z ) => { hostCalls++; return 16 * 64; } );
		target.Restore( RoundTrip( source.Capture() ) );
		target.Create( 1, (0, 2048, 0) );
		Run( target, 30 );
		Assert.IsTrue( hostCalls > 0 );
		Assert.AreEqual( 0, sourceCalls );
		var sprites = new List<ParticleSprite>(); target.Snapshot( sprites );
		Assert.IsTrue( sprites.Count > 0 && sprites.All( sprite => sprite.Y >= 64 ) );
	}
}
