using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The research window's sounds: two loops while it is open and a sentence while an effort slider is held (tp.exe 0x4aa480, 0x4a96b0).</summary>
[TestClass]
[DoNotParallelize]
public class ResearchWindowAudioTests
{
	private static ParkHud Hud() => (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );

	private static void Call( object target, string name, params object[] args ) =>
		typeof( ParkHud ).GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( target, args );

	[DataTestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 1, 0 )]
	[DataRow( 512, 50 )]
	[DataRow( 1023, 99 )]
	[DataRow( 1024, 100 )]
	public void TheWorkloadKnobSetsTheLoopsParameterToValueTimesHundredOver1024( int knob, int parameter ) =>
		Assert.AreEqual( parameter, ParkHud.ResearchHumParameter( knob ) );

	[TestMethod]
	public void TheLoopsStartWithTheWindowAndEndWithIt()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		Call( hud, "StartResearchSounds" );
		CollectionAssert.AreEqual( new uint[] { 154, 155 }, probe.Events( "cat_ui" ), "0x9A and 0x9B, flat" );
		Assert.IsTrue( probe.Fired.All( item => item.Position == null ) );
		Call( hud, "StopResearchSounds" );
		Call( hud, "StopResearchSounds" );
	}

	[TestMethod]
	public void AnEffortSliderHeldPlaysItsSentenceUntilItIsLetGo()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		Call( hud, "StartResearchSlider", 40 );
		Call( hud, "SetResearchSlider", 55 );
		Call( hud, "StopResearchSlider" );
		Call( hud, "SetResearchSlider", 60 );
		CollectionAssert.AreEqual( new uint[] { 156 }, probe.Events( "cat_ui" ), "0x9C once per grab" );
	}

	[TestMethod]
	public void ASliderReportsTheGrabOfItsThumbAndItsReleaseButNotAPageClick()
	{
		var canvas = new UiCanvas( 2048, 1536 );
		var value = 50;
		var grabbed = new System.Collections.Generic.List<int>();
		var released = 0;
		var slider = new UiSlider
		{
			Bounds = new UiRect( 0, 0, 1067, 67 ), Anchor = UiAnchor.TopLeft, Track = new UiRect( 0, 0, 1067, 67 ), KnobSize = new System.Numerics.Vector2( 67, 67 ),
			Steps = () => 101, Truncates = true, Value = () => value, Changed = v => value = v, Grabbed = grabbed.Add, Released = () => released++
		};
		IUiDragTarget target = slider;
		System.Numerics.Vector2 At( float x ) => canvas.Map( new UiRect( x, 0, 1, 1 ), UiAnchor.TopLeft ).Center;
		target.DragBegin( canvas, At( 400 ) );
		target.DragEnd( canvas, At( 400 ) );
		Assert.AreEqual( (0, 0), (grabbed.Count, released), "a click beside the thumb pages and is neither a grab nor a release" );
		var thumb = slider.KnobRect( value );
		target.DragBegin( canvas, At( thumb.X + thumb.Width / 2 ) );
		CollectionAssert.AreEqual( new[] { value }, grabbed, "0x801 carries the value" );
		target.DragEnd( canvas, At( thumb.X + thumb.Width / 2 ) );
		Assert.AreEqual( 1, released, "0x802" );
	}

	[TestMethod]
	public void TheShippedEventsAreTheLoopsAndTheBoundSentence()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var fileSystem = new BaseFileSystem( data );
		var ui = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ui" )!;
		Assert.IsNotNull( ui );
		foreach ( var (id, player, code) in new[] { (154u, SoundEventPlayer.BranchingSentence, (byte)12), (155u, SoundEventPlayer.BranchingSentence, (byte)13) } )
		{
			var soundEvent = ui.Catalog.Find( id )!;
			Assert.AreEqual( player, soundEvent.Player, id.ToString() );
			Assert.IsTrue( soundEvent.Sounds.All( sound => SoundSentence.BindsSlot( sound, code ) ), $"event {id} binds parameter {code} to its volume" );
		}
		var slider = ui.Catalog.Find( 156 )!;
		Assert.AreEqual( SoundEventPlayer.Sentence, slider.Player );
		Assert.IsTrue( slider.Sounds.All( sound => SoundSentence.BindsSlot( sound, 11 ) && SoundSentence.BindsSlot( sound, 10 ) ), "parameters 0xB and 0xA" );
	}
}
