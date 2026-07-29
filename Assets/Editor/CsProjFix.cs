using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Fodinae.Editor
{
    public class CsProjFix : AssetPostprocessor
    {
        // This method is called after Unity generates the .csproj file
        protected static string OnGeneratedCSProject(string path, string content)
        {
            // Use Regex to find the <LangVersion> tag and replace it
            // Unity usually sets this to specific versions like '9.0' or 'default'
            // Пингуем стабильный C# 12 (НЕ preview): детерминированный языковой уровень,
            // должен совпадать с Directory.Build.props
            const string pattern = @"<LangVersion>.*?</LangVersion>";
            const string replacement = "<LangVersion>12.0</LangVersion>";

            return Regex.Replace(content, pattern, replacement);
        }
    }
}
