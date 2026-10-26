namespace OpenTPW;

using SysMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// The gate in front of the original's visible-cell list (docs/LND.md, "The visible cells"). <c>0x56F090</c> builds the list and looks the cells up in the cache (<c>0x572440</c>) only
/// while bit 0x80000000 or 0x40000000 of <c>[0x8BD508]</c> is set (<c>0x56F0A2</c>; the same gate is at <c>0x56F698</c> and <c>0x570DB2</c>); otherwise the frame draws the list of the frame before.
/// </summary>
/// <remarks>
/// The frame function <c>0x576EC0</c> keeps the flag word of the frame's start (<c>local_98</c>) and puts it back at the end (<c>0x577326</c>), so a bit set during the frame lasts only for it and a bit set before it stays:
/// bit 0x80000000 is set during the frame when the 64 bytes of the camera matrix differ from the copy at <c>0x87A680</c> (<c>0x5773BE</c> to <c>0x5773E1</c>, only while the flags
/// are clear; the copy is refreshed every frame; the very first frame, with <c>[0x768AAC]</c> not yet 1, neither compares nor sets) or when there is more than one viewport (<c>0x5773FB</c>), and at the frame's very start when bit 0x40 is set
/// (<c>0x576ECB</c>; the flag is kept like the others). The scene-change setters <c>0x576574</c>, <c>0x576603</c>, <c>0x5767CA</c>, <c>0x473D9E</c>, <c>0x477177</c>, <c>0x477FAE</c> and <c>0x4786A7</c> set it outside the frame, so it
/// is part of the next frame's start flags and stays: nothing in the executable was found to clear it, so after the first scene change the list is built in every frame and the camera compare is never reached again.
/// </remarks>
internal sealed class TerrainListGate
{
	private SysMatrix copy = SysMatrix.Identity;
	private bool seenFrame, copyInitialised;

	/// <summary>Whether the frame-start flag word has bit 0x80000000 (or 0x40000000) set.</summary>
	public bool Persistent { get; private set; }

	/// <summary>The original's bit 0x40 of <c>[0x8BD508]</c>: the list is built in every frame.</summary>
	public bool Always { get; set; }

	/// <summary>Frames in which the list was built.</summary>
	public long Built { get; private set; }

	/// <summary>A scene-change setter ran: the flag stays set from the next frame on.</summary>
	public void MarkSceneChanged() => Persistent = true;

	/// <summary>The frame function's gate for one viewport and the camera matrix of the frame; returns whether <c>0x56F090</c> builds the list and looks it up.</summary>
	public bool Frame( in SysMatrix camera )
	{
		var run = Persistent;
		if ( Always )
		{
			Persistent = true;
			run = true;
		}
		if ( !seenFrame )
		{
			// [0x768AAC] is not 1 yet: the first frame sets it and compares nothing
			seenFrame = true;
		}
		else
		{
			if ( !copyInitialised )
			{
				copy = SysMatrix.Identity;
				copyInitialised = true;
			}
			if ( !run && !SameBytes( copy, camera ) )
				run = true;
			copy = camera;
		}
		if ( run )
			Built++;
		return run;
	}

	private static bool SameBytes( in SysMatrix a, in SysMatrix b )
		=> System.Runtime.InteropServices.MemoryMarshal.AsBytes( System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan( in a, 1 ) )
			.SequenceEqual( System.Runtime.InteropServices.MemoryMarshal.AsBytes( System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan( in b, 1 ) ) );
}
