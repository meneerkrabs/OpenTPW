namespace OpenTPW;

/// <summary>
/// The colour pulse of a queue's dead end (<c>quedead</c>): tp.exe fills the vertex colours of every part of the model with one colour each frame, red full and green and blue rising and falling between
/// 0 and 255 once a second, and the lighting pass leaves those parts alone, so the dead end blinks between white and red whatever the sun does. The phase is the original's single float, advanced by the scene
/// clock's frame gap.
/// </summary>
// [BIN:TPW-EXE:0x00522900 queue model table] the loader's table (rows of 12 bytes from 0x763388) gives the rows of the two quedead entries (indices 0 and 1 of QueueModels.Names) the flag word 0x100, so the load flags are 0x112423 for them and 0x12423 for the others
// [BIN:TPW-EXE:0x004629D0 loader flags] raw flag 0x100000 becomes the inner flag 0x2000 (0x461F10 then sets 0x2000000 in the template's flags, 0x462230), the creator copies the template's flags into the instance (0x4557C0, the 0x3F-dword copy) and 0x463060 registers an instance with 0x2000000 through 0x469580 (test 0x4638E1, call 0x4638EB)
// [BIN:TPW-EXE:0x00469580 colour list] the registration links the instance into the list 0x7AFE14, sets 0x1000000 in every part's flags (0x4695D7, 0x46962D) and, for parts that share their colour buffer with their parent, gives them one of their own; the lighting pass 0x57AA10 skips a part with 0x1000000 (test 0x57AA30, 0x57AA66), so the colours stay what the pulse writes
// [BIN:TPW-EXE:0x00469840 pulse] once per frame from the main loop (0x54E301): the colour is 0xFF000000 | 0xFF0000 | t << 8 | t with t = ftol(255 * (x <= 1 ? x : 2 - x)) from the phase x = [0x7AFD04] (0 at the start, written only here), written into every vertex-colour array of every listed instance (rep stosd, 0x4698D3); then x = fmod(x + 2 * dt, 2) with dt = [0x7B497C], the scene clock's gap times 0.001 (0x473440 at 0x473470); all in single precision (the thread's x87 runs at the 24-bit control word)
internal sealed class QueueModelPulse
{
	/// <summary>The queue model indices (<see cref="QueueModels.Names"/>) the loader flags 0x100000: the two <c>quedead</c> entries.</summary>
	public static bool Pulses( int modelKey ) => modelKey is 0 or 1;

	/// <summary>The phase, 0 up to (not including) 2 (<c>[0x7AFD04]</c>).</summary>
	public float Phase { get; private set; }

	/// <summary>The green and blue level of the current frame's colour, 0..255: the colour the pass writes, from the phase before it moves on.</summary>
	public int Level { get; private set; }

	/// <summary>The level as the unit the shader multiplies the green and blue channels by (<see cref="ModelEntity.PulseTint"/>).</summary>
	public float Tint => Level / 255f;

	/// <summary>One frame of the pass: the colour from the phase, then the phase advanced by the gap of the scene clock (<see cref="SceneClock.FrameMilliseconds"/>).</summary>
	public void Step( uint frameMilliseconds )
	{
		var folded = Phase <= 1f ? Phase : 2f - Phase;
		Level = (int)(folded * 255f);
		var gap = (float)frameMilliseconds * 0.001f;
		Phase = (Phase + 2f * gap) % 2f;
	}
}
