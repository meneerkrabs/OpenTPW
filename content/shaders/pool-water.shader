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

  // [EXT:pools-waves] a plane wave running along +x +y (PoolWaves: one period is 24 park turns, the wave front advances 21/64 of a period per cell, a cell is 2 engine units wide), the
  // same function the swimmers bob on, -1 to 1 times the strength; flat (strength 0) while the machine rests or the pool has none
  const float kWavePi = 3.14159265359;
  const float kWaveNumber = 21.0 / 128.0;
  // the surface's rise at full strength in engine units (PoolWaves.MaxHeave, which the swimmers' bob also reaches): below the 0.12 the water lies under the lowest deck
  const float kMaxHeave = 0.105;

  float poolWaveArgument( vec2 p )
  {
    return 2.0 * kWavePi * ( g_oUbo.g_flWavePhase - ( p.x + p.y ) * kWaveNumber );
  }

  float poolWave( vec2 p )
  {
    return sin( poolWaveArgument( p ) ) * g_oUbo.g_flWaveAmp;
  }

  // the slope of the heaved surface along x (the same along y): the derivative of kMaxHeave * poolWave
  float poolWaveSlope( vec2 p )
  {
    return -cos( poolWaveArgument( p ) ) * 2.0 * kWavePi * kWaveNumber * g_oUbo.g_flWaveAmp * kMaxHeave;
  }
}

vertex {
  // the shared 3d vertex shader (includes/3d.vert) with the water surface raised and lowered by the wave ([EXT:pools-waves]); flat water (strength 0) is left where it was
  layout( location = 0 ) in vec3 position;
  layout( location = 1 ) in vec3 normal;
  layout( location = 2 ) in vec2 texCoords;

  layout( location = 0 ) out struct VS_OUT {
    vec2 vTexCoords;
    vec3 vNormal;
    vec3 vPosition;
    vec3 vPositionWs;
    vec3 vPositionVs;
  } vs_out;

  void main()
  {
    vec4 pos = g_oUbo.g_mModel * vec4( position, 1.0 );
    pos.z += poolWave( pos.xy ) * kMaxHeave;

    vs_out.vTexCoords = texCoords;
    vs_out.vNormal = normal;
    vs_out.vPosition = pos.xyz;
    vs_out.vPositionVs = vec3( g_oUbo.g_mView * pos );
    vs_out.vPositionWs = pos.xyz;
    gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * pos;
  }
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

  // [BIN:TPW-EXE:0x567AD0 fog] the original's fog (docs/UI.md, UI-107): linear in the projection's depth between a start and an end (SetFog's FOGTABLEMODE linear); g_flViewDistanceScale carries the range as
  // start x 8 x 4096 + end x 8 in 1/8 world units of view depth (RenderFog.PackedRange), 0 for no fog
  float fogAmount( float packedRange )
  {
    if ( packedRange <= 0.0 )
      return 0.0;
    float startQ = floor( packedRange / 4096.0 );
    float fogStart = startQ * 0.125;
    float fogEnd = ( packedRange - startQ * 4096.0 ) * 0.125;
    float viewDepth = 1.0 / gl_FragCoord.w;
    return clamp( ( viewDepth - fogStart ) / max( fogEnd - fogStart, 0.001 ), 0.0, 1.0 );
  }

  vec3 applyFog( vec3 colour )
  {
    return mix( colour, g_oUbo.g_vFogColor, fogAmount( g_oUbo.g_flViewDistanceScale ) );
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
    float pi = kWavePi;
    // [EXT:pools-waves] the wave the vertex shader raised the surface by, here per pixel for its shading
    float wave = poolWave( p );
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
    // [EXT:pools-waves] the crests of a wave are lighter and the troughs darker
    colour = mix( colour, shallow, clamp( wave * 0.55, 0.0, 0.6 ) ) * ( 1.0 + min( wave, 0.0 ) * 0.35 );
    // the slopes of the heaved surface catch a fixed light from over the wave's back: the faces turned to it lighter, the far faces darker, and a glint where it reflects to the camera.
    // The normal is steepened for the shading only (the rise itself is a tenth of a unit), and flat water (slope 0, strength 0) keeps its colour.
    float slope = poolWaveSlope( p ) * 4.0;
    vec3 waveNormal = normalize( vec3( -slope, -slope, 1.0 ) );
    vec3 waveLight = normalize( vec3( -0.45, -0.45, 0.77 ) );
    colour *= 1.0 + dot( waveNormal.xy, waveLight.xy ) * 1.2;
    vec3 toCamera = normalize( g_oUbo.g_vCameraPos - vs_out.vPositionWs );
    float glint = pow( max( dot( reflect( -waveLight, waveNormal ), toCamera ), 0.0 ), 24.0 ) * clamp( g_oUbo.g_flWaveAmp * 3.0, 0.0, 1.0 );
    colour += vec3( glint * 0.45 );
    // a band of white water rides each crest, broken up by the caustic texture
    float crest = g_oUbo.g_flWaveAmp > 0.001 ? smoothstep( 0.55, 0.95, wave / g_oUbo.g_flWaveAmp ) * g_oUbo.g_flWaveAmp : 0.0;
    float foam = crest * clamp( 0.45 + layerA * 0.9, 0.0, 1.0 );
    colour = mix( colour, vec3( 0.93, 0.97, 1.0 ), clamp( foam * 0.75, 0.0, 0.75 ) );
    float alpha = clamp( mix( 0.72, 0.42, edge ) - wave * 0.12 + foam * 0.35 + glint * 0.2, 0.0, 0.95 );
    fragColor = vec4( applyFog( colour ), alpha );
  }
}
