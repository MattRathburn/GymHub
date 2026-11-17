namespace GymHub.BuildingBlocks.Persistence;

/// <summary>
/// Builds LIKE/ILIKE patterns from user text, so % and _ in a search match themselves. Pass
/// <see cref="EscapeCharacter"/> as the escape argument of EF.Functions.Like/ILike.
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = @"\";

    /// <summary>A pattern matching values that contain the (trimmed) text.</summary>
    public static string Contains(string text) =>
        $"%{text.Trim().Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter).Replace("%", EscapeCharacter + "%").Replace("_", EscapeCharacter + "_")}%";
}
