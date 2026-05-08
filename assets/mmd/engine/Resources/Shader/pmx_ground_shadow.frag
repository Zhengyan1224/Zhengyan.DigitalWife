#version 300 es

precision highp float;

uniform vec4 u_ShadowColor;

out vec4 fs_Color;

void main() {
    fs_Color = u_ShadowColor;
}
