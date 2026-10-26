vertex "content/shaders/includes/3d.vert"

common {
  layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
    mat4 g_mModel;
    mat4 g_mView;
    mat4 g_mProj;

    vec3 g_vLightPos;
    vec3 g_vLightColor;
    vec3 g_vCameraPos;

    float g_fTime;
    float g_flViewDistanceScale;
    vec3 g_vFogColor;
  } g_oUbo;
}

fragment {
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
    float uvScale = 0.05;
    float waveScale = 2.0 * uvScale;
    float waveFrequency = 0.25;
    float waveAmplitude = 0.025;
    float pi = 3.14159265359;

    vec2 sinTime = (vs_out.vPosition.yx * waveScale + g_oUbo.g_fTime * waveFrequency) * pi;
    vec2 uv = (vs_out.vPosition.xy * uvScale) + vec2(sin(sinTime.x), sin(sinTime.y)) * waveAmplitude;

    float offsetSpeed = 0.25;
    vec2 offset = vec2( 0, g_oUbo.g_fTime ) * offsetSpeed;

    vec4 vColor = texture( sampler2D( Color, s_Color ), uv + offset );
    vColor.xyz *= 0.4;
    fragColor = vColor;

    // the original's linear fog in the projection's depth (UI-107, RenderFog): g_flViewDistanceScale carries the range (start x 8 x 4096 + end x 8, 1/8 view units; 0 = no fog)
    float fogFactor = 0.0;
    if ( g_oUbo.g_flViewDistanceScale > 0.0 )
    {
      float startQ = floor( g_oUbo.g_flViewDistanceScale / 4096.0 );
      float fogStart = startQ * 0.125;
      float fogEnd = ( g_oUbo.g_flViewDistanceScale - startQ * 4096.0 ) * 0.125;
      fogFactor = clamp( ( 1.0 / gl_FragCoord.w - fogStart ) / max( fogEnd - fogStart, 0.001 ), 0.0, 1.0 );
    }

    // Mix with fog
    fragColor.xyz = mix(fragColor.xyz, g_oUbo.g_vFogColor, fogFactor);
  }
}