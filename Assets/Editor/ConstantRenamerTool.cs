#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    /// <summary>
    /// Редакторский инструмент для поиска и форматирования имён констант (const) с использованием подчёркиваний (_).
    /// </summary>
    public class ConstantRenamerTool : EditorWindow
    {
        private struct ConstantRenameItem
        {
            public string FilePath;
            public string OldName;
            public string NewName;
            public bool Selected;
        }

        private List<ConstantRenameItem> _renameItems = new List<ConstantRenameItem>();
        private Vector2 _scrollPos;
        private string _targetDirectory = "Assets/Scripts";

        private static readonly string[] KnownWords = new string[]
        {
            "VISUAL", "ROTATION", "OFFSET", "MIN", "MAX", "SMOOTH", "TIME", "REFERENCE", "MOVE", "SPEED",
            "POINT", "COUNT", "SEGMENT", "DIST", "HOTBAR", "COLS", "ROWS", "INVENTORY", "CELL", "SIZE",
            "GAP", "ICON", "PANEL", "WIDTH", "PADDING", "LABEL", "FONT", "TITLE", "BAR", "HEIGHT",
            "BTN", "BONUS", "SKILL", "GRID", "DURATION", "FLOAT", "FADE", "START", "MESSAGES",
            "DEFAULT", "CHUNK", "UPDATE", "DELAY", "THRESHOLD", "MINIMAP", "COLLISION", "DEBUG", "RANGE",
            "PER", "PAGE", "TOTAL", "CELLS", "BANK", "PATH"
        };

        [MenuItem("Fodinae/Tools/Format Constants (Snake Case)")]
        public static void ShowWindow()
        {
            GetWindow<ConstantRenamerTool>("Format Constants").Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Форматирование констант с подчёркиваниями (_)", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _targetDirectory = EditorGUILayout.TextField("Папка для поиска:", _targetDirectory);

            if (GUILayout.Button("1. Сканировать константы", GUILayout.Height(30)))
            {
                ScanConstants();
            }

            if (_renameItems.Count > 0)
            {
                EditorGUILayout.Space(10);
                GUILayout.Label($"Найдено констант для форматирования: {_renameItems.Count}", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Выбрать все"))
                {
                    SetAllSelected(true);
                }
                if (GUILayout.Button("Снять выбор"))
                {
                    SetAllSelected(false);
                }
                EditorGUILayout.EndHorizontal();

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(350));
                for (int i = 0; i < _renameItems.Count; i++)
                {
                    var item = _renameItems[i];
                    EditorGUILayout.BeginHorizontal();
                    item.Selected = EditorGUILayout.Toggle(item.Selected, GUILayout.Width(25));
                    EditorGUILayout.LabelField(item.OldName, GUILayout.Width(180));
                    EditorGUILayout.LabelField("➔", GUILayout.Width(20));
                    EditorGUILayout.LabelField(item.NewName, GUILayout.Width(200));
                    EditorGUILayout.LabelField(Path.GetFileName(item.FilePath), EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                    _renameItems[i] = item;
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(10);
                if (GUILayout.Button("2. Применить переименование", GUILayout.Height(35)))
                {
                    ApplyRenames();
                }
            }
        }

        private void SetAllSelected(bool selected)
        {
            for (int i = 0; i < _renameItems.Count; i++)
            {
                var item = _renameItems[i];
                item.Selected = selected;
                _renameItems[i] = item;
            }
        }

        private void ScanConstants()
        {
            _renameItems.Clear();
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), _targetDirectory);
            if (!Directory.Exists(fullPath))
            {
                Debug.LogError($"Директория не найдена: {fullPath}");
                return;
            }

            string[] csFiles = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories);
            Regex constRegex = new Regex(@"\bconst\s+[\w\<\>]+\s+([A-Za-z0-9_]+)\b");

            HashSet<string> seen = new HashSet<string>();

            foreach (var file in csFiles)
            {
                string text = File.ReadAllText(file);
                var matches = constRegex.Matches(text);

                foreach (Match match in matches)
                {
                    string oldName = match.Groups[1].Value;
                    if (seen.Contains(oldName)) continue;

                    string newName = FormatConstantName(oldName);
                    if (oldName != newName)
                    {
                        seen.Add(oldName);
                        _renameItems.Add(new ConstantRenameItem
                        {
                            FilePath = file,
                            OldName = oldName,
                            NewName = newName,
                            Selected = true
                        });
                    }
                }
            }

            Debug.Log($"[ConstantRenamer] Сканирование завершено. Найдено {_renameItems.Count} констант.");
        }

        public static string FormatConstantName(string name)
        {
            if (name.Contains("_")) return name;

            // Исключения для коротких слов
            if (name == "TAG" || name == "COLS" || name == "ROWS" || name == "GAP" || name == "PADDING" || name == "WIDTH" || name == "HEIGHT" || name == "PAGES")
                return name;

            // Если имя полностью в VERCHNEY_ZAPISI (ALL_CAPS) без подчёркиваний
            if (IsAllCaps(name))
            {
                return SplitAllCaps(name);
            }

            // Иначе PascalCase / camelCase -> UPPER_SNAKE_CASE
            return PascalToUpperSnakeCase(name);
        }

        private static bool IsAllCaps(string name)
        {
            foreach (char c in name)
            {
                if (char.IsLower(c)) return false;
            }
            return true;
        }

        private static string SplitAllCaps(string name)
        {
            List<string> parts = new List<string>();
            string curr = name;

            while (curr.Length > 0)
            {
                bool matched = false;
                foreach (var word in KnownWords)
                {
                    if (curr.StartsWith(word))
                    {
                        parts.Add(word);
                        curr = curr.Substring(word.Length);
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    if (parts.Count > 0)
                        parts[parts.Count - 1] += curr[0];
                    else
                        parts.Add(curr[0].ToString());
                    curr = curr.Substring(1);
                }
            }

            return string.Join("_", parts);
        }

        private static string PascalToUpperSnakeCase(string name)
        {
            string result = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1_$2");
            result = Regex.Replace(result, @"([A-Z]+)([A-Z][a-z])", "$1_$2");
            return result.ToUpper();
        }

        private void ApplyRenames()
        {
            int renamedCount = 0;
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), _targetDirectory);
            string[] csFiles = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories);

            foreach (var item in _renameItems)
            {
                if (!item.Selected) continue;

                Regex replacementRegex = new Regex(@"\b" + Regex.Escape(item.OldName) + @"\b");

                foreach (var file in csFiles)
                {
                    string content = File.ReadAllText(file);
                    if (content.Contains(item.OldName))
                    {
                        string updated = replacementRegex.Replace(content, item.NewName);
                        File.WriteAllText(file, updated);
                    }
                }

                renamedCount++;
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Готово", $"Переименовано {renamedCount} констант по всему проекту!", "ОК");
            ScanConstants();
        }
    }
}
#endif
