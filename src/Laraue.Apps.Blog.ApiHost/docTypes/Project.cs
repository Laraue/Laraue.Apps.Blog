using Laraue.CmsBackend;

namespace Laraue.Apps.Blog.ApiHost.docTypes;

public class Project : BaseContentType
{
    public string? GithubLink { get; init; }
    public string? ApplicationLink { get; init; }
    public string? ProjectType { get; init; }
    public required string[] Tags { get; init; }
    public required string Description { get; init; }
}