namespace DeskAI.Core.Roots;

/// <summary>
/// The single place that decides what DeskAI may do with an authorized folder.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, each caller compared the scope itself — <c>scope == MetadataOnly</c>
/// to allow searching, and the same comparison to block mutation. That works until a scope
/// is added: the new value quietly falls out of the "block mutation" test and inherits the
/// right to change files, without a single line of the check appearing to change. A
/// permission model that fails open when someone extends an enum is not a permission model.
/// </para>
/// <para>
/// Every question is therefore answered here by an explicit list of the scopes that grant
/// it. A scope not on the list is denied, so a value added later starts with no rights at
/// all and has to be granted them deliberately, in this file, under test.
/// </para>
/// <para>
/// Permission is checked alongside scope, because both must hold: a folder marked
/// restricted or protected grants nothing regardless of why it was connected.
/// </para>
/// </remarks>
public static class RootCapabilities
{
    /// <summary>May DeskAI list names, sizes, and dates in this folder?</summary>
    public static bool CanReadMetadata(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.AuthorizationScope switch
        {
            RootAuthorizationScope.MetadataOnly => true,
            RootAuthorizationScope.MetadataAndContent => true,
            RootAuthorizationScope.MetadataAndDocuments => true,
            _ => false,
        };
    }

    /// <summary>
    /// May DeskAI open the files in this folder and read what is inside them?
    /// </summary>
    /// <remarks>
    /// Only a folder connected specifically for content grants this. Metadata consent is not
    /// content consent: someone who agreed to DeskAI listing their file names has not agreed
    /// to it reading the letter inside one, and reusing the first grant for the second would
    /// be taking a permission that was never given.
    /// </remarks>
    public static bool CanReadContent(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.AuthorizationScope switch
        {
            RootAuthorizationScope.MetadataAndContent => true,
            RootAuthorizationScope.MetadataAndDocuments => true,
            _ => false,
        };
    }

    /// <summary>May DeskAI open modern Word and Excel documents here?</summary>
    public static bool CanReadDocuments(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.AuthorizationScope == RootAuthorizationScope.MetadataAndDocuments;
    }

    /// <summary>
    /// May a validated, approved plan move, rename, or create things in this folder?
    /// </summary>
    /// <remarks>
    /// Content access deliberately does not appear here. Being allowed to read a file says
    /// nothing about being allowed to move it, and a reading permission that quietly became
    /// a writing one would be the exact escalation this type exists to prevent.
    /// </remarks>
    public static bool CanMutate(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.AuthorizationScope switch
        {
            RootAuthorizationScope.ControlledDemo => true,
            RootAuthorizationScope.Organize => true,
            _ => CanTidy(root),
        };
    }

    /// <summary>
    /// May DeskAI tidy this folder: move its loose files into folders inside it?
    /// </summary>
    /// <remarks>
    /// Only a folder connected for reading can hold this permission, and only once the person
    /// allowed it. The practice workspace and the legacy organize scope are answered by
    /// <see cref="CanMutate"/> directly and never by a tidy grant. The scope list is explicit,
    /// so a scope added later cannot inherit tidying by falling through.
    /// </remarks>
    public static bool CanTidy(AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return IsUsable(root) && root.TidyAllowedSinceUtc is not null && root.AuthorizationScope switch
        {
            RootAuthorizationScope.MetadataOnly => true,
            RootAuthorizationScope.MetadataAndContent => true,
            RootAuthorizationScope.MetadataAndDocuments => true,
            _ => false,
        };
    }

    /// <summary>A folder that is restricted or protected grants nothing at all.</summary>
    private static bool IsUsable(AuthorizedRoot root) => root.Permission == RootAccessLevel.Allowed;
}
