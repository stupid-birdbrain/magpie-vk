#version 450
#extension GL_ARB_separate_shader_objects : enable

#include "../common.glsl" 

layout (binding = 0) uniform sampler2D texSampler;

layout (location = 0) in vec3 inColor;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in float fragViewZ;

layout (location = 0) out vec4 fragColor;

layout(push_constant) uniform PushConstants {
    mat4 model;
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
        fragColor = vec4(0, 0, 0 ,0);
        return;
    }

    float currentDistance = -fragViewZ; 
    float ditherFactor = smoothstep(0.8f, 0.05f, currentDistance);

    vec2 screenPos = gl_FragCoord.xy;
    float ditherValue = bayer4x4(screenPos);

    float effectiveAlphaThreshold = texColor.a * (1.0 - ditherFactor) * PI;

    if (ditherValue > effectiveAlphaThreshold) {
        fragColor = vec4(0, 0, 0 ,0);
        return;
    }

    fragColor = texColor * vec4(inColor, 1.0);
}