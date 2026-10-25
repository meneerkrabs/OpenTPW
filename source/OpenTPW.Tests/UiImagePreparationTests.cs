using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class UiImagePreparationTests
{
	[TestMethod]
	public void BudgetStopsBeforeStartingAnotherImageAndResumesNextPump()
	{
		var queue = new UiImagePreparation( new[] { "a", "b", "c", "d" } );
		var starts = new List<double>();
		var loaded = new List<string>();
		double elapsed = 0;
		void Load( string path ) { starts.Add( elapsed ); loaded.Add( path ); elapsed += 4; }
		queue.Pump( Load, () => elapsed, 6 );
		CollectionAssert.AreEqual( new[] { "a", "b" }, loaded );
		CollectionAssert.AreEqual( new[] { 0d, 4d }, starts );
		Assert.AreEqual( 2, queue.Remaining );
		elapsed = 0;
		queue.Pump( Load, () => elapsed, 6 );
		CollectionAssert.AreEqual( new[] { "a", "b", "c", "d" }, loaded );
		Assert.AreEqual( 0, queue.Remaining );
	}

	[TestMethod]
	public void DuplicateKeysAndAnAlreadySpentBudgetDoNoExtraWork()
	{
		var queue = new UiImagePreparation( new[] { "a", "a", "b", "a" } );
		var loaded = new List<string>();
		queue.Pump( loaded.Add, () => 6, 6 );
		Assert.AreEqual( 0, loaded.Count );
		Assert.AreEqual( 2, queue.Remaining );
		queue.Pump( loaded.Add, () => 0, 6 );
		CollectionAssert.AreEqual( new[] { "a", "b" }, loaded );
		queue.Pump( _ => Assert.Fail( "Drained queue must not load again." ), () => 0, 6 );
	}

	[TestMethod]
	public void FailedWorkDoesNotSilentlyConsumeTheRequest()
	{
		var queue = new UiImagePreparation( new[] { "a" } );
		Assert.ThrowsException<InvalidOperationException>( () => queue.Pump( _ => throw new InvalidOperationException(), () => 0, 6 ) );
		Assert.AreEqual( 1, queue.Remaining );
	}

	[TestMethod]
	public void MissingModelsProduceNoPreparationRequests()
	{
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
		Assert.AreEqual( 0, ParkHud.BuyWindowImagePaths( context ).Count() );
	}

	[TestMethod]
	public void PreparationUsesTheSameTextureKeyAsDrawing()
	{
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name =>
		{
			var bytes = Md2ModelFileTests.CreateGeometry();
			// Give each synthetic asset a distinct original root name, not just an alias.
			var rootName = System.Text.Encoding.ASCII.GetBytes( name + "\0" );
			Assert.IsTrue( rootName.Length <= 12 );
			Array.Clear( bytes, 0x1A4, 12 );
			rootName.CopyTo( bytes, 0x1A4 );
			var model = new ModelFile( new MemoryStream( bytes ) );
			model.Meshes.Single().Materials[0].Flags = UiImages.TransparentFlag;
			return new UiModel( model, name );
		} ) ) { ResolveTexture = _ => "/ui/textures/shared.wct" };
		var paths = ParkHud.BuyWindowImagePaths( context ).ToArray();
		Assert.AreEqual( 1, paths.Length, "Repeated frames and parts must share one warm request." );
		Assert.IsTrue( context.DrawModel( "w_big", 0, new UiRect( 0, 0, 100, 100 ) ) );
		Assert.AreEqual( "/ui/textures/shared.wct", paths[0], "the slot flag 0x2 does not change the texture" );
		Assert.AreEqual( paths[0], context.Batch.Draws.Single().Texture.ImagePath );
		context.ResolveTexture = _ => null;
		Assert.AreEqual( 0, ParkHud.BuyWindowImagePaths( context ).Count() );
	}
}
