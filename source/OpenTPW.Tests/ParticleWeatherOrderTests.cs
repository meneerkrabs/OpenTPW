using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ParticleWeatherOrderTests
{
	// Identity-only renderers: never invoke graphics on these uninitialized instances.
	private static T Empty<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void SameOrder( Entity[] expected, Entity[] actual )
	{
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var index = 0; index < expected.Length; index++ )
			Assert.AreSame( expected[index], actual[index], $"entity at draw position {index}" );
	}

	[TestMethod]
	public void OnlyWeatherMovesAcrossAnInterleavedOrdinaryObject()
	{
		var weather = Empty<WeatherRenderer>();
		var ordinary = Empty<Entity>();
		var particle = Empty<ParticleRenderer>();
		Entity[] input = { weather, ordinary, particle };
		var ordered = Level.WeatherAfterParticles( input );
		SameOrder( new Entity[] { ordinary, particle, weather }, ordered );
		SameOrder( new Entity[] { weather, ordinary, particle }, input );
		Assert.AreNotSame( input, ordered );
	}

	[TestMethod]
	public void MultipleEffectsPreserveBothTypeOrdersAndAllNonWeatherOrder()
	{
		var w1 = Empty<WeatherRenderer>(); var w2 = Empty<WeatherRenderer>(); var w3 = Empty<WeatherRenderer>();
		var p1 = Empty<ParticleRenderer>(); var p2 = Empty<ParticleRenderer>();
		var a = Empty<Entity>(); var b = Empty<Entity>(); var c = Empty<Entity>(); var d = Empty<Entity>();
		Entity[] input = { a, w1, p1, b, w2, c, p2, d, w3 };
		var copy = (Entity[])input.Clone();
		SameOrder( new Entity[] { a, p1, b, c, p2, w1, w2, d, w3 }, Level.WeatherAfterParticles( input ) );
		SameOrder( copy, input );
	}

	[TestMethod]
	public void AlreadyOrderedAndMissingPhaseSnapshotsKeepTheirArrayIdentity()
	{
		var weather = Empty<WeatherRenderer>(); var particle = Empty<ParticleRenderer>(); var ordinary = Empty<Entity>();
		foreach ( var input in new[]
		{
			Array.Empty<Entity>(), new Entity[] { ordinary }, new Entity[] { weather, ordinary },
			new Entity[] { ordinary, particle }, new Entity[] { particle, ordinary, weather },
			new Entity[] { ordinary, particle, weather, ordinary }
		} )
			Assert.AreSame( input, Level.WeatherAfterParticles( input ) );
	}

	private sealed class RecordingEntity( Action draw ) : Entity
	{
		protected override void OnRender() => draw();
	}

	private sealed class Fixture : IDisposable
	{
		private readonly List<Entity> previousEntities = Entity.All;
		private readonly Level previousLevel = OpenTPW.Level.Current;
		public Level Level { get; } = Empty<Level>();
		public Fixture()
		{
			Entity.All = new();
			OpenTPW.Level.Current = Level;
		}
		public void Dispose()
		{
			Entity.All = previousEntities;
			OpenTPW.Level.Current = previousLevel;
		}
	}

	[TestMethod]
	public void ActualParkPassRetainsSnapshotAdditionAndRemovalSemantics()
	{
		using var fixture = new Fixture();
		var calls = new List<string>();
		RecordingEntity? removed = null;
		var changed = false;
		_ = new RecordingEntity( () =>
		{
			calls.Add( "first" );
			if ( changed ) return;
			changed = true;
			removed!.Delete();
			_ = new RecordingEntity( () => calls.Add( "late" ) );
		} );
		removed = new RecordingEntity( () => calls.Add( "removed" ) );
		fixture.Level.Render();
		CollectionAssert.AreEqual( new[] { "first", "removed" }, calls );
		Assert.AreEqual( 1L, fixture.Level.RenderPass );
		calls.Clear();
		fixture.Level.Render();
		CollectionAssert.AreEqual( new[] { "first", "late" }, calls );
		Assert.AreEqual( 2L, fixture.Level.RenderPass );
	}

	[TestMethod]
	public void ActualParkPassPropagatesTheSameExceptionAndStopsThePass()
	{
		using var fixture = new Fixture();
		var failure = new InvalidOperationException( "synthetic render failure" );
		var calls = new List<string>();
		_ = new RecordingEntity( () => calls.Add( "first" ) );
		_ = new RecordingEntity( () => throw failure );
		_ = new RecordingEntity( () => calls.Add( "last" ) );
		var snapshot = Entity.All.ToArray();
		Assert.AreSame( failure, Assert.ThrowsException<InvalidOperationException>( () => fixture.Level.Render() ) );
		CollectionAssert.AreEqual( new[] { "first" }, calls );
		SameOrder( snapshot, Entity.All.ToArray() );
		Assert.AreEqual( 1L, fixture.Level.RenderPass );
	}
}
