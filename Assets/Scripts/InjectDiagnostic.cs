using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.Scripts
{
    public class InjectDiagnostic : MonoBehaviour
    {
        private static readonly string LogPath = Path.Combine(Application.dataPath, "..", "inject_diagnostic.txt");
        private static readonly Type InjectType = typeof(InjectAttribute);
    }
}
