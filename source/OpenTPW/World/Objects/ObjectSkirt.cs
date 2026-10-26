using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>One cell of an object's skirt table: the orientation code of its floor art and the texture slot of that art. A cell without a skirt keeps the default <c>{1, 10000}</c>.</summary>
public readonly record struct ObjectSkirtEntry( int Code, int Slot )
{
	/// <summary>The entry of a cell the type loader gave no skirt (the buffer's default).</summary>
	public static readonly ObjectSkirtEntry None = new( 1, 10000 );

	/// <summary>Whether the cell has a skirt (code bit 0x80).</summary>
	public bool HasSkirt => (Code & 0x80) != 0;

	/// <summary>Whether the floor art's quad runs along its other axis (code bit 0x40).</summary>
	public bool Flipped => (Code & 0x40) != 0;

	/// <summary>The orientation bits of the code (0, 0x08, 0x10 or 0x20: the quad corner whose texture coordinates are smallest).</summary>
	public int Orientation => Code & 0x38;
}

/// <summary>
/// What the type loader reads from an object model's floor, the "procedural base" or skirt (docs/OBJECTS.md, "Floors"): the table of edge cells with their floor art, the texture slots
/// that draw opaque, the slots whose wrap bits are cleared, the ground node's normalised texture coordinates and the list of ground vertices the placed object's edge conform replays
/// (<see cref="ObjectGroundConform"/>). Built once per type over the level's terrain, immutable.
/// </summary>
// [BIN:TPW-EXE:0x0046A5A0 type load] load flag 1 of the type loader 0x004629D0 runs the ground node search 0x0046A5A0 and, when the model header +0x30 has bit 0x08 ("has procedural base") and the type has a ground node (+0x2C) or the header has bit 0x80, the skirt reader 0x0046AA10; the base record's byte 0 then gets bit 0x40 (0x0046B37B)
public sealed class ObjectSkirt
{
	private ObjectSkirt( int width, int height )
	{
		Width = width;
		Height = height;
		Entries = new ObjectSkirtEntry[width * height];
		Array.Fill( Entries, ObjectSkirtEntry.None );
	}

	/// <summary>The skirt grid's width and height in cells: the type's <c>Info.Shape</c> size.</summary>
	// [BIN:TPW-EXE:0x004629D0 shape size] local_418/local_41c = shape words 0 and 1 (the caller's overrides in params 10/11 are 0 for objects) are stored at the base record's +0xF0/+0xF4 and handed to 0x0046A5A0 as the skirt grid's size and to the height map loader 0x00451640 (type +0xCC: its ushort width +8 and height +0xA must equal them, else 0x00451880 regenerates the map at that size). The grid the instance constructor 0x00463060 walks (+0xCC) is therefore the shape's size too, not another grid
	public int Width { get; private init; }
	public int Height { get; private init; }

	/// <summary>Whether the model has a procedural base: the base record's flag 0x40 (header +0x30 bit 0x08 and a ground node or header bit 0x80).</summary>
	public bool HasBase { get; private set; }

	/// <summary>The node the skirt was read from (the ground node, else the root with header bit 0x80), or -1.</summary>
	public int GroundNode { get; private set; } = -1;

	/// <summary>The skirt entries by <c>row × Width + column</c> (cell (0, 0) at the shape's first cell, rows counted as <see cref="ObjectMapGrid"/> stores them).</summary>
	public ObjectSkirtEntry[] Entries { get; private set; }

	public ObjectSkirtEntry EntryAt( int column, int row ) => (uint)column < (uint)Width && (uint)row < (uint)Height ? Entries[row * Width + column] : ObjectSkirtEntry.None;

	/// <summary>The cells that have a skirt, in the table's order (row by row).</summary>
	public IEnumerable<(int Column, int Row, ObjectSkirtEntry Entry)> Cells
	{
		get
		{
			for ( var row = 0; row < Height; row++ )
				for ( var column = 0; column < Width; column++ )
					if ( Entries[row * Width + column].HasSkirt )
						yield return (column, row, Entries[row * Width + column]);
		}
	}

	/// <summary>Per texture slot of the model: the slot gets the runtime flag 0x800, which makes its faces draw opaque (state 0x1256: no blend, no alpha test).</summary>
	public bool[] OpaqueSlots { get; private set; } = Array.Empty<bool>();

	/// <summary>Per texture slot of the model: the wrap bits 0x10/0x20 are cleared, so the slot clamps.</summary>
	public bool[] ClampedSlots { get; private set; } = Array.Empty<bool>();

	/// <summary>The ground node's texture coordinates per corner after the normalisation (<see cref="NormaliseTexCoords"/>), or null when no skirt was read.</summary>
	public NVector2[]? TexCoords { get; private set; }

	/// <summary>The mesh (index into the model's meshes) of <see cref="GroundNode"/>, or -1.</summary>
	public int GroundMesh { get; private set; } = -1;

	/// <summary>The ground vertex list of the counting walk, which the placed object's edge conform replays (<see cref="ObjectGroundConform"/>); empty for a model without ground vertices.</summary>
	public ushort[] ConformList { get; private set; } = Array.Empty<ushort>();

