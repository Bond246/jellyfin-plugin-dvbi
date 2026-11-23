using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.DvbI.Parsing;

/// <summary>
/// Namespace agnostic helpers for LINQ to XML.
/// DVB-I changes namespaces with every schema version (2022, 2023, 2024, 2026...), while the element names
/// stay stable. Matching on local names makes the parser work across versions and tolerant to extensions,
/// as required by TS 103 770 clause 4.6.3.
/// </summary>
internal static class XmlHelpers
{
    private static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>
    /// Parses XML from untrusted sources: DTDs (and with them entity expansion and external entities)
    /// are rejected with an <see cref="XmlException"/>.
    /// </summary>
    /// <param name="xml">XML text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="XmlException">Malformed XML or a DTD.</exception>
    public static XDocument ParseSafe(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), SafeSettings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// Gets the direct child elements with the given local name.
    /// </summary>
    /// <param name="element">Parent element.</param>
    /// <param name="localName">Local name.</param>
    /// <returns>Matching children.</returns>
    public static IEnumerable<XElement> Children(this XElement element, string localName)
        => element.Elements().Where(e => e.Name.LocalName == localName);

    /// <summary>
    /// Gets the first direct child element with the given local name.
    /// </summary>
    /// <param name="element">Parent element.</param>
    /// <param name="localName">Local name.</param>
    /// <returns>The child or null.</returns>
    public static XElement? Child(this XElement element, string localName)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>
    /// Gets the trimmed text of the first child element with the given local name.
    /// </summary>
    /// <param name="element">Parent element.</param>
    /// <param name="localName">Local name.</param>
    /// <returns>The text or null when missing or empty.</returns>
    public static string? ChildText(this XElement element, string localName)
        => NullIfEmpty(element.Child(localName)?.Value);

    /// <summary>
    /// Gets the trimmed texts of all child elements with the given local name.
    /// </summary>
    /// <param name="element">Parent element.</param>
    /// <param name="localName">Local name.</param>
    /// <returns>Non empty texts.</returns>
    public static List<string> ChildTexts(this XElement element, string localName)
        => element.Children(localName)
            .Select(e => NullIfEmpty(e.Value))
            .OfType<string>()
            .ToList();

    /// <summary>
    /// Gets an attribute value by local name.
    /// </summary>
    /// <param name="element">Element.</param>
    /// <param name="localName">Local name of the attribute.</param>
    /// <returns>The value or null.</returns>
    public static string? Attr(this XElement element, string localName)
        => NullIfEmpty(element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value);

    /// <summary>
    /// Gets a boolean attribute.
    /// </summary>
    /// <param name="element">Element.</param>
    /// <param name="localName">Local name of the attribute.</param>
    /// <param name="defaultValue">Value when the attribute is missing or invalid.</param>
    /// <returns>The value.</returns>
    public static bool BoolAttr(this XElement element, string localName, bool defaultValue)
    {
        var value = element.Attr(localName);
        return value switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets an integer attribute.
    /// </summary>
    /// <param name="element">Element.</param>
    /// <param name="localName">Local name of the attribute.</param>
    /// <returns>The value or null when missing or invalid.</returns>
    public static int? IntAttr(this XElement element, string localName)
        => int.TryParse(element.Attr(localName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// Returns the first descendant (depth first) with the given local name.
    /// </summary>
    /// <param name="element">Root element.</param>
    /// <param name="localName">Local name.</param>
    /// <returns>The element or null.</returns>
    public static XElement? FirstDescendant(this XElement element, string localName)
        => element.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
