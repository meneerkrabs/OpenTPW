using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class MapCellAggregationTests
{
	public TestContext TestContext { get; set; } = null!;
	private static long Allocation(Action action)
	{
		for (var i = 0; i < 10; i++) action();
		var start = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) action();
		return (GC.GetAllocatedBytesForCurrentThread() - start) / 100;
	}

	[TestMethod]
	public void SatisfactionGroupingMustNotAllocateAVisitorLookupEveryFrame()
	{
		var rig = new MapOverlayAllocationTests.Rig(1500);
		var with = Allocation(rig.Draw);
		rig.Hud.ToggleMapSatisfaction();
		var without = Allocation(rig.Draw);
		var grouping = Allocation(() => { foreach (var cell in ParkMapMetrics.Outcomes(rig.Guests.Guests, 32, 32)) _ = cell.Pleased; });
		TestContext.WriteLine($"Full map with outcomes: {with}; without: {without}; delta: {with-without}; old outcome grouping alone: {grouping} bytes/frame.");
		var scratch = new ParkMapMetrics.OutcomeScratch();
		var rebuilt = Allocation(() => _ = scratch.Rebuild(rig.Guests.Guests, 32, 32, enabled: true));
		TestContext.WriteLine($"Scratch rebuild alone: {rebuilt} bytes/frame.");
		Assert.IsTrue(rebuilt < 2048, $"Fresh outcome counts must reuse scratch storage: {rebuilt} bytes/frame.");
		// Tiered optimization can change unrelated draw allocations between the paired samples.
		// Keep the whole-draw comparison under the explicit reproducible measurement configuration.
		if (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0")
			Assert.IsTrue(with - without < 4096, $"Satisfaction grouping costs {with-without} bytes/frame.");
	}

	[TestMethod]
	public void DrawKeepsOutcomeColourGeometryOrderAndImmediateLiveChanges()
	{
		var rig = new MapOverlayAllocationTests.Rig(6);
		var guests = rig.Guests.Guests;
		guests[0].Navigation.X = 3 * Fixed16.One; guests[0].Navigation.Y = 3 * Fixed16.One;
		guests[1].Navigation.X = Fixed16.One; guests[1].Navigation.Y = Fixed16.One;
		guests[3].Navigation.X = 3 * Fixed16.One; guests[3].Navigation.Y = 3 * Fixed16.One;
		Check();
		guests[3].Thought = GuestThought.Dissatisfied; Check();
		guests[0].State = GuestState.Using; Check();
		guests[1].Navigation.X = -Fixed16.One; guests[3].Navigation.Y = 32 * Fixed16.One; Check();
		guests[4].Navigation.X = 2 * Fixed16.One; guests[4].Navigation.Y = 2 * Fixed16.One; Check();
		foreach(var guest in guests) guest.Thought = GuestThought.Happy;
		Check();
		guests[2].Thought = GuestThought.Pleased; Check();
		rig.Hud.ToggleMapSatisfaction(); rig.Draw();
		Assert.AreEqual(0, Actual().Length);
		rig.Hud.ToggleMapSatisfaction(); Check();
		void Check()
		{
			var expected = new UiBatch();
			foreach (var cell in ParkMapMetrics.Outcomes(guests,32,32))
				expected.AddRectangle(new UiRect(40 + cell.X * 5.5f, 40 + cell.Y * 5.5f, 5.5f, 5.5f), Colour(cell.Pleased,cell.Dissatisfied));
			rig.Draw();
			CollectionAssert.AreEqual(expected.Draws.SelectMany(draw => draw.Vertices).ToArray(), Actual(), "Current observation filters, first-encounter order and drawn rectangles must survive rebuilding.");
		}
		UiVertex[] Actual() => rig.Context.Batch.Draws.SelectMany(draw => draw.Vertices).Where(vertex => IsOutcome(vertex.Color)).ToArray();
	}
	private static RgbaByte Colour(int pleased, int dissatisfied) => pleased > 0 && dissatisfied > 0 ? new RgbaByte(255,165,35,240) : pleased > 0 ? new RgbaByte(35,235,35,240) : new RgbaByte(235,35,35,240);
	private static bool IsOutcome(RgbaByte colour) => colour.Equals(Colour(1,1)) || colour.Equals(Colour(1,0)) || colour.Equals(Colour(0,1));
}
