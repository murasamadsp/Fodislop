#nullable enable

using Fodinae.Core.Localization;
using Fodinae.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.Core
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BootstrapLoadingScreen : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private BootstrapLifetimeScope _bootstrap = null!;

        [Inject]
        private ILocalizationService _localization = null!;

        private VisualElement? _overlay;
        private Label? _phase;
        private bool _initialized;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            UIDocument document = GetComponent<UIDocument>();
            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/BootstrapLoadingScreen")
                ?? throw new System.InvalidOperationException("Required UI resource 'UI/BootstrapLoadingScreen' was not found.");

            VisualElement root = document.rootVisualElement;
            root.Clear();
            VisualElement tree = asset.CloneTree();
            root.Add(tree);
            UILocalizer.Apply(tree, _localization);
            _overlay = tree.Q<VisualElement>("BootstrapLoadingOverlay");
            _phase = tree.Q<Label>("BootstrapLoadingPhase");

            _bootstrap.TransitionStarted += Show;
            _bootstrap.TransitionCompleted += Hide;
            _bootstrap.TransitionFailed += OnTransitionFailed;
            _localization.RegisterLocalizable(this);
            _initialized = true;
            Hide(string.Empty);
        }

        public void ApplyLocalizedText()
        {
            if (_overlay?.parent != null)
            {
                UILocalizer.Apply(_overlay.parent, _localization);
            }
        }

        private void OnDestroy()
        {
            if (_bootstrap != null)
            {
                _bootstrap.TransitionStarted -= Show;
                _bootstrap.TransitionCompleted -= Hide;
                _bootstrap.TransitionFailed -= OnTransitionFailed;
            }

            _localization?.UnregisterLocalizable(this);
        }

        private void Show(string sceneName)
        {
            // The MainMenu -> MainGame transition is owned entirely by the MainMenu
            // descent screen and loader (LoaderContainer with planet animation & phase steps).
            // Do not show the generic bootstrap overlay over it.
            if (string.Equals(sceneName, "MainGame", System.StringComparison.Ordinal))
            {
                return;
            }

            if (_phase != null)
            {
                _phase.text = $"{_localization.Get("network.connecting")} {sceneName}";
            }

            _overlay?.EnableInClassList("bootstrap-loading-overlay--visible", true);
        }

        private void Hide(string _)
        {
            _overlay?.EnableInClassList("bootstrap-loading-overlay--visible", false);
        }

        private void OnTransitionFailed(string _, System.Exception __)
        {
            Hide(string.Empty);
        }
    }
}
