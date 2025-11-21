#version 450

layout (location = 0) in vec4 color;
layout (location = 0) out vec4 fragColor;

#define dither 1

const mat4 THRESHOLD_MATRIX = mat4(
vec4(1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0),
vec4(13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0),
vec4(4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0),
vec4(16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0));

void main() {
    fragColor = color;

#if dither
    int x = int(gl_FragCoord.x - 0.5);
    int y = int(gl_FragCoord.y - 0.5);

    int x_index = x % 4;
    int y_index = y % 4;

    float threshold = THRESHOLD_MATRIX[x_index][y_index];

    if (color.a < threshold) {
        discard;
    }

    vec3 dithered_rgb;
    dithered_rgb.r = (color.r > threshold) ? 1.0 : 0.0;
    dithered_rgb.g = (color.g > threshold) ? 1.0 : 0.0;
    dithered_rgb.b = (color.b > threshold) ? 1.0 : 0.0;

    fragColor = vec4(dithered_rgb, 1.0);
#endif
}