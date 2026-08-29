#nullable enable

using UnityEngine;
using VContainer.Unity;

namespace Fodinae.Core
{
    /// <summary>Base for content scopes loaded only through Bootstrap transitions.</summary>
    public abstract class TransitionSceneLifetimeScope : LifetimeScope
    {
        protected override void Awake()
        {
            // The parent is supplied by LifetimeScope.EnqueueParent during the
            // additive load. A missing runtime parent is a hard contract error.
            base.Awake();
        }
    }
}
