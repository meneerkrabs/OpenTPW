using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenTPW;

[StructLayout( LayoutKind.Sequential )]
struct ObjectUniformBuffer
{
	/*
	 * These fields are padded so that they're
	 * aligned (as blocks) to multiples of 16.
	 */

	public Matrix4x4 g_mModel; // 64
	public Matrix4x4 g_mView; // 64
	public Matrix4x4 g_mProj; // 64

	public Vector3 g_vLightPos; // 12
	public float _padding0; // 4

	public Vector3 g_vLightColor; // 12
	public float _padding1; // 4 (g_flLightCount in the lit shaders: the number of point lights that follow the block, see PointLights.cs)

	public Vector3 g_vCameraPos; // 12
	public float g_flTime; // 4

	public float _padding2; // 4 (g_flViewDistanceScale)
	public float _padding3; // 4
	public float _padding4; // 4
	public float _padding5; // 4

	public Vector3 g_vFogColor; // 12
	public float _padding6; // 4

	/// <summary>
	/// [EXT:point-lights] How many point lights follow this block in the uploaded bytes (<see cref="LitObjectUniformBuffer"/>), in the padding after the light colour that
	/// test.shader, pool-deck.shader and pool-basin.shader name <c>g_flLightCount</c>. 0 for every draw without lights, which is what the zeroed padding always meant.
	/// </summary>
	public float LightCount
	{
		readonly get => _padding1;
		set => _padding1 = value;
	}

	/// <summary>
	/// [EXT:winter-snow-surfaces] The settled snow cover of this draw in test.shader (<c>g_flSnowCover</c>, the padding after the fog colour): 0 means none, which is what the zeroed padding always meant;
	/// a negative value is slush. The pool shaders keep this slot for their glow and do not read it as snow.
	/// </summary>
	public float SnowCover
	{
		readonly get => _padding6;
		set => _padding6 = value;
	}

	/// <summary>
	/// [EXT:evening] The share of the original's 0.4 ambient light the lit shaders keep (<c>g_flAmbientLevel</c>, the padding after the sun position): 0 means unchanged, which is what the
	/// zeroed padding always meant. The pool water's shader keeps this slot for its wave strength and does not read it as an ambient level.
	/// </summary>
	public float AmbientLevel
	{
		readonly get => _padding0;
		set => _padding0 = value;
	}

	/// <summary>
	/// [EXT:evening] The tint the evening extension gives a pre-rendered sprite at dusk and night, through the light colour the sprite shader never used (all zero: no tint). At full day the buffer
	/// is returned as it was.
	/// </summary>
	public ObjectUniformBuffer WithSpriteTint( in EveningLook look )
	{
		if ( !look.IsDay )
			g_vLightColor = look.SpriteTint;
		return this;
	}

	/// <summary>The buffer with the engine fog colour (<see cref="RenderFog"/>), which every fogged shader (test, water, sprite, 3d) blends distant pixels into.</summary>
	public ObjectUniformBuffer WithFog()
	{
		g_vFogColor = RenderFog.WorldColour;
		return this;
	}
}
