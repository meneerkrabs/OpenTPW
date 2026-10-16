namespace OpenTPW;

/// <summary>
/// The camera block (tp.exe <c>0x42CEC0</c>, 40 bytes), in the loader's order and under the names its log strings give: <c>gf_CameraZoom</c>, <c>gf_YRotation</c>,
/// <c>gui_CameraFlags</c> (the loader clears the word again after reading it), <c>gs_RequiredPOIPosition</c> and <c>gs_SavedPOIPosition</c> (three floats each, the point of
/// interest the camera looks at) and <c>gf_SavedYRotation</c>. The loader then copies the yaw to <c>0x7909E0</c> and turns it by the camera's tilt angle when the tilt flag
/// <c>0x78D912</c> is set.
/// </summary>
public readonly record struct SaveCamera( float Zoom, float YRotation, uint Flags, (float X, float Y, float Z) RequiredPoi, (float X, float Y, float Z) SavedPoi, float SavedYRotation )
{
	public const int Bytes = 40;

	internal static SaveCamera Read( ReadOnlySpan<byte> payload, SavePayloadBlock block )
	{
		var reader = new SaveFieldReader( payload.Slice( 0, block.End ), block.Offset, "camera block" );
		var zoom = reader.F32();
		var yaw = reader.F32();
		var flags = reader.U32();
		var required = (reader.F32(), reader.F32(), reader.F32());
		var saved = (reader.F32(), reader.F32(), reader.F32());
		return new SaveCamera( zoom, yaw, flags, required, saved, reader.F32() );
	}
}

/// <summary>
/// The small runtime blocks of a decoded payload that have a traced width and no OpenTPW counterpart: the two clock words (<c>0x402E30</c>) and the vanilla-time word
/// (<c>0x403260</c>), the nine game-system words (<c>0x5506E0</c>; three of them are re-based on <c>0x8786BC</c> after the read), the cheat bytes (<c>0x405570</c>), the advisor
/// scoring word (<c>0x59C860</c>), the camera and the UI record count (<c>0x48EF70</c>: count, then 0x21C-byte window records). They are read for the import's report and kept raw.
/// </summary>
public sealed record SaveRuntimeBlocks( uint[] ClockWords, uint VanillaTime, uint[] GameSystemWords, byte[] Cheats, uint AdvisorScoring, SaveCamera Camera, uint UiRecords )
{
	/// <summary>Reads the blocks of <paramref name="layout"/> from <paramref name="payload"/> (their widths were checked by the split).</summary>
	public static SaveRuntimeBlocks Read( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		static uint[] Words( ReadOnlySpan<byte> payload, SavePayloadBlock block, int count )
		{
			var reader = new SaveFieldReader( payload.Slice( 0, block.End ), block.Offset, $"{block.Block} block" );
			var words = new uint[count];
			for ( var index = 0; index < count; index++ )
				words[index] = reader.U32();
			return words;
		}
		var ui = layout[SaveBlock.UI];
		return new SaveRuntimeBlocks( Words( payload, layout[SaveBlock.Clock], 2 ), Words( payload, layout[SaveBlock.VanillaTime], 1 )[0],
			Words( payload, layout[SaveBlock.GameSystem], SaveBlockWidths.GameSystemBytes / 4 ), payload.Slice( layout[SaveBlock.Cheat].Offset, SaveBlockWidths.CheatBytes ).ToArray(),
			Words( payload, layout[SaveBlock.AdvisorScoring], 1 )[0], SaveCamera.Read( payload, layout[SaveBlock.Camera] ),
			ui.Length >= 4 ? Words( payload, ui, 1 )[0] : 0 );
	}
}
