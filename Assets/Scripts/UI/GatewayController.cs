#nullable enable

using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Сцена Gateway: вход и онбординг перед главным меню.
    ///
    /// Поток сцен: Bootstrap → Gateway → MainMenu → MainGame. Раньше блок входа
    /// жил оверлеем внутри MainMenu.uxml; вынесен в свою сцену, чтобы меню не
    /// тащило чужой жизненный цикл, а ворота выгружались целиком.
    ///
    /// Онбординг показывается один раз — при первом запуске либо когда игрок
    /// открывает его сам. Пишет в те поля ClientConfig, которые действительно
    /// существуют: частоту кадров, вертикальную синхронизацию, пресет графики и
    /// приглушение звука в фоне.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GatewayController : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string OnboardingDonePrefsKey = "OnboardingCompleted1";

        // Состояние ворот. Ровно один класс на корне за раз: раньше видимость
        // была своя у каждого слоя, и ничто не мешало показать вход и онбординг
        // одновременно — онбординг просто ложился поверх формы.
        private const string StateAuthClass = "gateway--auth";
        private const string StateOnboardingClass = "gateway--onboarding";
        private const string StepActiveClass = "onb-step--active";
        private const string PillActiveClass = "onb-pill--active";
        private const string PillDoneClass = "onb-pill--done";
        private const string ButtonHiddenClass = "onb-btn--hidden";

        // Без префикса «Шаг N»: номер и тему шага уже несёт полоса пилюль
        // справа, и повтор только съедал ширину, из-за которой заголовок
        // наезжал на эту самую полосу.
        private static readonly string[] StepTitles =
        {
            "Доступность и визуальный комфорт",
            "Рендеринг и освещение",
            "Управление и звук",
        };

        private static readonly (string Label, int Value)[] FrameRates =
        {
            ("Без ограничений", -1),
            ("144 FPS", 144),
            ("120 FPS", 120),
            ("60 FPS", 60),
        };

        /// <summary>
        /// Пользовательский зум интерфейса — прямой аналог зума в браузере.
        ///
        /// PanelSettings работает в режиме ConstantPhysicalSize: размер элемента
        /// привязан к физическому размеру экрана, как CSS-пиксель. Это верно
        /// почти везде, но ломается там, где система врёт про DPI, — прежде
        /// всего на телевизорах и консолях, где Screen.dpi обычно 0 и в дело
        /// идёт fallbackDpi. Зум здесь и есть ручная поправка на такой случай.
        /// </summary>
        private static readonly (string Label, float Value)[] UIScales =
        {
            ("100% (Штатный)", 1.00f),
            ("115% (Увеличенный)", 1.15f),
            ("130% (Крупный)", 1.30f),
        };

        private UIDocument _document = null!;
        private VisualElement _root = null!;
        private VisualElement? _gatewayRoot;
        private VisualElement? _onboardingOverlay;
        private AuthGate? _authGate;
        private int _step;
        private bool _leaving;
        private bool _initialized;

        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private ISceneCoordinator _sceneCoordinator = null!;

        [Inject]
        public void Construct(IClientConfigManager clientConfig, ISceneCoordinator sceneCoordinator)
        {
            _clientConfig = clientConfig;
            _sceneCoordinator = sceneCoordinator;
            EnsureInitialized();
        }

        private void OnEnable()
        {
            if (_clientConfig == null || _sceneCoordinator == null)
            {
                return;
            }

            _document = GetComponent<UIDocument>();
            if (_document != null && _document.rootVisualElement != null && _document.rootVisualElement.childCount == 0)
            {
                _initialized = false;
            }

            EnsureInitialized();
        }

        private void Start()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_clientConfig == null || _sceneCoordinator == null)
            {
                return;
            }

            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                return;
            }

            if (_initialized && _document.rootVisualElement.childCount > 0)
            {
                return;
            }

            var asset = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.GatewayUxml);
            if (asset == null)
            {
                Debug.LogWarning($"[Gateway] UI resource '{ProjectRuntimeContracts.ResourcePaths.GatewayUxml}' is missing; returning to main menu.");
                GoToMainMenu();
                return;
            }

            _root = _document.rootVisualElement;
            if (_root == null)
            {
                return;
            }

            _initialized = true;
            _root.Clear();

            VisualElement tree = asset.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            // Тир раскладки вместо @media — как и в остальных экранах.
            UILayoutTier.Attach(tree);
            _root = tree;

            // Состояние ставится на тот же элемент, на котором оно задано в
            // разметке. Иначе начальный gateway--auth из UXML снять было бы
            // некому и форма входа осталась бы видимой поверх онбординга.
            _gatewayRoot = _root.Q<VisualElement>("GatewayRoot") ?? _root;

            _authGate = AuthGate.TryCreate(_root, _clientConfig);
            if (_authGate == null)
            {
                Debug.LogWarning("[Gateway] Ворота входа не собрались — сразу уходим в меню.");
                GoToMainMenu();
                return;
            }

            _authGate.Passed += OnAuthPassed;

            ApplySavedUIScale();
            BindOnboarding();

            SetState(StateAuthClass);
            _authGate.Show();
            Debug.Log("[Gateway] Gateway UI initialized and displayed.");
        }

        private void OnAuthPassed()
        {
            bool alreadyDone = !GatewayDevFlags.ForceGates
                && PlayerPrefs.GetInt(OnboardingDonePrefsKey, 0) == 1;

            if (alreadyDone || _onboardingOverlay == null)
            {
                GoToMainMenu();
                return;
            }

            SetState(StateOnboardingClass);
            ApplyStep(0);
        }

        /// <summary>Включает ровно одно состояние ворот и гасит остальные.</summary>
        private void SetState(string state)
        {
            if (_gatewayRoot == null)
            {
                return;
            }

            _gatewayRoot.EnableInClassList(StateAuthClass, state == StateAuthClass);
            _gatewayRoot.EnableInClassList(StateOnboardingClass, state == StateOnboardingClass);
        }

        // ─────────────────────────────────────────────────────────────
        // Онбординг
        // ─────────────────────────────────────────────────────────────

        private void BindOnboarding()
        {
            if (_clientConfig == null)
            {
                return;
            }

            _onboardingOverlay = _root.Q<VisualElement>("OnboardingOverlay");
            if (_onboardingOverlay == null)
            {
                return;
            }

            ClientConfig config = _clientConfig.Config;

            var uiScale = _root.Q<DropdownField>("OnbUIScale");
            if (uiScale != null)
            {
                var labels = new System.Collections.Generic.List<string>();
                foreach ((string label, float _) in UIScales)
                {
                    labels.Add(label);
                }

                uiScale.choices = labels;
                uiScale.index = IndexOfUIScale(config.UIScale);

                // Применяем сразу при выборе, а не по кнопке «Далее»: смысл
                // этой настройки в том, чтобы увидеть результат на себе.
                uiScale.RegisterValueChangedCallback(_ => ApplyUIScale(ValueOfUIScale(uiScale.index)));
            }

            var frameRate = _root.Q<DropdownField>("OnbFrameRate");
            if (frameRate != null)
            {
                var labels = new System.Collections.Generic.List<string>();
                foreach ((string label, int _) in FrameRates)
                {
                    labels.Add(label);
                }

                frameRate.choices = labels;
                frameRate.index = IndexOfFrameRate(config.TargetFrameRate);
            }

            var preset = _root.Q<DropdownField>("OnbGraphicsPreset");
            if (preset != null)
            {
                preset.choices = new System.Collections.Generic.List<string>
                {
                    "Ультра", "Высокое", "Среднее", "Быстрое",
                };
                preset.index = 0;
            }

            var vsync = _root.Q<Toggle>("OnbVSync");
            if (vsync != null)
            {
                vsync.SetValueWithoutNotify(config.VSync);
            }

            var mute = _root.Q<Toggle>("OnbMuteInBackground");
            if (mute != null)
            {
                mute.SetValueWithoutNotify(config.MuteAudioInBackground);
            }

            var prev = _root.Q<Button>("OnbPrevButton");
            if (prev != null)
            {
                prev.clicked += () => ApplyStep(_step - 1);
            }

            var next = _root.Q<Button>("OnbNextButton");
            if (next != null)
            {
                next.clicked += OnNext;
            }

            var skip = _root.Q<Button>("OnbSkipButton");
            if (skip != null)
            {
                skip.clicked += FinishOnboarding;
            }
        }

        private void OnNext()
        {
            if (_step >= StepTitles.Length - 1)
            {
                FinishOnboarding();
                return;
            }

            ApplyStep(_step + 1);
        }

        private void ApplyStep(int step)
        {
            _step = Mathf.Clamp(step, 0, StepTitles.Length - 1);

            for (int i = 0; i < StepTitles.Length; i++)
            {
                var content = _root.Q<VisualElement>($"OnbStep{i + 1}");
                content?.EnableInClassList(StepActiveClass, i == _step);

                var pill = _root.Q<Label>($"OnbPill{i + 1}");
                if (pill == null)
                {
                    continue;
                }

                pill.EnableInClassList(PillActiveClass, i == _step);
                pill.EnableInClassList(PillDoneClass, i < _step);
            }

            var title = _root.Q<Label>("OnboardingTitle");
            if (title != null)
            {
                title.text = StepTitles[_step];
            }

            // На первом шаге назад некуда — кнопка прячется, но место сохраняет,
            // иначе футер дёргается при переходе между шагами.
            _root.Q<Button>("OnbPrevButton")?.EnableInClassList(ButtonHiddenClass, _step == 0);

            var next = _root.Q<Button>("OnbNextButton");
            if (next != null)
            {
                next.text = _step >= StepTitles.Length - 1 ? "НАЧАТЬ ЭКСПЕДИЦИЮ →" : "ДАЛЕЕ →";
            }
        }

        private void FinishOnboarding()
        {
            SaveSettings();
            PlayerPrefs.SetInt(OnboardingDonePrefsKey, 1);
            PlayerPrefs.Save();
            GoToMainMenu();
        }

        private void SaveSettings()
        {
            _clientConfig.UpdateAndSave(config =>
            {
                var uiScale = _root.Q<DropdownField>("OnbUIScale");
                if (uiScale != null)
                {
                    config.UIScale = ValueOfUIScale(uiScale.index);
                }

                var frameRate = _root.Q<DropdownField>("OnbFrameRate");
                if (frameRate != null && frameRate.index >= 0 && frameRate.index < FrameRates.Length)
                {
                    config.TargetFrameRate = FrameRates[frameRate.index].Value;
                }

                var vsync = _root.Q<Toggle>("OnbVSync");
                if (vsync != null)
                {
                    config.VSync = vsync.value;
                }

                var mute = _root.Q<Toggle>("OnbMuteInBackground");
                if (mute != null)
                {
                    config.MuteAudioInBackground = mute.value;
                }
            });
        }

        /// <summary>
        /// Кладёт сохранённый зум в PanelSettings. Раньше это делал только
        /// PauseMenu при своей инициализации — то есть настройка вступала в
        /// силу лишь после того, как игрок хоть раз открыл паузу уже в игре,
        /// а ворота и меню всегда рисовались со стопроцентным масштабом.
        /// </summary>
        private void ApplySavedUIScale()
        {
            if (_clientConfig == null)
            {
                return;
            }

            float saved = _clientConfig.Config.UIScale;

            // Ноль означает «в конфиге ничего нет» — множитель ноль погасил бы
            // весь интерфейс, поэтому такое значение трактуем как штатное.
            ApplyUIScale(saved <= 0f ? 1f : saved);
        }

        private void ApplyUIScale(float scale)
        {
            PanelSettings? panel = _document.panelSettings;
            if (panel == null)
            {
                return;
            }

            // Диапазон тот же, что проверяет ClientConfigManager.
            panel.scale = Mathf.Clamp(scale, 0.5f, 2f);
        }

        private static float ValueOfUIScale(int index)
        {
            return index >= 0 && index < UIScales.Length ? UIScales[index].Value : 1f;
        }

        private static int IndexOfUIScale(float value)
        {
            for (int i = 0; i < UIScales.Length; i++)
            {
                if (Mathf.Abs(UIScales[i].Value - value) < 0.001f)
                {
                    return i;
                }
            }

            return 0;
        }

        private static int IndexOfFrameRate(int value)
        {
            for (int i = 0; i < FrameRates.Length; i++)
            {
                if (FrameRates[i].Value == value)
                {
                    return i;
                }
            }

            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        // Переход в меню
        // ─────────────────────────────────────────────────────────────

        private void GoToMainMenu()
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;
            LoadMainMenuAsync().Forget();
        }

        private async UniTaskVoid LoadMainMenuAsync()
        {
            if (_sceneCoordinator == null)
            {
                return;
            }

            await _sceneCoordinator.TransitionAsync(
                MainMenuSceneName,
                destroyCancellationToken);
        }
    }
}
