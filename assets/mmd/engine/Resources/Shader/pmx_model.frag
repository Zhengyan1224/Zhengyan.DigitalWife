#version 300 es

#define NUM_SHADOWMAP 4

precision highp float;
precision highp sampler2DShadow;

in vec3 vs_Pos;
in vec3 vs_Nor;
in vec2 vs_UV;

in vec4 vs_shadowMapCoord[NUM_SHADOWMAP];

out vec4 out_Color;

uniform float u_Alpha;
uniform vec3 u_Diffuse;
uniform vec3 u_Ambient;
uniform vec3 u_Specular;
uniform float u_SpecularPower;
uniform vec3 u_LightColor;
uniform vec3 u_LightDir;
uniform vec3 u_AmbientLightColor;
uniform float u_AmbientLightStrength;
uniform int u_PointLightCount;
uniform vec4 u_PointLightPositionRange[16];
uniform vec4 u_PointLightColorIntensity[16];
uniform int u_SpotLightCount;
uniform vec4 u_SpotLightPositionRange[16];
uniform vec4 u_SpotLightDirectionOuterCosine[16];
uniform vec4 u_SpotLightColorIntensity[16];
uniform vec4 u_SpotLightConeParameters[16];
uniform sampler2DShadow u_LocalShadowAtlas;
uniform float u_LocalShadowStrength;
uniform float u_LocalShadowBias;
uniform vec2 u_LocalShadowTexelSize;
uniform float u_LocalShadowNormalOffset;
uniform mat4 u_LocalShadowInverseView;
uniform vec4 u_PointLightShadowMeta[2];
uniform vec4 u_SpotLightShadowMeta[4];
uniform mat4 u_PointLightShadowMatrix[12];
uniform vec4 u_PointLightShadowAtlasRect[12];
uniform mat4 u_SpotLightShadowMatrix[4];
uniform vec4 u_SpotLightShadowAtlasRect[4];

uniform int u_TexMode;
uniform sampler2D u_Tex;
uniform vec4 u_TexMulFactor;
uniform vec4 u_TexAddFactor;

uniform int u_ToonTexMode;
uniform sampler2D u_ToonTex;
uniform vec4 u_ToonTexMulFactor;
uniform vec4 u_ToonTexAddFactor;

uniform int u_SphereTexMode;
uniform sampler2D u_SphereTex;
uniform vec4 u_SphereTexMulFactor;
uniform vec4 u_SphereTexAddFactor;

uniform float u_ShadowMapSplitPositions[NUM_SHADOWMAP + 1];
uniform sampler2DShadow u_ShadowMap0;
uniform sampler2DShadow u_ShadowMap1;
uniform sampler2DShadow u_ShadowMap2;
uniform sampler2DShadow u_ShadowMap3;
uniform int u_ShadowMapEnabled;
uniform float u_ShadowMapStrength;
uniform float u_ShadowMapBias;
uniform vec2 u_ShadowMapTexelSize;
uniform int u_ShadowDisplayMode;

vec3 ComputeTexMulFactor(vec3 texColor, vec4 factor) {
    vec3 ret = texColor * factor.rgb;
    return mix(vec3(1.0, 1.0, 1.0), ret, factor.a);
}

vec3 ComputeTexAddFactor(vec3 texColor, vec4 factor) {
    vec3 ret = texColor + (texColor - vec3(1.0)) * factor.a;
    ret = clamp(ret, vec3(0.0), vec3(1.0)) + factor.rgb;
    return ret;
}

float ComputeTextureAlpha(float textureAlpha) {
    if(u_TexMode == 4) {
        // Soft-alpha PMX textures are usually authored as coverage masks.
        // A mild coverage curve keeps lace/stockings opaque enough while preserving gradients.
        return 1.0 - pow(1.0 - textureAlpha, 1.5);
    }

    return textureAlpha;
}

float ComputeDirectionalShadowDepthBias(float surfaceNdotL) {
    float grazingAngle = 1.0 - clamp(surfaceNdotL, 0.0, 1.0);
    return u_ShadowMapBias * (1.0 + grazingAngle * 2.0);
}

