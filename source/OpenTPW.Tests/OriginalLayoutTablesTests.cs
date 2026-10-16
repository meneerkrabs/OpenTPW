using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The original UI layout tables and pointer rules generated from the Windows executable (tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py).</summary>
[TestClass]
public class OriginalLayoutTablesTests
{
	[TestMethod]
	public void TheRideItButtonSitsAtItsTableRectangleWithItsHelp()
	{
		var button = OriginalLayoutTables.RideWindow.Model( "b_ride it!" );
		Assert.AreEqual( new UiRect( 956, 800, 153, 153 ), button.Rect );
		Assert.AreEqual( 16, button.Help );
		Assert.AreEqual( 0x3E37u, button.Id );
		Assert.AreEqual( new UiRect( 248, 30, 1552, 977 ), OriginalLayoutTables.RideWindow.Root.Rect );
		Assert.AreEqual( "window2", OriginalLayoutTables.RideWindow.Root.Model );
	}

	[TestMethod]
	public void TheObjectWindowsShareTheirButtonsAndKeepTheDoorsToggleHelp()
	{
		foreach ( var table in new[] { OriginalLayoutTables.RideWindow, OriginalLayoutTables.ShopWindow, OriginalLayoutTables.SideshowWindow } )
		{
			Assert.AreEqual( new UiRect( 300, 800, 154, 153 ), table.Model( "b_erase" ).Rect, table.Name );
			Assert.AreEqual( new UiRect( 1430, 784, 205, 169 ), table.Model( "b_door" ).Rect, table.Name );
			Assert.IsTrue( table.Model( "b_door" ).IsToggle, table.Name );
			Assert.AreEqual( 1, table.Model( "b_okay" ).Help, table.Name );
		}
		Assert.AreEqual( (13, 12), (OriginalLayoutTables.RideWindow.Model( "b_door" ).Help, OriginalLayoutTables.RideWindow.Model( "b_door" ).AltHelp) );
		Assert.AreEqual( 8, OriginalLayoutTables.RideWindow.Model( "b_callmech" ).Help );
		Assert.AreEqual( 19, OriginalLayoutTables.RideWindow.Model( "b_upgrade" ).Help );
	}

	[TestMethod]
	public void TheConfirmDialogIsTheUpperCentreWindowWithTickAndCrossAtTheRight()
	{
		var table = OriginalLayoutTables.ConfirmDialog;
		Assert.AreEqual( new UiRect( 500, 233, 1016, 597 ), table.Root.Rect );
		Assert.AreEqual( "wdialogw", table.Root.Model );
		Assert.AreEqual( new UiRect( 1376, 524, 84, 84 ), table.Model( "b_okay" ).Rect );
		Assert.AreEqual( new UiRect( 1387, 613, 83, 83 ), table.Model( "b_exit" ).Rect );
		Assert.AreEqual( OriginalLayoutTables.ConfirmDialog.Root.Rect, OriginalLayoutTables.NameDialog.Root.Rect );
	}

	[TestMethod]
	public void TheLobbyTablesGiveThePanelButtonsAndNameField()
	{
		var lobby = OriginalLayoutTables.Lobby;
		Assert.AreEqual( new UiRect( 28, 855, 448, 666 ), lobby.Root.Rect );
		Assert.AreEqual( "lobbutbg", lobby.Root.Model2 );
		Assert.AreEqual( new UiRect( 67, 1273, 371, 211 ), lobby.Root.Inner );
		Assert.AreEqual( (new UiRect( 68, 946, 153, 153 ), 346), (lobby.Model( "b_worldview" ).Rect, lobby.Model( "b_worldview" ).Help) );
		Assert.AreEqual( (new UiRect( 68, 1114, 153, 153 ), 345), (lobby.Model( "b_entpark" ).Rect, lobby.Model( "b_entpark" ).Help) );
		Assert.AreEqual( (343, 344), (lobby.Model( "b_lobleft" ).Help, lobby.Model( "b_lobright" ).Help) );
		Assert.AreEqual( new UiRect( 477, 1282, 1094, 175 ), OriginalLayoutTables.LobbyCity.Control( 0x49D9 ).Rect );
	}

