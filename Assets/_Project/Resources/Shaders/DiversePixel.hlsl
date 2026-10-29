// Pixel-art sampling shared by Diverse/Sprite and Diverse/SpriteAdd.
// The camera zoom is not always a whole number of screen pixels per art pixel (the wheel zooms in small steps).
// Plain point sampling then draws some art pixels one screen pixel wider than others, and the pattern crawls while
// the camera moves (looks like judder). Here each art pixel is flat inside and blends with its neighbour across
// one screen pixel at its edge ("sharp bilinear"). At whole-number zooms this is exactly point sampling.
#ifndef DIVERSE_PIXEL_INCLUDED
#define DIVERSE_PIXEL_INCLUDED

half4 SamplePixelArt(TEXTURE2D_PARAM(tex, samp), float2 uv)
{
    float w, h;
    tex.GetDimensions(w, h);
    float2 size = float2(w, h);
    float2 p = uv * size;                       // texel units
    float2 seam = floor(p + 0.5);               // nearest texel edge
    float2 width = max(fwidth(p), 1e-4);        // texels per screen pixel
    p = seam + clamp((p - seam) / width, -0.5, 0.5);

    // Manual bilinear (textures stay point-filtered, so UI using the same sprites is unaffected).
    // Premultiplied, so transparent neighbours don't darken the edge.
    float2 q = p - 0.5;
    float2 f = frac(q);
    float2 b = (floor(q) + 0.5) / size;
    float2 o = 1.0 / size;
    half4 c00 = SAMPLE_TEXTURE2D_LOD(tex, samp, b, 0);
    half4 c10 = SAMPLE_TEXTURE2D_LOD(tex, samp, b + float2(o.x, 0), 0);
    half4 c01 = SAMPLE_TEXTURE2D_LOD(tex, samp, b + float2(0, o.y), 0);
    half4 c11 = SAMPLE_TEXTURE2D_LOD(tex, samp, b + o, 0);
    c00.rgb *= c00.a; c10.rgb *= c10.a; c01.rgb *= c01.a; c11.rgb *= c11.a;
    half4 c = lerp(lerp(c00, c10, f.x), lerp(c01, c11, f.x), f.y);
    c.rgb /= max(c.a, 1e-4);
    return c;
}

#endif
