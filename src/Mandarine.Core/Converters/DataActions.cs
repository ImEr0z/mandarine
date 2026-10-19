using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Mandarine.Core.Actions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Mandarine.Core.Converters;

internal static class DataFormats
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToJson(JsonNode? node) => (node?.ToJsonString(Indented) ?? "null") + Environment.NewLine;

    public static JsonNode? ParseJson(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

    // ---- YAML ----

    public static string JsonToYaml(string json)
    {
        var graph = ToPlainObject(ParseJson(json));
        var serializer = new SerializerBuilder().DisableAliases().WithQuotingNecessaryStrings().Build();
        return serializer.Serialize(graph);
    }

    /// <summary>
    /// YAML aliases point back to earlier nodes; expanding them into JSON copies the target each time,
    /// so a few lines of nested aliases ("billion laughs") would grow exponentially. Cap the output.
    /// </summary>
    private const int MaxYamlNodes = 2_000_000;

    public static string YamlToJson(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var budget = new int[] { MaxYamlNodes };
        return stream.Documents.Count switch
        {
            0 => ToJson(null),
            1 => ToJson(YamlToNode(stream.Documents[0].RootNode, budget)),
            _ => ToJson(new JsonArray(stream.Documents.Select(d => YamlToNode(d.RootNode, budget)).ToArray())),
        };
    }

    private static JsonNode? YamlToNode(YamlNode node, int[] budget)
    {
        if (--budget[0] < 0)
        {
            throw new InvalidOperationException("Document YAML trop volumineux une fois ses alias développés.");
        }

        return node switch
        {
            YamlMappingNode map => new JsonObject(map.Children.Select(p =>
                new KeyValuePair<string, JsonNode?>(((p.Key as YamlScalarNode)?.Value) ?? p.Key.ToString(), YamlToNode(p.Value, budget)))),
            YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(n => YamlToNode(n, budget)).ToArray()),
            YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain => PlainScalar(scalar.Value ?? string.Empty),
            YamlScalarNode scalar => JsonValue.Create(scalar.Value),
            _ => null,
        };
    }

    /// <summary>YAML 1.2 core schema: unquoted scalars may be null, booleans or numbers.</summary>
    private static JsonNode? PlainScalar(string value)
    {
        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            && !(value.TrimStart('-', '+').Length > 1 && value.TrimStart('-', '+')[0] == '0'))
        {
            return JsonValue.Create(integer);
        }

        if (value.Any(char.IsDigit)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static object? ToPlainObject(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(p => p.Key, p => ToPlainObject(p.Value)),
        JsonArray array => array.Select(ToPlainObject).ToList(),
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetValue<long>(out var l) => l,
            JsonValueKind.Number => value.GetValue<double>(),
            _ => null,
        },
        _ => null,
    };


    // ---- CSV ----

    public static string CsvToJson(string csv, string path)
    {
        var rows = Csv.Parse(csv, Csv.DetectDelimiter(csv, path));
        if (rows.Count == 0)
        {
            return ToJson(new JsonArray());
        }

        var headers = rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"colonne{i + 1}" : h.Trim()).ToArray();
        var array = new JsonArray();
        foreach (var row in rows.Skip(1))
        {
            var obj = new JsonObject();
            for (var i = 0; i < headers.Length; i++)
            {
                obj[headers[i]] = InferScalar(i < row.Length ? row[i] : string.Empty);
            }

            array.Add(obj);
        }

        return ToJson(array);
    }

    private static JsonNode? InferScalar(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value is "true" or "false")
        {
            return JsonValue.Create(value == "true");
        }

        // Leading zeros (postal codes, phone numbers) must stay text.
        var leadingZero = value.Length > 1 && value[0] == '0' && value[1] != '.' && value[1] != ',';
        if (!leadingZero && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (!leadingZero && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(raw);
    }

    public static string JsonToCsv(string json)
    {
        var node = ParseJson(json);
        var items = node switch
        {
            JsonArray array => array.ToList(),
            JsonObject obj when obj.Count == 1 && obj.First().Value is JsonArray inner => inner.ToList(),
            JsonObject obj => [obj],
            _ => throw new InvalidOperationException("Le JSON doit contenir un tableau d'objets."),
        };

        var columns = new List<string>();
        foreach (var item in items.OfType<JsonObject>())
        {
            foreach (var key in item.Select(p => p.Key).Where(k => !columns.Contains(k)))
            {
                columns.Add(key);
            }
        }

        if (columns.Count == 0)
        {
            columns.Add("valeur");
            return Csv.Write([columns, .. items.Select(i => (IReadOnlyList<string>)[CellText(i)])], ',');
        }

        var rows = items.Select(item => (IReadOnlyList<string>)columns
            .Select(c => item is JsonObject obj && obj.TryGetPropertyValue(c, out var v) ? CellText(v) : string.Empty)
            .ToList());
        return Csv.Write([columns, .. rows], ',');
    }

    private static string CellText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    public static string ChangeDelimiter(string csv, string path, char delimiter) =>
        Csv.Write(Csv.Parse(csv, Csv.DetectDelimiter(csv, path)), delimiter);

    // ---- XML ----

    public static string XmlToJson(string xml)
    {
        var document = XDocument.Parse(xml);
        var root = document.Root ?? throw new InvalidOperationException("Document XML vide.");
        return ToJson(new JsonObject { [root.Name.LocalName] = ElementToJson(root) });
    }

    private static JsonNode? ElementToJson(XElement element)
    {
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        var children = element.Elements().ToList();
        if (attributes.Count == 0 && children.Count == 0)
        {
            return element.IsEmpty ? null : JsonValue.Create(element.Value);
        }

        var obj = new JsonObject();
        foreach (var attribute in attributes)
        {
            obj["@" + attribute.Name.LocalName] = attribute.Value;
        }

        foreach (var group in children.GroupBy(c => c.Name.LocalName))
        {
            obj[group.Key] = group.Count() == 1
                ? ElementToJson(group.First())
                : new JsonArray(group.Select(ElementToJson).ToArray());
        }

        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (text.Length > 0)
        {
            obj["#text"] = text;
        }

        return obj;
    }

    public static string JsonToXml(string json)
    {
        var node = ParseJson(json);
        XElement root;
        if (node is JsonObject obj && obj.Count == 1 && obj.First().Value is not JsonArray)
        {
            var single = obj.First();
            root = JsonToElement(XmlName(single.Key), single.Value);
        }
        else
        {
            root = JsonToElement("root", node);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + Environment.NewLine
            + root.ToString(SaveOptions.None) + Environment.NewLine;
    }

    private static XElement JsonToElement(string name, JsonNode? node)
    {
        var element = new XElement(name);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.StartsWith('@') && value is JsonValue)
                    {
                        element.SetAttributeValue(XmlName(key[1..]), CellText(value));
                    }
                    else if (key == "#text")
                    {
                        element.Add(new XText(CellText(value)));
                    }
                    else if (value is JsonArray items)
                    {
                        element.Add(items.Select(item => JsonToElement(XmlName(key), item)));
                    }
                    else
                    {
                        element.Add(JsonToElement(XmlName(key), value));
                    }
                }

                break;
            case JsonArray array:
                element.Add(array.Select(item => JsonToElement("item", item)));
                break;
            case JsonValue value:
                element.Value = CellText(value);
                break;
        }

        return element;
    }

    private static string XmlName(string key)
    {
        try
        {
            return XmlConvert.VerifyName(key);
        }
        catch (XmlException)
        {
            return XmlConvert.EncodeLocalName(key.Length == 0 ? "_" : key)!;
        }
    }
}