float SampleShadowMap(sampler2DShadow shadowMap, vec4 clipCoord, float surfaceNdotL) {
    vec3 ndc = clipCoord.xyz / max(abs(clipCoord.w), 0.0001);
    vec2 uv = ndc.xy * 0.5 + 0.5;
    if(uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z < -1.0 || ndc.z > 1.0) {
        return 1.0;
    }

    float depth = (ndc.z * 0.5 + 0.5) - ComputeDirectionalShadowDepthBias(surfaceNdotL);
    vec2 filterRadius = u_ShadowMapTexelSize;
    vec2 minimumUv = filterRadius;
    vec2 maximumUv = vec2(1.0) - filterRadius;
    float visibility = 0.0;
    for(int y = -1; y <= 1; ++y) {
        for(int x = -1; x <= 1; ++x) {
            vec2 offset = vec2(x, y) * u_ShadowMapTexelSize;
            visibility += texture(shadowMap, vec3(clamp(uv + offset, minimumUv, maximumUv), depth));
        }
    }

    return visibility / 9.0;
}

float ComputeLocalShadowDepthBias(vec4 clipCoord, vec2 depthRange, float surfaceNdotL) {
    float nearPlane = max(depthRange.x, 0.0001);
    float farPlane = max(depthRange.y, nearPlane + 0.0001);
    float lightDistance = max(abs(clipCoord.w), nearPlane);
    float perspectiveScale = nearPlane * farPlane / max(farPlane - nearPlane, 0.0001);
    float slopeScale = 1.0 + (1.0 - clamp(surfaceNdotL, 0.0, 1.0)) * 2.0;
    return u_LocalShadowBias * slopeScale * perspectiveScale
        / (lightDistance * lightDistance) * 0.5;
}

float SampleLocalShadow(vec4 clipCoord, vec4 atlasRect, vec2 depthRange, float surfaceNdotL) {
    vec3 ndc = clipCoord.xyz / max(abs(clipCoord.w), 0.0001);
    vec2 localUv = ndc.xy * 0.5 + 0.5;
    if(localUv.x < 0.0 || localUv.x > 1.0 || localUv.y < 0.0 || localUv.y > 1.0 || ndc.z < -1.0 || ndc.z > 1.0) {
        return 1.0;
    }

    float depth = ndc.z * 0.5 + 0.5
        - ComputeLocalShadowDepthBias(clipCoord, depthRange, surfaceNdotL);
    vec2 atlasUv = atlasRect.xy + localUv * atlasRect.zw;
    vec2 filterRadius = u_LocalShadowTexelSize;
    vec2 minimumUv = atlasRect.xy + filterRadius;
    vec2 maximumUv = atlasRect.xy + atlasRect.zw - filterRadius;
    float visibility = 0.0;
    for(int y = 0; y <= 1; ++y) {
        for(int x = 0; x <= 1; ++x) {
            vec2 offset = (vec2(x, y) - vec2(0.5)) * u_LocalShadowTexelSize;
            visibility += texture(
                u_LocalShadowAtlas,
                vec3(clamp(atlasUv + offset, minimumUv, maximumUv), depth));
        }
    }
    return visibility * 0.25;
}

float ApplyReceivedShadow(float visibility, float strength) {
    // Toon mode intentionally keeps only lit/shadowed levels. The PCF lookup
    // still anti-aliases the geometric edge, but does not expose its gray bands.
    if(u_ShadowDisplayMode != 0) {
        visibility = step(0.5, visibility);
    }
    return mix(1.0 - clamp(strength, 0.0, 1.0), 1.0, visibility);
}

int SelectPointShadowFace(vec3 direction) {
    vec3 absoluteDirection = abs(direction);
    if(absoluteDirection.x >= absoluteDirection.y && absoluteDirection.x >= absoluteDirection.z) return direction.x >= 0.0 ? 0 : 1;
    if(absoluteDirection.y >= absoluteDirection.z) return direction.y >= 0.0 ? 2 : 3;
    return direction.z >= 0.0 ? 4 : 5;
}

