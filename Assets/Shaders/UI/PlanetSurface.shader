Shader "Fodinae/UI/PlanetSurface"
{
    // Rocky crust of the GJ-1132b analogue: cold basalt highlands under a thick
    // sulfur/chlorine haze, cut by rift systems that glow from below.
    //
    // Everything here is evaluated per-fragment on a plain sphere - there is no
    // displacement, so the silhouette stays smooth (which is correct: a planet
    // seen from orbit has no visible profile relief) and all the terrain reads
    // through shading alone.
    Properties
    {
        [Header(Lighting)]
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.38, 0.16, -0.91, 0)
        _SunColor ("Sun Color (M-dwarf, warm)", Color) = (1.0, 0.90, 0.76, 1)
        _SunIntensity ("Sun Intensity", Range(0, 6)) = 5.0
        _NightAmbient ("Night Ambient", Range(0, 0.1)) = 0.004
        _TwilightColor ("Twilight Scatter Color", Color) = (0.30, 0.34, 0.13, 1)
        _TwilightIntensity ("Twilight Intensity", Range(0, 2)) = 1.10
        // Oren-Nayar's whole purpose is to flatten the cosine falloff - it is why
        // the full Moon looks like a disc rather than a ball. At 0.85 it was doing
        // exactly that to this planet: with the star behind the camera, N.L only
        // ranges 0.62..0.77 over most of the disc to begin with, and the
        // backscatter term erased even that. Kept low enough to still soften the
        // terminator without cancelling the shading that makes a sphere a sphere.
        _Roughness ("Surface Roughness (Oren-Nayar)", Range(0, 1)) = 0.40

        [Header(Terrain)]
        _ContinentScale ("Continent Scale", Range(0.5, 8)) = 3.0
        _WarpStrength ("Domain Warp Strength", Range(0, 2)) = 0.50
        _RidgeScale ("Mountain Ridge Scale", Range(1, 30)) = 11.0
        _MountainHeight ("Mountain Height", Range(0, 1)) = 0.28
        _DetailScale ("Detail Scale", Range(10, 400)) = 140
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.20
        _ReliefStrength ("Relief (normal) Strength", Range(0, 4)) = 0.75

        [Header(Materials)]
        // These three are tuned in albedo space AGAINST the star's own colour:
        // the sun (1.0, 0.9, 0.76) multiplies the green channel by 0.9, so an
        // albedo with G slightly above R lands as olive, while an albedo with
        // R >= G lands as ochre. Earlier values (R > G) are exactly how the
        // world drifted into warm orange under this star.
        _BasaltColor ("Basalt (steep rock)", Color) = (0.070, 0.072, 0.062, 1)
        _RegolithColor ("Olive Regolith", Color) = (0.080, 0.095, 0.050, 1)
        _CrustColor ("Sulfur Crust (flats)", Color) = (0.150, 0.180, 0.115, 1)
        _PeakColor ("Peak Rock", Color) = (0.120, 0.145, 0.110, 1)
        _BasinLevel ("Basin Level", Range(0, 1)) = 0.42
        _PeakLevel ("Peak Level", Range(0, 1)) = 0.72

        [Header(Rifts)]
        _MagmaColor ("Magma Color", Color) = (1.0, 0.24, 0.045, 1)
        _MagmaIntensity ("Magma Intensity", Range(0, 12)) = 2.4
        _CrackScale ("Crack Network Scale", Range(1, 40)) = 9.0
        _CrackThreshold ("Crack Threshold", Range(0.5, 1)) = 0.885

        [Header(Liquid Sulfur)]
        _PoolAlbedo ("Pool Albedo", Color) = (0.048, 0.030, 0.014, 1)
        _PoolSpecColor ("Pool Specular Tint", Color) = (1.0, 0.66, 0.28, 1)
        _PoolIntensity ("Pool Specular Intensity", Range(0, 12)) = 3.0
        _PoolGloss ("Pool Gloss (specular exponent)", Range(32, 4096)) = 900
        _PoolF0 ("Pool Normal Reflectance", Range(0, 0.2)) = 0.045
        _PoolScale ("Pool Patch Scale", Range(1, 60)) = 22.0
        _PoolCoverage ("Pool Coverage", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Integer bit ops in the pcg3d hash need SM4+.
            #pragma target 4.5

            // Включается из C# (PlanetFieldBaker), когда поля уже запечены в
            // кубическую карту. Вариант без ключевого слова остаётся рабочим и
            // используется там, где вычислительных шейдеров нет.
            #pragma multi_compile _ PLANET_FIELDS_BAKED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetSurfaceFields.hlsl"

#if defined(PLANET_FIELDS_BAKED)
            // Не свойство материала, а глобальная текстура: планета в сцене
            // одна, карту заводит и раздаёт запекатель, и .mat-ассеты об этом
            // знать не обязаны.
            TEXTURECUBE(_PlanetSurfaceFields);
            SAMPLER(sampler_PlanetSurfaceFields);
#endif

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirWS;
                float4 _SunColor;
                float _SunIntensity;
                float _NightAmbient;
                float4 _TwilightColor;
                float _TwilightIntensity;
                float _Roughness;

                float _ContinentScale;
                float _WarpStrength;
                float _RidgeScale;
                float _MountainHeight;
                float _DetailScale;
                float _DetailStrength;
                float _ReliefStrength;

                float4 _BasaltColor;
                float4 _RegolithColor;
                float4 _CrustColor;
                float4 _PeakColor;
                float _BasinLevel;
                float _PeakLevel;

                float4 _MagmaColor;
                float _MagmaIntensity;
                float _CrackScale;
                float _CrackThreshold;

                float4 _PoolAlbedo;
                float4 _PoolSpecColor;
                float _PoolIntensity;
                float _PoolGloss;
                float _PoolF0;
                float _PoolScale;
                float _PoolCoverage;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            // Итоговая высота в [0, 1].
            //
            // Сами формулы полей живут в PlanetSurfaceFields.hlsl: их же
            // исполняет запекающее ядро, и держать их в двух местах означало бы
            // тихое расхождение картинки с запечённой картой. Здесь остаётся
            // только сборка — база плюс детальный слой.
            //
            // Детальный слой не запекается и запечён быть не может: он умножен
            // на detailFade, то есть зависит от угла обзора, а не от одного лишь
            // направления. Затухание тут не украшение, а борьба с алиасингом.
            // Детальная полоса идёт на 140 циклов по шару — около 9 px длины
            // волны в центре диска. К краю та же полоса сжимается как 1/(N.V),
            // уходит под пиксель, и подпиксельный сигнал, взятый одной выборкой,
            // не усредняется, а бьётся о сетку растра. Это и есть крапчатая
            // бахрома на тёмном лимбе, и MSAA её не трогает: он суперсэмплит
            // покрытие геометрии, а не то, что шейдер считает внутри фрагмента.
            float ElevationFromBase(float3 dir, float elevBase, float detailFade)
            {
                float detail = FodinaeElevationDetail(dir, _DetailScale);
                return saturate(elevBase + (detail * _DetailStrength * 0.03 * detailFade));
            }

            // Rift network: ridged-noise crests thresholded into thin connected
            // lines, gated to low ground so rifts sit in basins, not on peaks.
            //
            // Takes the fault field as an argument rather than evaluating it,
            // because the sulfur pools need the same field to know where the
            // geothermal zones are - and RidgedFbm is far too expensive to run
            // twice per pixel.
            float CrackMask(float3 dir, float elev, float faultField)
            {
                // Not named 'line': that is an HLSL geometry-shader primitive
                // keyword and using it as an identifier fails to compile.
                float ridge = smoothstep(_CrackThreshold, 1.0, faultField);

                // Break the network up along its length so it reads as a
                // discontinuous fault system rather than one drawn contour.
                float breakUp = smoothstep(-0.25, 0.35, FodinaeCrackBreakup(dir, _CrackScale));
                float lowGround = 1.0 - smoothstep(_BasinLevel, _BasinLevel + 0.30, elev);

                return ridge * breakUp * lowGround;
            }

            // Ponded liquid sulfur.
            //
            // Sulfur melts around 115 C, so on a world with this one's
            // geothermal gradient it pools wherever hot ground meets a flat
            // floor - which is why this is gated on three things at once: near
            // the fault network (heat), low ground (it runs downhill), and
            // genuinely flat (a pool cannot sit on a slope). Patchiness on top
            // keeps it from filling every basin uniformly.
            float PoolMask(float3 dir, float elev, float slope, float faultField)
            {
                float geothermal = smoothstep(0.40, 0.86, faultField);
                float flatGround = 1.0 - smoothstep(0.05, 0.18, slope);
                float lowGround = 1.0 - smoothstep(_BasinLevel - 0.06, _BasinLevel + 0.06, elev);
                float patches = smoothstep(0.05, 0.55, (Fbm(dir * _PoolScale, 3) * 0.5) + 0.5);

                return saturate(geothermal * flatGround * lowGround * patches * _PoolCoverage);
            }

            // Oren-Nayar (qualitative form, closed-form algebraic formulation).
            // Eliminates transcendental acos/sin/tan and square root normalizations,
            // producing mathematically identical backscattering with pure dot/mad ALU.
            float OrenNayar(float3 N, float3 L, float3 V, float roughness)
            {
                float s2 = roughness * roughness;
                float A = 1.0 - (0.5 * (s2 / (s2 + 0.33)));
                float B = 0.45 * (s2 / (s2 + 0.09));

                float ndl = saturate(dot(N, L));
                float ndv = saturate(dot(N, V));

                float s = dot(L, V) - (ndl * ndv);
                float t = (s > 0.0) ? (s / max(max(ndl, ndv), 0.0001)) : 0.0;

                return ndl * (A + (B * t));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.positionOS);
                float3 geoN = normalize(TransformObjectToWorldNormal(dir));
                float3 L = normalize(_SunDirWS.xyz);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Foreshortening toward the limb
                float ndv = saturate(dot(geoN, V));
                float detailFade = smoothstep(0.05, 0.42, ndv);

                // Один из двух источников одних и тех же чисел.
#if defined(PLANET_FIELDS_BAKED)
                float4 fields = SAMPLE_TEXTURECUBE(_PlanetSurfaceFields, sampler_PlanetSurfaceFields, dir);
                float2 slopeVec = fields.xy;
                float elevBase = fields.z;
                float faultField = fields.w;
#else
                float2 slopeVec = FodinaeElevationSlope(dir, _ContinentScale, _RidgeScale, _MountainHeight);
                float elevBase = FodinaeElevationBase(dir, _ContinentScale, _WarpStrength, _RidgeScale, _MountainHeight);
                float faultField = FodinaeFaultField(dir, _CrackScale);
#endif

                float elev = ElevationFromBase(dir, elevBase, detailFade);

                // Базис нужен и в запечённой ветке: в карте лежит наклон, а
                // нормаль из него собирается здесь.
                float3 tangent;
                float3 bitangent;
                FodinaePlanetTangentFrame(dir, tangent, bitangent);

                float3 normalOS = normalize(dir - (((tangent * slopeVec.x) + (bitangent * slopeVec.y)) * _ReliefStrength * 0.45));
                float3 N = normalize(TransformObjectToWorldNormal(normalOS));

                // ---- Albedo ----
                float slope = saturate(length(slopeVec) * 0.25);
                float3 albedo = lerp(_CrustColor.rgb, _RegolithColor.rgb, smoothstep(0.08, 0.35, slope));
                albedo = lerp(albedo, _BasaltColor.rgb, smoothstep(0.30, 0.70, slope));

                float basin = 1.0 - smoothstep(_BasinLevel - 0.10, _BasinLevel + 0.14, elev);
                albedo = lerp(albedo, _CrustColor.rgb, basin * (1.0 - smoothstep(0.30, 0.65, slope)) * 0.75);
                albedo = lerp(albedo, _PeakColor.rgb, smoothstep(_PeakLevel - 0.05, _PeakLevel + 0.15, elev));

                // ---- Radiance Cascades Global Illumination ----
                // Cascade 0: Direct sun irradiance with Oren-Nayar
                float diffuse = OrenNayar(N, L, V, _Roughness);
                float ndlGeo = dot(geoN, L);
                float ndlRelief = dot(N, L);
                float grazing = saturate(1.0 - ndlGeo);
                float shadow = saturate(1.0 - (max(0.0, ndlGeo - ndlRelief) * grazing * 1.2));
                float3 sun = _SunColor.rgb * _SunIntensity;
                float3 cascade0_Direct = albedo * diffuse * shadow * sun;

                // Cascade 1: Grand tectonic rift magma emission & bounce
                float crack = CrackMask(dir, elev, faultField) * lerp(0.2, 1.0, detailFade);
                float3 hotMagma = lerp(_MagmaColor.rgb, float3(1.0, 0.90, 0.65), pow(crack, 3.0));
                float3 riftEmission = hotMagma * crack * _MagmaIntensity;
                float riftBounce = smoothstep(0.70, 0.95, faultField) * (1.0 - crack) * 0.35;
                float3 cascade1_Geothermal = riftEmission + (albedo * _MagmaColor.rgb * riftBounce * _MagmaIntensity * 0.35);

                // Cascade 2: Atmospheric multiple-scattering & twilight wrap
                float twilightTerm = smoothstep(-0.35, 0.18, ndlGeo) * (1.0 - saturate(ndlGeo * 2.8));
                float3 atmosphericWrap = _TwilightColor.rgb * twilightTerm * _TwilightIntensity;
                float3 planetaryAmbient = albedo * _NightAmbient;
                float3 cascade2_Atmosphere = (albedo * atmosphericWrap) + planetaryAmbient;

                // Merge Cascades: L_total = Cascade0 + Cascade1 + Cascade2
                float3 color = cascade0_Direct + cascade1_Geothermal + cascade2_Atmosphere;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
