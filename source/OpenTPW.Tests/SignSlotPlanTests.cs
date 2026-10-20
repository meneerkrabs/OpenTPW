using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The sign slots must not show the shipped magenta "SIGN1 SIGN2" placeholder: the park gate shows the park name when the level's gate sign exists and a plain
/// board when it failed; the front-end lobby's sign models show their sign once composed and the board until then.
/// </summary>
[TestClass]
public class SignSlotPlanTests
{
	private const string GatePath = "/levels/jungle/features/gates";
	private const int GateId = 1601;

	private static ObjectSignImages Images( string line ) => new( line, "", new byte[4], new byte[4], 0, [] );

	private static ObjectSignImages NoTypeSign() => throw new AssertFailedException( "the gate has no type sign" );

	[TestMethod]
	public void TheGateShowsTheParkNameWhenItsSignExists()
	{
		var gate = Images( "LOST" );
		var plan = SignSlotPlan.For( true, GateId, GatePath, GateId, gate, NoTypeSign );
		Assert.AreSame( gate, plan.Images );
		Assert.IsFalse( plan.UsesFallback );
		Assert.AreEqual( SignSlotSource.RuntimeSign, plan.SourceOf( "sign1.wct" ) );
	}

	[TestMethod]
	public void TheGateIsFoundByTheLevelsGateIdNotByItsName()
	{
		var gate = Images( "LOST" );
		Assert.AreSame( gate, SignSlotPlan.For( true, 1602, "/levels/jungle/features/portal", 1602, gate, NoTypeSign ).Images );
		// another fixed item, even one whose archive is named "gates", is not the gate when the level names another
		var other = SignSlotPlan.For( true, 1650, GatePath, 1602, gate, () => null );
		Assert.IsNull( other.Images );
		Assert.IsTrue( other.UsesFallback );
		// a level that names no gate falls back to the fixed item named "gates"
		Assert.AreSame( gate, SignSlotPlan.For( true, GateId, GatePath, null, gate, NoTypeSign ).Images );
	}

	[TestMethod]
	public void TheGateWithoutItsSignShowsThePlainBoardNotThePlaceholder()
	{
		// the level's gate sign failed (fonts or board unreadable): the gate never asks for its type sign, which would draw the default lines instead of the park's name
		foreach ( var gateId in new int?[] { GateId, null } )
		{
			var plan = SignSlotPlan.For( true, GateId, GatePath, gateId, null, NoTypeSign );
			Assert.IsNull( plan.Images );
			Assert.IsTrue( plan.UsesFallback );
			Assert.AreEqual( SignSlotSource.FallbackBoard, plan.SourceOf( "SIGN2" ) );
			Assert.AreEqual( SignSlotSource.Shipped, plan.SourceOf( "wood.wct" ), "only the sign slots change" );
		}
	}

	[TestMethod]
	public void AnotherFixedItemDrawsItsOwnSignOrThePlainBoard()
	{
		var own = Images( "BUS" );
		Assert.AreSame( own, SignSlotPlan.For( true, 1660, "/levels/jungle/features/bus", GateId, Images( "LOST" ), () => own ).Images );
		Assert.IsTrue( SignSlotPlan.For( true, 1660, "/levels/jungle/features/bus", GateId, Images( "LOST" ), () => null ).UsesFallback );
	}

	[TestMethod]
	public void APlacedObjectUsesItsTypeSignAndOtherwiseKeepsItsArt()
	{
		var own = Images( "BELLY" );
		var plan = SignSlotPlan.For( false, 1004, "/levels/jungle/rides/bouncy", GateId, Images( "LOST" ), () => own );
		Assert.AreSame( own, plan.Images );
		var without = SignSlotPlan.For( false, 1004, "/levels/jungle/rides/bouncy", GateId, null, () => null );
		Assert.IsFalse( without.UsesFallback, "a placed object without a sign keeps its shipped art (a mod may ship its own)" );
		Assert.AreEqual( SignSlotSource.Shipped, without.SourceOf( "sign1" ) );
	}

	[TestMethod]
	public void ALobbySignModelNeverResolvesToThePlaceholder()
	{
		// a lobby model's sign is composed over the first frames (or cannot be): until then, and when it fails, its slots show the board
		foreach ( var images in new ObjectSignImages?[] { null, Images( "LOST" ) } )
		{
			var plan = SignSlotPlan.ForScene( images );
			foreach ( var slot in new[] { "sign1", "sign2", "SIGN1.wct", "sign2.tga" } )
				Assert.AreNotEqual( SignSlotSource.Shipped, plan.SourceOf( slot ), slot );
			Assert.AreEqual( images == null ? SignSlotSource.FallbackBoard : SignSlotSource.RuntimeSign, plan.SourceOf( "sign1" ) );
			Assert.AreEqual( SignSlotSource.Shipped, plan.SourceOf( "Jun_rock.wct" ), "only the sign slots change" );
		}
	}

