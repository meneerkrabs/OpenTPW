using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A serializer's reflection metadata made ahead in steps (<see cref="JsonMetadataWarmup"/>): one type a step, deepest first, the fields only when the options serialize them;
/// the economy digest's (<see cref="WorldStateHash.WarmupSteps"/>) changes no hash.
/// </summary>
[TestClass]
public class JsonMetadataWarmupTests
{
	public sealed class WarmRoot
	{
		public WarmChild? Child { get; set; }
		public WarmField? Field;
	}

	public sealed class WarmChild
	{
		public int Value { get; set; }
	}

	public sealed class WarmField
	{
		public long Value { get; set; }
	}

	[TestMethod]
	public void EveryTypeAValueReachesIsAStepOfItsOwnAndTheFieldsOnlyWhenTheOptionsSerializeThem()
	{
		// a property's type, its own property's type, then the root: int, WarmChild, WarmRoot
		Assert.AreEqual( 3, JsonMetadataWarmup.Steps( new JsonSerializerOptions(), typeof( WarmRoot ) ).Count() );
		// with fields: long and WarmField too
		Assert.AreEqual( 5, JsonMetadataWarmup.Steps( new JsonSerializerOptions { IncludeFields = true }, typeof( WarmRoot ) ).Count() );
		// options made read-only by the warm-up serialize as before
		var options = new JsonSerializerOptions { IncludeFields = true };
		foreach ( var _ in JsonMetadataWarmup.Steps( options, typeof( WarmRoot ) ) ) { }
		Assert.IsTrue( options.IsReadOnly );
		Assert.AreEqual( "{\"Child\":{\"Value\":1},\"Field\":{\"Value\":2}}",
			JsonSerializer.Serialize( new WarmRoot { Child = new WarmChild { Value = 1 }, Field = new WarmField { Value = 2 } }, options ) );
	}

	[TestMethod]
	public void TheDigestsMetadataIsMadeInStepsAndChangesNoHash()
	{
		var park = EconomyTestData.Park();
		var expected = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		var steps = WorldStateHash.WarmupSteps().Count();
		// every type the park save reaches is a step of its own, so the metadata is not made in one frame
		Assert.IsTrue( steps > 40, $"{steps} steps" );
		Assert.AreEqual( expected, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
		Assert.AreEqual( steps, WorldStateHash.WarmupSteps().Count(), "running it again makes nothing new and fails nowhere" );
	}
}
