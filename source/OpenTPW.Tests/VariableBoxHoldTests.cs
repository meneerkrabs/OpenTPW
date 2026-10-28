using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The variable box's b_minus and b_plus while held (tp.exe 0x668dd9, 0x66a7e1, 0x66aa61; UI-024).</summary>
[TestClass]
public class VariableBoxHoldTests
{
	[TestMethod]
	public void TheHoldStartStepsOnlyThePlusButtonAndTheIncrementGrowsTenfoldEveryTwentyTicks()
	{
		var hold = new ParkHud.VariableBoxHold();
		Assert.AreEqual( 11, hold.Start( 10, plus: true, 0, 500 ), "0x66a7e1 case 3: value + step at the hold start" );
		Assert.AreEqual( 10, new ParkHud.VariableBoxHold().Start( 10, plus: false, 0, 500 ), "case 2 only arms the count" );
		var value = 11;
		for ( var tick = 1; tick <= 19; tick++ )
			value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 11 + 19, value, "twenty ticks at an increment of 1 ..." );
		value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 11 + 20, value, "... the twentieth still adds 1" );
		Assert.AreEqual( 10, hold.Increment, "and then the increment is 10" );
		Assert.AreEqual( 20, hold.Counter );
		value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 31 + 10, value );
		for ( var tick = 2; tick <= 20; tick++ )
			value = hold.Tick( value, true, 0, 10000 );
		Assert.AreEqual( 100, hold.Increment, "the next twenty ticks end in another tenfold step" );
	}

	[TestMethod]
	public void TheMinusButtonCountsDownAndEveryValueStaysInTheBoxsRange()
	{
		var hold = new ParkHud.VariableBoxHold();
		hold.Start( 5, plus: false, 0, 500 );
		Assert.AreEqual( 4, hold.Tick( 5, false, 0, 500 ) );
		Assert.AreEqual( 0, hold.Tick( 1, false, 0, 500 ) );
		Assert.AreEqual( 0, hold.Tick( 0, false, 0, 500 ), "0x66a71c raises a value below the minimum to it" );
		Assert.AreEqual( 500, new ParkHud.VariableBoxHold().Start( 500, true, 0, 500 ) );
	}

	private static (UiScreen Screen, UiButton Button, List<string> Log) Fixture()
	{
		var log = new List<string>();
		var screen = new UiScreen( "box" );
		var button = new UiButton
		{
			Id = "plus", Model = null, Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = () => log.Add( "click" ),
			Repeat = new UiButtonRepeat( 0.2f, 0.05f, () => log.Add( "start" ), () => log.Add( "tick" ) )
		};
		screen.Add( button );
		return (screen, button, log);
	}

	private static void Frame( UiScreen screen, float time, bool down = false, bool pressed = false, bool released = false )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( n => throw new System.IO.FileNotFoundException( n ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
		screen.Update( context, new UiInput( new NVector2( 50, 50 ), down || pressed, pressed, released, false, UiKeys.None ) );
	}

	[TestMethod]
	public void AQuickPressIsAClickAndNothingRepeats()
	{
		var (screen, _, log) = Fixture();
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.1f, down: true );
		Frame( screen, 0.15f, released: true );
		CollectionAssert.AreEqual( new[] { "click" }, log );
	}

	[TestMethod]
	public void AHeldButtonStartsAfter200MsTicksOverFiftyMsApartAndLettingGoIsNoClick()
	{
		var (screen, button, log) = Fixture();
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.1f, down: true );
		Frame( screen, 0.19f, down: true );
		Assert.AreEqual( 0, log.Count, "nothing before the delay" );
		Frame( screen, 0.2f, down: true );
		CollectionAssert.AreEqual( new[] { "start" }, log );
		Frame( screen, 0.24f, down: true );
		Assert.AreEqual( 1, log.Count, "not yet 50 ms after the start" );
		Frame( screen, 0.25f, down: true );
		Assert.AreEqual( 1, log.Count, "exactly 50 ms is not more than the interval" );
		Frame( screen, 0.26f, down: true );
		CollectionAssert.AreEqual( new[] { "start", "tick" }, log );
		Frame( screen, 0.33f, down: true );
		Frame( screen, 0.33f, released: true );
		CollectionAssert.AreEqual( new[] { "start", "tick", "tick" }, log, "the release of a repeating button posts no click (0x668eff)" );
		Assert.AreEqual( 0, button.HoldState );
	}

	private static void FrameAt( UiScreen screen, float time, float x, bool down = false, bool pressed = false, bool released = false )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( n => throw new System.IO.FileNotFoundException( n ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
		screen.Update( context, new UiInput( new NVector2( x, 50 ), down || pressed, pressed, released, false, UiKeys.None ) );
	}

	/// <summary>A plus button on a value as the object window wires it, <paramref name="guest"/> adding the press's <see cref="UiButtonRepeat.Pressed"/>.</summary>
	private static (UiScreen Screen, ParkHud.VariableBoxHold Hold, Func<int> Value) PlusBox( bool guest )
	{
		var value = 100;
		var hold = new ParkHud.VariableBoxHold();
		var screen = new UiScreen( "box" );
		screen.Add( new UiButton
		{
			Id = "plus", Model = null, Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft,
			Repeat = new UiButtonRepeat( ParkHud.VariableBoxHold.Delay, ParkHud.VariableBoxHold.Interval, () => value = hold.Start( value, true, 0, 10000 ), () => value = hold.Tick( value, true, 0, 10000 ) )
			{
				Pressed = guest ? hold.Press : null
			}
		} );
		return (screen, hold, () => value);
	}

	/// <summary>A hold over the button from its press to 20 ticks and the release: the increment ends at ten.</summary>
	private static float HoldToTens( UiScreen screen, ParkHud.VariableBoxHold hold )
	{
		FrameAt( screen, 0, 50, pressed: true );
		FrameAt( screen, 0.2f, 50, down: true );
		var time = 0.2f;
		for ( var tick = 0; tick < 20; tick++ )
			FrameAt( screen, time += 0.06f, 50, down: true );
		FrameAt( screen, time, 50, released: true );
		Assert.AreEqual( 10, hold.Increment, "the first hold reached increments of ten" );
		return time;
	}

	[TestMethod]
	public void TheScreenRunsPressedAtTheButtonsPressAndPressStartsTheHoldAfreshWithoutThePlusStep()
	{
		var (screen, hold, value) = PlusBox( guest: true );
		hold.Start( 0, true, 0, 10 );
		for ( var tick = 0; tick < 20; tick++ )
			hold.Tick( 0, true, 0, 10 );
		hold.LastAsked = 7;
		Assert.AreEqual( 10, hold.Increment );
		FrameAt( screen, 5, 50, pressed: true );
		Assert.AreEqual( 20, hold.Counter, "the press restarts the count" );
		Assert.AreEqual( 1, hold.Increment, "and the increment" );
		Assert.IsNull( hold.LastAsked, "and empties the memory" );
		Assert.AreEqual( 100, value(), "without the plus step, which only the hold's start takes" );
	}

	[TestMethod]
	public void AGuestsHoldWhosePointerMissedItsStartStepsByOneAtItsFirstTickAfterAnEarlierHoldOfTens()
	{
		var (screen, hold, value) = PlusBox( guest: true );
		var time = HoldToTens( screen, hold ) + 1;
		var before = value();
		FrameAt( screen, time, 50, pressed: true );
		FrameAt( screen, time + 0.1f, 500, down: true );
		FrameAt( screen, time + 0.2f, 500, down: true );
		Assert.AreEqual( before, value(), "the start was skipped: the pointer was outside at the delay" );
		FrameAt( screen, time + 0.26f, 50, down: true );
		Assert.AreEqual( before + 1, value(), "the first tick steps by exactly one" );
		Assert.AreEqual( 1, hold.Increment );
	}

	[TestMethod]
	public void AHostsBoxWithoutPressedKeepsTheOriginalArithmeticOfTheSameSequence()
	{
		var (screen, hold, value) = PlusBox( guest: false );
		var time = HoldToTens( screen, hold ) + 1;
		var before = value();
		FrameAt( screen, time, 50, pressed: true );
		FrameAt( screen, time + 0.1f, 500, down: true );
		FrameAt( screen, time + 0.2f, 500, down: true );
		FrameAt( screen, time + 0.26f, 50, down: true );
		Assert.AreEqual( before + 10, value(), "only the start resets the increment, so the missed start leaves the earlier hold's ten" );
		Assert.AreEqual( 10, hold.Increment );
	}
}
