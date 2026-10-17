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
    // the pool's deep water colour (the free padding of the engine's uniform buffer)
    float g_flDeepR;
    float g_flDeepG;
    float g_flDeepB;
    vec3 g_vFogColor;
    float g_flGlow;
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
    vec3 position = vs_out.vPosition;
    bool isFloor = abs( vs_out.vNormal.z ) > 0.5;
    // the tile texture from the world position: the floor along the ground, a wall along its length and up
    vec2 uv = isFloor ? position.xy : vec2( position.x + position.y, position.z );
    vec3 tile = texture( sampler2D( Color, s_Color ), uv ).rgb;
    float depth = clamp( vs_out.vTexCoords.x, 0.0, 1.0 );
    vec3 deep = vec3( g_oUbo.g_flDeepR, g_oUbo.g_flDeepG, g_oUbo.g_flDeepB );
    float t = g_oUbo.g_flTime;
    float caustic = pow( abs( sin( position.x * 2.6 + t * 0.9 ) * sin( position.y * 2.3 - t * 0.7 ) + sin( ( position.x + position.y ) * 1.7 + t * 0.5 ) * 0.5 ), 3.0 );
    vec3 lit = tile * ( 0.75 + 0.35 * caustic * ( 1.0 - depth * 0.5 ) );
    vec3 colour = mix( lit, deep * 0.55, depth * 0.62 );
    colour *= mix( 1.0, 0.45, depth );
    colour += g_oUbo.g_flGlow * deep * 0.25 * ( 1.0 - depth * 0.4 );
    fragColor = vec4( applyFog( colour ), 1.0 );
  }
}
