using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>An object's footprint as the original's box reader 0x466B70 gives it: the middle (engine space, at the base height) and the size in cells.</summary>
internal readonly record struct ParticleFootprint( NVector3 EngineMiddle, int WidthCells, int DepthCells );

/// <summary>The per-type particle effects an object starts at its footprint: the build, demolish, repair and upgrade effects of its category <c>.sam</c>.</summary>
public enum ObjectParticleEffect
{
	Create,
	Destroy,
	Repair,
	Upgrade
}

public sealed partial class OriginalObjectRuntime
{
	private Func<ParticleFootprint?>? particleFootprint;
	/// <summary>The handle of the repair effect the script's REPAIREFFECT 1 started (the original keeps it at the script's +0xCC); 0: none.</summary>
	private int repairParticle;

	/// <summary>Presentation-only footprint of the object for its type effects; headless runtimes have none and start nothing.</summary>
	internal void SetParticleFootprint( Func<ParticleFootprint?> footprint ) => particleFootprint = footprint;

	/// <summary>The effect id the object's category <c>.sam</c> names for <paramref name="effect"/> (0: none).</summary>
	public int ParticleEffectId( ObjectParticleEffect effect ) => effect switch
	{
		ObjectParticleEffect.Create => Entry.CreateParticleEffect,
		ObjectParticleEffect.Destroy => Entry.DestroyParticleEffect,
		ObjectParticleEffect.Repair => Entry.RepairParticleEffect,
		_ => Entry.UpgradeParticleEffect
	};

	/// <summary>
	/// Starts the type's build, demolish or upgrade effect at the object's footprint, sized to it, and returns the pool's handle (0 when nothing started).
	/// The repair effect has its own switch (<see cref="SetRepairParticle"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x558e7c create] the script attach 0x5587F0 (from 0x4dcf90, with the flag DAT_00785a2c clear: not while a park loads) reads the type record's +0x60, and when it and the box (0x466B70) exist creates the effect with 0x521E60 at ((x0 + x1) * 0.5, the object's base height, (z0 + z1) * 0.5) * 1024, then sizes its volume with 0x520030 to (width * 512, (y1 - y0) * 256, depth * 512) >> 4
	// [BIN:TPW-EXE:0x5590f7 destroy] the kill 0x559060 (from the object's removal 0x4dd0a0, flags 7 while no park loads or clears, 0 in mode 2, 4 otherwise) reads +0x64 and runs the same creation only when the flag's bit 2 is set (0x559102), before the script is torn down
	// [BIN:TPW-EXE:0x55A100 upgrade] reads +0x6C and runs the same creation and sizing (then a further dispatch 0x51BFC0 that is not traced); called from 0x4df8f0 when a repair job on a ride in upgrade mode (+0x19C == 2) finishes and its level is raised
	// [APPROX:PTCL-908] the box's height (the original's +0x1C/+0x28 of the footprint record, not traced) is taken as zero, so the volume has no vertical spread of its own
	internal int StartObjectEffect( ObjectParticleEffect effect )
	{
		var id = ParticleEffectId( effect );
		if ( stopped || id <= 0 || ParticleStarter is not { } starter || particleFootprint?.Invoke() is not { } footprint )
			return 0;
		var request = new ParticleEventRequest( AttractionId, 0, 1, -1, id, 1000, footprint.EngineMiddle, null, -1, -1, VolumeExtent: ExtentOf( footprint ) );
		var handle = starter( request );
		return handle is 0 or -1 ? 0 : handle;
	}

	/// <summary>The volume extent the original sets after creating: the footprint's width and depth in original units times 512 (the pool halves them), height 0.</summary>
	internal static (int X, int Y, int Z) ExtentOf( ParticleFootprint footprint ) => ((int)(footprint.WidthCells * 10f * 512f), 0, (int)(footprint.DepthCells * 10f * 512f));

	/// <summary>
	/// REPAIREFFECT: a non-zero value starts the type's repair effect at the footprint and keeps its handle; zero ends it (life -2) and forgets it.
	/// </summary>
	// [BIN:TPW-EXE:0x556DE0 repair effect] the handler 0x5562a6 calls it (0x5562bd) before its sounds: value 0 kills the handle at +0xCC (0x51FF70, life -2) and clears it; otherwise reads the type's +0x68 and creates the effect at the box middle without sizing it, keeping the handle (a handle already there is overwritten, not ended); the script's destruction (0x55854b) kills it too
	internal void SetRepairParticle( bool on )
	{
		if ( !on )
		{
			if ( repairParticle != 0 )
			{
				ParticleKiller?.Invoke( repairParticle );
				repairParticle = 0;
			}
			return;
		}
		var id = ParticleEffectId( ObjectParticleEffect.Repair );
		if ( stopped || id <= 0 || ParticleStarter is not { } starter || particleFootprint?.Invoke() is not { } footprint )
			return;
		var handle = starter( new ParticleEventRequest( AttractionId, 0, 1, -1, id, 1000, footprint.EngineMiddle, null, -1, -1 ) );
		repairParticle = handle is 0 or -1 ? 0 : handle;
	}

	/// <summary>The live repair effect's handle (0 when none), for tests.</summary>
	internal int RepairParticleHandle => repairParticle;
}
