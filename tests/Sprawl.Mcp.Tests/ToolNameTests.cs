using System.Reflection;
using System.Text.RegularExpressions;

namespace Sprawl.Mcp.Tests;

/// <summary>
/// Orbit Hub prefixes every tool (sprawl_get_canvas) and rewrites only backticked names in instructions and prompts, so every tool
/// name must be backticked, and other apps' tools written `app.tool` (`socrates.add_questions`). Names that are also plain words
/// (search, connect, disconnect, reorganize) cannot be told apart from prose, so only names with an underscore are checked.
/// </summary>
public sealed class ToolNameTests(McpHost host) : IClassFixture<McpHost>
{
    private static readonly string Everyday = McpHost.Token();
    private static readonly string[] OtherApps = ["add_questions", "add_cards", "update_question", "get_note_links", "read_note", "search_notes"];

    [Fact]
    public async Task Instructions_prompts_and_tool_descriptions_backtick_every_tool_name()
    {
        var (_, tools, _) = await Rpc.Send(host.Client(), McpHost.Token("canvas.read canvas.write canvas.organize canvas.admin"), "tools/list");
        var toolList = tools.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var names = toolList.Select(t => t.GetProperty("name").GetString()!).Concat(OtherApps).Where(n => n.Contains('_')).ToList();
        Assert.Contains("update_cards", names);

        var texts = new Dictionary<string, string> { ["server instructions"] = Instructions() };
        // The Hub rewrites tool descriptions too, so each tool's description and every parameter description are checked.
        foreach (var tool in toolList)
        {
            var name = tool.GetProperty("name").GetString()!;
            texts[$"tool {name}"] = tool.GetProperty("description").GetString()!;
            var parameters = new List<string>();
            Collect(tool.GetProperty("inputSchema"), parameters);
            texts[$"tool {name} parameters"] = string.Join("\n", parameters);
        }
        var (_, prompts, _) = await Rpc.Send(host.Client(), Everyday, "prompts/list");
        foreach (var prompt in prompts.GetProperty("result").GetProperty("prompts").EnumerateArray())
        {
            var name = prompt.GetProperty("name").GetString()!;
            var arguments = prompt.TryGetProperty("arguments", out var args)
                ? args.EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!, a => (object?)"3")
                : [];
            var (_, got, _) = await Rpc.Send(host.Client(), Everyday, "prompts/get", new Dictionary<string, object?> { ["name"] = name, ["arguments"] = arguments });
            texts[$"prompt {name}"] = got.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!;
        }
        Assert.True(texts.Count > 10);

        var bare = new List<string>();
        foreach (var (where, text) in texts)
            foreach (var tool in names)
                foreach (Match match in Regex.Matches(text, $@"(?<![`\w.]){Regex.Escape(tool)}(?![`\w])"))
                    bare.Add($"{where}: '{tool}' near \"{text.Substring(Math.Max(0, match.Index - 30), Math.Min(70, text.Length - Math.Max(0, match.Index - 30)))}\"");
        Assert.True(bare.Count == 0, "Bare tool names:\n" + string.Join("\n", bare));

        // Other apps' tools are written with their app's prefix.
        var all = string.Join("\n", texts.Values);
        Assert.Contains("`socrates.add_questions`", all);
        Assert.Contains("`heyann.add_cards`", all);
        Assert.DoesNotContain("`add_questions`", all);
        Assert.DoesNotContain("`add_cards`", all);
    }

    private static void Collect(System.Text.Json.JsonElement node, List<string> into)
    {
        if (node.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name == "description" && property.Value.ValueKind == System.Text.Json.JsonValueKind.String) into.Add(property.Value.GetString()!);
                else Collect(property.Value, into);
            }
        else if (node.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) Collect(item, into);
    }

    private static string Instructions() =>
        (string)typeof(Program).Assembly.GetType("Sprawl.Mcp.Instructions", throwOnError: true)!
            .GetField("Text", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
