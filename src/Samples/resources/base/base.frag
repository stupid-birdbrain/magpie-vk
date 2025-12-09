#version 450
#extension GL_ARB_separate_shader_objects : enable

#include "../common.glsl"

layout (binding = 0) uniform sampler2D texSampler;

layout (location = 0) in vec3 inColor;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in float fragViewZ;

layout (location = 0) out vec4 fragColor;

layout(push_constant) uniform PushConstants {
    mat4 view;
    mat4 proj;
} pc;

float bayer4x4(vec2 p) {
    int x = int(mod(p.x, 4.0));
    int y = int(mod(p.y, 4.0));
    return BAYER_MATRIX[y * 4 + x] / 16.0;
}

void main() {
    vec4 texColor = texture(texSampler, inTexCoord);

    if (texColor.a < 0.05) {
        discard;
    }

    vec4 baseFragmentColor = texColor * vec4(inColor, 1.0);

    float currentDistance = -fragViewZ;
    
    const float fogStartDistance = 5.0f;
    const float fogEndDistance = 9.0f;
    const vec4 fogColor = vec4(0.f);

    float fogFactor = clamp((fogEndDistance - currentDistance) / (fogEndDistance - fogStartDistance), 0.0, 1.0);

    vec4 foggedColor = mix(fogColor, baseFragmentColor, fogFactor);


    const float minDitherDistance = 0.05f;
    const float maxDitherDistance = 0.8f;
    float ditherFactor = smoothstep(maxDitherDistance, minDitherDistance, currentDistance);

    vec2 screenPos = gl_FragCoord.xy;
    float ditherValue = bayer4x4(screenPos);

    float effectiveAlphaThreshold = texColor.a * (1.0 - ditherFactor);

    if (ditherValue > effectiveAlphaThreshold) {
        discard;
    }

    fragColor = foggedColor;
}