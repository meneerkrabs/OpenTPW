vertex "content/shaders/includes/3d.vert"

common {
  layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
    mat4 g_mModel;
    mat4 g_mView;
    mat4 g_mProj;

    vec3 g_vLightPos;
    vec3 g_vLightColor;
    vec3 g_vCameraPos;

    float g_flTime;
    float g_flViewDistanceScale;
    vec3 g_vFogColor;
  } g_oUbo;
}

fragment {
  // [EXT:pools-render] The pool water of the opentpw.pools extension (docs/POOLS.md), adapted from water.shader (the lake entity World/Terrain/Water.cs uses it; the park never
  // instantiates that). The texture is our own generated caustic image (World/Pools/PoolTextures.cs): its red channel mixes a deep and a shallow blue, and the surface is
  // translucent. The coordinates come from the position on the ground, so every cell shows the same continuous water.
  //
  // In
  //
  layout( location = 0 ) in struct VS_OUT {
    vec2 vTexCoords;
    vec3 vNormal;
    vec3 vPosition;
    vec3 vPositionWs;
    vec3 vPositionVs;
  } vs_out;

  //
  // Out
  //
  layout( location = 0 ) out vec4 fragColor;

  layout( set = 1, binding = 0 ) uniform texture2D Color;
  layout( set = 1, binding = 1 ) uniform sampler s_Color;

  void main()
  {
    float uvScale = 0.5;
    float waveScale = 2.0 * uvScale;
    float waveFrequency = 0.25;
    float waveAmplitude = 0.02;
    float pi = 3.14159265359;

    vec2 sinTime = ( vs_out.vPosition.yx * waveScale + g_oUbo.g_flTime * waveFrequency ) * pi;
    vec2 uv = ( vs_out.vPosition.xy * uvScale ) + vec2( sin( sinTime.x ), sin( sinTime.y ) ) * waveAmplitude;

    float offsetSpeed = 0.06;
    vec2 offset = vec2( 0, g_oUbo.g_flTime ) * offsetSpeed;

    float caustic = texture( sampler2D( Color, s_Color ), uv + offset ).r;
    vec3 deep = vec3( 0.06, 0.42, 0.62 );
    vec3 shallow = vec3( 0.40, 0.82, 0.92 );
    vec3 water = mix( deep, shallow, caustic );

    // Calculate fog using view space depth, as water.shader does
    float viewSpaceDepth = length( vs_out.vPositionVs );
    float fogFactor = exp( viewSpaceDepth * 0.01 ) * 0.025;
    fogFactor = clamp( fogFactor, 0, 1 );

    fragColor = vec4( mix( water, g_oUbo.g_vFogColor, fogFactor ), 0.85 );
  }
}