	[TestMethod]
	public void TheMessagePanelCameraPanelAndCamcorderHaveTheirRectangles()
	{
		var panel = OriginalLayoutTables.MessagePanel;
		Assert.AreEqual( new UiRect( 458, 1101, 615, 341 ), panel.Root.Rect );
		Assert.AreEqual( new UiRect( 486, 1325, 103, 103 ), panel.Model( "mb_erase" ).Rect );
		Assert.AreEqual( new UiRect( 486, 1218, 103, 102 ), panel.Control( 0x10D2F ).Rect );
		Assert.IsNull( panel.Control( 0x10D2F ).Model, "the action button's art is chosen per message" );
		Assert.AreEqual( new UiRect( 474, 1316, 153, 153 ), OriginalLayoutTables.CameraPanel.Model( "b_1person" ).Rect );
		Assert.AreEqual( (479, 480), (OriginalLayoutTables.CameraPanel.Model( "b_1person" ).Help, OriginalLayoutTables.CameraPanel.Model( "b_postcard" ).Help) );
		Assert.AreEqual( new UiRect( 170, 170, 1708, 1196 ), OriginalLayoutTables.Camcorder.Model( "viewfinder" ).Rect );
		Assert.AreEqual( new UiRect( 1916, 1404, 102, 102 ), OriginalLayoutTables.Camcorder.Model( "b_eject" ).Rect );
	}

	[TestMethod]
	public void EveryTableModelNameHashesToItsStoredKey()
	{
		Assert.AreEqual( unchecked((int)0xAAEE5929u), UiModels.RootNameKey( "b_ride it!" ) );
		var tables = new[] { OriginalLayoutTables.RideWindow, OriginalLayoutTables.ShopWindow, OriginalLayoutTables.SideshowWindow, OriginalLayoutTables.MessagePanel,
			OriginalLayoutTables.CameraPanel, OriginalLayoutTables.Camcorder, OriginalLayoutTables.ConfirmDialog, OriginalLayoutTables.NameDialog, OriginalLayoutTables.Lobby, OriginalLayoutTables.LobbyCity };
		foreach ( var model in tables.SelectMany( table => table.All ).SelectMany( node => new[] { node.Model, node.Model2 } ).Where( name => name != null ).Distinct() )
			Assert.IsTrue( model!.Length is > 0 and <= 255, model );
	}

	[TestMethod]
	public void ThePointerTableHasTheTwentyTwoIdsWithTheirFiles()
	{
		Assert.AreEqual( OriginalPointers.Count, OriginalPointerTables.Files.Length );
		CollectionAssert.AreEqual( Enumerable.Range( 0, OriginalPointers.Count ).ToArray(), OriginalPointerTables.Files.Select( entry => entry.Id ).ToArray() );
		Assert.AreEqual( "CNor", OriginalPointers.File( 0 ) );
		Assert.AreEqual( "Cpic", OriginalPointers.File( 2 ) );
		Assert.AreEqual( "Cnog", OriginalPointers.File( 8 ) );
		Assert.AreEqual( "Ccar", OriginalPointers.File( 9 ) );
		Assert.AreEqual( "Ccam", OriginalPointers.File( 19 ) );
		Assert.AreEqual( "CCro", OriginalPointers.File( 21 ) );
		// every id has its own OpenTPW pointer type
		Assert.AreEqual( OriginalPointers.Count, Enumerable.Range( 0, OriginalPointers.Count ).Select( OriginalPointers.TypeOf ).Distinct().Count() );
		Assert.AreEqual( 21, OriginalPointers.IdOf( Input.CursorTypes.Crosshair ) );
	}

	[TestMethod]
	public void TheCashPointerFollowsTheToolsCostAgainstTheBalance()
	{
		// [BIN:TPW-EXE:0x523662 blueprint cash flag] cost > balance, equal allowed
		Assert.IsTrue( OpenTPW.Hud.ParkHud.CashPointerShown( 4, 2500, 2499, null ) );
		Assert.IsFalse( OpenTPW.Hud.ParkHud.CashPointerShown( 4, 2500, 2500, null ), "equal is allowed" );
		Assert.IsFalse( OpenTPW.Hud.ParkHud.CashPointerShown( 4, null, 0, null ), "no tool cost" );
		var stopped = new SegmentResult( (0, 0), System.Array.Empty<(int X, int Y)>(), 0, CellBuildResult.NotEnoughMoney, false );
		var fine = stopped with { };
		Assert.IsTrue( OpenTPW.Hud.ParkHud.CashPointerShown( 1, null, 0, stopped ), "a path line stopped for money" );
		Assert.IsTrue( OpenTPW.Hud.ParkHud.CashPointerShown( 3, null, 0, stopped ), "a queue line stopped for money" );
		Assert.IsFalse( OpenTPW.Hud.ParkHud.CashPointerShown( 1, null, 0, new SegmentResult( (0, 0), System.Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) ) );
		Assert.IsFalse( OpenTPW.Hud.ParkHud.CashPointerShown( 0, 9999, 0, stopped ), "other tools" );
		Assert.AreEqual( OriginalPointers.Cash, OriginalPointers.ForTool( 4, cash: true ), "cash overrides the placing pointer" );
		Assert.IsNotNull( fine );
	}

