#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class GuidTruthDump
    {
        [InitializeOnLoadMethod]
        public static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GUIDTRUTH] === BEGIN ===");

            string[] guids =
            {
                "e55a1800031c9374ba92fb984b2216f6", // PlayerMovementController
                "ea1ef0b5d60544e3b4923a071c651690", // Robot
                "4f9b8c7d6e5a4b3c2d1e0f9a8b7c6d5e", // PlayerInteractionController
                "309b32c6878a84474a1b2376baed4735", // PlayerInputHandler
                "7d01101e153d8b846858b2936a854cc6", // RobotHeadlight
            };

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                sb.AppendLine($"[GUIDTRUTH] guid {g} -> '{path}'");
            }

            string[] paths =
            {
                "Assets/Scripts/Player/Logic/PlayerMovementController.cs",
                "Assets/Scripts/Game/Robot.cs",
                "Assets/Scripts/Player/PlayerInteractionController.cs",
                "Assets/Scripts/Player/Input/PlayerInputHandler.cs",
                "Assets/Scripts/Game/RobotHeadlight.cs",
            };

            foreach (var p in paths)
            {
                var g = AssetDatabase.AssetPathToGUID(p);
                var main = AssetDatabase.LoadMainAssetAtPath(p);
                sb.AppendLine($"[GUIDTRUTH] path {p} -> guid {g}, asset={(main == null ? "NULL" : main.GetType().FullName)}");
            }

            var prefab = AssetDatabase.LoadMainAssetAtPath("Assets/Player.prefab") as GameObject;
            if (prefab == null)
            {
                sb.AppendLine("[GUIDTRUTH] Player.prefab LOAD FAILED");
            }
            else
            {
                foreach (var c in prefab.GetComponents<Component>())
                {
                    if (c == null)
                    {
                        sb.AppendLine("[GUIDTRUTH] component: MISSING SCRIPT");
                    }
                    else if (c is MonoBehaviour mb)
                    {
                        var ms = MonoScript.FromMonoBehaviour(mb);
                        sb.AppendLine($"[GUIDTRUTH] component: {mb.GetType().FullName} script={(ms == null ? "NULL" : ms.name)}");
                    }
                    else
                    {
                        sb.AppendLine($"[GUIDTRUTH] component: {c.GetType().FullName}");
                    }
                }
            }

            sb.AppendLine("[GUIDTRUTH] === END ===");
            Debug.Log(sb.ToString());
        }
    }
}
#endif
