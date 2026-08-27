#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Fodinae.Core.Lifecycle
{
    [DisallowMultipleComponent]
    public sealed class ContentSceneRoot : MonoBehaviour
    {
        [SerializeField]
        private GameObject _contentRoot = null!;
        [SerializeField]
        private Transform _servicesRoot = null!;
        [SerializeField]
        private Transform _runtimeRoot = null!;
        [SerializeField]
        private Transform _robotsRoot = null!;
        [SerializeField]
        private Transform _buildingsRoot = null!;
        [SerializeField]
        private Transform _vfxRoot = null!;
        [SerializeField]
        private Transform _floatingUIRoot = null!;
        [SerializeField]
        private Transform _audioEventsRoot = null!;
        [SerializeField]
        private bool _keepContentActiveWhilePreparing;

        private readonly List<ILifecycleParticipant> _participants = [];
        private LifecycleGraph? _graph;
        private IObjectResolver? _resolver;
        private ulong _generation;

        public GameObject ContentRoot => _contentRoot;

        public Transform ServicesRoot => _servicesRoot;

        public Transform RuntimeRoot => _runtimeRoot;

        public ulong Generation => _generation;

        public bool IsEntered { get; private set; }

        public void BindResolver(IObjectResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public void ValidateOrThrow()
        {
            var errors = new List<string>();
            Require(_contentRoot, nameof(_contentRoot), errors);
            Require(_servicesRoot, nameof(_servicesRoot), errors);
            Require(_runtimeRoot, nameof(_runtimeRoot), errors);
            Require(_robotsRoot, nameof(_robotsRoot), errors);
            Require(_buildingsRoot, nameof(_buildingsRoot), errors);
            Require(_vfxRoot, nameof(_vfxRoot), errors);
            Require(_floatingUIRoot, nameof(_floatingUIRoot), errors);
            Require(_audioEventsRoot, nameof(_audioEventsRoot), errors);

            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.targetTexture == null && camera.enabled)
                    {
                        camera.enabled = false;
                        camera.tag = "Untagged";
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Scene '{gameObject.scene.name}' lifecycle hierarchy is invalid:\n - " +
                    string.Join("\n - ", errors));
            }
        }

        public async UniTask PrepareAsync(
            IObjectResolver fallbackResolver,
            ulong generation,
            CancellationToken cancellationToken)
        {
            ValidateOrThrow();
            IObjectResolver resolver = _resolver ?? fallbackResolver;
            _generation = generation;
            if (_graph != null)
            {
                var existingContext = new LifecycleContext(gameObject.scene, generation, resolver);
                await _graph.InitializeAndPrepareAsync(existingContext, cancellationToken);
                return;
            }

            _participants.Clear();
            var behaviours = new List<MonoBehaviour>();
            gameObject.GetComponentsInChildren(includeInactive: true, behaviours);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is ILifecycleParticipant participant)
                {
                    _participants.Add(participant);
                }
            }

            _graph = new LifecycleGraph(_participants);
            bool keepActive = _keepContentActiveWhilePreparing || _contentRoot.GetComponent<UnityEngine.UIElements.UIDocument>() != null;
            _contentRoot.SetActive(keepActive);
            if (_runtimeRoot != null && _runtimeRoot.gameObject != _contentRoot)
            {
                _runtimeRoot.gameObject.SetActive(false);
            }

            var context = new LifecycleContext(gameObject.scene, generation, resolver);
            await _graph.InitializeAndPrepareAsync(context, cancellationToken);
        }

        public async UniTask EnterAsync(CancellationToken cancellationToken)
        {
            LifecycleGraph graph = _graph ?? throw new InvalidOperationException(
                $"Scene '{gameObject.scene.name}' must be prepared before Enter.");
            _contentRoot.SetActive(true);
            if (_runtimeRoot != null && _runtimeRoot.gameObject != _contentRoot)
            {
                _runtimeRoot.gameObject.SetActive(true);
            }

            try
            {
                await graph.EnterAsync(cancellationToken);
                IsEntered = true;
            }
            catch
            {
                _contentRoot.SetActive(false);
                if (_runtimeRoot != null && _runtimeRoot.gameObject != _contentRoot)
                {
                    _runtimeRoot.gameObject.SetActive(false);
                }

                throw;
            }
        }

        public async UniTask ExitAsync(CancellationToken cancellationToken)
        {
            if (_graph != null)
            {
                await _graph.ExitAsync(cancellationToken);
            }

            IsEntered = false;
            _contentRoot.SetActive(false);
            if (_runtimeRoot != null && _runtimeRoot.gameObject != _contentRoot)
            {
                _runtimeRoot.gameObject.SetActive(false);
            }
        }

        public async UniTask DisposeAsync()
        {
            if (_graph != null)
            {
                await _graph.DisposeAsync();
                _graph = null;
            }

            _participants.Clear();
            _resolver = null;
        }

        public Transform GetRuntimeOwner(RuntimeOwner owner) => owner switch
        {
            RuntimeOwner.Robots => _robotsRoot,
            RuntimeOwner.Buildings => _buildingsRoot,
            RuntimeOwner.Vfx => _vfxRoot,
            RuntimeOwner.FloatingUI => _floatingUIRoot,
            RuntimeOwner.AudioEvents => _audioEventsRoot,
            _ => _runtimeRoot,
        };

        private static void Require(UnityEngine.Object? value, string name, ICollection<string> errors)
        {
            if (value == null)
            {
                errors.Add($"required reference {name} is missing");
            }
        }
    }
}
