using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>Reads an icon name as a model or Mermaid writes it: "fa:fa-database", "mdi:email", "db" and "postgres" all find Sprawl's icon.</summary>
public static class IconNames
{
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["person"] = "user", ["actor"] = "user", ["customer"] = "user", ["people"] = "users", ["team"] = "users", ["group"] = "users",
        ["db"] = "database", ["sql"] = "database", ["postgres"] = "database", ["postgresql"] = "database", ["mysql"] = "database", ["sqlite"] = "database",
        ["mongo"] = "database", ["mongodb"] = "database", ["redis"] = "cache", ["memcached"] = "cache",
        ["kafka"] = "queue", ["rabbitmq"] = "queue", ["sqs"] = "queue", ["inbox"] = "queue", ["stream"] = "queue", ["topic"] = "queue",
        ["mobile"] = "phone", ["smartphone"] = "phone", ["browser"] = "globe", ["web"] = "globe", ["internet"] = "globe", ["world"] = "globe",
        ["desktop"] = "monitor", ["computer"] = "laptop", ["email"] = "mail", ["envelope"] = "mail", ["chat"] = "message", ["comment"] = "message",
        ["notification"] = "bell", ["storage"] = "disk", ["hdd"] = "disk", ["hard-drive"] = "disk", ["s3"] = "disk", ["ram"] = "memory",
        ["docker"] = "container", ["kubernetes"] = "container", ["k8s"] = "container", ["pod"] = "container", ["lambda"] = "function", ["serverless"] = "function",
        ["gear"] = "settings", ["cog"] = "settings", ["config"] = "settings", ["lightning"] = "bolt", ["zap"] = "bolt", ["event"] = "bolt",
        ["alert"] = "warning", ["exclamation"] = "warning", ["check"] = "ok", ["success"] = "ok", ["fail"] = "error", ["failure"] = "error",
        ["dollar"] = "money", ["billing"] = "payment", ["credit-card"] = "payment", ["shopping-cart"] = "cart", ["robot"] = "bot", ["ai"] = "sparkles",
        ["llm"] = "brain", ["lb"] = "load-balancer", ["balancer"] = "load-balancer", ["proxy"] = "load-balancer", ["gateway"] = "api", ["plug"] = "api",
        ["chart"] = "bar-chart", ["metrics"] = "line-chart", ["monitoring"] = "gauge", ["dashboard"] = "gauge", ["docs"] = "book", ["documentation"] = "book",
        ["house"] = "home", ["office"] = "building", ["company"] = "building", ["lightbulb"] = "idea", ["wrench"] = "tool", ["refresh"] = "sync",
        ["github"] = "git", ["branch"] = "git", ["cli"] = "terminal", ["shell"] = "terminal", ["source"] = "code", ["auth"] = "key", ["password"] = "key",
        ["security"] = "shield", ["secure"] = "lock", ["timer"] = "clock", ["schedule"] = "calendar", ["cron"] = "clock", ["photo"] = "image",
    };

    public static string Normalize(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        foreach (var prefix in new[] { "fa:", "fas:", "far:", "fab:", "mdi:", "logos:", "lucide:" })
            if (n.StartsWith(prefix, StringComparison.Ordinal)) { n = n[prefix.Length..]; break; }
        if (n.StartsWith("fa-", StringComparison.Ordinal)) n = n[3..];
        n = n.Replace(' ', '-').Replace('_', '-');
        return Icons.All.Contains(n) ? n : Synonyms.GetValueOrDefault(n, n);
    }
}
