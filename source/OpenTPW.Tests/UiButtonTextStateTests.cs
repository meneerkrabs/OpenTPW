using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The text of a model button (tp.exe 0x668820 state getter, 0x668AD3 applier): white at (0, 0), black at (1, 1) on hover, white at (2, 1) pressed.</summary>
[TestClass, DoNotParallelize]
public class UiButtonTextStateTests
{
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, "N".Select( character => new FontAtlasTests.SyntheticGlyph( character, 2, 2, 0, 0, 3 ) ).ToArray() ) );
		var models = new UiModels( name =>
		{
			var model = new UiModel( new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) ), name );
			var vertices = new[] { new UiModelVertex( NVector2.Zero, 0, NVector2.Zero ), new UiModelVertex( new NVector2( 51, 0 ), 0, NVector2.Zero ), new UiModelVertex( new NVector2( 0, 51 ), 0, NVector2.Zero ) };
			typeof( UiModel ).GetField( "<Frames>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( model, Enumerable.Range( 0, 6 )
				.Select( frame => new UiModelFrame( $"synthetic-{frame}", new[] { new UiModelPart( "frame", vertices, new[] { 0, 1, 2 } ) }, 0, 0, 51, 51 ) ).ToArray() );
			return model;
		} );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), models ) { Canvas = new UiCanvas( 1024, 768 ), ResolveTexture = name => name };
	}

	private static UiButton Button( bool withText, Func<bool>? selected = null ) => new() { Model = "b_test", Text = withText ? () => "N" : null, Selected = selected, Bounds = new UiRect( 400, 400, 400, 200 ) };

	/// <summary>The text's colour and its first vertex position: the draws after the model's own vertices.</summary>
	private static (RgbaByte Colour, NVector2 Position) Text( UiContext context, UiButton button, bool hover, bool pressed )
	{
		context.Batch.Clear();
		Button( false ).Draw( context, hover, pressed );
		var modelOnly = context.Batch.Draws.SelectMany( draw => draw.Vertices ).Count();
		context.Batch.Clear();
		button.Draw( context, hover, pressed );
		var glyph = context.Batch.Draws.SelectMany( draw => draw.Vertices ).Skip( modelOnly ).ToArray();
		Assert.IsTrue( glyph.Length > 0 );
		return (glyph[0].Color, new NVector2( glyph.Min( vertex => vertex.Position.X ), glyph.Min( vertex => vertex.Position.Y ) ));
	}

	private static void Expect( UiContext context, UiButton button, bool hover, bool pressed, RgbaByte colour, float x, float y, string state )
	{
		var step = context.ModePixel;
		var (actualColour, position) = Text( context, button, hover, pressed );
		var origin = Text( context, Button( true ), false, false ).Position;
		Assert.AreEqual( colour, actualColour, state );
		Assert.AreEqual( origin.X + x * step, position.X, 0.01f, state + " x" );
		Assert.AreEqual( origin.Y + y * step, position.Y, 0.01f, state + " y" );
	}

	[TestMethod]
	public void ANormalButtonDrawsWhiteAtTheOriginHoverBlackAtOneOneAndPressedWhiteAtTwoOne()
	{
		var context = Context();
		var button = Button( true );
		Expect( context, button, false, false, UiColors.ButtonText, 0, 0, "normal" );
		Expect( context, button, true, false, UiColors.ButtonHoverText, 1, 1, "hover" );
		Expect( context, button, true, true, UiColors.ButtonText, 2, 1, "pressed" );
	}

	[TestMethod]
	public void ACheckedButtonOnlyDiffersWhileThePointerIsOverItAndCheckedPressedIsTheHoverState()
	{
		var context = Context();
		var button = Button( true, () => true );
		Expect( context, button, false, false, UiColors.ButtonText, 0, 0, "checked" );
		Expect( context, button, true, false, UiColors.ButtonHoverText, 1, 1, "checked with the pointer" );
		// 0x668820 tests the checked flag before the pressed one: state 3, not 4
		Expect( context, button, true, true, UiColors.ButtonHoverText, 1, 1, "checked and pressed" );
	}

	[TestMethod]
	public void ADisabledButtonKeepsTheLastAppliedColourAndOffsetAndIsWhiteWhenNeverHovered()
	{
		var context = Context();
		var fresh = Button( true );
		fresh.Enabled = false;
		Expect( context, fresh, false, false, UiColors.ButtonText, 0, 0, "disabled, never applied" );
		var hovered = Button( true );
		Expect( context, hovered, true, false, UiColors.ButtonHoverText, 1, 1, "hover" );
		hovered.Enabled = false;
		Expect( context, hovered, false, false, UiColors.ButtonHoverText, 1, 1, "disabled after hover keeps the hover state" );
	}
}
