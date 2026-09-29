using Laraue.CmsBackend;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Blog.ApiHost.Controllers;

[ApiController]
[Route("api/blog")]
public class BlogController(ICmsBackend cmsBackend) : ControllerBase
{
    private static readonly string[] RootPath = ["blog"];

    /// <summary>
    /// Combined feed of articles and projects shown on the blog home page.
    /// </summary>
    [HttpGet("feed")]
    public IShortPaginatedResult<CardItem> GetFeed(
        [FromQuery] string languageCode,
        [FromQuery] string? tag,
        [FromQuery] int page = 0,
        [FromQuery] int perPage = 16)
    {
        return BlogQueryHelpers.GetList(cmsBackend, RootPath, languageCode, ["article", "project"], tag, page, perPage);
    }

    [HttpGet("categories")]
    public List<ManuItem> GetCategories([FromQuery] string languageCode)
    {
        var rows = cmsBackend
            .GetSections(new GetSectionsRequest
            {
                LanguageCode = languageCode,
                Depth = 2,
                FromPath = RootPath,
            })
            .Where(x => x.FileName != "undefined")
            .ToList();

        var mainSection = cmsBackend
            .GetSections(new GetSectionsRequest
            {
                LanguageCode = languageCode,
                Depth = 1,
            })
            .First();

        mainSection.Children = rows.ToArray();

        rows.Insert(0, mainSection);

        return rows
            .Select(BlogQueryHelpers.Map)
            .ToList();
    }

    [HttpGet("tags")]
    public List<Tag> GetTags(
        [FromQuery] string languageCode,
        [FromQuery] string[] path)
    {
        return BlogQueryHelpers.GetTags(cmsBackend, path, languageCode);
    }
}
