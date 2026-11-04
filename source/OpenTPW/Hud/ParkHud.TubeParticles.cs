using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private IAdvisorParticlePool? uiParticlePool;

	/// <summary>
	/// The pool the UI's particles start in: the level's own (the original's UI shares the one global particle pool with the world, <c>0x521E60</c>); a test or a host can name another.
	/// A pool that refuses (no renderer, no particle files) makes the UI's effects nothing.
	/// </summary>
	internal IAdvisorParticlePool UiParticlePool
	{
		get => uiParticlePool ?? ParticleRendererModule.AdvisorPool;
		set => uiParticlePool = value;
	}

	/// <summary>
	/// The native particle position of a UI rectangle's centre: the x as the centre column times 100000 / 2048 and the z as the centre row times 75000 / 1536, the 2048 x 1536 virtual canvas read
	/// as the screen's −1..1 (<see cref="ParticleScreenSpace"/>); y is 0.
	/// </summary>
	// [BIN:TPW-EXE:0x48DE1A tag particle position] the window's rectangle (0x65d42e, absolute) gives the centre (right - left) >> 1 + left and (bottom - top) >> 1 + top; x = (centre * 100000 + ((centre * 100000 >> 31) & 0x7FF)) >> 11 (a truncating division by 2048)
	// and z = high32(centre row * 75000 * 0x2AAAAAAB) >> 8 plus its sign bit (a truncating division by 1536), pushed as the arguments 0x521E60(effect, x, 0, z); 0x486B90 (the first-visibility burst) and 0x48DF88 (the move) compute them alike
	internal static (int X, int Y, int Z) UiParticleNative( UiRect rect )
	{
		var left = (int)MathF.Round( rect.X );
		var top = (int)MathF.Round( rect.Y );
		var width = (int)MathF.Round( rect.Width );
		var height = (int)MathF.Round( rect.Height );
		var centreX = (width >> 1) + left;
		var centreY = (height >> 1) + top;
		var scaled = unchecked( centreX * 100000 );
		var x = unchecked( scaled + ((scaled >> 31) & 0x7FF) ) >> 11;
		var product = (long)(centreY * 75000) * 0x2AAAAAAB;
		var z = (int)(product >> 32) >> 8;
		z += (int)((uint)z >> 31);
		return (x, 0, z);
	}

	private (int X, int Y, int Z) TubeIconNative( int index ) => UiParticleNative( TubeIcon( index ) );

	/// <summary>
	/// A tag window is shown (the add routine's first show): its first-visibility burst starts once at the window's centre, and a retained particle that lives is shown again.
	/// </summary>
	// [BIN:TPW-EXE:0x48DFA6 tag shown] message 0x11 with a shown window: property 10 still 0 requests effect 0x5A at the window's centre through 0x486B90 (which acts only for a shown window, 0x65d773) and sets it to 1 (0x48dfb7-0x48dfca); a retained particle (property 9) that still lives is unhidden (0x51ff10 with 0, 0x48dff6-0x48dffe)
	private void ShowTubeRow( int index )
	{
		var row = pendingChallengeRows![index];
		if ( !row.FirstShown )
		{
			UiParticlePool.Create( TagShownEffect, TubeIconNative( index ) );
			row.FirstShown = true;
		}
		if ( row.ParticleHandle is { } handle && UiParticlePool.IsAlive( handle ) )
			UiParticlePool.SetHidden( handle, false );
	}

	/// <summary>A tag window is hidden: its retained particle, when it lives, is hidden with it.</summary>
	// [BIN:TPW-EXE:0x48E006 tag hidden] message 0x11 with a hidden window hides a live retained particle (0x51ff10 with 1, 0x48e02b-0x48e033)
	private void HideTubeRow( TubeRow row )
	{
		row.Hidden = true;
		if ( row.ParticleHandle is { } handle && UiParticlePool.IsAlive( handle ) )
			UiParticlePool.SetHidden( handle, true );
	}

	/// <summary>
	/// The warning timer fired: a row without a retained particle starts effect 0x5B at its centre and keeps the handle (hidden at once when the row is hidden); the timer then disables itself.
	/// </summary>
	// [BIN:TPW-EXE:0x48DD80 tag warning] timer 0x80081: property 9 at -1 requests effect 0x5B at the window's centre (0x48de1a), stores the handle there, disables the timer (0x48de2e-0x48de35) and hides the new particle when the window is not shown (0x48de3c-0x48de49); a window that already holds a handle leaves the timer enabled (0x48dda8)
	private void WarnTubeRow( TubeRow row )
	{
		if ( row.ParticleHandle != null )
			return;
		var index = pendingChallengeRows!.IndexOf( row );
		var native = TubeIconNative( index );
		row.ParticleHandle = UiParticlePool.Create( TagWarningEffect, native );
		row.ParticleAt = (native.X, native.Z);
		row.WarningFired = true;
		row.Warning?.Disable();
		if ( !row.Drawn && row.ParticleHandle is { } handle )
			UiParticlePool.SetHidden( handle, true );
	}

	/// <summary>Ends a row's retained particle when it still lives (0x5222E0 then 0x51FEB0); the handle stays, as the original's property does.</summary>
	private void EndTubeParticle( TubeRow row )
	{
		if ( row.ParticleHandle is { } handle && UiParticlePool.IsAlive( handle ) )
			UiParticlePool.Kill( handle );
	}

	/// <summary>
	/// A tag window that moves (a row below it left the tube) takes its retained particle along (message 0x13: 0x51fe30 at the new centre).
	/// </summary>
	// [BIN:TPW-EXE:0x48DEEB tag moved] message 0x13: a live retained particle is moved to the window's new centre (0x48df10-0x48df88)
	internal void MovesParticlesForTests() => MoveTubeParticles();

	private void MoveTubeParticles()
	{
		if ( pendingChallengeRows == null )
			return;
		for ( var index = 0; index < pendingChallengeRows.Count; index++ )
		{
			var row = pendingChallengeRows[index];
			if ( row.ParticleHandle is not { } handle )
				continue;
			var native = TubeIconNative( index );
			if ( row.ParticleAt == (native.X, native.Z) || !UiParticlePool.IsAlive( handle ) )
				continue;
			UiParticlePool.Move( handle, native );
			row.ParticleAt = (native.X, native.Z);
		}
	}
}
