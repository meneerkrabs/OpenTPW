using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// AUDIO-034: the held chain walks the links of its newest child's sound when the next child is made (tp.exe 0x6bdbb0 → 0x6c3e00): a sound
/// without links makes the chain start over at the event's first sound, links that do not hold the parameter halt the chain for good
/// (root flag 0x40, parameter byte 0x7f), and the pick is ror13(state) % total over the covering links' target weights. Synthetic data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AudioChainWalkTests
{
	private AudioMixer mixer = null!;
	private SoundEventSystem system = null!;

	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void SetProperty( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static T Field<T>( SoundChain chain, string name ) => (T)typeof( SoundChain ).GetField( name, BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( chain )!;
	private static void MakeDue( SoundChain chain ) => typeof( SoundChain ).GetField( "nextChildAt", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( chain, 0L );

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		mixer = new AudioMixer( null );
		system = new SoundEventSystem( mixer, 7 );
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		system.StopChains();
		mixer.Dispose();
	}

	private static SoundEntry Sound( uint weight, params SoundLink[] links ) =>
		new( 100, 100, 0, 0, 0, 0, weight, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, links, Array.Empty<byte>() );

	private (SoundCategory Category, SoundEvent Event) Category( params SoundEntry[] sounds )
	{
		var category = Empty<SoundCategory>();
		var catalog = Empty<SoundCatalog>();
		var decoded = (Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!;
		decoded[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 22050 ).ToArray(), 1, 0 );
		var soundEvent = new SoundEvent( 5, 0, 0x404, 0, sounds );
		SetProperty( catalog, "Events", new[] { soundEvent } );
		SetProperty( category, "Catalog", catalog );
		return (category, soundEvent);
	}

	private static SoundLink Link( uint target, byte minimum, byte maximum ) => new( target, 0, minimum, maximum );

	[TestMethod]
	public void ASoundWithoutLinksMakesTheChainStartOverAtTheFirstSound()
	{
		var (category, _) = Category( Sound( 1, Link( 2, 0, 100 ) ), Sound( 1 ) );
		var chain = system.StartChain( category, 5, AudioChannel.Effects, 50, null )!;
		system.Pump();
		Assert.AreEqual( 0, Field<int>( chain, "soundIndex" ), "the first child plays the first sound" );
		MakeDue( chain );
		system.Pump();
		Assert.AreEqual( 1, Field<int>( chain, "soundIndex" ), "the link leads to the second sound" );
		MakeDue( chain );
		system.Pump();
		Assert.IsFalse( Field<bool>( chain, "started" ), "the terminal sound fails the walk without a flag: no child is made this tick" );
		Assert.IsFalse( chain.IsHalted );
		system.Pump();
		Assert.AreEqual( 0, Field<int>( chain, "soundIndex" ), "the next tick starts the first sound again" );
		Assert.IsTrue( Field<bool>( chain, "started" ) );
	}

	[TestMethod]
	public void LinksThatDoNotHoldTheParameterHaltTheChainUntilAParameterIsSet()
	{
		var (category, _) = Category( Sound( 1, Link( 2, 10, 20 ) ), Sound( 1 ) );
		var chain = system.StartChain( category, 5, AudioChannel.Effects, 50, null )!;
		system.Pump();
		Assert.AreEqual( 1, Field<List<AudioVoice>>( chain, "children" ).Count );
		MakeDue( chain );
		system.Pump();
		Assert.IsTrue( chain.IsHalted, "links exist, none holds 50" );
		Assert.AreEqual( SoundChain.HaltedParameter, chain.Parameter, "0x7f goes into the root's parameter byte" );
		Assert.IsFalse( chain.IsStopped, "the voice lives until it is stopped" );
		// without a parameter call nothing clears the root flag 0x40: no further child
		MakeDue( chain );
		var voices = mixer.Voices.Count;
		system.Pump();
		system.Pump();
		Assert.AreEqual( voices, mixer.Voices.Count, "no further child" );
		Assert.IsTrue( chain.IsHalted );
		// 0x6bda90: the root's parameter setter keeps the flag only when the stored byte is 0x7f
		chain.Parameter = SoundChain.HaltedParameter;
		Assert.IsTrue( chain.IsHalted, "0x7f leaves the flag set" );
		chain.Parameter = 15;
		Assert.IsFalse( chain.IsHalted, "any other value clears the flag" );
		system.Pump();
		Assert.IsFalse( chain.IsHalted );
		Assert.AreEqual( 1, Field<int>( chain, "soundIndex" ), "the walk resumes with the new parameter" );
	}

	[TestMethod]
	public void TheLinkIsWalkedWithTheParameterOfTheMomentTheNextChildIsMade()
	{
		var (category, _) = Category( Sound( 1, Link( 2, 0, 30 ), Link( 3, 31, 100 ) ), Sound( 1 ), Sound( 1 ) );
		var chain = system.StartChain( category, 5, AudioChannel.Effects, 10, null )!;
		system.Pump();
		chain.Parameter = 90;
		MakeDue( chain );
		system.Pump();
		Assert.AreEqual( 2, Field<int>( chain, "soundIndex" ), "the parameter changed during the wait decides the successor" );
	}

	[TestMethod]
	public void ThePickIsTheDrawModuloTheTotalWeightAgainstTheRunningSum()
	{
		var (_, soundEvent) = Category( Sound( 1, Link( 2, 0, 100 ), Link( 3, 0, 100 ), Link( 4, 50, 60 ) ), Sound( 1 ), Sound( 3 ), Sound( 100 ) );
		uint? asked = null;
		Func<uint, uint> pick( uint value ) => total => { asked = total; return value; };
		// covering links at parameter 20: targets 2 and 3 with weights 1 and 3, total 4
		Assert.AreEqual( SoundChain.LinkWalk.Next, SoundChain.NextLink( soundEvent, 0, 20, pick( 0 ), out var next ) );
		Assert.AreEqual( 4u, asked );
		Assert.AreEqual( 1, next, "pick 0: the first link (running sum 1 >= 0)" );
		SoundChain.NextLink( soundEvent, 0, 20, pick( 1 ), out next );
		Assert.AreEqual( 1, next, "the running sum 1 reaches the pick 1" );
		SoundChain.NextLink( soundEvent, 0, 20, pick( 2 ), out next );
		Assert.AreEqual( 2, next, "pick 2 passes the first link" );
		SoundChain.NextLink( soundEvent, 0, 20, pick( 3 ), out next );
		Assert.AreEqual( 2, next );
		// at parameter 55 the third link joins: total 104
		SoundChain.NextLink( soundEvent, 0, 55, pick( 103 ), out next );
		Assert.AreEqual( 3, next );
		Assert.AreEqual( 104u, asked );
	}

	[TestMethod]
	public void NoDrawIsMadeWhenNoLinkCoversOrThereAreNone()
	{
		var (_, soundEvent) = Category( Sound( 1, Link( 2, 10, 20 ) ), Sound( 1 ) );
		var draws = 0;
		Func<uint, uint> count = _ => { draws++; return 0; };
		Assert.AreEqual( SoundChain.LinkWalk.Uncovered, SoundChain.NextLink( soundEvent, 0, 50, count, out var next ) );
		Assert.AreEqual( -1, next );
		Assert.AreEqual( SoundChain.LinkWalk.Terminal, SoundChain.NextLink( soundEvent, 1, 50, count, out next ) );
		Assert.AreEqual( 0, draws, "0x6c3e00 draws only after counting a covering link" );
	}
}
