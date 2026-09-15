#nullable enable

namespace Fodinae.UI;
public static class GatewayDevFlags
{
    public const string ForceGatesPrefsKey = "Fodinae.Gateway.ForceGates";

    public static bool ForceGates
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(ForceGatesPrefsKey, true);
#else
            return false;
#endif
        }
    }
}
