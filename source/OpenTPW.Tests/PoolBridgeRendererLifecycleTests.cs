using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PoolBridgeRendererLifecycleTests
{
	[TestMethod]
	public void EmptyBridgeDrawSlotsPrecedeSpritesEvenWhenTheWorldAndBridgeArriveLater()
	{
		var previous = Entity.All;
		Entity.All = new();
		try
		{
			PoolBridgeWorld? world = null;
			var renderer = new PoolBridgeRenderer( () => world, SVector2.Zero, PoolStyles.ForTheme( "jungle" ) );
			var slots = Entity.All.OfType<PoolPartEntity>().ToArray();
			Assert.AreEqual( 3, slots.Length );
			// Empty atlases keep these real sprite renderers headless; their draw slots still enter Entity.All.
			var walkers = new GuestRenderer( null!, null!, Array.Empty<GuestSpriteAtlas>() );
			var swimmers = new GuestRenderer( null!, null!, Array.Empty<GuestSpriteAtlas>() );
			renderer.Update();
			var cells = new ParkCellMap( 20, 20 );
			world = new PoolBridgeWorld( cells );
			renderer.Update();
			Assert.IsTrue( slots.All( part => part.Model == null && part.PartMaterial == null ), "Absent and empty worlds allocate no mesh, material or texture." );
			cells.SetType( 1, 3, ParkCellType.Path );
			cells.SetType( 9, 3, ParkCellType.Path );
			for ( var x = 4; x <= 6; x++ ) cells.SetType( x, 3, ParkCellType.PoolWater );
			Assert.AreEqual( PoolBridgeResult.Ok, world.TryAdd( 2, 3, 1 ) );
			// The GPU update will fill these existing entities, never append draw slots behind the sprites.
			CollectionAssert.AreEqual( slots, Entity.All.OfType<PoolPartEntity>().ToArray() );
			Assert.IsTrue( slots.All( part => Entity.All.IndexOf( part ) < Entity.All.IndexOf( walkers ) && Entity.All.IndexOf( part ) < Entity.All.IndexOf( swimmers ) ) );
			Assert.AreEqual( PoolBridgeResult.Ok, world.RemoveAt( 2, 3 ) );
			renderer.Update();
			world = null;
			renderer.Update();
			renderer.Delete();
			Assert.IsFalse( Entity.All.OfType<PoolPartEntity>().Any() );
			Assert.IsTrue( Entity.All.Contains( walkers ) && Entity.All.Contains( swimmers ) );
		}
		finally
		{
			// Empty-atlas sprites own no resources or subscriptions; their normal deletion requires a graphics host.
			foreach ( var entity in Entity.All.ToArray() )
				if ( entity is GuestRenderer ) Entity.All.Remove( entity ); else entity.Delete();
			Entity.All = previous;
		}
	}
}
