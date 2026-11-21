using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's mission window, "Current Objective" (docs/reverse/TPI-MISSIONS.md): the generated layout tables of the window and its
/// Challenges tab.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncMissionWindowTests
{
	[TestMethod]
	public void TheMissionTableIsTheCurrentObjectiveWindowWithItsControls()
	{
		var table = ThemeParkIncLayoutTables.MissionWindow;
		Assert.AreEqual( 0x9BE808, table.Address );
		Assert.AreEqual( "!window", table.Root.Model );
		Assert.AreEqual( 34, table.All.Count() );
		Assert.AreEqual( "objectim", table.Control( 0xCEC55 ).Model );
		Assert.AreEqual( "mugshot", table.Control( 0xCEC5D ).Model );
		Assert.AreEqual( "tab_text", table.Control( 0xCEC58 ).Model );
		Assert.AreEqual( 71, table.Control( 0xCEC58 ).Help );
		Assert.AreEqual( "tab_text", table.Control( 0xCEC59 ).Model );
		Assert.AreEqual( "b_ltoggle10", table.Control( 0xCEC68 ).Model );
		Assert.AreEqual( 100, table.Control( 0xCEC68 ).Help );
		Assert.AreEqual( "b_updown", table.Control( 0xCEC69 ).Model );
		Assert.AreEqual( "b_help", table.Control( 0xCEC6B ).Model );
		// the close and the cancel button share the id -2
		Assert.AreEqual( 2, table.All.Count( node => node.Id == 0xFFFFFFFEu ) );
		Assert.AreEqual( 281, table.Model( "b_cancel" ).Help );
		Assert.AreEqual( 2, table.Model( "b_close" ).Help );
		Assert.IsNotNull( table.Control( 0xCEC5A ).Inner, "the letter frame has an inner rectangle" );
	}

	[TestMethod]
	public void TheChallengesTabIsItsOwnWindow()
	{
		var table = ThemeParkIncLayoutTables.ChallengesWindow;
		Assert.AreEqual( 0x9B54CC, table.Address );
		Assert.AreEqual( 30, table.All.Count() );
		Assert.AreEqual( "tab_text", table.Control( 0xC1200 ).Model );
		Assert.AreEqual( 70, table.Control( 0xC1200 ).Help );
		Assert.AreEqual( "!listnarrow", table.Control( 0xC120E ).Model );
	}
}
