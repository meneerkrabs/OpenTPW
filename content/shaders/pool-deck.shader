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
    // [EXT:evening] the ambient share the evening extension leaves (0 keeps the 0.4)
    return max( dot( N, L ), 0.0 ) * g_oUbo.g_vLightColor + vec3( 0.4 ) * ( g_oUbo.g_flAmbientLevel > 0.0 ? g_oUbo.g_flAmbientLevel : 1.0 );
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
    vec3 texel = texture( sampler2D( Color, s_Color ), vs_out.vTexCoords ).rgb;
    vec3 shading = parkLight( vs_out.vNormal );
    // [EXT:point-lights] lights in reach add to the deck's shading like the sun and the ambient
    if ( g_oUbo.g_flLightCount > 0.5 )
      shading += pointLights( vs_out.vPosition );
    fragColor = vec4( applyFog( texel * shading ), 1.0 );
  }
}
