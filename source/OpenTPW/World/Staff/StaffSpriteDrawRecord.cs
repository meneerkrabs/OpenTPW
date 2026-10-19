namespace OpenTPW;

public enum StaffSpriteRoute { World, Screen }

/// <summary>A detached immutable output snapshot. Raw words survive unsupported rendering unchanged.</summary>
public sealed class StaffSpriteDrawRecord
{
	private readonly uint[] registers;
	public long Session { get; }
	public int RegistrySlot { get; }
	public ulong Generation { get; }
	public bool EmployeeClone { get; }
	public uint OutputValid { get; }
	public StaffSpriteDiagnostic Diagnostic { get; }
	public uint this[StaffSpriteRegister register] => registers[(int)register];
	public uint Kind => this[StaffSpriteRegister.Kind];
	public uint Variant => this[StaffSpriteRegister.Variant];
	public uint Slot => this[StaffSpriteRegister.Slot];
	public uint Frame => this[StaffSpriteRegister.Frame];
	public uint FrameCount => this[StaffSpriteRegister.FrameCount];
	public uint Heading => this[StaffSpriteRegister.Heading];
	public uint Flags => this[StaffSpriteRegister.Flags];
	public uint X => this[StaffSpriteRegister.X];
	public uint Y => this[StaffSpriteRegister.Y];
	public uint Z => this[StaffSpriteRegister.Z];
	public uint PrimaryScale => this[StaffSpriteRegister.PrimaryScale];
	public uint SecondaryScale => this[StaffSpriteRegister.SecondaryScale];
	public uint RotationX => this[StaffSpriteRegister.RotationX];
	public uint RotationY => this[StaffSpriteRegister.RotationY];
	public uint RotationZ => this[StaffSpriteRegister.RotationZ];
	public uint Alpha => this[StaffSpriteRegister.Alpha];
	public float PositionX => BitConverter.UInt32BitsToSingle( X );
	public float PositionY => BitConverter.UInt32BitsToSingle( Y );
	public float PositionZ => BitConverter.UInt32BitsToSingle( Z );
	public StaffSpriteRoute Route => (Flags & 0x40000) != 0 ? StaffSpriteRoute.Screen : StaffSpriteRoute.World;

	public StaffSpriteDrawRecord( StaffSpriteVmState state, long session, int slot, ulong generation, bool employeeClone,
		StaffSpriteDiagnostic diagnostic = StaffSpriteDiagnostic.None )
	{
		ArgumentNullException.ThrowIfNull( state );
		registers = (uint[])state.Registers.Clone(); OutputValid = state.OutputValid;
		Session = session; RegistrySlot = slot; Generation = generation; EmployeeClone = employeeClone; Diagnostic = diagnostic;
	}
}

public readonly record struct StaffSpriteFrameResolution( StaffSpriteDiagnostic Diagnostic, StaffSpriteRoute Route,
	int Atlas = -1, int FrameIndex = -1, bool Mirror = false, GuestAtlasFrame Frame = default )
{
	public bool CanDraw => Diagnostic == StaffSpriteDiagnostic.None;
}

/// <summary>Strict read-only CPU resolution, deliberately separate from the forgiving guest/hire helpers.</summary>
public static class StaffSpriteFrameResolver
{
	public static StaffSpriteFrameResolution Resolve( StaffSpriteMetadataCatalog catalog, StaffSpriteDrawRecord record, float cameraYaw = 0 )
	{
		StaffSpriteFrameResolution Reject( StaffSpriteDiagnostic reason ) => new( reason, record.Route );
		if ( record.Diagnostic is StaffSpriteDiagnostic.InvalidVm or StaffSpriteDiagnostic.StaleIdentity or StaffSpriteDiagnostic.Retired or StaffSpriteDiagnostic.SourceAbsent )
			return Reject( record.Diagnostic );
		if ( record.OutputValid == 0 )
			return Reject( StaffSpriteDiagnostic.NoOutput );
		if ( record.Frame == uint.MaxValue )
			return Reject( StaffSpriteDiagnostic.FrameSuppressed );
		var bank = catalog.Resolve( record.Kind, record.Variant );
		if ( !bank.Available )
			return Reject( StaffSpriteDiagnostic.MetadataUnavailable );
		if ( bank.Multipart )
			return Reject( StaffSpriteDiagnostic.UnsupportedResource );
		if ( record.Alpha != 255 || record.PrimaryScale != BitConverter.SingleToUInt32Bits( record.EmployeeClone ? 8f : 1f )
			|| record.SecondaryScale != BitConverter.SingleToUInt32Bits( 1f ) || record.RotationX != 0 || record.RotationY != 0 || record.RotationZ != 0 )
			return Reject( StaffSpriteDiagnostic.UnsupportedTransform );
		if ( !float.IsFinite( record.PositionX ) || !float.IsFinite( record.PositionY ) || !float.IsFinite( record.PositionZ ) )
			return Reject( StaffSpriteDiagnostic.InvalidPose );
		if ( record.EmployeeClone && record.Route == StaffSpriteRoute.World )
			return Reject( StaffSpriteDiagnostic.WorldRouteNotImplemented );
		if ( record.Slot >= bank.Slots.Count )
			return Reject( StaffSpriteDiagnostic.InvalidSlot );
		var slot = bank.Slots[(int)record.Slot];
		if ( slot.IsEmpty )
			return Reject( StaffSpriteDiagnostic.MissingSlot );
		if ( record.Frame >= slot.FramesPerDirection )
			return Reject( StaffSpriteDiagnostic.InvalidFrame );
		if ( record.Heading > 7 || !float.IsFinite( cameraYaw ) )
			return Reject( StaffSpriteDiagnostic.InvalidDirection );
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( (int)record.Heading,
			record.Route == StaffSpriteRoute.Screen ? 0 : cameraYaw, slot.Directions );
		if ( (uint)direction >= slot.Directions )
			return Reject( StaffSpriteDiagnostic.InvalidDirection );
		var index = (long)slot.FirstFrame + (long)direction * slot.FramesPerDirection + record.Frame;
		if ( index < 0 || index >= bank.Frames.Count )
			return Reject( StaffSpriteDiagnostic.InvalidFrame );
		var frame = bank.Frames[(int)index];
		if ( frame.PixelsPerUnitX == 0 || frame.PixelsPerUnitY == 0 )
			return Reject( StaffSpriteDiagnostic.UnsupportedResource );
		return new( StaffSpriteDiagnostic.None, record.Route, bank.Atlas, (int)index, mirror, frame );
	}
}
