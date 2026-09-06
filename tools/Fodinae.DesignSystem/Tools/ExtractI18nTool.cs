using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace Fodinae.DesignSystem.Tools;

internal static class ExtractI18nTool
{
    public static int Run(string[] args)
    {
        string root = GetRoot();
        string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
        string indexPath = Path.Combine(root, "index.html");

        var skipTags = new HashSet<string> { "script", "style", "svg", "defs", "g", "path", "use", "circle", "rect", "title" };

        var parser = new HtmlParser();
        var doc = parser.ParseDocument(File.ReadAllText(indexPath));

        var found = new List<(string text, string key)>();
        void Walk(IEnumerable<INode> stack, INode node)
        {
            var newStack = stack.Append(node);
            if (node.NodeType == NodeType.Text)
            {
                string text = Regex.Replace(node.TextContent, @"\s+", " ").Trim();
                if (!string.IsNullOrEmpty(text) && Regex.IsMatch(text, "[A-Za-zА-Яа-яЁё]"))
                {
                    if (newStack.Any(n => n is IElement el && skipTags.Contains(el.TagName)))
                        return;
                    bool notr = newStack.Any(n => n is IElement el && el.GetAttribute("translate") == "no");
                    bool devDrawer = newStack.Any(n => n is IElement el && el.GetAttribute("class")?.Contains("dev-drawer") == true);
                    if (!notr && !devDrawer)
                    {
                        found.Add((text, ""));
                    }
                }
            }
            else if (node is IElement el)
            {
                foreach (var child in el.Children)
                {
                    Walk(newStack, child);
                }
            }
        }

        Walk(Array.Empty<INode>(), doc.DocumentElement);

        Console.WriteLine($"текстовых узлов: {found.Count}");
        return 0;
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
