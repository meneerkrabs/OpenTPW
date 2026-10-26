vertex "content/shaders/includes/3d.vert"

common {
  layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
    mat4 g_mModel;
    mat4 g_mView;
    mat4 g_mProj;

    vec3 g_vLightPos;
    float g_flAmbientLevel; // [EXT:evening] the share of the original 0.4 ambient that is left (0 = unchanged: the extension is off or it is day); the padding after the sun position
    vec3 g_vLightColor;
    // [EXT:point-lights] the number of lights after the block (the padding after the light colour)
    float g_flLightCount;
    vec3 g_vCameraPos;

    float g_flTime;
    float g_flViewDistanceScale;
    // the pool's deep water colour (the free padding of the engine's uniform buffer)
    float g_flDeepR;
    float g_flDeepG;
    float g_flDeepB;
    vec3 g_vFogColor;
    float g_flGlow;
    // [EXT:point-lights] 16 lights x 2 vec4, from byte 272; only the lights of a draw are uploaded
    vec4 g_vPointLight[32];
  } g_oUbo;
}

fragment {
  // [EXT:pools-render] The pool floor and the walls below the water line (docs/POOLS.md): the tiles darkened and tinted toward the deep water colour by depth (the first texture
  // coordinate, 0 at the water line and 1 at the floor), with moving caustic light on the floor. Opaque; the translucent water is drawn over it.
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

  // [EXT:point-lights] The park's point lights (docs/LIGHTS.md): what the lit uniform block carries after g_vFogColor, g_flLightCount lights of two vec4 each (the position and the squared
  // radius; the colour and one over the squared radius). The original engine's falloff, (r2 - d2) x (1 / r2) added to the lit colour with no normal term, full at the centre and nothing
  // at the radius (tp.exe 0x5741B0). The world position is the model-space position the shader gets from g_mModel. With no light (count 0) nothing is added, so such a draw shades as before.
  vec3 pointLights( vec3 positionWs )
  {
    vec3 sum = vec3( 0.0 );
    int count = min( int( g_oUbo.g_flLightCount ), 16 );
    for ( int i = 0; i < count; i++ )
    {
      vec4 a = g_oUbo.g_vPointLight[i * 2];
      vec4 b = g_oUbo.g_vPointLight[i * 2 + 1];
      vec3 d = positionWs - a.xyz;
      float share = ( a.w - dot( d, d ) ) * b.w;
      sum += b.xyz * max( share, 0.0 );
    }
    return sum;
  }

  void main()
  {
    vec3 position = vs_out.vPosition;
    bool isFloor = abs( vs_out.vNormal.z ) > 0.5;
    // the tile texture from the world position: the floor along the ground, a wall along its length and up
    vec2 uv = isFloor ? position.xy : vec2( position.x + position.y, position.z );
    vec3 tile = texture( sampler2D( Color, s_Color ), uv ).rgb;
    float depth = clamp( vs_out.vTexCoords.x, 0.0, 1.0 );
    vec3 deep = vec3( g_oUbo.g_flDeepR, g_oUbo.g_flDeepG, g_oUbo.g_flDeepB );
    float t = g_oUbo.g_flTime;
    float caustic = pow( abs( sin( position.x * 2.6 + t * 0.9 ) * sin( position.y * 2.3 - t * 0.7 ) + sin( ( position.x + position.y ) * 1.7 + t * 0.5 ) * 0.5 ), 3.0 );
    vec3 shading = vec3( 0.75 + 0.35 * caustic * ( 1.0 - depth * 0.5 ) );
    // [EXT:evening] the basin's own flat light follows the ambient the evening extension leaves (0 keeps it as it was)
    shading *= ( g_oUbo.g_flAmbientLevel > 0.0 ? g_oUbo.g_flAmbientLevel : 1.0 );
    // [EXT:point-lights] lights in reach (a lamp over or in the pool) add to the tiles' shading, under the water tint
    if ( g_oUbo.g_flLightCount > 0.5 )
      shading += pointLights( position );
    vec3 lit = tile * shading;
    vec3 colour = mix( lit, deep * 0.55, depth * 0.62 );
    colour *= mix( 1.0, 0.45, depth );
    colour += g_oUbo.g_flGlow * deep * 0.25 * ( 1.0 - depth * 0.4 );
    fragColor = vec4( applyFog( colour ), 1.0 );
  }
}
