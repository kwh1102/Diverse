// Existing sprites keep their texture, pivot and collision. All phases are anchored in world coordinates.
// Materials are shared per theme/motion; no per-tree Update or material instance is needed.
TEXTURE2D(_EnvironmentMask); SAMPLER(sampler_EnvironmentMask);
float _EnvironmentTime;
float _EnvironmentEnabled;

float2 EnvironmentUV(float2 uv, float2 root, float2 world)
{
    if (_EnvironmentEnabled < .5 || _EnvMotion < .5) return uv;
    float t = floor(_EnvironmentTime * _EnvFPS) / max(1, _EnvFPS);
    if (_EnvMotion > 3.5 && _EnvMotion < 4.5)
    {
        uv.x += fmod(floor(t * _EnvFPS + frac(sin(dot(root, float2(12.9898, 78.233))) * 43758.5453) * 4), 4) * .25;
        return uv;
    }
    if (_EnvMotion > 2.5) return uv;
    float w, h; _MainTex.GetDimensions(w, h);
    float phase = dot(world, float2(_EnvSpatial, _EnvSpatial * .6));
    float gust = .65 + .35 * sin(t * .37 - phase * .3);
    if (_EnvMotion < 1.5)
    {
        float height = saturate((uv.y - .24) / .76);
        float bend = (sin(t * _EnvSpeed - phase) + .25 * sin(t * _EnvSpeed * 1.7 - phase)) * gust;
        uv.x -= round(bend * _EnvStrength * height * height) / w;
    }
    else
    {
        // The pole and lower half remain fixed. Only the free end of the cloth ripples.
        float weight = saturate((uv.x - .3) / .7) * saturate((uv.y - .38) / .3);
        uv.y -= round(sin(t * _EnvSpeed * 1.4 - phase - uv.x * 6) * _EnvStrength * weight) / h;
    }
    return uv;
}

half4 EnvironmentColor(half4 c, float2 uv, float2 world, float2 root)
{
    if (_EnvironmentEnabled < .5) return c;
    float t = floor(_EnvironmentTime * _EnvFPS) / max(1, _EnvFPS);
    if (_EnvMotion > 2.5 && _EnvMotion < 3.5)
    {
        float band = abs(frac(uv.y + uv.x * .3 - t * .14 + world.x * .031) - .5);
        float pulse = step(band, .045) * (.5 + .5 * sin(t * .7 + world.y * .08));
        c.rgb = lerp(c.rgb, _EnvTint.rgb, pulse * _EnvStrength);
    }
    if (_EnvMotion > 4.5)
    {
        float liquid = SAMPLE_TEXTURE2D(_EnvironmentMask, sampler_EnvironmentMask, uv).r;
        float2 p = floor(world * 16) / 16;
        float ripple = step(.94, sin(p.y * 5 + t * _EnvSpeed + sin(p.x * .7))) * step(.2, frac(p.x * .23 - t * .08));
        float breath = .5 + .5 * sin(t * _EnvSpeed + p.x * .3 + p.y * .2);
        float amount = liquid > .8 ? breath : ripple;
        if (liquid > .05) c.rgb = lerp(c.rgb, liquid > .8 ? half3(1, .6, .12) : liquid > .5 ? half3(.65, .8, .3) : half3(.65, .87, .9), amount * _EnvStrength);
    }
    return c;
}