int FindPointShadowSlot(int packedLightIndex) {
    for(int slot = 0; slot < 2; ++slot) {
        if(int(u_PointLightShadowMeta[slot].x) == packedLightIndex) return slot;
    }
    return -1;
}

int FindSpotShadowSlot(int packedLightIndex) {
    for(int slot = 0; slot < 4; ++slot) {
        if(int(u_SpotLightShadowMeta[slot].x) == packedLightIndex) return slot;
    }
    return -1;
}

void main() {
    vec3 eyeDir = normalize(-vs_Pos);
    vec3 lightDir = normalize(-u_LightDir);
    vec3 nor = normalize(vs_Nor);
    float ndotl = clamp(dot(nor, lightDir), 0.0, 1.0);
    float toonCoord = clamp(dot(nor, lightDir) * 0.5 + 0.5, 0.0, 1.0);
    vec3 albedo = u_Diffuse;
    float alpha = u_Alpha;

    if(u_ShadowMapEnabled != 0) {
        float visibility = SampleShadowMap(u_ShadowMap0, vs_shadowMapCoord[0], ndotl);
        float shadowFactor = ApplyReceivedShadow(visibility, u_ShadowMapStrength);
        ndotl *= shadowFactor;
        toonCoord = mix(0.0, toonCoord, shadowFactor);
    }

    if(u_TexMode != 0) {
        vec4 texColor = texture(u_Tex, vs_UV);
        texColor.rgb = ComputeTexMulFactor(texColor.rgb, u_TexMulFactor);
        texColor.rgb = ComputeTexAddFactor(texColor.rgb, u_TexAddFactor);

        albedo *= texColor.rgb;
        if(u_TexMode == 2 || u_TexMode == 4) {
            alpha *= ComputeTextureAlpha(texColor.a);
        }
    }
    if(alpha < 0.01) {
        discard;
    }

    vec3 baseColor = albedo;
    if(u_SphereTexMode != 0) {
        vec2 spUV = vec2(0.0);
        spUV.x = nor.x * 0.5 + 0.5;
        spUV.y = 1.0 - (nor.y * 0.5 + 0.5);
        vec3 spColor = texture(u_SphereTex, spUV).rgb;
        spColor = ComputeTexMulFactor(spColor, u_SphereTexMulFactor);
        spColor = ComputeTexAddFactor(spColor, u_SphereTexAddFactor);
        if(u_SphereTexMode == 1) {
            baseColor *= spColor.rgb;
        } else if(u_SphereTexMode == 2) {
            baseColor += spColor.rgb;
        }
    }

    vec3 ambientColor = albedo * u_Ambient * u_AmbientLightColor * u_AmbientLightStrength;
    vec3 litColor = u_LightColor * ndotl;
    if(u_ToonTexMode != 0) {
        vec3 toonColor = texture(u_ToonTex, vec2(0.0, toonCoord)).rgb;
        toonColor = ComputeTexMulFactor(toonColor, u_ToonTexMulFactor);
        toonColor = ComputeTexAddFactor(toonColor, u_ToonTexAddFactor);
        litColor *= toonColor.rgb;
    }

    vec3 specular = vec3(0.0);
    if(u_SpecularPower > 0.0 && ndotl > 0.0) {
        vec3 halfVec = normalize(eyeDir + lightDir);
        vec3 specularColor = u_Specular * u_LightColor;
        specular += pow(max(0.0, dot(halfVec, nor)), u_SpecularPower) * specularColor;
    }

    vec3 pointDiffuse = vec3(0.0);
    vec3 pointSpecular = vec3(0.0);
    for(int i = 0; i < min(u_PointLightCount, 16); ++i) {
        vec4 positionRange = u_PointLightPositionRange[i];
        vec4 colorIntensity = u_PointLightColorIntensity[i];
        vec3 toLight = positionRange.xyz - vs_Pos;
        float distanceToLight = length(toLight);
        float range = max(positionRange.w, 0.0001);
        if(distanceToLight >= range) {
            continue;
        }

        vec3 pointDirection = toLight / max(distanceToLight, 0.0001);
        float pointNdotL = max(dot(nor, pointDirection), 0.0);
        float falloff = max(1.0 - distanceToLight / range, 0.0);
        vec3 radiance = colorIntensity.rgb * colorIntensity.a * falloff * falloff;
        int shadowSlot = FindPointShadowSlot(i);
        if(shadowSlot >= 0 && shadowSlot < 2 && u_LocalShadowStrength > 0.0) {
            vec3 worldLightToSurface = mat3(u_LocalShadowInverseView) * (-toLight);
            int faceIndex = shadowSlot * 6 + SelectPointShadowFace(worldLightToSurface);
            vec3 shadowPosition = vs_Pos + nor * u_LocalShadowNormalOffset * (2.0 - pointNdotL);
            float visibility = SampleLocalShadow(
                u_PointLightShadowMatrix[faceIndex] * vec4(shadowPosition, 1.0),
                u_PointLightShadowAtlasRect[faceIndex],
                u_PointLightShadowMeta[shadowSlot].yz,
                pointNdotL);
            radiance *= ApplyReceivedShadow(visibility, u_LocalShadowStrength);
        }
        pointDiffuse += radiance * pointNdotL;
        if(u_SpecularPower > 0.0 && pointNdotL > 0.0) {
            vec3 pointHalfVec = normalize(eyeDir + pointDirection);
            pointSpecular += pow(max(0.0, dot(pointHalfVec, nor)), u_SpecularPower)
                * u_Specular * radiance;
        }
    }

    vec3 spotDiffuse = vec3(0.0);
    vec3 spotSpecular = vec3(0.0);
    for(int i = 0; i < min(u_SpotLightCount, 16); ++i) {
        vec4 positionRange = u_SpotLightPositionRange[i];
        vec4 directionOuter = u_SpotLightDirectionOuterCosine[i];
        vec4 colorIntensity = u_SpotLightColorIntensity[i];
        vec3 lightToSurface = vs_Pos - positionRange.xyz;
        float distanceToLight = length(lightToSurface);
        float range = max(positionRange.w, 0.0001);
        if(distanceToLight >= range) {
            continue;
        }

        vec3 fromLight = lightToSurface / max(distanceToLight, 0.0001);
        float cone = smoothstep(directionOuter.w, u_SpotLightConeParameters[i].x,
            dot(fromLight, normalize(directionOuter.xyz)));
        vec3 surfaceToLight = -fromLight;
        float spotNdotL = max(dot(nor, surfaceToLight), 0.0);
        float falloff = max(1.0 - distanceToLight / range, 0.0);
        vec3 radiance = colorIntensity.rgb * colorIntensity.a * falloff * falloff * cone;
        int shadowSlot = FindSpotShadowSlot(i);
        if(shadowSlot >= 0 && shadowSlot < 4 && u_LocalShadowStrength > 0.0) {
            vec3 shadowPosition = vs_Pos + nor * u_LocalShadowNormalOffset * (2.0 - spotNdotL);
            float visibility = SampleLocalShadow(
                u_SpotLightShadowMatrix[shadowSlot] * vec4(shadowPosition, 1.0),
                u_SpotLightShadowAtlasRect[shadowSlot],
                u_SpotLightShadowMeta[shadowSlot].yz,
                spotNdotL);
            radiance *= ApplyReceivedShadow(visibility, u_LocalShadowStrength);
        }
        spotDiffuse += radiance * spotNdotL;
        if(u_SpecularPower > 0.0 && spotNdotL > 0.0) {
            vec3 spotHalfVec = normalize(eyeDir + surfaceToLight);
            spotSpecular += pow(max(0.0, dot(spotHalfVec, nor)), u_SpecularPower)
                * u_Specular * radiance;
        }
    }

    vec3 color = baseColor * (litColor + pointDiffuse + spotDiffuse);
    color += specular;
    color += pointSpecular;
    color += spotSpecular;
    color += ambientColor;
    color = clamp(color, 0.0, 1.0);

    out_Color = vec4(color, alpha);
}
