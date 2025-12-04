#version 450
#extension GL_ARB_separate_shader_objects : enable

layout (binding = 0) uniform sampler2D texSampler;
layout (binding = 1) uniform sampler2D texSampler2;
layout (binding = 2) uniform sampler2D texSampler3;

layout (location = 0) in vec3 inColor;
layout (location = 1) in vec2 inTexCoord;
layout (location = 2) in float fragViewZ;

layout (location = 0) out vec4 fragColor;

layout(binding = 0, set = 0, rgba8) uniform image2D myStorageImage;

layout(binding = 1, set = 0, std430) buffer MyStorageBuffer {
    vec4 someData[10];
    uint elementCount;
    float threshold;
} mySsbo;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 view;
    mat4 proj;
    vec4 asd;
} pc;

layout(binding = 0) uniform UniformBufferObject {
    mat4 model;
    vec4 model2;
} ubo;

layout(binding = 0) uniform UniformBufferObject2 {
    mat4 model;
} ubo2;

layout(binding = 0) uniform UniformBufferObject3 {
    mat4 model;
} ubo3;

float bayer4x4(vec2 p) {
    const float BAYER_MATRIX[16] = float[](
        0.0,  8.0,  2.0, 10.0,
        12.0, 4.0, 14.0, 6.0,
        3.0, 11.0, 1.0,  9.0,
        15.0, 7.0, 13.0, 5.0
    );

    int x = int(mod(p.x, 4.0));
    int y = int(mod(p.y, 4.0));

    return BAYER_MATRIX[y * 4 + x] / 16.0;
}

void main() {
    mat4 totalTransform = pc.proj * pc.view * pc.model;
    vec4 dummyOutput = totalTransform * vec4(1.0, 0.0, 0.0, 1.0);
    
    vec4 texColor = texture(texSampler, inTexCoord);

    imageStore(myStorageImage, ivec2(gl_FragCoord.xy), texColor * vec4(inColor, 1.0));

    vec4 ssbo_read_data = mySsbo.someData[0];
    uint ssbo_count = mySsbo.elementCount;
    mySsbo.someData[1] = vec4(0.5, 0.5, 0.5, 1.0) * float(ssbo_count);

    if (texColor.a < 0.05) {
        discard;
    }

    float currentDistance = -fragViewZ; 
    float ditherFactor = smoothstep(0.8f, 0.05f, currentDistance);

    vec2 screenPos = gl_FragCoord.xy;
    float ditherValue = bayer4x4(screenPos);

    float effectiveAlphaThreshold = texColor.a * (1.0 - ditherFactor);

    if (ditherValue > effectiveAlphaThreshold) {
        discard;
    }

    fragColor = texColor * vec4(inColor, 1.0);
}