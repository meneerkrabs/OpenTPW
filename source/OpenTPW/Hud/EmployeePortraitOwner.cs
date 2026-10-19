namespace OpenTPW;

public enum EmployeePortraitState { Closed, OpenUnbound, OpenBound, OpenRetired }

/// <summary>Independent CPU employee clone. Partial copying never creates or repairs an allocation.</summary>
public sealed class EmployeePortraitOwner : IDisposable
{
	private readonly StaffSpritePresentation presentation;
	private StaffBodySprite? bound;
	private StaffSpriteTicket? clone;
	private EmployeePortraitState state;
	public EmployeePortraitOwner( StaffSpritePresentation presentation )
	{
		this.presentation = presentation;
		presentation.Registry.Removed += Removed;
		presentation.Invalidated += Close;
	}
	public EmployeePortraitState State => state;
	public StaffSpriteTicket? CloneTicket => clone;
	public StaffSpriteIdentity? BoundIdentity => bound?.Identity;
	public long Epoch { get; private set; }
	public StaffSpriteDiagnostic Diagnostic { get; private set; }
	public StaffSpriteVmState? ReadCloneState() => TryResolve( out var value ) ? value.DeepCopy() : null;
	internal bool TryResolve( out StaffSpriteVmState value )
	{
		value = null!;
		return clone is { } ticket && bound?.IsLive == true && presentation.Registry.TryResolve( ticket, out value );
	}
	public StaffSpriteDrawRecord? DrawRecord => TryResolve( out var value ) && clone is { } ticket
		? new StaffSpriteDrawRecord( value, ticket.Session, ticket.Slot, ticket.Generation, true, presentation.Registry.Diagnostic( ticket ) ) : null;

	/// <summary>Immediate full event. Authored normalized clone coordinates remain raw even if its program later chooses world space.</summary>
	public void FullRefresh( StaffMember member, float x, float y, float z )
	{
		RemoveClone();
		bound = null;
		state = EmployeePortraitState.OpenUnbound;
		Epoch++;
		if ( presentation.IsDisposed || member.BodySprite is not { IsLive: true } source || !ReferenceEquals( source.Presentation, presentation )
			|| !source.TryResolve( out var sourceState ) ) { Diagnostic = StaffSpriteDiagnostic.SourceAbsent; return; }
		bound = source;
		clone = presentation.Registry.Allocate( source.Identity, StaffSpriteOwnerKind.EmployeeClone, handle =>
			StaffSpriteCloneCopy.Create( sourceState, handle, BitConverter.SingleToUInt32Bits( x ), BitConverter.SingleToUInt32Bits( y ),
				BitConverter.SingleToUInt32Bits( z ), BitConverter.SingleToUInt32Bits( 8f ) ) );
		state = EmployeePortraitState.OpenBound;
		Diagnostic = source.Diagnostic;
	}

	public void PartialRefresh()
	{
		if ( state != EmployeePortraitState.OpenBound || !TryResolve( out var destination ) || bound == null ) return;
		if ( bound.TryResolve( out var source ) ) StaffSpriteCloneCopy.Refresh( destination, source );
	}
	private void Removed( StaffSpriteTicket ticket, StaffSpriteRemoval reason )
	{
		if ( clone != ticket ) return;
		clone = null;
		if ( reason is StaffSpriteRemoval.IdentityInvalidated or StaffSpriteRemoval.SessionDisposed ) { bound = null; state = EmployeePortraitState.Closed; }
		else state = EmployeePortraitState.OpenRetired;
		Diagnostic = StaffSpriteDiagnostic.Retired;
	}
	private void RemoveClone()
	{
		if ( clone is not { } ticket ) return;
		clone = null;
		presentation.Registry.Remove( ticket );
	}
	public void Close()
	{
		RemoveClone();
		bound = null;
		state = EmployeePortraitState.Closed;
	}
	public void Dispose()
	{
		Close();
		presentation.Registry.Removed -= Removed;
		presentation.Invalidated -= Close;
	}
}
