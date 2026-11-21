using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using System;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's letter window (docs/reverse/TPI-BANK-MAIL.md): the generated layout table, the window's fields and buttons on synthetic
/// strings, and over the retail corpus when <c>OPENTPW_TPI_PATH</c> names it, the letter's texts, its date and a headless draw.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncLetterTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		UiImages.ResetListings();
	}

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		UiImages.ResetListings();
		return data;
	}

	private static UiStringTable SyntheticStrings()
	{
		var ui = Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray();
		var help = Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray();
		return new UiStringTable( "English", ui, help, Array.Empty<string>(), Array.Empty<string>() );
	}

	private static UiContext FakeContext( UiStringTable strings )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1920, 1080 ), ResolveTexture = _ => null };
	}

	private static void Click( UiScreenStack stack, UiContext context, string id )
	{
		var element = stack.Top!.Find( id ) ?? throw new AssertFailedException( $"The top screen has no element {id}." );
		var centre = element.ScreenRect( context.Canvas ).Center;
		stack.Update( context, UiInput.Idle( centre ) );
		stack.Update( context, UiInput.Click( centre ) );
	}

	[TestMethod]
	public void TheLetterTableIsTheMailWindowWithItsControls()
	{
		var table = ThemeParkIncLayoutTables.LetterWindow;
		Assert.AreEqual( 0x9B4854, table.Address );
		Assert.AreEqual( "!window", table.Root.Model );
		Assert.AreEqual( new UiRect( 46, 44, 1956, 1448 ), table.Root.Rect );
		Assert.AreEqual( "mugshot", table.Control( 0xA7FE ).Model );
		Assert.AreEqual( "b_okay", table.Control( 0xA805 ).Model );
		Assert.AreEqual( "b_close", table.Control( 0xA804 ).Model );
		Assert.AreEqual( "b_help", table.Control( 0xA807 ).Model );
		Assert.AreEqual( "b_goto", table.Control( 0xA808 ).Model );
		Assert.AreEqual( 279, table.Control( 0xA807 ).Help );
		Assert.IsNotNull( table.Control( 0xA801 ).Inner, "the body frame has an inner rectangle" );
	}

	[TestMethod]
	public void TheWindowShowsTheLetterAndOnlyCloseOkayAndGoToDismissIt()
	{
		var strings = SyntheticStrings();
		var letter = new ThemeParkIncLetterWindow.Letter( "Subject", "Sender", "Player", "1.2.2003", "Body", 0 );
		var dismissed = 0;
		var stack = new UiScreenStack();
		var screen = ThemeParkIncLetterWindow.Create( letter, strings, () => dismissed++ );
		stack.Push( screen );
		string Text( string id ) => ((UiLabel)screen.Find( id )!).Text();
		Assert.AreEqual( "Subject", Text( "title" ) );
		Assert.AreEqual( "Subject", Text( "subject" ) );
		Assert.AreEqual( "Sender", Text( "from" ) );
		Assert.AreEqual( "Player", Text( "to" ) );
		Assert.AreEqual( "1.2.2003", Text( "date" ) );
		Assert.AreEqual( "Body", Text( "body" ) );
		Assert.AreEqual( "ui857", Text( "subjectLabel" ) );
		Assert.AreEqual( "ui856", Text( "fromLabel" ) );
		Assert.AreEqual( "ui855", Text( "toLabel" ) );
		Assert.AreEqual( "ui858", Text( "dateLabel" ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 0, 0x96, 0xFF ), ((UiLabel)screen.Find( "fromLabel" )!).Color, "the labels' colour" );
		Assert.AreEqual( UiColors.Ink, ((UiLabel)screen.Find( "from" )!).Color, "the values' colour" );
		Assert.IsFalse( screen.Find( "goto" )!.Enabled, "a notification letter has no go-to" );
		var context = FakeContext( strings );
		Click( stack, context, "help" );
		Assert.AreEqual( 0, dismissed, "help does not close the letter" );
		Click( stack, context, "ok" );
		Assert.AreEqual( 1, dismissed, "okay closes it" );
		var again = ThemeParkIncLetterWindow.Create( letter, strings, () => dismissed++ );
		stack.Push( again );
		Click( stack, context, "close" );
		Assert.AreEqual( 2, dismissed, "close does too" );
	}

	[TestMethod]
	public void TheRetailLetterCarriesTheGameOverTextsAndTheParkDate()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var tags = language.LoadStrings( "TAG_SYSTEM.str" );
		var letter = ThemeParkIncLetterWindow.Compose( ThemeParkIncAdviceMail.RedSixMonths, tags, "Player", "date" );
		Assert.AreEqual( "Game Over - You Lose!", letter.Subject );
		Assert.AreEqual( "Mr Maybury", letter.From );
		Assert.IsTrue( letter.Body.StartsWith( tags.Entries[166], StringComparison.Ordinal ), "the bank's letter body" );
		Assert.IsTrue( letter.Body.EndsWith( "\nMr Maybury\nPresident, Theme Park Inc.", StringComparison.Ordinal ), "the signature: " + letter.Body );
		Assert.AreEqual( 0, letter.Picture );
		var fired = ThemeParkIncLetterWindow.Compose( ThemeParkIncAdviceMail.ChallengeLost, tags, "Player", "date" );
		Assert.IsTrue( fired.Body.StartsWith( tags.Entries[167], StringComparison.Ordinal ), "the firing letter's body" );
		Assert.AreEqual( "To:", strings.Entry( ThemeParkIncLetterWindow.ToLabel ) );
		Assert.AreEqual( "From:", strings.Entry( ThemeParkIncLetterWindow.FromLabel ) );
		Assert.AreEqual( "Subject:", strings.Entry( ThemeParkIncLetterWindow.SubjectLabel ) );
		Assert.AreEqual( "Date:", strings.Entry( ThemeParkIncLetterWindow.DateLabel ) );
		// [BIN:TPI-EXE:0x518C30 letter date] the template's slots take the day (0x29), the month (0x2A) and the year (0x2B)
		Assert.AreEqual( "3.2.2001", strings.FormatByType( ThemeParkIncLetterWindow.DateTemplate,
			new System.Collections.Generic.Dictionary<int, string> { [0x29] = "3", [0x2A] = "2", [0x2B] = "2001" } ) );
	}

	[TestMethod]
	public void TheLetterBuildsAndDrawsOverTheRealModels()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var loader = UiFonts.CachedLoader( language );
		var fonts = new UiFonts( _ => loader( "PERSONNELFILEMED.bf4" ) );
		var letter = ThemeParkIncLetterWindow.Compose( ThemeParkIncAdviceMail.RedSixMonths, language.LoadStrings( "TAG_SYSTEM.str" ), "Player", "1.7.2000" );
		var stack = new UiScreenStack();
		stack.Push( ThemeParkIncLetterWindow.Create( letter, strings, () => { } ) );
		var context = new UiContext( strings, fonts, new UiModels() ) { Canvas = new UiCanvas( 1920, 1080 ) };
		stack.Update( context, UiInput.Idle( new N.Vector2( 960, 540 ) ) );
		context.Batch.Clear();
		stack.Draw( context );
		Assert.IsTrue( context.Batch.Draws.Count > 0, "the letter draws" );
		// every model the table names loads from Theme Park Inc's ui.wad with a drawable frame and resolvable textures
		var models = new UiModels();
		foreach ( var name in Flatten( ThemeParkIncLayoutTables.LetterWindow.Root ).Select( node => node.Model ).OfType<string>().Distinct() )
		{
			var model = models.Get( name );
			Assert.IsNotNull( model, $"UiModel {name} loads" );
			Assert.IsTrue( model!.Frames.Count >= 1 && model.Frames[0].Parts.Count >= 1, $"{name} has a drawable frame" );
			foreach ( var part in model.Frames[0].Parts.Where( part => part.TextureName.Length > 0 ) )
				Assert.IsNotNull( UiImages.Resolve( part.TextureName ), $"{name} texture {part.TextureName}" );
		}
	}

	private static System.Collections.Generic.IEnumerable<LayoutNode> Flatten( LayoutNode node ) => new[] { node }.Concat( node.Children.SelectMany( Flatten ) );
}
