using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// A guest's slider asks once per step ([EXT:online-coop-commands], C4b-2): <see cref="UiSlider.AsksOncePerStep"/> through a <see cref="UiScreen"/> driven frame by frame as the game drives it.
/// The track is 1000 wide with a 100 knob at the screen's origin (scale 1), so the eleven steps are 90 apart: step <c>i</c> has its knob centre at <c>90 i + 50</c>.
/// </summary>
[TestClass]
public class UiSliderGestureTests
{
	private sealed class Rig
	{
		public readonly UiScreen Screen = new( "slider" );
		public readonly UiSlider Slider;
		public readonly List<int> Asked = new();
		/// <summary>The value the slider draws, as a queued change's drawn value would read.</summary>
		public int Value;
		/// <summary>Whether an ask is accepted (the value becomes it) or refused (nothing changes).</summary>
		public bool Accept = true;
		public bool Asks = true;
		private float time;

		public Rig( bool truncates = true, Func<bool>? rule = null, bool ruleSet = true )
		{
			Slider = new UiSlider
			{
				Id = "slider", Bounds = new UiRect( 0, 0, 1000, 100 ), Anchor = UiAnchor.TopLeft, Track = new UiRect( 0, 0, 1000, 100 ), KnobSize = new NVector2( 100, 100 ),
				KnobTop = 0, Steps = () => 11, Value = () => Value, Truncates = truncates,
				AsksOncePerStep = ruleSet ? rule ?? (() => Asks) : null,
				Changed = value =>
				{
					Asked.Add( value );
					if ( Accept )
						Value = value;
				}
			};
			Screen.Add( Slider );
		}

		/// <summary>The x of the pointer inside step <paramref name="step"/>'s span, 10 right of its knob centre.</summary>
		public static float At( int step ) => 90 * step + 60;
		public static float Centre( int step ) => 90 * step + 50;

		private void Frame( float x, bool down, bool pressed, bool released )
		{
			var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
			var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( n => throw new System.IO.FileNotFoundException( n ) ) )
				{ Canvas = new UiCanvas( 2048, 1536 ), Time = time += 0.01f, ResolveTexture = _ => null };
			Screen.Update( context, new UiInput( new NVector2( x, 50 ), down || pressed, pressed, released, false, UiKeys.None ) );
		}