	/// <summary>The nodes that own entries of <see cref="ConformList"/> (node flag 0x1000).</summary>
	public bool[] ConformNodes { get; private set; } = Array.Empty<bool>();

	/// <summary>Cells that should have had a skirt but found no qualifying floor face (the original logs an error once per type; the entry stays <see cref="ObjectSkirtEntry.None"/>).</summary>
	public int MissingFaceCells { get; private set; }

	/// <summary>
	/// The skirt of <paramref name="settings"/>' type over <paramref name="terrain"/>, or null for a fixed item (<c>Info.DontApplyOffset</c>, loaded without the base flags). A type whose model has
	/// no procedural base still returns the conform list and an empty table.
	/// </summary>
	public static ObjectSkirt? Build( ObjectSettings settings, ModelFile model, HoardingTerrain terrain )
	{
		if ( settings.GetBool( "Info.DontApplyOffset" ) )
			return null;
		return Build( ObjectMapGrid.ShapeOf( settings ), model, terrain );
	}

	/// <summary><see cref="Build(ObjectSettings, ModelFile, HoardingTerrain)"/> for a parsed shape.</summary>
	internal static ObjectSkirt Build( ObjectMapGrid? shape, ModelFile model, HoardingTerrain terrain )
	{
		var scan = ObjectHoardingGeometry.ScanGround( model, shape, terrain );
		var skirt = new ObjectSkirt( shape?.Width ?? 0, shape?.Height ?? 0 )
		{
			ConformList = scan.Listed,
			ConformNodes = scan.ListedNodes,
			OpaqueSlots = new bool[model.Textures.Count],
			ClampedSlots = new bool[model.Textures.Count]
		};
		var drape = (model.HeaderFlags & 0x80) != 0;
		// [BIN:TPW-EXE:0x0046A5A0 base gate] the skirt reader runs when the header has bit 0x08 and the ground node (+0x2C) is set or the header has bit 0x80 (the drape flag: the node is then the root +0x78); without bit 0x08 it logs "has not been exported with 'has procedural base'" and skips the base
		if ( (model.HeaderFlags & 8) == 0 )
			return skirt;
		var node = scan.Node >= 0 ? scan.Node : drape ? model.RootNodeIndex : -1;
		if ( node < 0 )
			return skirt;
		skirt.HasBase = true;
		skirt.GroundNode = node;
		var meshIndex = node < model.Nodes.Count ? model.Nodes[node].MeshIndex : -1;
		if ( meshIndex < 0 || meshIndex >= model.Meshes.Count || shape == null || model.Meshes[meshIndex].TexCoords.Length < model.Meshes[meshIndex].Vertices.Length )
			return skirt;
		skirt.Read( shape, model, terrain, meshIndex, node, drape );
		return skirt;
	}

	private void Read( ObjectMapGrid shape, ModelFile model, HoardingTerrain terrain, int meshIndex, int node, bool drape )
	{
		var mesh = model.Meshes[meshIndex];
		GroundMesh = meshIndex;
		var parents = new List<float[]>();
		for ( var parent = model.Nodes[node].ParentIndex; parent >= 0; parent = model.Nodes[parent].ParentIndex )
			parents.Add( ObjectFloorMath.ToArray( model.Nodes[parent].Transform ) );
		var faces = new int[mesh.Indices.Length / 3][];
		for ( var face = 0; face < faces.Length; face++ )
			faces[face] = new[] { (int)mesh.Indices[face * 3], (int)mesh.Indices[face * 3 + 1], (int)mesh.Indices[face * 3 + 2] };
		var ground = new SkirtNode( ObjectFloorMath.ToArray( model.Nodes[node].Transform ), parents, mesh.Positions.Select( position => position.GetSystemVector3() ).ToArray(),
			mesh.TexCoords.Select( uv => (NVector2)uv ).ToArray(), mesh.CornerPositionIndices.Select( corner => (int)corner ).ToArray(), faces,
			mesh.Materials.Select( material => new SkirtGroup( material.TextureIndex, material.a, material.b ) ).ToArray() );
		var slotFlags = model.Textures.Select( texture => texture.Flags ).ToArray();
		var result = SkirtReading.Read( ground, new SkirtShape( shape.Width, shape.Height, shape.RawTypes ), slotFlags, drape, terrain.CellSizeX, terrain.CellSizeZ );
		Entries = result.Entries;
		TexCoords = result.Uvs;
		for ( var slot = 0; slot < slotFlags.Length; slot++ )
		{
			OpaqueSlots[slot] = (result.Slots[slot] & TextureSlotAddressing.OpaqueSlotBit) != 0;
			ClampedSlots[slot] = ((slotFlags[slot] ^ result.Slots[slot]) & TextureSlotAddressing.WrapFlags) != 0;
		}
		MissingFaceCells = result.Errors;
		FlippedCells = result.Flipped;
	}

	/// <summary>The number of skirt cells whose floor art is flipped (code bit 0x40), as the original counts and prints them.</summary>
	public int FlippedCells { get; private set; }
}