internal static class DataActions
{
    public static IEnumerable<FileAction> All()
    {
        yield return new TextTransformAction("json.to.yaml", FileKind.Json, "yaml", "Données",
            (text, _) => DataFormats.JsonToYaml(text));
        yield return new TextTransformAction("json.to.csv", FileKind.Json, "csv", "Tableau",
            (text, _) => DataFormats.JsonToCsv(text));
        yield return new TextTransformAction("json.to.xml", FileKind.Json, "xml", "Données",
            (text, _) => DataFormats.JsonToXml(text));
        yield return new TextTransformAction("yaml.to.json", FileKind.Yaml, "json", "Données",
            (text, _) => DataFormats.YamlToJson(text));
        yield return new TextTransformAction("xml.to.json", FileKind.Xml, "json", "Données",
            (text, _) => DataFormats.XmlToJson(text));
        yield return new TextTransformAction("csv.to.json", FileKind.Csv, "json", "Données",
            DataFormats.CsvToJson);
        yield return new TextTransformAction("csv.to.tsv", FileKind.Csv, "tsv", "Tabulations",
            (text, path) => DataFormats.ChangeDelimiter(text, path, '\t'));
        yield return new TextTransformAction("csv.to.csv", FileKind.Csv, "csv", "Virgules",
            (text, path) => DataFormats.ChangeDelimiter(text, path, ','));
    }
}
