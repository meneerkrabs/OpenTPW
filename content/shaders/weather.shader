vertex {
    //
    // Layout: the weather quads reuse the shared vertex format; the normal slot carries the grey level in x, the
    // alpha in y and the clip w in z (content/shaders/weather.shader, docs/AUDIO.md "Weather visuals").
    //
    layout( location = 0 ) in vec3 position;
    layout( location = 1 ) in vec3 tint;
    layout( location = 2 ) in vec2 texCoords;

    layout( set = 0, binding = 0 ) uniform ObjectUniformBuffer {
        mat4 g_mModel;
        mat4 g_mView;
        mat4 g_mProj;
        vec3 g_vLightPos;
        vec3 g_vLightColor;
        vec3 g_vCameraPos;
        float g_flTime;
    } g_oUbo;

    layout( location = 0 ) out struct VS_OUT {
        vec2 vTexCoords;
        float vGrey;
        float vAlpha;
    } vs_out;

    void main() {
        vs_out.vTexCoords = texCoords;
        vs_out.vGrey = tint.x;
        vs_out.vAlpha = tint.y;
        // clip-space vertex: position is (x w, y w, z w) and the tint's third component is w (WeatherProjection), the original's rhw interpolation
        gl_Position = vec4( position, tint.z );
    }
}

fragment {
    layout( location = 0 ) in struct VS_OUT {
        vec2 vTexCoords;
        float vGrey;
        float vAlpha;
    } vs_out;

    layout( location = 0 ) out vec4 fragColor;

    layout( set = 1, binding = 0 ) uniform texture2D Color;
    layout( set = 1, binding = 1 ) uniform sampler s_Color;

    // Textured camera-facing particles (rain streaks, snow flakes, the bolt's ribbon): the image modulated by the
    // vertex's grey level, its alpha by the vertex's alpha. The material picks the blend (source alpha over one for
    // the rain and the bolts, alpha for the snow). All three states of the original (0x200D52, 0x556) carry the alpha
    // test 0x400 with the low reference 0x10 and ALPHAFUNC greater-or-equal (tp.exe 0x567620, 0x566840).
    void main()
    {
        vec4 vColor = texture( sampler2D( Color, s_Color ), vs_out.vTexCoords );
        float alpha = vColor.a * vs_out.vAlpha;
        if ( alpha < 16.0 / 255.0 )
            discard;
        fragColor = vec4( vColor.rgb * vs_out.vGrey, alpha );
    }
}
