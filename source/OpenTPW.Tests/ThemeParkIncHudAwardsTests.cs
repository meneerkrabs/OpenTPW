using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The experimental Theme Park Inc HUD's shares and awards line and its award notes ([EXT:tpi-text-hud]), in the game's own UITEXT words.</summary>
[TestClass]
public class ThemeParkIncHudAwardsTests
{
	private static UiStringTable Strings()
	{
		var uiText = new string[461];
		uiText[23] = "Shareholding % :";
		uiText[24] = "Shares Owned :";
		uiText[25] = "Share Price :";
		uiText[452] = "Park Awards - ";
		for ( var award = 0; award < 8; award++ )
			uiText[453 + award] = $"Award{award}";
		return new UiStringTable( "English", uiText, new[] { "" }, new string[1], new string[1] );
	}

	private static ThemeParkIncHud BuildHud( ParkEconomy economy )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		Set( level, "CellTool", new CellBuildTool() );
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		Set( runtime, "Economy", economy );
		Set( level, "Park", runtime );
		return new ThemeParkIncHud( level, Strings(), new ThemeParkIncHudHost(), GameSpeed.Normal );
	}

	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void TheChallengePanelShowsTheHoldingAndTheAwardsAndAnAwardBecomesANote()
	{
		var economy = ThemeParkIncHudChallengeTests.Economy();
		var hud = BuildHud( economy );
		var career = economy.TpiCareer!;
		career.Shares.Initialize( new ThemeParkIncShareSettings( 30000, 1000, 100 ) );
		Assert.AreEqual( "Shares Owned : 1000   Shareholding % : 3   Share Price : 100   Park Awards - ", hud.SharesAndAwardsText() );
		economy.TpiAwardNotices.Add( new ThemeParkIncAwardNotice( ThemeParkIncAward.Security, false, 30 ) );
		hud.ShowAwardNotices();
		Assert.AreEqual( "Park Awards - Award4", hud.Messages[0] );
		Assert.AreEqual( 0, economy.TpiAwardNotices.Count, "the notes are taken" );
	}
}
