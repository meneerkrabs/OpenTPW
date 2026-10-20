vertex {

layout(location = 0) in vec3 vPosition;
layout(location = 1) in vec2 vTexCoords;

layout(location = 0) out vec2 vs_texCoords;

void main() {
    // Positions are already in normalized device coordinates, with the depth in 0..1 (smaller is nearer)
    gl_Position = vec4(vPosition, 1.0);
    vs_texCoords = vTexCoords;
}

}

fragment {

layout(set = 0, binding = 0) uniform texture2D Image;
layout(set = 0, binding = 1) uniform sampler s_Image;

layout(location = 0) in vec2 fs_texCoords;
layout(location = 0) out vec4 fragColor;

void main() {
    // Original images, pink keyed out on upload: a keyed texel is not drawn and writes no depth
    vec4 colour = texture(sampler2D(Image, s_Image), fs_texCoords);
    if (colour.a < 0.5)
        discard;
    fragColor = vec4(colour.rgb, 1.0);
}

}
