#version 450

// jacked from shadertoy temporarily

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

float voronoi(vec2 uv) {
    vec2 cell = floor(uv);
    vec2 local = fract(uv);
    float minDist = 8.0;

    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            vec2 offset = vec2(x, y);
            vec2 point = hash22(cell + offset) + offset;
            vec2 diff = point - local;
            float distanceSq = dot(diff, diff);
            minDist = min(minDist, distanceSq);
        }
    }

    return sqrt(minDist);
}

void main() {
    vec4 texColor = texture(uTexture, vTexCoord);

    vec2 uv = vTexCoord * 12.0;
    float cellDistance = voronoi(uv);
    float edge = clamp(1.0 - cellDistance * 3.0, 0.0, 1.0);

    vec3 stylized = mix(texColor.rgb, vec3(edge), 0.65);
    vec4 color = vec4(stylized, 1.0) * vColor;
    color.a = 1.0;
    outColor = color;
}
