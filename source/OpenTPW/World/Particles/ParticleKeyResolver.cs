using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

public enum ParticleKeyStatus
{
	Resolved,
	/// <summary>The key entry has no matrix: the native resolver hands back position (0, 0, 0) and direction (0, 0, 1024) in world space.</summary>
	ResolvedAtWorldOrigin,
	UnsupportedSelector,
	UnsupportedFallback,
	MissingKey,
	UnsupportedNode,
	InvalidTransform
}

/// <summary>Engine coordinates (Z up); direction is unit length, without the native resolver's 1024 multiplier.
/// Failed results carry no pose, and so does <see cref="ParticleKeyStatus.ResolvedAtWorldOrigin"/> (the pose is the world origin, not an engine position).</summary>
public readonly record struct ParticleKeyResult( ParticleKeyStatus Status, int AttributeIndex, int NodeIndex,
	NVector3? EnginePosition, NVector3? EngineUnitDirection );

/// <summary>Read-only EVENT1/2 model-key resolver. Uses attribute values and native node ordinals, never node names.</summary>
public sealed class ParticleKeyResolver
{
	public const uint EventSelector = 0x100;
	/// <summary>The selector an EVENT sound's key is looked up with (tp.exe <c>0x556b90</c> passes 0x200 to <c>0x44b220</c>); particles use <see cref="EventSelector"/>.</summary>
	public const uint SoundSelector = 0x200;
	/// <summary>The selector a tour walk's node is looked up with (tp.exe <c>0x5571A0</c> passes 0x800 to <c>0x556b90</c>).</summary>
	public const uint WalkSelector = 0x800;
	public const float NativeDirectionMultiplier = 1024f;
	private readonly ModelFile model;

