vertex {
    //
    // Layout: the particle quads reuse the shared vertex format (docs/PARTICLES.md). The normal slot carries the
    // colour: x is red (0..255), y is green, z is blue + 256 * alpha, all whole numbers, so one float attribute
    // holds an 8-bit RGBA colour without an integer attribute.
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
        vec4 vColor;
    } vs_out;

    void main() {
        float alpha = floor( tint.z / 256.0 );
        float blue = tint.z - alpha * 256.0;
        vs_out.vTexCoords = texCoords;
        vs_out.vColor = vec4( tint.x, tint.y, blue, alpha ) / 255.0;
        // [EXT:evening] the sky is tinted at dusk and night through the unused light colour; every other particle draw leaves it zero, which keeps the colour as it was
        if ( g_oUbo.g_vLightColor.x + g_oUbo.g_vLightColor.y + g_oUbo.g_vLightColor.z > 0.0 )
            vs_out.vColor.rgb *= g_oUbo.g_vLightColor;
        gl_Position = g_oUbo.g_mProj * g_oUbo.g_mView * vec4( position, 1.0 );
    }
}

fragment {
    layout( location = 0 ) in struct VS_OUT {
        vec2 vTexCoords;
        vec4 vColor;
    } vs_out;

    layout( location = 0 ) out vec4 fragColor;

    layout( set = 1, binding = 0 ) uniform texture2D Color;
    layout( set = 1, binding = 1 ) uniform sampler s_Color;

    // Camera-facing particles: the sprite frame modulated by the particle's colour and alpha. The material picks the
    // blend (alpha, source-alpha-over-one, or one-plus-one).
    void main()
    {
        vec4 vSprite = texture( sampler2D( Color, s_Color ), vs_out.vTexCoords );
        fragColor = vSprite * vs_out.vColor;
    }
}
