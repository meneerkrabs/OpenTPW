using System;
using System.IO;
using System.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>CPU-only decoder failures during a completed texture-pack notification; every image is synthetic.</summary>
[TestClass]
[DoNotParallelize]
public class PathCellArtCacheTests
{
	private static byte[] Pixels( byte red )
	{
		var pixels = new byte[16];
		for ( var at = 0; at < pixels.Length; at += 4 )
		{
			pixels[at] = red;
			pixels[at + 3] = 255;
		}
		return pixels;
	}

	private static void CompleteSwitch()
	{
		var change = new TexturePackSwitch( new(), TimeSpan.FromSeconds( 1 ), _ => throw new InvalidOperationException(), decodeOnRenderThread: true );
		change.Pump();
		Assert.IsTrue( change.Finished );
	}

	[TestMethod]
	public void AFailedDecoderRetainsItsArtWhileOtherTilesAndSubscribersAdvance()
	{
		foreach ( var error in new Exception[] { new InvalidOperationException( "malformed image" ), new InvalidDataException( "invalid data" ), new IOException( "unreadable data" ) } )
		{
			Exception? failure = null;
			var red = (byte)20;
			var decodes = new int[2];
			var notifications = 0;
			PathCellArtCache? cache = null;
			ProceduralCellArt? first = null, second = null;
			(byte[], int, int)? Decode( int texture )
			{
				decodes[texture]++;
				if ( texture == 0 && failure != null )
					throw failure;
				return (Pixels( red ), 2, 2);
			}
			void Sync()
			{
				first = cache!.Get( 0, 0x180 );
				second = cache.Get( 1, 0x180 );
			}
			void LaterSubscriber() => notifications++;
			using ( cache = new PathCellArtCache( Decode, Sync ) )
			{
				Sync();
				var original = first!;
				var originalSecond = second!;
				TexturePackSwitch.PackChanged += LaterSubscriber;
				try
				{
					failure = error;
					red = 80;
					CompleteSwitch();
					Assert.AreEqual( 1, notifications, "a malformed tile cannot interrupt later subscribers" );
					Assert.IsTrue( ProceduralCellArt.Same( original, first ), "failure retains the last successful art generation" );
					Assert.AreSame( original.Rgba, first!.Rgba );
					Assert.IsFalse( ProceduralCellArt.Same( originalSecond, second ), "another texture still refreshes" );
					CollectionAssert.AreEqual( Pixels( 80 ), second!.Rgba );
					Sync();
					CollectionAssert.AreEqual( new[] { 2, 2 }, decodes, "including failures, decode once per texture per generation" );
					failure = null;
					red = 180;
					CompleteSwitch();
					Assert.AreEqual( 2, notifications );
					CollectionAssert.AreEqual( new[] { 3, 3 }, decodes, "the next completed switch retries the failed texture" );
					Assert.IsFalse( ProceduralCellArt.Same( original, first ) );
					CollectionAssert.AreEqual( Pixels( 180 ), first!.Rgba );
				}
				finally
				{
					TexturePackSwitch.PackChanged -= LaterSubscriber;
				}
			}
		}
	}

	[TestMethod]
	public void ExplicitMissingArtDoesNotReviveOldPixelsOnALaterDecodeFailure()
	{
		var mode = 0;
		using var cache = new PathCellArtCache( _ => mode switch
		{
			0 => (Pixels( 20 ), 2, 2),
			1 => null,
			_ => throw new InvalidOperationException( "malformed replacement after removal" ),
		}, () => { } );
		Assert.IsNotNull( cache.Get( 0, 0x180 ) );
		mode = 1;
		CompleteSwitch();
		Assert.IsNull( cache.Get( 0, 0x180 ), "a null decoder result removes the old art" );
		mode = 2;
		CompleteSwitch();
		Assert.IsNull( cache.Get( 0, 0x180 ), "failure falls back to the most recent state, not older removed art" );
	}

	[TestMethod]
	public void FatalAndPermissionFailuresAreNotConvertedToMissingOrStaleArt()
	{
		static void Escapes<T>( T failure ) where T : Exception
		{
			using var cache = new PathCellArtCache( _ => throw failure, () => { } );
			Assert.AreSame( failure, Assert.ThrowsException<T>( () => cache.Get( 0, 0x180 ) ) );
		}
		Escapes( new OutOfMemoryException( "synthetic, no allocation pressure" ) );
		Escapes( new UnauthorizedAccessException( "synthetic access denial" ) );
		Escapes( new SecurityException( "synthetic security denial" ) );
	}

	[TestMethod]
	public void CorruptReplacementPngUsesLastGoodPixelsUntilTheNextSuccessfulSwitch()
	{
		var previousFiles = FileSystem;
		var previousPack = TexturePack.Directory;
		var root = Directory.CreateTempSubdirectory( "opentpw-path-pack-decode-" ).FullName;
		const string gamePath = "/levels/synthetic/terrain/pathtex/tile.wct";
		try
		{
			FileSystem = new BaseFileSystem( Directory.CreateDirectory( Path.Combine( root, "game" ) ).FullName );
			TexturePack.Directory = Directory.CreateDirectory( Path.Combine( root, "pack" ) ).FullName;
			var replacement = Path.Combine( TexturePack.Directory, TexturePack.RelativeFileName( gamePath ) );
			Directory.CreateDirectory( Path.GetDirectoryName( replacement )! );
			File.WriteAllBytes( replacement, PngImage.EncodeRgba( 2, 2, Pixels( 20 ) ) );
			PathCellArtCache? cache = null;
			ProceduralCellArt? art = null;
			var laterCalls = 0;
			void Sync() => art = cache!.Get( 0, 0x180 );
			void LaterSubscriber() => laterCalls++;
			using ( cache = new PathCellArtCache( _ => Texture.DecodeWct( gamePath ), Sync ) )
			{
				Sync();
				var original = art!;
				TexturePackSwitch.PackChanged += LaterSubscriber;
				try
				{
					File.WriteAllBytes( replacement, new byte[] { 0, 1, 2, 3 } );
					Assert.ThrowsException<InvalidOperationException>( () => Texture.DecodeWct( gamePath ), "STB rejects these synthetic replacement bytes" );
					CompleteSwitch();
					Assert.AreEqual( 1, laterCalls );
					Assert.IsTrue( ProceduralCellArt.Same( original, art ) );
					CollectionAssert.AreEqual( Pixels( 20 ), art!.Rgba );
					File.WriteAllBytes( replacement, PngImage.EncodeRgba( 2, 2, Pixels( 180 ) ) );
					CompleteSwitch();
					Assert.AreEqual( 2, laterCalls );
					Assert.IsFalse( ProceduralCellArt.Same( original, art ) );
					CollectionAssert.AreEqual( Pixels( 180 ), art!.Rgba );
				}
				finally
				{
					TexturePackSwitch.PackChanged -= LaterSubscriber;
				}
			}
		}
		finally
		{
			FileSystem = previousFiles!;
			TexturePack.Directory = previousPack;
			Directory.Delete( root, recursive: true );
		}
	}
}
