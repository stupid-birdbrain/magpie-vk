#version 450

// jacked from shadertoy

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vTexCoord;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uTexture;

vec2 hash22(vec2 p) {
    // Pseudo-random generator based on iq's hash.
    const vec2 k1 = vec2(127.1, 311.7);
    const vec2 k2 = vec2(269.5, 183.3);
    vec2 s = vec2(dot(p, k1), dot(p, k2));
    return -1.0 + 2.0 * fract(sin(s) * 43758.5453123);
}

void main() {
    vec4 color = texture(uTexture, vTexCoord);
    vec2 uv = vTexCoord * 12.0;

    float luminance = dot(color.rgb, vec3(0.299, 0.587, 0.114));
    vec3 grayscale = vec3(luminance) * hash22(uv).xyx;

    outColor = vec4(grayscale, color.a) * vColor;
}
