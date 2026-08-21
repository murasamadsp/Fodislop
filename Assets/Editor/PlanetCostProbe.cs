#nullable enable

using System.Collections.Generic;
using System.Text;
using Fodinae.UI;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Замер стоимости кадра планеты в четырёх состояниях: запечённые и
    /// процедурные поля, обзор и точка высадки.
    ///
    /// Существует потому, что про производительность нельзя говорить словами.
    /// Стоимость здесь идёт за закрашиваемой площадью, поэтому одно число ни о
    /// чём не говорит — нужны оба кадрирования, и нужны они рядом с эталоном.
    /// </summary>
    internal static class PlanetCostProbe
    {
        // Первые кадры после смены состояния уходят на перекомпиляцию варианта
        // шейдера и на перезалив кадрового буфера — в среднее их пускать нельзя.
        private const float WarmupSeconds = 2f;
        private const float MeasureSeconds = 4f;

        private static readonly Vector3 LandingSiteDirection = new(-0.48f, 0.10f, -0.87f);

        /// <summary>Какие слои планеты видны в фазе. Разделены, чтобы остаток
        /// стоимости можно было приписать конкретному шейдеру, а не «планете».</summary>
        [System.Flags]
        private enum Layers
        {
            None = 0,
            Surface = 1,
            Atmosphere = 2,
            Both = Surface | Atmosphere,
        }

        private readonly struct Phase
        {
            public Phase(string label, float framing, bool procedural, Layers layers = Layers.Both)
            {
                Label = label;
                Framing = framing;
                Procedural = procedural;
                Visible = layers;
            }

            public string Label { get; }
            public float Framing { get; }
            public bool Procedural { get; }
            public Layers Visible { get; }
        }

        // Порядок значим только тем, что список прогоняется дважды и в отчёт
        // идёт второй проход: первая фаза после входа в режим игры всегда
        // дороже остальных, потому что в неё попадает компиляция вариантов
        // шейдера и первая заливка кадрового буфера.
        private static readonly Phase[] Phases =
        {
            new("пол кадра (без планеты)", 0f, false, Layers.None),
            new("процедурно, обзор", 0f, true),
            new("процедурно, высадка", 1f, true),
            new("запечено, обзор", 0f, false),
            new("запечено, высадка", 1f, false),
            new("запечено, высадка, поверхность", 1f, false, Layers.Surface),
            new("запечено, высадка, атмосфера", 1f, false, Layers.Atmosphere),
        };

        private const int Passes = 2;

        private static readonly List<string> Results = new();

        private static int _phase;
        private static int _pass;
        private static float _phaseStart;
        private static float _measureStart;
        private static int _frames;
        private static bool _running;

        [MenuItem("Fodinae/Art/Замер стоимости планеты")]
        private static void Run()
        {
            if (_running)
            {
                Debug.LogWarning("[Замер] Уже идёт.");
                return;
            }

            _running = true;
            _phase = -1;
            _pass = 0;
            Results.Clear();
            EditorApplication.isPlaying = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            MenuSceneryController? scenery = MenuSceneryController.Current;
            if (scenery == null)
            {
                return;
            }

            if (_phase < 0)
            {
                BeginPhase(0, scenery);
                return;
            }

            float now = Time.realtimeSinceStartup;

            if (now - _phaseStart < WarmupSeconds)
            {
                return;
            }

            if (_frames == 0)
            {
                _measureStart = now;
            }

            _frames++;

            if (now - _measureStart < MeasureSeconds)
            {
                return;
            }

            float elapsed = now - _measureStart;
            float ms = (elapsed / _frames) * 1000f;

            if (_pass + 1 >= Passes)
            {
                Results.Add($"{Phases[_phase].Label,-32} {ms,6:F1} мс/кадр   {_frames / elapsed,5:F0} FPS");
            }

            if (_phase + 1 < Phases.Length)
            {
                BeginPhase(_phase + 1, scenery);
                return;
            }

            if (_pass + 1 < Passes)
            {
                _pass++;
                BeginPhase(0, scenery);
                return;
            }

            Finish();
        }

        /// <summary>
        /// Прячет или показывает оба слоя планеты, чтобы отделить её стоимость
        /// от стоимости всего остального в кадре.
        /// </summary>
        private static void SetPlanetVisible(MenuSceneryController scenery, Layers layers)
        {
            foreach (Renderer renderer in scenery.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                string shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : string.Empty;
                if (shader == "Fodinae/UI/PlanetSurface")
                {
                    renderer.enabled = (layers & Layers.Surface) != 0;
                }
                else if (shader == "Fodinae/UI/PlanetAtmosphere")
                {
                    renderer.enabled = (layers & Layers.Atmosphere) != 0;
                }
            }
        }

        private static void BeginPhase(int index, MenuSceneryController scenery)
        {
            _phase = index;
            Phase phase = Phases[index];

            PlanetFieldBaker.ForceProcedural = phase.Procedural;
            scenery.RefreshFields();
            scenery.SetDescentFraming(phase.Framing, LandingSiteDirection);
            SetPlanetVisible(scenery, phase.Visible);

            _phaseStart = Time.realtimeSinceStartup;
            _frames = 0;
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            _running = false;
            PlanetFieldBaker.ForceProcedural = false;

            MenuSceneryController? scenery = MenuSceneryController.Current;
            if (scenery != null)
            {
                SetPlanetVisible(scenery, Layers.Both);
            }

            var report = new StringBuilder("[Замер] Стоимость кадра планеты\n");
            foreach (string line in Results)
            {
                report.Append("  ").AppendLine(line);
            }

            Debug.Log(report.ToString());
            EditorApplication.isPlaying = false;
        }
    }
}
