using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Wisdi.AppPlatform.Llm;

/// <summary>
/// Converts HTML5 to XHTML format that can be loaded into XDocument
/// </summary>
public static class HtmlToXhtmlConverter
{
    /// <summary>
    /// Converts HTML5 to XHTML format that can be loaded into XDocument
    /// </summary>
    public static string ConvertToXhtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var xhtml = html;

        // 1. Add XML declaration at the beginning if not present
        var trimmed = xhtml.TrimStart();
        if (!trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            xhtml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + xhtml;
        }

        // 2. Replace HTML5 DOCTYPE with XHTML DOCTYPE
        xhtml = Regex.Replace(xhtml,
            @"<!DOCTYPE\s+html\s*>",
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">",
            RegexOptions.IgnoreCase);

        // 3. Add xmlns attribute to html element if missing
        if (!xhtml.Contains("xmlns=\"http://www.w3.org/1999/xhtml\"", StringComparison.OrdinalIgnoreCase) &&
            !xhtml.Contains("xmlns='http://www.w3.org/1999/xhtml'", StringComparison.OrdinalIgnoreCase))
        {
            // Find <html> tag and add xmlns attribute
            var htmlMatch = Regex.Match(xhtml, @"<html(\s+[^>]*?)>", RegexOptions.IgnoreCase);
            if (htmlMatch.Success)
            {
                var attributes = htmlMatch.Groups[1].Value;
                var replacement = $"<html{attributes} xmlns=\"http://www.w3.org/1999/xhtml\">";
                xhtml = xhtml.Substring(0, htmlMatch.Index) + replacement + xhtml.Substring(htmlMatch.Index + htmlMatch.Length);
            }
            else
            {
                // If no attributes, just add xmlns
                xhtml = Regex.Replace(xhtml, @"<html\s*>", "<html xmlns=\"http://www.w3.org/1999/xhtml\">", RegexOptions.IgnoreCase);
            }
        }

        // 3a. Escape unescaped ampersands (must be &amp; in XML)
        // Match & that is not already part of an entity (&amp; &lt; &gt; &quot; &apos; &#...; &x...;)
        xhtml = Regex.Replace(xhtml, @"&(?!(?:amp|lt|gt|quot|apos|#\d+|#x[0-9a-f]+);)", "&amp;", RegexOptions.IgnoreCase);

        // 3b. Fix boolean attributes (checked, selected, disabled, readonly, etc.) to have values
        var booleanAttributes = new[] { "checked", "selected", "disabled", "readonly", "multiple", "required", "autofocus", "novalidate" };
        foreach (var attr in booleanAttributes)
        {
            // Match attribute without value: attr (not attr="..." or attr='...')
            var pattern = $@"\b({attr})(?=\s|>|/)(?![^<]*?[""']|[""'])";
            xhtml = Regex.Replace(xhtml, pattern, $"$1=\"{attr}\"", RegexOptions.IgnoreCase);
        }

        // 4. Close self-closing tags properly (add />)
        // Tags that must be self-closing in XHTML
        var selfClosingTags = new[] { "meta", "link", "img", "input", "br", "hr", "area", "base", "col", "embed", "source", "track", "wbr" };

        foreach (var tag in selfClosingTags)
        {
            // Pattern: <tag ... > where tag is not already self-closed and not part of a closing tag
            // Match opening tag that's not already self-closing: <tag ... > but not <tag ... />
            var pattern = $@"<{tag}([^>]*?)(?<!\/)\s*>(?!</{tag}>)";
            xhtml = Regex.Replace(xhtml, pattern, $"<{tag}$1 />", RegexOptions.IgnoreCase);
        }

        return xhtml;
    }

    /// <summary>
    /// Converts HTML to XHTML and loads it into an XDocument
    /// </summary>
    public static XDocument LoadAsXDocument(string html)
    {
        var xhtml = ConvertToXhtml(html);

        // Use XmlReader with lenient settings to handle HTML quirks
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            CheckCharacters = false,
            ConformanceLevel = ConformanceLevel.Auto
        };

        using (var stringReader = new StringReader(xhtml))
        using (var xmlReader = XmlReader.Create(stringReader, settings))
        {
            try
            {
                return XDocument.Load(xmlReader);
            }
            catch (XmlException)
            {
                // If strict parsing fails, try wrapping script/style content in CDATA
                xhtml = WrapScriptAndStyleInCdata(xhtml);
                using (var stringReader2 = new StringReader(xhtml))
                using (var xmlReader2 = XmlReader.Create(stringReader2, settings))
                {
                    return XDocument.Load(xmlReader2);
                }
            }
        }
    }

    private static string WrapScriptAndStyleInCdata(string html)
    {
        // Wrap script content in CDATA to avoid XML parsing issues
        html = Regex.Replace(html,
            @"<script([^>]*)>([^<]*(?:<(?!/script>)[^<]*)*)</script>",
            "<script$1><![CDATA[$2]]></script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Wrap style content in CDATA
        html = Regex.Replace(html,
            @"<style([^>]*)>([^<]*(?:<(?!/style>)[^<]*)*)</style>",
            "<style$1><![CDATA[$2]]></style>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return html;
    }
}
