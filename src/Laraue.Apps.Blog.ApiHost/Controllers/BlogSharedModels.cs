using Laraue.Interpreter.Markdown;

namespace Laraue.Apps.Blog.ApiHost.Controllers;

public class Tag
{
    public required string Key { get; init; }
}

public class ManuItem
{
    public required string Title { get; init; }
    public required string Key { get; init; }
    public required string[] Path { get; init; }
    public required string? Icon { get; init; }
    public required int Count { get; init; }
}

public class CardItem
{
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string ContentType { get; init; }
    public required string[] Path { get; init; }
    public required int Length { get; init; }
    public required string?[] Tags { get; init; }
    public required string?[] Projects { get; init; }
}

public class CardDetail
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Content { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required DateTime CreatedAtIso { get; init; }
    public required DateTime UpdatedAtIso { get; init; }
    public required string ContentType { get; init; }
    public required long Length { get; init; }
    public required string?[] Tags { get; init; }
    public required string?[] Projects { get; init; }
    public string?[]? Keywords { get; init; }
    public required MarkdownInnerLink[] InnerLinks { get; init; }
    public NeighborCard? PreviousLink { get; set; }
    public NeighborCard? NextLink { get; set; }
}

public class NeighborCard
{
    public required string Title { get; init; }
    public required string[] Path { get; init; }
}
