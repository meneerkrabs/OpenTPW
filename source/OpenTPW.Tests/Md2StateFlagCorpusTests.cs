using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The model flag bits that tp.exe's state builder 0x581790 turns into render-state bits, against every MD2 of the original data (docs/MD2-MODELS.md, "Render-state bits that no shipped model sets"),
/// and the interface cut-outs. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class Md2StateFlagCorpusTests
{
	[TestMethod]
	public void NoShippedMeshWithATextureSlotSetsTheNodeTypeBitsTheStateBuilderReads()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var nodeTypes = new SortedDictionary<uint, int>();
		var slotFlags = new SortedSet<uint>();
		var typeTwoWithSlots = 0;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (_, file) in Md2CorpusTests.Md2Members( archive.Root, "" ) )
			{
				ModelFile model;
				try { model = new ModelFile( new MemoryStream( file.GetData() ) ); }
				catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException ) { continue; }
				file.Free();
				foreach ( var mesh in model.Meshes )
				{
					nodeTypes[mesh.NodeType] = nodeTypes.GetValueOrDefault( mesh.NodeType ) + 1;
					if ( ( mesh.NodeType & 2 ) != 0 && mesh.Materials.Length > 0 )
						typeTwoWithSlots++;
					foreach ( var material in mesh.Materials )
						slotFlags.Add( material.Flags );
				}
			}
		}
		Console.WriteLine( "Node types: " + string.Join( ", ", nodeTypes.Select( pair => $"0x{pair.Key:X}={pair.Value}" ) ) );
		Console.WriteLine( "Slot flags: " + string.Join( ", ", slotFlags.Select( flag => $"0x{flag:X}" ) ) );
		// state bit 0x800 (depth write off) needs bit 0x2, 0x5156 bit 0x2000000, 0x20000 bit 0x8000000 of the mesh record's first dword
		CollectionAssert.IsSubsetOf( nodeTypes.Keys.ToArray(), new uint[] { 0x1, 0x2, 0x401 } );
		Assert.AreEqual( 0, typeTwoWithSlots, "only the empty heightfield meshes have type 2" );
		// the slot flags 0x10 and 0x20 are the wrap bits (loader 0x575E10); nothing above 0x73 is set
		Assert.IsTrue( slotFlags.All( flag => flag <= 0x73 ) );
	}

	[TestMethod]
	public void TheInterfaceCutOutsCarryTheirOwnAlphaPlane()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var ui = Directory.EnumerateFiles( dataPath, "ui.wad", SearchOption.AllDirectories ).First();
		using var archive = new WadArchive( ui );
		foreach ( var name in new[] { "textures/ipan.wct", "stexture/ipan.wct", "textures/smallguage.wct" } )
		{
			var texture = new TextureFile( new MemoryStream( archive.GetFile( name ).GetData() ) );
			Assert.AreEqual( TextureAlphaType.Cutout, texture.AlphaType, name );
			var data = texture.Data.Data;
			var below = 0;
			for ( var index = 3; index < data.Length; index += 4 )
				below += data[index] < UiImages.CutOutReference ? 1 : 0;
			Assert.IsTrue( below > 0, $"{name} has texels below the alpha test" );
		}
	}
}
