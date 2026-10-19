using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenTPW.Tests;

/// <summary>
/// The park gate's sign slots must not show the shipped magenta "SIGN1 SIGN2" placeholder: the park name when the level's gate sign exists,
/// a plain board when it does not (it failed, or the gate was built before it existed in a park built over frames).
/// </summary>
[TestClass]
public class SignSlotPlanTests
{
	private const string GatePath = "/levels/jungle/features/gates";

	private static ObjectSignImages Images( string line ) => new( line, "", new byte[4], new byte[4], 0, [] );

	private static ObjectSignImages NoTypeSign() => throw new AssertFailedException( "the gate has no type sign" );

	[TestMethod]
	public void TheGateShowsTheParkNameWhenItsSignExists()
	{
		var gate = Images( "LOST" );
		var plan = SignSlotPlan.For( true, GatePath, GatePath, gate, NoTypeSign );
		Assert.AreSame( gate, plan.Images );
		Assert.IsFalse( plan.UsesFallback );
		Assert.AreEqual( SignSlotSource.RuntimeSign, plan.SourceOf( "sign1.wct" ) );
	}

	[TestMethod]
	public void TheGateIsFoundByItsArchiveNotByItsName()
	{
		var gate = Images( "LOST" );
		Assert.AreSame( gate, SignSlotPlan.For( true, "/levels/jungle/features/Portal/", "/levels/jungle/features/portal", gate, NoTypeSign ).Images );
		// another fixed item next to it is not the gate
		var other = SignSlotPlan.For( true, "/levels/jungle/features/lights", GatePath, gate, () => null );
		Assert.IsNull( other.Images );
		Assert.IsTrue( other.UsesFallback );
	}

	[TestMethod]
	public void TheGateWithoutItsSignShowsThePlainBoardNotThePlaceholder()
	{
		// the sign is missing when its creation failed, or when the gate object was built before the level's sign (the time-sliced build's order)
		foreach ( var gateArchive in new string?[] { null, GatePath } )
		{
			var plan = SignSlotPlan.For( true, GatePath, gateArchive, null, NoTypeSign );
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
		Assert.AreSame( own, SignSlotPlan.For( true, "/levels/jungle/features/bus", GatePath, Images( "LOST" ), () => own ).Images );
		Assert.IsTrue( SignSlotPlan.For( true, "/levels/jungle/features/bus", GatePath, Images( "LOST" ), () => null ).UsesFallback );
	}

	[TestMethod]
	public void APlacedObjectUsesItsTypeSignAndOtherwiseKeepsItsArt()
	{
		var own = Images( "BELLY" );
		var plan = SignSlotPlan.For( false, "/levels/jungle/rides/bouncy", GatePath, Images( "LOST" ), () => own );
		Assert.AreSame( own, plan.Images );
		var without = SignSlotPlan.For( false, "/levels/jungle/rides/bouncy", GatePath, null, () => null );
		Assert.IsFalse( without.UsesFallback, "a placed object without a sign keeps its shipped art (a mod may ship its own)" );
		Assert.AreEqual( SignSlotSource.Shipped, without.SourceOf( "sign1" ) );
	}

	[TestMethod]
	public void ASignIsCachedUnlessItsReadMaySucceedLater()
	{
		var sign = Images( "LOST" );
		Assert.IsTrue( ObjectSigns.CanCache( null, sign, false ) );
		Assert.IsTrue( ObjectSigns.CanCache( null, null, true ), "an archive with files and no .sgn has no sign" );
		Assert.IsFalse( ObjectSigns.CanCache( null, null, false ), "an archive with no files yet (the browser copies a theme when a park starts)" );
		Assert.IsFalse( ObjectSigns.CanCache( new IOException(), null, false ) );
		Assert.IsFalse( ObjectSigns.CanCache( new UnauthorizedAccessException(), null, true ) );
		Assert.IsTrue( ObjectSigns.CanCache( new InvalidDataException(), null, false ), "unreadable data stays unreadable: not recomposed for every object" );
		Assert.IsTrue( ObjectSigns.CanCache( new NotSupportedException(), null, false ) );
	}

	[TestMethod]
	public void OnlyExpectedFailuresLeaveAParkWithoutItsGateSign()
	{
		foreach ( Exception failure in new Exception[] { new IOException(), new InvalidDataException(), new KeyNotFoundException(), new ArgumentException(), new NotSupportedException(), new InvalidOperationException(), new UnauthorizedAccessException() } )
			Assert.IsTrue( Level.IsGateSignFailure( failure ), failure.GetType().Name );
		Assert.IsFalse( Level.IsGateSignFailure( new NullReferenceException() ), "a bug must not hide" );
		Assert.IsFalse( Level.IsGateSignFailure( new OutOfMemoryException() ) );
	}
}