	[TestMethod]
	public void TheToolSwitchPicksThePointerWithItsFlagOverrides()
	{
		Assert.AreEqual( 3, OriginalPointers.ForTool( 1 ), "path tool" );
		Assert.AreEqual( 4, OriginalPointers.ForTool( 3 ), "queue tool" );
		Assert.AreEqual( 7, OriginalPointers.ForTool( 4 ), "placing" );
		Assert.AreEqual( 7, OriginalPointers.ForTool( 59 ), "mode 0x3b places as well" );
		Assert.AreEqual( 6, OriginalPointers.ForTool( 11 ), "track tools" );
		Assert.AreEqual( 6, OriginalPointers.ForTool( 16 ) );
		Assert.AreEqual( 11, OriginalPointers.ForTool( 21 ), "pylon" );
		Assert.AreEqual( 17, OriginalPointers.ForTool( 22 ), "move" );
		Assert.AreEqual( 5, OriginalPointers.ForTool( 1, link: true ), "link overrides" );
		Assert.AreEqual( 18, OriginalPointers.ForTool( 1, end: true ), "end overrides" );
		Assert.AreEqual( 5, OriginalPointers.ForTool( 3, link: true, end: true ), "link is tested before end" );
		Assert.AreEqual( 18, OriginalPointers.ForTool( 21, link: true, end: true ), "the pylon tool has no link pointer" );
		Assert.AreEqual( 3, OriginalPointers.ForTool( 57, link: true, end: true ), "mode 0x39 takes its pointer without the flags" );
		Assert.IsNull( OriginalPointers.ForTool( 0 ), "no tool leaves the pointer alone" );
		Assert.IsNull( OriginalPointers.ForTool( 2 ) );
		Assert.IsNull( OriginalPointers.ForTool( 200 ) );
		Assert.AreEqual( 8, OriginalPointers.ForTool( 4, noPlace: true ), "noplace overrides the tool's pointer" );
		Assert.AreEqual( 8, OriginalPointers.ForTool( 0, noPlace: true ), "also without a tool" );
		Assert.AreEqual( 20, OriginalPointers.ForTool( 4, noPlace: true, cash: true ), "cash overrides both" );
	}

	[TestMethod]
	public void TheHelpBoxIsAFixedRectangleAtTheBottom()
	{
		// a 16-pixel line at 1536 lines: h = 16 * 3072 / 1536 + 10 = 42 units; the box ends at y 1520 and is 900 wide whatever the text
		var rect = UiScreenStack.HelpBoxRect( new UiCanvas( 2048, 1536 ), 16 );
		Assert.AreEqual( 574f, rect.X, 0.5f );
		Assert.AreEqual( 900f, rect.Width, 0.5f );
		Assert.AreEqual( 1520f, rect.Bottom, 0.5f );
		Assert.AreEqual( 42f, rect.Height, 0.5f );
		var scaled = UiScreenStack.HelpBoxRect( new UiCanvas( 1280, 720 ), 16 );
		Assert.AreEqual( 900f * 720f / 1536f, scaled.Width, 0.5f, "the width is a fixed share of the canvas" );
		Assert.AreEqual( 640f + (574f - 1024f) * 720f / 1536f, scaled.X, 0.5f );
		Assert.AreEqual( 720f - 16f * 720f / 1536f, scaled.Bottom, 0.5f );
		// the video's 37-pixel box at 720p: a font of about 16 pixels
		Assert.AreEqual( 79f * 720f / 1536f, UiScreenStack.HelpBoxRect( new UiCanvas( 1280, 720 ), 16.2f ).Height, 1f );
	}
}