		public void Press( float x ) => Frame( x, false, true, false );
		public void Hold( float x ) => Frame( x, true, false, false );
		public void Release( float x ) => Frame( x, false, false, true );
	}

	[TestMethod]
	public void AThumbHeldStillOnARefusedStepAsksOnceAndAgainOnlyForAnotherStep()
	{
		var rig = new Rig { Accept = false };
		rig.Press( Rig.Centre( 0 ) );
		rig.Hold( Rig.At( 3 ) );
		CollectionAssert.AreEqual( new[] { 3 }, rig.Asked, "the grab asks for the step it moved to" );
		rig.Hold( Rig.At( 3 ) );
		rig.Hold( Rig.At( 3 ) + 5 );
		CollectionAssert.AreEqual( new[] { 3 }, rig.Asked, "the pointer staying on the refused step asks nothing more" );
		rig.Accept = true;
		rig.Hold( Rig.At( 3 ) );
		CollectionAssert.AreEqual( new[] { 3 }, rig.Asked, "also after asks would be accepted again" );
		rig.Hold( Rig.At( 4 ) );
		CollectionAssert.AreEqual( new[] { 3, 4 }, rig.Asked, "another step is asked for" );
		Assert.AreEqual( 4, rig.Value );
	}

	[TestMethod]
	public void APressedAgainOnTheSameStepAsksAgain()
	{
		var grab = new Rig { Accept = false };
		for ( var gesture = 0; gesture < 2; gesture++ )
		{
			grab.Press( Rig.Centre( 0 ) );
			grab.Hold( Rig.At( 3 ) );
			grab.Release( Rig.At( 3 ) );
		}
		CollectionAssert.AreEqual( new[] { 3, 3 }, grab.Asked, "a thumb grab asks again when moved in the next gesture" );

		var page = new Rig { Accept = false };
		for ( var gesture = 0; gesture < 2; gesture++ )
		{
			page.Press( Rig.At( 5 ) );
			page.Release( Rig.At( 5 ) );
		}
		CollectionAssert.AreEqual( new[] { 1, 1 }, page.Asked, "a track press asks its page step again in the next gesture" );
	}

	[TestMethod]
	public void AFarTrackClickAsksItsOnePageStepAtThePressAndNothingAtTheRelease()
	{
		var rig = new Rig();
		rig.Press( Rig.At( 9 ) );
		CollectionAssert.AreEqual( new[] { 1 }, rig.Asked, "one page step at the press, not the pointer's step" );
		rig.Release( Rig.At( 9 ) );
		CollectionAssert.AreEqual( new[] { 1 }, rig.Asked, "the release frame and the release's click ask nothing" );
		Assert.AreEqual( 1, rig.Value );
	}

	[TestMethod]
	public void AnOffCentreThumbClickWithoutAMoveAsksNothing()
	{
		var rig = new Rig { Value = 3 };
		rig.Press( Rig.Centre( 3 ) + 20 );
		rig.Release( Rig.Centre( 3 ) + 20 );
		Assert.AreEqual( 0, rig.Asked.Count, "the thumb keeps its grab offset, so the pointer stays on the drawn step" );
	}

	[TestMethod]
	public void AStepRefusedAtThePressAsksNothingAtTheReleaseAlsoWhenAsksAreAcceptedByThen()
	{
		var rig = new Rig { Accept = false };
		rig.Press( Rig.At( 9 ) );
		CollectionAssert.AreEqual( new[] { 1 }, rig.Asked );
		rig.Accept = true;
		rig.Release( Rig.At( 9 ) );
		CollectionAssert.AreEqual( new[] { 1 }, rig.Asked, "the refused page step is not asked at the release" );
		Assert.AreEqual( 0, rig.Value );
	}

	[TestMethod]
	public void AGrabReleasedOnAStepNewlyCrossedInTheReleaseFrameAsksNothingForIt()
	{
		var rig = new Rig();
		rig.Press( Rig.Centre( 0 ) );
		rig.Hold( Rig.Centre( 0 ) );
		rig.Release( Rig.At( 3 ) );
		Assert.AreEqual( 0, rig.Asked.Count, "the release frame's pointer is not followed" );
		Assert.AreEqual( 0, rig.Value );
	}

	[TestMethod]
	public void AKeyAsksOnceRegardlessOfTheMemoryAndLeavesItAlone()
	{
		var rig = new Rig { Accept = false };
		rig.Press( Rig.At( 9 ) );
		rig.Release( Rig.At( 9 ) );
		rig.Slider.Adjust( 1 );
		CollectionAssert.AreEqual( new[] { 1, 1 }, rig.Asked, "a key asks the step the gesture asked for before" );
		rig.Slider.Adjust( 1 );
		CollectionAssert.AreEqual( new[] { 1, 1, 1 }, rig.Asked, "every key asks" );

		rig = new Rig { Accept = false };
		rig.Press( Rig.Centre( 0 ) );
		rig.Hold( Rig.At( 3 ) );
		rig.Slider.Adjust( 1 );
		rig.Hold( Rig.At( 3 ) );
		CollectionAssert.AreEqual( new[] { 3, 1 }, rig.Asked, "the key's step did not replace the gesture's memory" );
	}

	[TestMethod]
	public void ASliderWithoutTheTruncatingPressAsksFromThePointerOncePerValueOnItsHeldFrames()
	{
		var rig = new Rig( truncates: false ) { Accept = false };
		rig.Press( Rig.Centre( 3 ) );
		rig.Hold( Rig.Centre( 3 ) );
		CollectionAssert.AreEqual( new[] { 3 }, rig.Asked, "the press asks the pointer's value once, held frames on it add nothing" );
		rig.Accept = true;
		rig.Hold( Rig.Centre( 3 ) );
		CollectionAssert.AreEqual( new[] { 3 }, rig.Asked );
		rig.Hold( Rig.Centre( 4 ) );
		CollectionAssert.AreEqual( new[] { 3, 4 }, rig.Asked );
		rig.Release( Rig.Centre( 4 ) );
		CollectionAssert.AreEqual( new[] { 3, 4 }, rig.Asked, "the release frame and the release's click add nothing on the same value" );
	}

	/// <summary>A host's slider: no rule, and a rule that says no.</summary>
	private static IEnumerable<Rig> HostRigs()
	{
		yield return new Rig( ruleSet: false );
		yield return new Rig( rule: () => false );
	}

	[TestMethod]
	public void AHostsSliderKeepsItsPagePressDragReleaseFrameAndReleaseClick()
	{
		foreach ( var rig in HostRigs() )
		{
			rig.Press( Rig.Centre( 5 ) + 10 + 90 * 4 );
			Assert.AreEqual( 1, rig.Value, "the page press moves one page" );
			rig.Release( Rig.Centre( 5 ) );
			CollectionAssert.AreEqual( new[] { 1, 5 }, rig.Asked, "the release's click sets the pointer's value" );
			Assert.AreEqual( 5, rig.Value );
		}
		foreach ( var rig in HostRigs() )
		{
			rig.Press( Rig.Centre( 0 ) );
			rig.Hold( Rig.At( 3 ) );
			rig.Hold( Rig.At( 4 ) );
			CollectionAssert.AreEqual( new[] { 3, 4 }, rig.Asked, "a drag sets each value the pointer reaches" );
			rig.Release( Rig.At( 5 ) );
			CollectionAssert.AreEqual( new[] { 3, 4, 5 }, rig.Asked, "the release frame's pointer is followed, and the release's click adds nothing on the same value" );
			Assert.AreEqual( 5, rig.Value );
		}
	}
}
