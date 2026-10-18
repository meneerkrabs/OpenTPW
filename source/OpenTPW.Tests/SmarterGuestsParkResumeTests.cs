using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The smarter-guests section in a whole park save (docs/SMARTER-GUESTS.md, "Persistence"): the level captures it only while the extension is on, loading checks the recorded mod set, and the
/// resume path puts the saved state back. Built on the level fixture of the upgrade-catalogue tests.
/// </summary>
[TestClass, DoNotParallelize]
public class SmarterGuestsParkResumeTests
{
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly Type Fixture = typeof( UpgradeCatalogueDefaultsTests );

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }

	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private static void Set( object target, string name, object value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );

	private (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) World() =>
		((Level, ParkEconomy, OriginalObject, OriginalObject, ParkObjectState))Fixture.GetMethod( "World", Private )!.Invoke( fixture, new object[] { "", 0, false } )!;

	private static Dictionary<string, string> Prepare( (Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State) world ) =>
		(Dictionary<string, string>)Fixture.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;

	[TestMethod]
	public void AnOffParkSavesNoSectionAndNoRecord()
	{
		var world = World();
		var assets = Prepare( world );
		var snapshot = world.Level.CaptureResumeSnapshot( null, "off", assets );
		Assert.IsNull( snapshot.SmarterGuests );
		Assert.IsNull( snapshot.Mods );
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( snapshot ) ) )!.AsObject();
		Assert.IsFalse( json.ContainsKey( "SmarterGuests" ) );
		Assert.IsFalse( json.ContainsKey( "Mods" ) );
	}

	[TestMethod]
	public void AnOnParkSavesTheSectionAndTheRecordAndResumesItsState()
	{
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var source = World();
		var assets = Prepare( source );
		Assert.IsNotNull( source.Level.Guests!.SmarterGuests, "the level's guests were built with the extension on" );
		source.Level.Guests.SmarterGuests!.Restore( 77, 5, true );
		var saved = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( source.Level.CaptureResumeSnapshot( null, "on", assets ) ) );
		Assert.AreEqual( new ParkModRecord( "opentpw.smarter-guests", "1.6.0", ModSet.NoFilesSha256 ), saved.Mods!.Single() );
		Assert.AreEqual( 77UL, saved.SmarterGuests!.ParkSeed );
		Assert.AreEqual( 5UL, saved.SmarterGuests.NextBirthId );
		Assert.IsTrue( saved.SmarterGuests.Bootstrapped );

		// the resume path: a candidate built with the same extension takes the section back
		var restoredEconomy = ParkSaveFile.Restore( saved.Economy, EconomyTestData.Settings(), EconomyTestData.Catalog() );
		var candidateLevel = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		Set( candidateLevel, "<Objects>k__BackingField", objects );
		Set( candidateLevel, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( restoredEconomy, saved.StartKind ) );
		Set( candidateLevel, "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( candidateLevel, "resumeState", saved );
		var one = new OriginalObject( source.One.Entry, source.One.Placement, objects.ScriptWorld, seed: 1 ^ objects.ObjectScriptKey, open: true, attractionId: source.One.Runtime.AttractionId );
		var two = new OriginalObject( source.Two.Entry, source.Two.Placement, objects.ScriptWorld, seed: 2 ^ objects.ObjectScriptKey, open: true, attractionId: source.Two.Runtime.AttractionId );
		((List<OriginalObject>)objects.Objects).AddRange( new[] { one, two } );
		Set( objects, "nextSeed", 3 );
		var candidate = (Level: candidateLevel, Economy: restoredEconomy, One: one, Two: two, State: restoredEconomy.Objects.Single( item => item.Id == source.State.Id ));
		Prepare( candidate );
		var state = candidate.Level.Guests!.SmarterGuests!;
		Assert.AreEqual( 1UL, state.NextBirthId, "a fresh candidate starts with a fresh state" );
		typeof( Level ).GetMethod( "ConnectObjectsToEconomy", Private )!.Invoke( candidateLevel, null );
		typeof( Level ).GetMethod( "SetupParkGate", Private )!.Invoke( candidateLevel, null );
		typeof( Level ).GetMethod( "RestoreResumeSimulation", Private )!.Invoke( candidate.Level, new object[] { saved } );
		Assert.AreEqual( (77UL, 5UL, true), (state.ParkSeed, state.NextBirthId, state.Bootstrapped), "the resume put the saved state back" );
		Assert.AreEqual( JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( saved ) ) )!["SmarterGuests"]!.ToJsonString(),
			JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( candidate.Level.CaptureResumeSnapshot( null, "on", assets ) ) ) )!["SmarterGuests"]!.ToJsonString(), "capturing the resumed park gives the same section" );
	}

	[TestMethod]
	public void LoadingRefusesAnOnSaveWithTheExtensionOffAndAnOffSaveWithItOn()
	{
		ParkResumeSnapshot on;
		using ( OpenTpwExtensions.Use( SmarterGuestsTests.On() ) )
		{
			var world = World();
			on = world.Level.CaptureResumeSnapshot( null, "on", Prepare( world ) );
		}
		var offWorld = World();
		var offAssets = Prepare( offWorld );
		var off = offWorld.Level.CaptureResumeSnapshot( null, "off", offAssets );
		var catalog = offWorld.Level.Objects.Catalog;
		var settings = offWorld.Economy.Settings;
		void Validate( ParkResumeSnapshot snapshot ) => ParkResumeSaveFile.ValidateSnapshot( snapshot, catalog, settings, offWorld.Economy.Catalog, offAssets );
		Validate( off );
		var refused = Assert.ThrowsException<InvalidDataException>( () => Validate( on ) );
		StringAssert.Contains( refused.Message, "needs opentpw.smarter-guests 1.6.0" );
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		var extra = Assert.ThrowsException<InvalidDataException>( () => Validate( off ) );
		StringAssert.Contains( extra.Message, "was saved without opentpw.smarter-guests 1.6.0" );
	}
}
