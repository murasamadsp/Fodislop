using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Internal;

namespace VContainer.Unity
{
    public sealed class VContainerParentTypeReferenceNotFound : Exception
    {
        public readonly Type ParentType;

        public VContainerParentTypeReferenceNotFound(Type parentType, string message)
            : base(message)
        {
            ParentType = parentType;
        }

        public VContainerParentTypeReferenceNotFound()
            : base()
        {
        }

        public VContainerParentTypeReferenceNotFound(string message)
            : base(message)
        {
        }

        public VContainerParentTypeReferenceNotFound(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public partial class LifetimeScope
    {
        private static readonly List<LifetimeScope> WaitingList = new List<LifetimeScope>();

        private static void EnqueueAwake(LifetimeScope lifetimeScope)
        {
            WaitingList.Add(lifetimeScope);
        }

        private static void CancelAwake(LifetimeScope lifetimeScope)
        {
            WaitingList.Remove(lifetimeScope);
        }

        private static void AwakeWaitingChildren(LifetimeScope awakenParent)
        {
            if (WaitingList.Count <= 0)
            {
                return;
            }

            using (ListPool<LifetimeScope>.Get(out var buffer))
            {
                for (var i = WaitingList.Count - 1; i >= 0; i--)
                {
                    var waitingScope = WaitingList[i];
                    if (waitingScope.ParentReference.Type == awakenParent.GetType())
                    {
                        waitingScope.ParentReference.Object = awakenParent;
                        WaitingList.RemoveAt(i);
                        buffer.Add(waitingScope);
                    }
                }

                foreach (var waitingScope in buffer)
                {
                    waitingScope.Awake();
                }
            }
        }
    }
}
