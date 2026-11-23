using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Jellyfin.Plugin.DvbI.Parsing;

/// <summary>
/// Thrown when a DVB-I document cannot be used.
/// </summary>
public sealed class DvbIParseException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIParseException"/> class.
    /// </summary>
    public DvbIParseException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIParseException"/> class.
    /// </summary>
    /// <param name="message">Message.</param>
    public DvbIParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIParseException"/> class.
    /// </summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Inner exception.</param>
    public DvbIParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
