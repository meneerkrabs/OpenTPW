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
  // [EXT:pools-render] The pool's deck, skirt and upper walls (docs/POOLS.md): opaque, lit and fogged exactly as the park's ground and objects are (test.shader), with the texture
  // coordinates in world units so the slabs keep their size on every wall.
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
    vec3 texel = texture( sampler2D( Color, s_Color ), vs_out.vTexCoords ).rgb;
    fragColor = vec4( applyFog( texel * parkLight( vs_out.vNormal ) ), 1.0 );
  }
}
