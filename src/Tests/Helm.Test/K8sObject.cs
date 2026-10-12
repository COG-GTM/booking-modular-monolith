using YamlDotNet.RepresentationModel;

namespace Helm.Test;

/// <summary>A rendered Kubernetes object with path-based accessors over the YAML tree.</summary>
public sealed class K8sObject(YamlMappingNode root)
{
    public YamlMappingNode Root { get; } = root;

    public string Kind => Get("kind")!;

    public string Name => Get("metadata.name")!;

    public string ApiVersion => Get("apiVersion")!;

    public override string ToString() => $"{Kind}/{Name}";

    /// <summary>Scalar value at a dotted path; list elements by index (<c>spec.ports.0.name</c>) or by <c>name</c> (<c>spec.ports[grpc].port</c>).</summary>
    public string? Get(string path) => Node(path) is YamlScalarNode s ? s.Value : null;

    public bool Has(string path) => Node(path) is not null;

    public YamlNode? Node(string path)
    {
        YamlNode? current = Root;
        foreach (var segment in Tokenize(path))
        {
            if (current is null)
            {
                return null;
            }

            current = segment switch
            {
                { ByName: { } byName } => (current as YamlSequenceNode)
                    ?.Children.OfType<YamlMappingNode>()
                    .FirstOrDefault(m => Scalar(m, "name") == byName),
                { Index: { } idx } => current is YamlSequenceNode seq && idx < seq.Children.Count
                    ? seq.Children[idx]
                    : null,
                _ => current is YamlMappingNode map
                && map.Children.TryGetValue(new YamlScalarNode(segment.Key), out var child)
                    ? child
                    : null,
            };
        }

        return current;
    }

    public IReadOnlyList<YamlMappingNode> List(string path) =>
        Node(path) is YamlSequenceNode seq ? seq.Children.OfType<YamlMappingNode>().ToList() : [];

    public IReadOnlyDictionary<string, string> Map(string path) =>
        Node(path) is YamlMappingNode map
            ? map.Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv => ((YamlScalarNode)kv.Value).Value!,
                StringComparer.Ordinal
            )
            : new Dictionary<string, string>(StringComparer.Ordinal);

    public static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s ? s.Value : null;

    private static IEnumerable<Segment> Tokenize(string path)
    {
        foreach (var raw in path.Split('.'))
        {
            var open = raw.IndexOf('[', StringComparison.Ordinal);
            if (open >= 0 && raw.EndsWith(']'))
            {
                var key = raw[..open];
                var selector = raw[(open + 1)..^1];
                if (key.Length > 0)
                {
                    yield return new Segment(key, null, null);
                }

                yield return new Segment(string.Empty, null, selector);
            }
            else if (int.TryParse(raw, out var idx))
            {
                yield return new Segment(string.Empty, idx, null);
            }
            else
            {
                yield return new Segment(raw, null, null);
            }
        }
    }

    private readonly record struct Segment(string Key, int? Index, string? ByName);
}
