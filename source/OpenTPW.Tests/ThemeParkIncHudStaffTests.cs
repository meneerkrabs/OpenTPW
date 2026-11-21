using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The experimental Theme Park Inc HUD's read-only Staff panel.</summary>
[TestClass]
public class ThemeParkIncHudStaffTests
{
	private static readonly string[] Types = { "Cleaner", "Engineer", "Entertainer", "Guard", "Scientist", "Gardener" };

	private static readonly string[] TypeTable = Types.SelectMany( type => new[] { type, type + "s" } ).ToArray();

	private static ThemeParkIncHireCandidate Candidate( int id, ThemeParkIncStaffRole role, int nameIndex, int wage, int experience )
		=> new( id, role, nameIndex, wage, 0, 0, experience, 0, 100 );

	private static UiStringTable Strings() => new( "English", new string[1], new[] { "" }, new string[1], new string[1],
		staffTypes: TypeTable, staffNames: Enumerable.Range( 0, 6 ).Select( role => Enumerable.Range( 0, 40 ).Select( index => $"{Types[role]}Name{index}" ).ToArray() ).ToArray() );

	private static ThemeParkIncHud BuildHud( UiStringTable strings, ThemeParkIncStaff staff )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		Set( level, "CellTool", new CellBuildTool() );
		var economy = (ParkEconomy)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomy ) );
		Set( economy, "TpiStaff", staff );
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		Set( runtime, "Economy", economy );
		Set( level, "Park", runtime );
		return new ThemeParkIncHud( level, strings, new ThemeParkIncHudHost() );
	}

	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static ThemeParkIncStaff SyntheticStaff( params (int Slot, ThemeParkIncHireCandidate Candidate)[] placed )
	{
		var staff = (ThemeParkIncStaff)RuntimeHelpers.GetUninitializedObject( typeof( ThemeParkIncStaff ) );
		var slots = new ThemeParkIncHireCandidate?[ThemeParkIncStaff.PoolSlots];
		foreach ( var (slot, candidate) in placed )
			slots[slot] = candidate;
		typeof( ThemeParkIncStaff ).GetField( "slots", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( staff, slots );
		typeof( ThemeParkIncStaff ).GetField( "members", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( staff, new List<ThemeParkIncStaffMember>() );
		return staff;
	}

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void PanelListsExactlyThePoolsCandidatesInSlotOrderWithRoleAndNameTexts()
	{
		var staff = SyntheticStaff( (5, Candidate( 1, ThemeParkIncStaffRole.Guard, 7, 1200, 30 )), (0, Candidate( 2, ThemeParkIncStaffRole.Gardener, 3, 900, 12 )),
			(31, Candidate( 3, ThemeParkIncStaffRole.Researcher, 0, 2500, 80 )) );
		var hud = BuildHud( Strings(), staff );
		var before = staff.Slots.ToArray();
		var rows = hud.StaffRows();
		Assert.AreEqual( 3, rows.Count );
		StringAssert.Contains( rows[0], "Gardener: GardenerName3" );
		StringAssert.Contains( rows[1], "Guard: GuardName7" );
		StringAssert.Contains( rows[2], "Scientist: ScientistName0" );
		StringAssert.Contains( rows[1], "30" );
		StringAssert.Contains( rows[1], "1,200" );
		CollectionAssert.AreEqual( before, staff.Slots.ToArray(), "the panel does not change the pool" );
	}

	[TestMethod]
	public void StaffButtonTogglesThePanelWithoutPausingTheSimulation()
	{
		var hud = BuildHud( Strings(), SyntheticStaff( (0, Candidate( 1, ThemeParkIncStaffRole.Handyman, 1, 800, 5 )) ) );
		var panel = new[] { "staffPanelTitle", "staffPanelNote", "staffPanelList", "staffPanelClose" }.Select( id => hud.Screen.Find( id )! ).ToArray();
		Assert.IsTrue( panel.All( element => !element.Visible ) );
		var scale = hud.TimeScale;
		var button = (UiButton)hud.Screen.Find( "staff" )!;
		button.Clicked!();
		Assert.IsTrue( panel.All( element => element.Visible ) );
		Assert.AreEqual( scale, hud.TimeScale );
		Assert.AreEqual( GameSpeed.Normal, hud.Speed );
		Assert.AreEqual( "Hiring comes in a later version.", ((UiLabel)panel[1]).Text() );
		button.Clicked!();
		Assert.IsTrue( panel.All( element => !element.Visible ) );
	}

	[TestMethod]
	public void EveryStaffTextHasElevenNonEmptyTranslations()
	{
		var keys = Enum.GetValues<OpenTpwText>().Where( key => key.ToString().StartsWith( "TpiStaff", StringComparison.Ordinal ) ).ToArray();
		Assert.AreEqual( 5, keys.Length );
		Assert.AreEqual( 11, SupplementaryStrings.SupportedLanguages.Count );
		foreach ( var key in keys )
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ), $"{key} {language}" );
	}

	[TestMethod]
	public void RetailTablesGiveRoleAndNameTexts()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( data );
			var language = GameLanguage.Resolve( data, "English", null );
			var strings = UiStringTable.Load( language );
			for ( var role = 0; role < 6; role++ )
			{
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffType( 2 * role ) ), $"role {role}" );
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffName( role, 0 ) ), $"name 0 of role {role}" );
				Assert.IsFalse( string.IsNullOrWhiteSpace( strings.StaffName( role, 34 ) ), $"name 34 of role {role}" );
				Assert.AreEqual( "", strings.StaffName( role, 35 ), $"role {role} table has 35 entries" );
			}
			CollectionAssert.AreEqual( new[] { "Cleaner", "Engineer", "Entertainer", "Guard", "Scientist", "Gardener" }, Enumerable.Range( 0, 6 ).Select( role => strings.StaffType( 2 * role ) ).ToArray() );
		}
		finally { FileSystem = previous; }
	}
}
