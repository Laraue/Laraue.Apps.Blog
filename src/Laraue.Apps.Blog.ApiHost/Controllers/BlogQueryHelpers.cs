using Laraue.CmsBackend;
using Laraue.CmsBackend.Contracts;
using Laraue.CmsBackend.Utils;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Blog.ApiHost.Controllers;

/// <summary>
/// Shared read logic used by the per-resource blog controllers, so each of them
/// only has to define its own routes and root path.
/// </summary>
internal static class BlogQueryHelpers
{
    public static IShortPaginatedResult<CardItem> GetList(
        ICmsBackend cmsBackend,
        string[] fromPath,
        string languageCode,
        string[] contentTypes,
        string? tag,
        int page,
        int perPage)
    {
        var filters = new List<FilterRow>();
        if (contentTypes.Length > 0)
        {
            filters.Add(new FilterRow
            {
                Property = "contentType",
                Operator = FilterOperator.ValueInList,
                Value = contentTypes,
            });
        }

        if (!string.IsNullOrEmpty(tag))
        {
            filters.Add(new FilterRow
            {
                Property = "tags",
                Operator = FilterOperator.ValueListContain,
                Value = tag,
            });
        }

        return cmsBackend.GetEntities<CardItem>(new GetEntitiesRequest
        {
            FromPath = fromPath,
            LanguageCode = languageCode,
            Properties = ["fileName", "title", "description", "contentType", "path", "length(content)", "tags", "projects"],
            Pagination = new PaginationData { Page = page, PerPage = perPage },
            Filters = filters.ToArray(),
            Sorting =
            [
                new () { Property = "createdAt", SortOrder = SortOrder.Descending }
            ]
        });
    }

    public static CardDetail GetDetails(ICmsBackend cmsBackend, string[] path, string languageCode)
    {
        var entity = cmsBackend
            .GetEntity(new GetEntityRequest
            {
                Path = path,
                LanguageCode = languageCode,
                Properties = [
                    "title",
                    "content",
                    "format(createdAt, \"dd MMM yyyy\") as createdAt",
                    "format(updatedAt, \"dd MMM yyyy\") as updatedAt",
                    "createdAt as createdAtIso",
                    "updatedAt as updatedAtIso",
                    "length(content)",
                    "innerLinks",
                    "tags",
                    "projects",
                    "keywords",
                    "nextLink",
                    "previousLink",
                    "contentType",
                    "description"
                ]
            });

        TryAddLink(cmsBackend, entity, "nextLink", path, languageCode);
        TryAddLink(cmsBackend, entity, "previousLink", path, languageCode);

        return ObjectCreator.Initialize<CardDetail>(entity);
    }

    public static List<Tag> GetTags(ICmsBackend cmsBackend, string[] fromPath, string languageCode)
    {
        var values = cmsBackend
            .CountPropertyValues(new CountPropertyValuesRequest
            {
                Property = "tags",
                FromPath = fromPath,
                LanguageCode = languageCode,
            });

        return values
            .OrderBy(x => x.Key)
            .Select(x => new Tag { Key = x.Key })
            .ToList();
    }

    public static ManuItem Map(SectionItem x)
    {
        return new ManuItem
        {
            Key = x.FileName,
            Path = x.FullPath,
            Count = x.GetAllChildren().Count(y => y.HasContent),
            Title = x.Title,
            Icon = x.MdFile?.GetValueOrDefault("icon") as string,
        };
    }

    private static void TryAddLink(
        ICmsBackend cmsBackend,
        Dictionary<string, object> entity,
        string linkProperty,
        string[] path,
        string languageCode)
    {
        if (!entity.TryGetValue(linkProperty, out var nextLink) || nextLink is not string stringLink)
            return;

        var relatedPath = path.Take(path.Length - 1).Append(stringLink).ToArray();

        try
        {
            var relatedEntity = cmsBackend.GetEntity<NeighborCard>(
                new GetEntityRequest
                {
                    Path = relatedPath,
                    LanguageCode = languageCode,
                    Properties = [
                        "title",
                        "path"
                    ]
                });

            entity[linkProperty] = relatedEntity;
        }
        catch (NotFoundException)
        {
            entity.Remove(linkProperty);
        }
    }

    public static string[] CombinePath(string[] rootPath, string? catchAllPath)
    {
        if (string.IsNullOrEmpty(catchAllPath))
        {
            return rootPath;
        }

        return [..rootPath, ..catchAllPath.Split('/', StringSplitOptions.RemoveEmptyEntries)];
    }
}
