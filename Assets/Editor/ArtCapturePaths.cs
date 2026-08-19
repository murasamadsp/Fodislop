#nullable enable

using System.IO;
using UnityEngine;

namespace Fodinae.Editor
{
    // Shared output location for the art self-inspection captures.
    //
    // Deliberately OUTSIDE Assets/. When these PNGs lived in Assets/Textures/UI
    // every capture triggered an asset import, and the project's texture import
    // policy brought them in as sprites - which logged sprite-rect and
    // memoryless-depth warnings (with full native stack traces) into the console
    // on every single capture. Nothing in the game references them; they exist
    // only to be looked at, so they do not belong in the asset database at all.
    internal static class ArtCapturePaths
    {
        private const string FolderName = "ArtCaptures";

        public static string Resolve(string fileName)
        {
            // Application.dataPath is <project>/Assets, so its parent is the
            // project root - a sibling of Assets/, which Unity does not scan.
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string folder = Path.Combine(projectRoot, FolderName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }
    }
}
