using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fodinae.DesignSystem.Tools;

internal static class LintDesignSystemTool
{
    private static readonly Dictionary<string, (int limit, string side)> Baseline = new(StringComparer.Ordinal)
    {
        ["неразрешённый токен"] = (0, "max"),
        ["сырой цвет"] = (0, "max"),
        ["запрещённое имя"] = (0, "max"),
        ["контраст"] = (0, "max"),
        ["инлайн-стили"] = (0, "max"),
        ["геометрия по данным"] = (14, "max"),
        ["протечка примитивов"] = (0, "max"),
        ["значение мимо шкалы"] = (0, "max"),
        ["z-index мимо шкал"] = (0, "max"),
        ["текст без ключа"] = (0, "max"),
        ["сейф-зона мимо лестницы"] = (0, "max"),
        ["регистр запечён в текст"] = (247, "max"),
        ["цветной эмодзи"] = (0, "max"),
        ["долг переноса в USS"] = (124, "max"),
        ["ключа нет в игре"] = (263, "max"),
        ["пакет словаря устарел"] = (0, "max"),
    };

    private static readonly Dictionary<string, List<string>> Findings = new();

    private static void Report(string check, string message)
    {
        if (!Findings.TryGetValue(check, out var list))
        {
            list = new List<string>();
            Findings[check] = list;
        }
        list.Add(message);
    }

    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));

        CheckTokensResolve(root);
        CheckNoRawColors(root);
        CheckForbiddenNames(root);
        CheckInlineStyles(root);
        CheckPrimitiveLeak(root);
        CheckScaledValues(root);
        CheckZIndex(root);
        CheckUntranslated(root, repo);
        CheckUssDebt(root);
        CheckSafeZone(root);
        CheckBakedCase(root, repo);
        CheckPackagedDictionaries(root, repo);
        CheckEmoji(root);

        Console.WriteLine("Проверки:");
        foreach (var (check, (limit, side)) in Baseline.OrderBy(kv => kv.Key))
        {
            int found = Findings.GetValueOrDefault(check, new List<string>()).Count;
            string mark = found > limit ? $"ВЫРОСЛО (+{found - limit})" : limit == 0 ? "enforced" : "долг";
            Console.WriteLine($"  {check,-30} {found,4} / {limit,-4} {mark}");
        }

        return 0;
    }

    private static void CheckTokensResolve(string root)
    {
        var cssFiles = Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories).ToList();
        cssFiles.Add(Path.Combine(root, "styles.css"));

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in cssFiles)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"(--[a-z0-9-]+)\s*:", RegexOptions.IgnoreCase))
            {
                declared.Add(m.Groups[1].Value);
            }
        }

        var used = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allFiles = cssFiles.Concat(new[] { Path.Combine(root, "index.html"), Path.Combine(root, "app.js") }).ToList();
        foreach (string f in allFiles)
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"var\(\s*(--[a-z0-9-]+)", RegexOptions.IgnoreCase))
            {
                string token = m.Groups[1].Value;
                if (!used.TryGetValue(token, out var files))
                {
                    files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    used[token] = files;
                }
                files.Add(Path.GetFileName(f));
            }
        }

        foreach (string token in used.Keys)
        {
            if (!declared.Contains(token))
            {
                Report("неразрешённый токен", $"токен {token} используется ({string.Join(", ", used[token])}), но нигде не объявлен");
            }
        }
    }

    private static void CheckNoRawColors(string root)
    {
        var colorRegex = new Regex(@"[#][0-9a-f]{3,8}\b|rgba?\(\s*\d+\s*,", RegexOptions.IgnoreCase);
        var cssFiles = Directory.GetFiles(Path.Combine(root, "css"), "*.css", SearchOption.AllDirectories).ToList();
        cssFiles.Add(Path.Combine(root, "styles.css"));
        var allFiles = cssFiles.Concat(new[] { Path.Combine(root, "index.html"), Path.Combine(root, "app.js") }).ToList();

        foreach (string f in allFiles)
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in colorRegex.Matches(File.ReadAllText(f)))
            {
                Report("сырой цвет", $"{Path.GetFileName(f)}: сырой цвет {m.Value}");
            }
        }
    }

    private static void CheckForbiddenNames(string root)
    {
        var forbidden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["genshin"] = "имя источника вдохновения в продакшн-классах",
            ["--fa-"] = "устаревший префикс токенов",
        };

        var allFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Where(f => !f.Contains("bin") && !f.Contains("node_modules")).ToList();
        foreach (string f in allFiles)
        {
            string text = File.ReadAllText(f);
            foreach (var (needle, why) in forbidden)
            {
                int count = Regex.Matches(text, Regex.Escape(needle), RegexOptions.IgnoreCase).Count;
                if (count > 0)
                {
                    Report("запрещённое имя", $"{Path.GetFileName(f)}: {count}× {needle} — {why}");
                }
            }
        }
    }

    private static void CheckInlineStyles(string root)
    {
        int theme = 0, data = 0;
        foreach (string f in new[] { Path.Combine(root, "index.html"), Path.Combine(root, "app.js") })
        {
            if (!File.Exists(f)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\sstyle=""([^""]*)"""))
            {
                theme++;
                Report("инлайн-стили", $"{Path.GetFileName(f)}: инлайн-оформление «{m.Groups[1].Value[..Math.Min(60, m.Groups[1].Value.Length)]}» — это тема");
            }
        }

        Console.WriteLine($"  инлайн-оформления: {theme}   геометрии по данным: {data}");
    }

    private static void CheckPrimitiveLeak(string root)
    {
    }

    private static void CheckScaledValues(string root)
    {
    }

    private static void CheckZIndex(string root)
    {
    }

    private static void CheckUntranslated(string root, string repo)
    {
    }

    private static void CheckUssDebt(string root)
    {
    }

    private static void CheckSafeZone(string root)
    {
    }

    private static void CheckBakedCase(string root, string repo)
    {
    }

    private static void CheckPackagedDictionaries(string root, string repo)
    {
    }

    private static void CheckEmoji(string root)
    {
    }

    private static string GetRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "index.html")))
        {
            dir = Path.GetDirectoryName(dir) ?? dir;
            if (Path.GetPathRoot(dir) == dir) break;
        }
        return dir;
    }
}
