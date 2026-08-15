#nullable enable

using Fodinae.Player;
using UnityEngine;

namespace Fodinae.UI
{
    public static class ChatInput
    {
        public static bool IsFocused { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            IsFocused = false;
        }

        public static void OnFocus()
        {
            IsFocused = true;
        }

        public static void OnBlur()
        {
            IsFocused = false;
        }
    }
}
