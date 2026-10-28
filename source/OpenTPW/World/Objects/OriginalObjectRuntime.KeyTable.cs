using System.Numerics;

namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Matrix4x4[]? keyPose;

	/// <summary>
	/// The node matrices the EVENT keys, sounds' anchors and the walk-off particle's node are read from: the instance's key table once a host registered the instance with the scene
	/// (<see cref="RefreshKeyPose"/>), else the animator's live pose (a runtime without a scene, as the tests and standalone scripts use it).
	/// </summary>
	// [BIN:TPW-EXE:0x556B90 key lookup] the key resolver reads the node's matrix (+0x30..+0x38 position, +0x20.. direction) out of the instance's key table, not out of the node hierarchy; the table is rebuilt by 0x44AB30 only at the refresh points of RefreshKeyPose
	internal IReadOnlyList<Matrix4x4> KeyPose => keyPose ?? Animator.NodeTransforms;

	/// <summary>True once <see cref="RefreshKeyPose"/> has put the runtime on a key table of its own.</summary>
	internal bool HasKeyTable => keyPose != null;

	/// <summary>
	/// Rebuilds the key table from the animator's current pose: the creation of a registered instance (0x463851, after its draw object joined the scene lists) and the instance's frames the scene
	/// prepares it for (the render callback 0x463030 → 0x44E380 → 0x473C70 with the posing flag clear, 0x473E01..0x473E1B), and the ride view's instance on every list pass.
	/// </summary>
	// [BIN:TPW-EXE:0x44AB30 key table refresh] runs only for an instance the scene registered (instance bit 0x1) whose table has a matrix key (table bit 0x1), and not for an instance flagged 0x20000000 (0x473E11); 0x473C70 reaches it at its end for a posing call (argument bit 0x8 clear) and, for a list-pass call, only when the model header has flag 0x4 (0x473E01..0x473E0F), which the object loader strips (0x461F10)
	// [BIN:TPW-EXE:0x44AB30 other refreshes] the remaining callers are 0x42A6D6 (the ride view entering a placed instance) and 0x438A53 (the coaster car bend, COAST-034), outside the ride objects' scripts
	internal void RefreshKeyPose()
	{
		var nodes = Animator.NodeTransforms;
		if ( keyPose == null || keyPose.Length != nodes.Count )
			keyPose = new Matrix4x4[nodes.Count];
		for ( var node = 0; node < keyPose.Length; node++ )
			keyPose[node] = nodes[node];
	}
}
