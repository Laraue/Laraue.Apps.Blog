using System.Text;
using Laraue.Apps.Blog.ApiHost.Services;
using Laraue.CmsBackend;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Blog.ApiHost.Controllers;

[ApiController]
[Route("api/rss")]
public class RssController(ICmsBackend cmsBackend, IRssFeedGenerator rssGenerator) : ControllerBase
{
    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetFeed([FromQuery] string languageCode = "en")
    {
        var items = new List<RssFeedItem>();
        
        items.AddRange(GetFeedItems("projects", languageCode));
        items.AddRange(GetFeedItems("articles", languageCode));
        
        items = items.OrderByDescending(x => x.CreatedAt).ToList();

        var xml = rssGenerator.Generate(languageCode, items);

        return Content(xml, "application/rss+xml", Encoding.UTF8);
    }

    private IList<RssFeedItem> GetFeedItems(string section, string languageCode)
    {
        var result = cmsBackend.GetEntities<RssFeedItem>(new GetEntitiesRequest
        {
            FromPath = ["blog", section],
            LanguageCode = languageCode,
            Pagination = new PaginationData
            {
                Page = 0,
                PerPage = 100_000,
            }
        });

        return result.Data;
    }
}