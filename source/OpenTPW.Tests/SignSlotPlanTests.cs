using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park gate's sign slots must never show the shipped magenta "SIGN1 SIGN2" placeholder: the park name when the level's gate sign exists,
/// a plain board when it does not (it failed, or the gate was built before it existed in a park built over frames).
/// </summary>
[TestClass]
public class SignSlotPlanTests
{
	private static ObjectSignImages Images( string line ) => new( line, "", new byte[4], new byte[4], 0, [] );

	[TestMethod]
	public void TheGateShowsTheParkNameWhenItsSignExists()
	{
		var gate = Images( "LOST" );
		var plan = SignSlotPlan.For( true, "gates", gate, () => throw new AssertFailedException( "a fixed item has no type sign" ) );
		Assert.AreSame( gate, plan.Images );
		Assert.IsFalse( plan.UsesFallback );
	}

	[TestMethod]
	public void TheGateWithoutItsSignShowsThePlainBoardNotThePlaceholder()
	{
		// the sign is missing when its creation failed, or when the gate object was built before the level's sign (the time-sliced build's order)
		var plan = SignSlotPlan.For( true, "Gates", null, () => throw new AssertFailedException( "a fixed item has no type sign" ) );
		Assert.IsNull( plan.Images );
		Assert.IsTrue( plan.UsesFallback );
	}

	[TestMethod]
	public void AnyOtherFixedItemWithSignSlotsGetsThePlainBoardToo()
	{
		var plan = SignSlotPlan.For( true, "lights", Images( "LOST" ), () => null );
		Assert.IsNull( plan.Images );
		Assert.IsTrue( plan.UsesFallback );
	}

	[TestMethod]
	public void APlacedObjectUsesItsTypeSign()
	{
		var own = Images( "BELLY" );
		var plan = SignSlotPlan.For( false, "bouncy", Images( "LOST" ), () => own );
		Assert.AreSame( own, plan.Images );
		Assert.IsFalse( plan.UsesFallback );
		Assert.IsFalse( SignSlotPlan.For( false, "bouncy", null, () => null ).UsesFallback, "a placed object without a sign keeps its shipped art (mods may ship their own)" );
	}
}
