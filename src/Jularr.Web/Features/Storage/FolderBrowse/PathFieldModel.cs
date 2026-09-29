using Jularr.Web.Features.Localization;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// One path text field of a settings form (<c>Pages/Shared/_PathField.cshtml</c>). A browsable field
/// gets the Browse button of the shared folder browser and a live check of what the container sees at
/// the path; manual entry always stays possible.
/// </summary>
public sealed record PathFieldModel(string Id, string Name, string Label, string? Value, UiTextBundle Ui)
{
    public string? Placeholder { get; init; }

    public bool Required { get; init; }

    public bool Disabled { get; init; }

    /// <summary>
    /// False renders a plain text field: the path is not a folder of this container (a remote path)
    /// or the viewer may not look at the container's file system.
    /// </summary>
    public bool Browsable { get; init; } = true;

    /// <summary>
    /// Fields of one group in the same form are checked against each other (a library folder and its
    /// inbox folder must not be the same folder).
    /// </summary>
    public string? PairGroup { get; init; }
}