	public ParticleKeyResolver( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "Particle keys require a geometry model.", nameof( model ) );
		this.model = model;
	}

	/// <summary>Resolve against the caller's current model-space hierarchy matrices (ObjectAnimator.NodeTransforms),
	/// or explicitly supplied bind matrices. The resolver neither advances animation nor reads a camera.
	/// Selector 0x100 is qualified for particles and 0x200 for sounds. Negative keys require the unqualified native box/height fallback.
	/// No extra position offset is inferred from the optional native output direction pointer.</summary>
	// [BIN:STP-PPC:0x1019B298 model key lookup] matches attribute Value and selector mask; first matching entry wins
	// [BIN:STP-PPC:0x1019AE64 lookup construction] entry i selects header word +0x46 plus i; +0x44 selects mesh versus dummy records
	// [BIN:STP-PPC:0x100AC7F0 particle key resolver] position comes from matrix +48/+52/+56; direction from +32/+36/+40, optionally negated by runtime lookup flag 0x10, normalized then multiplied by 1024
	// [BIN:TPW-EXE:0x44AB30 key table refresh] the refresh runs only when the instance block's bit 0x1 is set (0x45BCB0 and 0x45BD00 set it at 0x45BCF9 and 0x45BD27, right after adding the block's draw object to the scene lists, 0x576410 and 0x576620: a registered instance) and the table's bit 0x1 is set (0x44A98C: at least one entry of the model's key list has the matrix flags 0x30, 0x44A904; bit 0x10 marks a key node with flag 0x400, whose direction the lookup negates)
	// [APPROX:PTCL-900] current ObjectAnimator hierarchy matrices supply key frames at EVENT time; the original reads its key table (0x556B90), which 0x44AB30 refreshes at the instance's creation (0x463851) and per draw (render callback 0x463030 -> 0x44E380 -> 0x473C70), for a registered instance with a matrix key, and at 0x42A6D6/0x438A53 — about one render frame behind and stale while the object is culled
	public ParticleKeyResult Resolve( int key, uint selector, IReadOnlyList<Matrix4x4> nodeTransforms,
		ObjectPlacement placement, bool includeDirection = false )
	{
		ArgumentNullException.ThrowIfNull( nodeTransforms );
		ParticleKeyResult Refuse( ParticleKeyStatus status, int attribute = -1, int node = -1 ) => new( status, attribute, node, null, null );
		if ( selector != EventSelector && selector != SoundSelector && selector != WalkSelector )
			return Refuse( ParticleKeyStatus.UnsupportedSelector );
		if ( key < 0 )
			return Refuse( ParticleKeyStatus.UnsupportedFallback );
		if ( nodeTransforms.Count != model.Nodes.Count || !ObjectFootprint.IsValidRotation( placement.Rotation )
			|| placement.X < int.MinValue / 10 || placement.X > int.MaxValue / 10
			|| placement.Y < int.MinValue / 10 || placement.Y > int.MaxValue / 10
			|| !float.IsFinite( placement.BaseHeight ) || !float.IsFinite( placement.GridOrigin.X ) || !float.IsFinite( placement.GridOrigin.Y ) )
			return Refuse( ParticleKeyStatus.InvalidTransform );
		for ( var attribute = 0; attribute < model.DummyAttributes.Count; attribute++ )
		{
			var entry = model.DummyAttributes[attribute];
			if ( entry.Value != (uint)key || (entry.Type & selector) == 0 )
				continue;
			var node = model.Counts.Unknown46 + attribute;
			if ( node >= model.Nodes.Count )
				return Refuse( ParticleKeyStatus.UnsupportedNode, attribute, node );
			// [BIN:TPW-EXE:0x556BF1 null key matrix] a key entry without a matrix (flags 0x30 clear) is a success at the world origin: position (0, 0, 0) and direction (0, 0, 1024) in world space,
			// not at the placed object's anchor
			if ( (entry.Type & 0x30) == 0 )
				return new ParticleKeyResult( ParticleKeyStatus.ResolvedAtWorldOrigin, attribute, node, null, null );
			var frame = nodeTransforms[node];
			if ( !Finite( frame ) || frame.M14 != 0 || frame.M24 != 0 || frame.M34 != 0 || frame.M44 != 1 )
				return Refuse( ParticleKeyStatus.InvalidTransform, attribute, node );
			var engineFrame = ObjectPlacement.SwapAxes( frame ) * placement.ModelToEngine;
			var position = engineFrame.Translation;
			NVector3? direction = null;
			if ( includeDirection )
			{
				// Preserve MD2's third basis row through the shared axis/placement conversion.
				var localDirection = new NVector3( frame.M31, frame.M32, frame.M33 );
				// 0x19AE98..0x19AED8: flags 0x30 enable the matrix, node flag 0x400 sets lookup flag 0x10.
				if ( (model.Nodes[node].Type & 0x400) != 0 )
					localDirection = -localDirection;
				var engineDirection = NVector3.TransformNormal( new NVector3( localDirection.X, localDirection.Z, localDirection.Y ), placement.ModelToEngine );
				var length = engineDirection.Length();
				if ( !float.IsFinite( length ) || length <= 0 )
					return Refuse( ParticleKeyStatus.InvalidTransform, attribute, node );
				direction = engineDirection / length;
			}
			if ( !Finite( position ) || (direction is { } vector && !Finite( vector )) )
				return Refuse( ParticleKeyStatus.InvalidTransform, attribute, node );
			return new ParticleKeyResult( ParticleKeyStatus.Resolved, attribute, node, position, direction );
		}
		return Refuse( ParticleKeyStatus.MissingKey );
	}

	private static bool Finite( NVector3 vector ) => float.IsFinite( vector.X ) && float.IsFinite( vector.Y ) && float.IsFinite( vector.Z );
	private static bool Finite( Matrix4x4 m ) =>
		float.IsFinite( m.M11 ) && float.IsFinite( m.M12 ) && float.IsFinite( m.M13 ) && float.IsFinite( m.M14 )
		&& float.IsFinite( m.M21 ) && float.IsFinite( m.M22 ) && float.IsFinite( m.M23 ) && float.IsFinite( m.M24 )
		&& float.IsFinite( m.M31 ) && float.IsFinite( m.M32 ) && float.IsFinite( m.M33 ) && float.IsFinite( m.M34 )
		&& float.IsFinite( m.M41 ) && float.IsFinite( m.M42 ) && float.IsFinite( m.M43 ) && float.IsFinite( m.M44 );
}
