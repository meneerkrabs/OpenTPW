vertex {
    layout(location = 0) in vec3 position;
    layout(location = 1) in vec3 normal;
    layout(location = 2) in vec2 texCoords;
    layout(location = 3) in int texIndex;
    layout(location = 4) in uint matFlags;

    layout(set = 0, binding = 0) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        vec3 g_vLightColor;
        vec3 g_vCameraPos;
        float g_flTime;
    } g_oUbo;

    layout(location = 0) out VS_OUT {
        vec2 vTexCoords;
        vec3 vViewPosition;
    } vs_out;

    layout(location = 5) out flat int outTexIndex;

    void main() {
        vec4 pos = g_oUbo.g_mModel * vec4(position, 1.0);
        vs_out.vTexCoords = texCoords;
        vs_out.vViewPosition = vec3(g_oUbo.g_mView * pos);
        gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * pos;
        outTexIndex = texIndex;
    }
}

fragment {
    layout(location = 0) in VS_OUT {
        vec2 vTexCoords;
        vec3 vViewPosition;
    } vs_out;

    layout(location = 5) in flat int texIndex;

    layout(location = 0) out vec4 fragColor;

    layout( set = 1, binding = 0 ) uniform texture2D Color0;
    layout( set = 1, binding = 1 ) uniform texture2D Color1;
    layout( set = 1, binding = 2 ) uniform texture2D Color2;
    layout( set = 1, binding = 3 ) uniform texture2D Color3;
    layout( set = 1, binding = 4 ) uniform texture2D Color4;
    layout( set = 1, binding = 5 ) uniform texture2D Color5;
    layout( set = 1, binding = 6 ) uniform texture2D Color6;
    layout( set = 1, binding = 7 ) uniform texture2D Color7;
    layout( set = 1, binding = 8 ) uniform sampler s_Color;

    // Pre-rendered original kid sprites: no lighting, alpha-tested so the depth buffer sorts them.
    void main()
    {
        vec2 uv = vs_out.vTexCoords;
        vec4 color = vec4(1, 0, 1, 1);
        if ( texIndex == 0 ) color = texture( sampler2D( Color0, s_Color ), uv );
        if ( texIndex == 1 ) color = texture( sampler2D( Color1, s_Color ), uv );
        if ( texIndex == 2 ) color = texture( sampler2D( Color2, s_Color ), uv );
        if ( texIndex == 3 ) color = texture( sampler2D( Color3, s_Color ), uv );
        if ( texIndex == 4 ) color = texture( sampler2D( Color4, s_Color ), uv );
        if ( texIndex == 5 ) color = texture( sampler2D( Color5, s_Color ), uv );
        if ( texIndex == 6 ) color = texture( sampler2D( Color6, s_Color ), uv );
        if ( texIndex == 7 ) color = texture( sampler2D( Color7, s_Color ), uv );
        if ( color.a < 0.5 ) discard;

        float viewSpaceDepth = length( vs_out.vViewPosition );
        float fogFactor = clamp( exp( viewSpaceDepth * 0.01 ) * 0.025, 0, 1 );
        fragColor = vec4( mix( color.rgb, vec3( 0.301, 0.84, 1 ), fogFactor ), 1.0 );
    }
}
