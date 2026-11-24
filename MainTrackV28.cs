using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v28 track look (all generated in code, no image or model files added).
    //  1. Sleepers: every sleeper is one complete flat-topped concrete slab, full length and full height (v24 had a narrow, low waist that
    //     disappeared under the ballast, so only two small blocks per sleeper showed: "a little bit here, a little bit there").
    //  2. Ballast: light cement-grey granite. The warm / dark vertex-colour bands are switched off and the stone texture is lifted and neutralised.
    //  3. Formation strip beside the ballast: light cement grey as well (was dark mud brown).
    //  4. Rails: iron-brown rust patches in random places (world-space texture, so no two metres of rail look the same).
    // RTV28Init() is called at the end of RTV24Init(), i.e. after RTMaterials() and before the first piece of track is built.
    public partial class Main
    {
        const string RTMudShader28 = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D mud_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_tex : filter_linear_mipmap, repeat_enable;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 uv = mod(wp.xz, 5000.0);
    vec3 t1 = texture(mud_tex, uv * 0.32).rgb;
    vec3 t2 = texture(mud_tex, uv * 0.045).rgb;
    vec3 m = texture(macro_tex, uv * 0.011).rgb;
    float wet = smoothstep(0.35, 0.72, m.g);
    ALBEDO = t1 * (0.80 + 0.4 * t2) * mix(vec3(1.0), vec3(0.88, 0.85, 0.80), wet);
    ROUGHNESS = 0.95;
}";

        // One complete sleeper in the unit box space used by the track loop (x across, scaled 2.75 m; y up, 0.21 m; z along, 0.26 m).
        static ArrayMesh RTMakeSleeperFull()
        {
            return BuildMesh(st =>
            {
                Color body = new Color(1f, 1f, 1f), steel = new Color(.22f, .22f, .24f);
                AddBoxC(st, Vector3.Zero, new Vector3(1f, 1f, 1f), body);                                   // the whole sleeper: one flat slab, end to end
                for (int sd = -1; sd <= 1; sd += 2)
                    for (int k = -1; k <= 1; k += 2)
                        AddBoxC(st, new Vector3(sd * .3175f + k * .0382f, .5f, 0f), new Vector3(.018f, .14f, .38f), steel);   // cast-in shoulders at each rail seat
            });
        }

        // cement-grey ballast: keep the stone relief (brightness differences), drop the brown / black and lift the level
        static byte[] RTLiftGrey(byte[] src, float lo, float gain, float pivot, float warm)
        {
            var dst = new byte[src.Length];
            for (int i = 0; i + 2 < src.Length; i += 3)
            {
                float l = (src[i] * .30f + src[i + 1] * .59f + src[i + 2] * .11f) / 255f;
                float g = Mathf.Clamp(lo + (l - pivot) * gain + .0f, lo * .8f, .95f);
                dst[i] = RTByte(g * (1f + warm)); dst[i + 1] = RTByte(g); dst[i + 2] = RTByte(g * (1f - warm));
            }
            return dst;
        }

        // rust: white = clean iron, orange-brown blotches and streaks multiply the rail colour. Tileable.
        static byte[] RTMakeRust(int size)
        {
            var d = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float big = RTFbm(u, v, 3, 4, 301);                                   // where the rust patches are
                    float mid = RTFbm(u + .21f, v + .43f, 8, 3, 302);                      // edge breakup
                    float fine = RTHash(x, y, 303);
                    float m = Mathf.Clamp((big * .75f + mid * .25f - .50f) * 6.5f, 0f, 1f);   // 0 = clean, 1 = rusty
                    m *= .55f + .45f * mid;
                    float spot = RTFbm(u, v, 14, 2, 304) > .70f ? .5f : 0f;                // a few small rust spots outside the patches
                    m = Mathf.Max(m, spot * .8f);
                    float pit = .90f + .10f * fine;
                    float cr = Mathf.Lerp(1f, 1.00f, m), cg = Mathf.Lerp(1f, .60f - .10f * mid, m), cb = Mathf.Lerp(1f, .36f - .08f * mid, m);
                    int o = (y * size + x) * 3;
                    d[o] = RTByte(cr * pit); d[o + 1] = RTByte(cg * pit); d[o + 2] = RTByte(cb * pit);
                }
            return d;
        }

        void RTV28Init()
        {
            // 1. complete sleepers
            rtSleeperN26 = RTMakeSleeperFull();

            // 2. light cement-grey ballast
            if (matGravel != null && rtGraniteJob != null)
            {
                var g = rtGraniteJob.Result;
                matGravel.AlbedoTexture = RTTexture(RTLiftGrey(g.Albedo, .52f, 1.15f, .30f, .015f), g.Size, false);
                matGravel.VertexColorUseAsAlbedo = false;          // the warm and dark vertex-colour bands made the ballast look brown / black
                matGravel.AlbedoColor = Colors.White;
            }

            // 3. formation strip beside the ballast: light cement grey (RTFormation only builds its own when this is still null)
            if (rtMudMat == null)
            {
                var t = RTMakeMudCement(256);
                rtMudMat = new ShaderMaterial { Shader = new Shader { Code = RTMudShader28 } };
                rtMudMat.SetShaderParameter("mud_tex", RTTexture(RTLiftGrey(t.Albedo, .55f, 1.1f, .40f, .01f), t.Size, false));
                rtMudMat.SetShaderParameter("macro_tex", ProcTex.Macro());
            }

            // 4. rusty rails: world-space triplanar rust texture multiplies the rail vertex colours
            if (matRail != null)
            {
                matRail.AlbedoTexture = RTTexture(RTMakeRust(256), 256, false);
                matRail.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                matRail.Uv1Triplanar = true; matRail.Uv1WorldTriplanar = true;
                matRail.Uv1Scale = new Vector3(.16f, .16f, .16f);
            }
        }
    }
}
