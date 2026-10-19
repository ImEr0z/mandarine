using System.Text.Json.Nodes;

namespace Mandarine.Core.Tests;

public class DataTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private async Task<string> ConvertAsync(string actionId, string input) =>
        await File.ReadAllTextAsync((await _dir.RunAsync(actionId, input)).OutputFiles[0]);

    [Fact]
    public async Task Json_yaml_round_trip_keeps_types()
    {
        var json = _dir.WriteText("config.json",
            """{ "nom": "Mandarine", "version": 2, "ratio": 0.5, "actif": true, "vide": null, "tags": ["a", "b"], "zip": "01000" }""");

        var yaml = await ConvertAsync("json.to.yaml", json);
        Assert.Contains("nom: Mandarine", yaml);
        Assert.Contains("- a", yaml);

        var back = JsonNode.Parse(await ConvertAsync("yaml.to.json", _dir.File("config.yaml")))!;
        Assert.Equal(2, back["version"]!.GetValue<long>());
        Assert.Equal(0.5, back["ratio"]!.GetValue<double>());
        Assert.True(back["actif"]!.GetValue<bool>());
        Assert.Equal("01000", back["zip"]!.GetValue<string>());
        Assert.Equal(2, back["tags"]!.AsArray().Count);
    }

    [Fact]
    public async Task French_csv_with_semicolons_becomes_typed_json()
    {
        var csv = _dir.WriteText("clients.csv", "﻿nom;code postal;age;note\n\"Dupont; Jean\";06000;42;\"dit \"\"JD\"\"\"\nMartin;75011;;3.5\n");

        var array = JsonNode.Parse(await ConvertAsync("csv.to.json", csv))!.AsArray();

        Assert.Equal(2, array.Count);
        Assert.Equal("Dupont; Jean", array[0]!["nom"]!.GetValue<string>());
        Assert.Equal("06000", array[0]!["code postal"]!.GetValue<string>());
        Assert.Equal(42, array[0]!["age"]!.GetValue<long>());
        Assert.Equal("dit \"JD\"", array[0]!["note"]!.GetValue<string>());
        Assert.Null(array[1]!["age"]);
        Assert.Equal(3.5, array[1]!["note"]!.GetValue<double>());
    }

    [Fact]
    public async Task Json_array_becomes_csv_with_union_of_columns()
    {
        var json = _dir.WriteText("data.json", """[{"a": 1, "b": "x,y"}, {"a": 2, "c": {"d": true}}]""");

        var csv = await ConvertAsync("json.to.csv", json);

        Assert.Equal("a,b,c\r\n1,\"x,y\",\r\n2,,\"{\"\"d\"\":true}\"\r\n", csv);
    }

    [Fact]
    public async Task Tsv_and_csv_switch_delimiters()
    {
        var tsv = _dir.WriteText("t.tsv", "a\tb\n1\t2,5\n");

        Assert.Equal("a,b\r\n1,\"2,5\"\r\n", await ConvertAsync("csv.to.csv", tsv));
        Assert.Equal("a\tb\r\n1\t2,5\r\n", await ConvertAsync("csv.to.tsv", _dir.File("t.csv")));
    }

    [Fact]
    public async Task Xml_json_round_trip()
    {
        var xml = _dir.WriteText("catalogue.xml",
            """<catalogue version="1"><fruit id="1">Mandarine</fruit><fruit id="2">Kiwi</fruit><vide/></catalogue>""");

        var json = JsonNode.Parse(await ConvertAsync("xml.to.json", xml))!;
        var fruits = json["catalogue"]!["fruit"]!.AsArray();
        Assert.Equal("1", json["catalogue"]!["@version"]!.GetValue<string>());
        Assert.Equal("Kiwi", fruits[1]!["#text"]!.GetValue<string>());

        var back = await ConvertAsync("json.to.xml", _dir.File("catalogue.json"));
        Assert.Contains("<catalogue version=\"1\">", back);
        Assert.Contains("<fruit id=\"2\">Kiwi</fruit>", back);
    }

    [Fact]
    public async Task Markdown_html_round_trip()
    {
        var md = _dir.WriteText("notes.md", "# Titre\n\nDu **gras** et un [lien](https://example.com).\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");

        var html = await ConvertAsync("markdown.to.html", md);
        Assert.Contains("<h1 id=\"titre\">Titre</h1>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<title>notes</title>", html);

        var back = await ConvertAsync("html.to.md", _dir.File("notes.html"));
        Assert.Contains("# Titre", back);
        Assert.Contains("**gras**", back);
        Assert.Contains("[lien](https://example.com)", back);
        Assert.DoesNotContain("font-family", back);
    }

    public void Dispose() => _dir.Dispose();
}
