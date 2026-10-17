using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A swim set has art for some slots only: a frame request for any slot of the 16-slot table must still answer (docs/POOLS.md, "Swimwear slots").</summary>
[TestClass]
[DoNotParallelize]
public class PoolSwimSlotFallbackTests
{
	[TestMethod]
	public void EverySlotOfTheTableAnswersWithAFrameOfTheSetEvenWhereTheSetHasNoArt()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "content", "pools", "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
			{
				PoolContent.DirectoryOverride = Path.Combine( directory.FullName, "content", "pools" );
				break;
			}
		try
		{
			var sets = PoolSwimSprites.Load();
			Assert.IsTrue( sets.Count > 0 );
			foreach ( var set in sets )
			{
				var withArt = Enumerable.Range( 0, set.Animations.Animations.Count ).Count( slot => !set.Animations.Animations[slot].IsEmpty );
				Assert.IsTrue( withArt >= 3, $"{set.Name} has the strokes" );
				for ( var slot = 0; slot < set.Animations.Animations.Count; slot++ )
					for ( var direction = 0; direction <= 4; direction++ )
						for ( var step = 0; step < 12; step++ )
							Assert.IsTrue( set.GetFrame( slot, direction, step ) >= 0 && set.GetFrame( slot, direction, step ) < set.Frames.Length, $"{set.Name} slot {slot}" );
			}
		}
		finally
		{
			PoolContent.DirectoryOverride = null;
		}
	}
}

/// <summary>The renderer asks <see cref="GuestSpriteAtlas.UsableSlot"/> for the slot an activity wishes: a slot without art draws as treading water.</summary>
[TestClass]
[DoNotParallelize]
public class PoolSwimUsableSlotTests
{
	[TestMethod]
	public void ASlotWithoutArtIsDrawnAsTreadingWaterAndASlotWithArtStays()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "content", "pools", "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
			{
				PoolContent.DirectoryOverride = Path.Combine( directory.FullName, "content", "pools" );
				break;
			}
		try
		{
			foreach ( var set in PoolSwimSprites.Load() )
			{
				Assert.IsTrue( set.HasArt( 10 ), $"{set.Name} has the treading-water art" );
				for ( var slot = 0; slot < set.Animations.Animations.Count; slot++ )
				{
					var usable = set.UsableSlot( slot );
					Assert.IsTrue( set.HasArt( usable ), $"{set.Name} slot {slot} is drawn with art" );
					Assert.AreEqual( set.HasArt( slot ) ? slot : 10, usable, $"{set.Name} slot {slot}" );
				}
			}
		}
		finally
		{
			PoolContent.DirectoryOverride = null;
		}
	}
}
