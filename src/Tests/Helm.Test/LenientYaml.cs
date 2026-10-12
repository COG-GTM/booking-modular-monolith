using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Helm.Test;

/// <summary>
/// Loads YAML into <see cref="YamlNode"/> trees without rejecting duplicate mapping keys (last value wins,
/// which is how Helm/Kubernetes decode them). Duplicates are reported through <see cref="DuplicateKeys"/>.
/// </summary>
public sealed class LenientYaml
{
    private readonly List<string> _duplicates = [];

    public IReadOnlyList<string> DuplicateKeys => _duplicates;

    public IReadOnlyList<YamlNode> LoadAll(TextReader reader)
    {
        var parser = new Parser(reader);
        var documents = new List<YamlNode>();
        parser.Consume<StreamStart>();
        while (parser.TryConsume<DocumentStart>(out _))
        {
            if (!parser.Accept<DocumentEnd>(out _))
            {
                documents.Add(ReadNode(parser, "$"));
            }

            parser.Consume<DocumentEnd>();
        }

        return documents;
    }

    private YamlNode ReadNode(IParser parser, string path)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new YamlScalarNode(scalar.Value);
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var sequence = new YamlSequenceNode();
            var index = 0;
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                sequence.Add(ReadNode(parser, $"{path}.{index++}"));
            }

            return sequence;
        }

        if (parser.TryConsume<MappingStart>(out _))
        {
            var mapping = new YamlMappingNode();
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                var value = ReadNode(parser, $"{path}.{key}");
                var keyNode = new YamlScalarNode(key);
                if (mapping.Children.ContainsKey(keyNode))
                {
                    _duplicates.Add($"{path}.{key}");
                    mapping.Children[keyNode] = value;
                }
                else
                {
                    mapping.Add(keyNode, value);
                }
            }

            return mapping;
        }

        throw new YamlException($"unexpected YAML event {parser.Current?.GetType().Name} at {path}");
    }
}
