using Mandarine.Core.Actions;

namespace Mandarine.Core.Converters;

internal static class LibreOffice
{
    // soffice refuses to run twice on the same profile, so conversions are serialized.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string ProfileDirectory => Path.Combine(Path.GetTempPath(), "Mandarine", "libreoffice-profile");

    private const string HardeningMarker = "DisableActiveContent";

    /// <summary>
    /// Documents to convert come from anywhere: never run their macros, never follow DDE/OLE/external
    /// links or fetch linked content while loading them. Written into Mandarine's private profile only.
    /// </summary>
    private const string HardeningItems = """
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableActiveContent" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="BlockUntrustedRefererLinks" oor:op="fuse"><value>true</value></prop></item>
        """;

    private static void Harden()
    {
        var file = Path.Combine(ProfileDirectory, "user", "registrymodifications.xcu");
        var content = File.Exists(file) ? File.ReadAllText(file) : null;
        if (content?.Contains(HardeningMarker, StringComparison.Ordinal) == true)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        content = content is not null && content.Contains("</oor:items>", StringComparison.Ordinal)
            ? content.Replace("</oor:items>", HardeningItems + "</oor:items>", StringComparison.Ordinal)
            : """<?xml version="1.0" encoding="UTF-8"?>""" + "\n"
              + """<oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">"""
              + "\n" + HardeningItems + "</oor:items>\n";
        File.WriteAllText(file, content);
    }

    /// <summary>
    /// Converts <paramref name="input"/> with soffice in headless mode and moves the result to <paramref name="output"/>.
    /// </summary>
    /// <param name="convertTo">soffice "--convert-to" value, e.g. "pdf" or "csv:Text - txt - csv (StarCalc):44,34,76".</param>
    /// <param name="inputFilter">Optional "--infilter" value, e.g. to open HTML in Writer instead of Writer/Web.</param>
    public static async Task ConvertAsync(
        ActionContext context,
        string input,
        string convertTo,
        string output,
        CancellationToken cancellationToken,
        string? inputFilter = null)
    {
        var soffice = context.Tools.Require(ExternalTool.LibreOffice);
        var workDirectory = Directory.CreateTempSubdirectory("mandarine-").FullName;
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Harden();
            List<string> arguments =
            [
                "-env:UserInstallation=" + new Uri(ProfileDirectory + Path.DirectorySeparatorChar).AbsoluteUri,
                "--headless", "--norestore", "--nolockcheck", "--nodefault", "--nologo",
                "--convert-to", convertTo,
                "--outdir", workDirectory,
            ];
            if (inputFilter is not null)
            {
                arguments.Add("--infilter=" + inputFilter);
            }

            arguments.Add(Path.GetFullPath(input));
            await ProcessRunner.RunAsync(soffice, arguments, cancellationToken).ConfigureAwait(false);

            var produced = Directory.EnumerateFiles(workDirectory).FirstOrDefault()
                ?? throw new ExternalToolException("LibreOffice n'a produit aucun fichier : format non pris en charge ?");
            File.Move(produced, output, overwrite: true);
        }
        finally
        {
            Gate.Release();
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>Office document conversion through LibreOffice (doc, docx, odt, xlsx, pptx...).</summary>
internal sealed class OfficeConvertAction(
    FileKind[] kinds,
    string extension,
    string convertTo,
    string? subtitle = null,
    Func<string, string?>? inputFilter = null) : PerFileAction
{
    public override string Id => $"office.{string.Join('-', kinds).ToLowerInvariant()}.to.{extension}";

    public override string Title => extension.ToUpperInvariant();

    public override string? Subtitle => subtitle;

    public override ActionGroup Group => ActionGroup.Convert;

    public override ExternalTool Requires => ExternalTool.LibreOffice;

    public override string TargetExtension => extension;

    public override bool Accepts(string path) => kinds.Contains(FileKinds.Detect(path));

    protected override async Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var output = OutputPaths.ForFile(input, extension, context.OutputDirectory);
        var filter = inputFilter?.Invoke(input) ?? DefaultInputFilter(input);
        await LibreOffice.ConvertAsync(context, input, convertTo, output, cancellationToken, filter)
            .ConfigureAwait(false);
        return output;
    }

    /// <summary>
    /// Plain text and CSV are read as UTF-8 (LibreOffice otherwise guesses the ANSI code page).
    /// Recent LibreOffice ignores a bare "UTF8": the text import options must be spelled out in full.
    /// </summary>
    private static string? DefaultInputFilter(string input) => FileKinds.Detect(input) switch
    {
        FileKind.Text => "Text (encoded):UTF8,LF,,,",
        FileKind.Csv => $"CSV:{(int)Csv.DetectDelimiter(ReadHead(input), input)},34,76,1",
        _ => null,
    };

    private static string ReadHead(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[4096];
        return new string(buffer, 0, reader.Read(buffer, 0, buffer.Length));
    }

    public static IEnumerable<FileAction> All()
    {
        FileKind[] word = [FileKind.WordDocument];
        FileKind[] text = [FileKind.Text];
        FileKind[] sheet = [FileKind.Spreadsheet];
        FileKind[] slides = [FileKind.Presentation];
        const string Csv = "csv:Text - txt - csv (StarCalc):44,34,76";

        yield return new OfficeConvertAction([.. word, .. text], "pdf", "pdf", "Document");
        yield return new OfficeConvertAction([.. word, .. text], "docx", "docx:MS Word 2007 XML", "Word");
        yield return new OfficeConvertAction(word, "odt", "odt", "OpenDocument");
        yield return new OfficeConvertAction(word, "rtf", "rtf", "Texte enrichi");
        yield return new OfficeConvertAction(word, "txt", "txt:Text (encoded):UTF8", "Texte brut");

        yield return new OfficeConvertAction(sheet, "pdf", "pdf", "Document");
        yield return new OfficeConvertAction(sheet, "xlsx", "xlsx:Calc MS Excel 2007 XML", "Excel");
        yield return new OfficeConvertAction(sheet, "ods", "ods", "OpenDocument");
        yield return new OfficeConvertAction(sheet, "csv", Csv, "Données");

        yield return new OfficeConvertAction(slides, "pdf", "pdf", "Document");
        yield return new OfficeConvertAction(slides, "pptx", "pptx:Impress MS PowerPoint 2007 XML", "PowerPoint");
        yield return new OfficeConvertAction(slides, "odp", "odp", "OpenDocument");

        yield return new OfficeConvertAction([FileKind.Csv], "xlsx", "xlsx:Calc MS Excel 2007 XML", "Excel");
        yield return new OfficeConvertAction([FileKind.Html], "pdf", "pdf:writer_web_pdf_Export", "Document");
        yield return new OfficeConvertAction([FileKind.Html], "docx", "docx:MS Word 2007 XML", "Word",
            inputFilter: _ => "HTML (StarWriter)");
    }
}