	[TestMethod]
	public void EveryLobbyThemeHasItsGate()
	{
		// tp.exe 0x409B10: the four themes' gate rows; the levels' global.sam name the same gates
		Assert.AreEqual( 1601, LobbySigns.GateInfoId( "jungle" ) );
		Assert.AreEqual( 2601, LobbySigns.GateInfoId( "Hallow" ) );
		Assert.AreEqual( 4601, LobbySigns.GateInfoId( "fantasy" ) );
		Assert.AreEqual( 3601, LobbySigns.GateInfoId( "space" ) );
		Assert.IsNull( LobbySigns.GateInfoId( "beach" ) );
	}

	[TestMethod]
	public void ALobbyIslandShowsItsThemeGatesRowAsItIs()
	{
		// tp.exe 0x409B10 + 0x413020/0x4130B0: the jungle gate 1601's two OBJECT_NAMES texts (entries 85 and 86), passed as two lines and not split again
		var names = Enumerable.Repeat( "", 400 ).ToArray();
		names[85] = "LOST";
		names[86] = "Kingdom";
		var island = new OpenTPW.FrontEnd.LobbyIslandInfo( 0, "jungle", "data\\lobby\\terrain", "jun_isle", "jun_gate", "Jungle", 0, 35, (0, 0, 0), Array.Empty<string>(), false, 0 );
		Assert.AreEqual( new ParkNameLines( "LOST", "Kingdom" ), LobbySigns.Lines( island, names ) );
	}

	[TestMethod]
	public void ASignIsCachedUnlessItsReadMaySucceedLater()
	{
		var sign = Images( "LOST" );
		Assert.IsTrue( ObjectSigns.CanCache( null, sign, false ) );
		Assert.IsTrue( ObjectSigns.CanCache( null, null, true ), "an archive with files and no .sgn has no sign" );
		Assert.IsFalse( ObjectSigns.CanCache( null, null, false ), "an archive with no files yet (the browser copies a theme when a park starts)" );
		Assert.IsFalse( ObjectSigns.CanCache( new IOException(), null, false ) );
		Assert.IsFalse( ObjectSigns.CanCache( new FileNotFoundException(), null, true, ObjectSigns.ReadAttempts - 1 ) );
		Assert.IsTrue( ObjectSigns.CanCache( new FileNotFoundException(), null, true, ObjectSigns.ReadAttempts ), "a read that keeps failing is given up (the park HUD asks on every preview draw)" );
		Assert.IsFalse( ObjectSigns.CanCache( new UnauthorizedAccessException(), null, true ) );
		Assert.IsTrue( ObjectSigns.CanCache( new UnauthorizedAccessException(), null, true, ObjectSigns.ReadAttempts ) );
		Assert.IsTrue( ObjectSigns.CanCache( new InvalidDataException(), null, false ), "unreadable data stays unreadable: not recomposed for every object" );
		Assert.IsTrue( ObjectSigns.CanCache( new NotSupportedException(), null, false ) );
		Assert.IsTrue( ObjectSigns.CanCache( new KeyNotFoundException(), null, false ), "a font or glyph the sign names but the data lacks stays missing" );
		Assert.IsTrue( ObjectSigns.CanCache( new ArgumentException(), null, false ), "values the composer refuses stay refused" );
	}

	[TestMethod]
	public void OnlyExpectedFailuresLeaveAParkWithoutItsGateSign()
	{
		foreach ( Exception failure in new Exception[] { new IOException(), new InvalidDataException(), new KeyNotFoundException(), new ArgumentException(), new NotSupportedException(), new InvalidOperationException(), new UnauthorizedAccessException() } )
			Assert.IsTrue( Level.IsGateSignFailure( failure ), failure.GetType().Name );
		Assert.IsFalse( Level.IsGateSignFailure( new NullReferenceException() ), "a bug must not hide" );
		Assert.IsFalse( Level.IsGateSignFailure( new ObjectDisposedException( "sign" ) ), "a disposed object is a bug, although it is an InvalidOperationException" );
		Assert.IsFalse( Level.IsGateSignFailure( new ArgumentNullException( "sign" ) ), "a null argument is a bug, although it is an ArgumentException" );
		Assert.IsFalse( Level.IsGateSignFailure( new OutOfMemoryException() ) );
	}
}
