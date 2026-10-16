using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The buy window's list holds only the types the original marks available (tp.exe 0x4aaff0; UI-024).</summary>
[TestClass]
public class BuyListFilterTests
{
	private sealed class Catalog : IBuildCatalog
	{
		private readonly int[] ids;
		public Catalog( params int[] ids ) => this.ids = ids.Length == 0 ? Enumerable.Range( 1, 5 ).ToArray() : ids;

		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => category == BuildCategory.Rides
			? ids.Select( id => new BuildItem( $"r{id}", id, BuildCategory.Rides, id, id * 100, null, Array.Empty<string>() ) ).ToArray() : Array.Empty<BuildItem>();
	}

	[TestMethod]
	public void OnlyAvailableTypesGetARowAndTheSelectionFollows()
	{
		var available = new HashSet<int> { 2, 4 };
		var model = new BuyListModel( new Catalog(), item => $"ride {item.InfoId}", item => item.Cost, listed: item => available.Contains( item.InfoId ) );
		CollectionAssert.AreEqual( new[] { 2, 4 }, model.Items.Select( item => item.InfoId ).ToArray(), "unresearched rides have no row" );
		model.Select( 1 );
		Assert.AreEqual( 4, model.SelectedItem!.InfoId );
		available.Remove( 4 );
		available.Add( 1 );
		model.EnsureSelection();
		CollectionAssert.AreEqual( new[] { 1, 2 }, model.Items.Select( item => item.InfoId ).ToArray(), "a newly researched ride shows when the window is opened again" );
		Assert.AreEqual( 1, model.SelectedItem!.InfoId, "the selection that lost its row falls back to the first one" );
	}

	[TestMethod]
	public void WithoutAFilterEveryItemIsListed()
	{
		var model = new BuyListModel( new Catalog(), item => $"ride {item.InfoId}", item => item.Cost );
		Assert.AreEqual( 5, model.Items.Count );
	}

	[TestMethod]
	public void ResearchingATypeListsItWhenTheWindowIsOpenedAgainAndALevelWithoutAnEconomyListsEverything()
	{
		var park = EconomyTestData.Park();
		var project = park.Research.Items.First( item => item.Level == 0 && item.Cost > 0 && park.Catalog.TryGet( item.InfoId, out var info ) && info.Kind == ParkObjectKind.Ride );
		var status = new EconomyParkStatus( () => park );
		var catalog = new Catalog( project.InfoId );
		var model = new BuyListModel( catalog, item => $"ride {item.InfoId}", item => item.Cost, listed: status.IsAvailable );
		Assert.AreEqual( 0, model.Items.Count, "an unresearched ride has no row" );
		park.Research.Restore( park.Research.Completed.Append( (project.InfoId, 0) ).ToArray(), Array.Empty<((int, int), long)>(), park.Research.Effort.ToArray(), null );
		model.EnsureSelection();
		CollectionAssert.AreEqual( new[] { project.InfoId }, model.Items.Select( item => item.InfoId ).ToArray(), "the researched ride is listed as soon as the window opens" );
		var sandbox = new BuyListModel( catalog, item => $"ride {item.InfoId}", item => item.Cost, listed: new NoEconomyStatus().IsAvailable );
		Assert.AreEqual( 1, sandbox.Items.Count, "a level without an economy lists the whole catalogue" );
	}
}
