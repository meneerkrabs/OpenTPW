vertex "content/shaders/includes/3d.vert"

common {
  layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
    mat4 g_mModel;
    mat4 g_mView;
    mat4 g_mProj;

    vec3 g_vLightPos;
    // the wave machine's strength (0 flat water to 1 the highest swell); the free padding after the light position
    float g_flWaveAmp;
    vec3 g_vLightColor;
    // the wave's phase in cycles at the grid origin (the free padding after the light colour)
    float g_flWavePhase;
    vec3 g_vCameraPos;

    float g_flTime;
    float g_flViewDistanceScale;
    // the pool's deep water colour (the free padding of the engine's uniform buffer)
    float g_flDeepR;
    float g_flDeepG;
    float g_flDeepB;
    vec3 g_vFogColor;
    float g_flGlow;
  } g_oUbo;
}

fragment {
  // [EXT:pools-render] The pool water (docs/POOLS.md), adapted from water.shader (which the lake entity World/Terrain/Water.cs uses and the park never instantiates): two layers of
  // our own caustic texture at different scales scrolling different ways, so the repeat does not show; lighter and more translucent near the edge (the first texture coordinate is the
  // edge proximity, 1 at the wall), darker and more opaque in the middle; an optional glow. The shallow colour is in the uniform buffer's sun position, the deep colour in its padding.
  layout( location = 0 ) in struct VS_OUT {
    vec2 vTexCoords;
    vec3 vNormal;
    vec3 vPosition;
    vec3 vPositionWs;
    vec3 vPositionVs;
  } vs_out;

  layout( location = 0 ) out vec4 fragColor;

  layout( set = 1, binding = 0 ) uniform texture2D Color;
  layout( set = 1, binding = 1 ) uniform sampler s_Color;

  // the park's own fog (test.shader): exponential in view depth, stretched by the graphics settings' view distance
  vec3 applyFog( vec3 colour )
  {
    float viewDistanceScale = g_oUbo.g_flViewDistanceScale > 0.0 ? g_oUbo.g_flViewDistanceScale : 1.0;
    float viewSpaceDepth = length( vs_out.vPositionVs ) / viewDistanceScale;
    float fogFactor = clamp( exp( viewSpaceDepth * 0.01 ) * 0.025, 0.0, 1.0 );
    return mix( colour, g_oUbo.g_vFogColor, fogFactor );
  }

  // the park's own lighting (test.shader): the sun's diffuse plus 0.4 ambient
  vec3 parkLight( vec3 normal )
  {
    vec3 N = normalize( normal );
    vec3 L = normalize( g_oUbo.g_vLightPos - vs_out.vPositionVs );
    return max( dot( N, L ), 0.0 ) * g_oUbo.g_vLightColor + vec3( 0.4 );
  }

  void main()
  {
    vec2 p = vs_out.vPosition.xy;
    float t = g_oUbo.g_flTime;
    float pi = 3.14159265359;
    // [EXT:pools-waves] a plane wave running along +x +y (PoolWaves: one period is 24 park turns, the wave front advances 21/64 of a period per cell, a cell is 2 engine units wide),
    // the same function the swimmers bob on; flat (amplitude 0) while the machine rests or the pool has none
    float wave = sin( 2.0 * pi * ( g_oUbo.g_flWavePhase - ( p.x + p.y ) * ( 21.0 / 128.0 ) ) ) * g_oUbo.g_flWaveAmp;
    // [EXT:pools-river] the current of a lazy river cell rides in the x and y of the normal (zero elsewhere): the surface slides along it
    vec2 flow = vs_out.vNormal.xy;
    vec2 drift = flow * t * 0.45;
    vec2 wobble = vec2( sin( ( p.y * 0.9 + t * 0.35 ) * pi ), sin( ( p.x * 0.9 + t * 0.30 ) * pi ) ) * 0.02 + vec2( wave ) * 0.05;
    float layerA = texture( sampler2D( Color, s_Color ), p * 0.5 + wobble + vec2( 0.0, t * 0.05 ) - drift ).r;
    float layerB = texture( sampler2D( Color, s_Color ), p * 0.17 + vec2( 0.31, 0.57 ) + vec2( t * 0.012, -t * 0.02 ) - drift * 0.4 ).r;
    float caustic = clamp( layerA * layerB * 2.4, 0.0, 1.0 );

    float edge = clamp( vs_out.vTexCoords.x, 0.0, 1.0 );
    vec3 deep = vec3( g_oUbo.g_flDeepR, g_oUbo.g_flDeepG, g_oUbo.g_flDeepB );
    vec3 shallow = g_oUbo.g_vLightPos;
    vec3 colour = mix( deep, shallow, clamp( 0.18 + edge * 0.6 + caustic * 0.32, 0.0, 1.0 ) );
    colour += g_oUbo.g_flGlow * shallow * ( 0.3 + caustic * 0.5 );
    // [EXT:pools-river] a lazy river's lane is lighter and streaked: stripes across the flow slide along it, so the current can be seen
    float lane = step( 0.5, length( flow ) );
    float streak = 0.5 + 0.5 * sin( 2.0 * pi * ( dot( p, flow ) * 0.8 - t * 0.7 ) );
    colour = mix( colour, shallow, lane * ( 0.14 + 0.26 * streak ) );
    // the crests of a wave are lighter and the troughs darker
    colour = mix( colour, shallow, clamp( wave * 0.55, 0.0, 0.6 ) ) * ( 1.0 + min( wave, 0.0 ) * 0.35 );
    float alpha = mix( 0.72, 0.42, edge ) - wave * 0.12;
    fragColor = vec4( applyFog( colour ), alpha );
  }
}
