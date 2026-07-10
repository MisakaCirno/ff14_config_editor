using System;
using System.Collections.Generic;
using System.Linq;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    public IReadOnlyList<WayMarkFavorite> GetWayMarkFavoritesSnapshot()
    {
        return CloneWayMarkFavorites(WayMarkFavorites);
    }

    public WayMarkFavorite AddWayMarkFavorite(WayMarkSnapshot marker, string commentName)
    {
        ArgumentNullException.ThrowIfNull(marker);

        DateTime now = DateTime.Now;
        WayMarkFavorite favorite = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CommentName = NormalizeFavoriteCommentName(commentName),
            CreatedAt = now,
            UpdatedAt = now,
            Marker = WayMarkSnapshotConverter.Clone(marker)
        };

        List<WayMarkFavorite> nextFavorites = CloneWayMarkFavorites(WayMarkFavorites);
        nextFavorites.Insert(0, favorite);
        SaveWayMarkFavorites(nextFavorites);

        return WayMarkSnapshotConverter.CloneFavorite(favorite);
    }

    public void UpdateWayMarkFavorite(WayMarkFavorite favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        if (string.IsNullOrWhiteSpace(favorite.Id))
        {
            throw new InvalidOperationException("收藏标点缺少 ID，无法更新。");
        }

        List<WayMarkFavorite> nextFavorites = CloneWayMarkFavorites(WayMarkFavorites);
        int index = nextFavorites.FindIndex(item =>
            string.Equals(item.Id, favorite.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("找不到要更新的收藏标点。");
        }

        WayMarkFavorite current = nextFavorites[index];
        nextFavorites[index] = new WayMarkFavorite
        {
            Id = current.Id,
            CommentName = NormalizeFavoriteCommentName(favorite.CommentName),
            CreatedAt = current.CreatedAt,
            UpdatedAt = DateTime.Now,
            Marker = WayMarkSnapshotConverter.Clone(favorite.Marker)
        };

        SaveWayMarkFavorites(nextFavorites);
    }

    public void DeleteWayMarkFavorite(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        List<WayMarkFavorite> nextFavorites = CloneWayMarkFavorites(WayMarkFavorites)
            .Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SaveWayMarkFavorites(nextFavorites);
    }

    public bool MoveWayMarkFavorite(string id, int offset)
    {
        if (string.IsNullOrWhiteSpace(id) || offset == 0)
        {
            return false;
        }

        List<WayMarkFavorite> nextFavorites = CloneWayMarkFavorites(WayMarkFavorites);
        int currentIndex = nextFavorites.FindIndex(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            throw new InvalidOperationException("找不到要移动的收藏标点。");
        }

        int targetIndex = currentIndex + offset;
        if (targetIndex < 0 || targetIndex >= nextFavorites.Count)
        {
            return false;
        }

        WayMarkFavorite movedFavorite = nextFavorites[currentIndex];
        nextFavorites.RemoveAt(currentIndex);
        nextFavorites.Insert(targetIndex, movedFavorite);
        SaveWayMarkFavorites(nextFavorites);
        return true;
    }

    public bool SortWayMarkFavoritesByRegion(bool ascending)
    {
        List<WayMarkFavorite> nextFavorites = CloneWayMarkFavorites(WayMarkFavorites);
        if (nextFavorites.Count <= 1)
        {
            return false;
        }

        List<WayMarkFavorite> sortedFavorites = ascending
            ? [.. nextFavorites
                .OrderBy(favorite => WayMarkRegionSort.GetZeroLastBucket(favorite.RegionID))
                .ThenBy(favorite => favorite.RegionID)]
            : [.. nextFavorites
                .OrderBy(favorite => WayMarkRegionSort.GetZeroLastBucket(favorite.RegionID))
                .ThenByDescending(favorite => favorite.RegionID)];
        if (nextFavorites.Select(favorite => favorite.Id).SequenceEqual(sortedFavorites.Select(favorite => favorite.Id), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        SaveWayMarkFavorites(sortedFavorites);
        return true;
    }

    private void LoadWayMarkFavorites()
    {
        WayMarkFavorites.Clear();
        wayMarkFavoritesFileInvalid = false;

        JsonFileReadResult<WayMarkFavoritesData> result = ReadJsonFile<WayMarkFavoritesData>(WayMarkFavoritesFilePath);
        if (result.Status == JsonFileReadStatus.Invalid)
        {
            wayMarkFavoritesFileInvalid = true;
            AddJsonReadWarning(
                WayMarkFavoritesFilePath,
                "标点收藏无法读取，列表已留空。为避免覆盖损坏文件，本次运行会阻止保存标点收藏。",
                result.Error);
            return;
        }

        foreach (WayMarkFavorite favorite in NormalizeWayMarkFavorites(result.Value?.Favorites ?? []))
        {
            WayMarkFavorites.Add(favorite);
        }
    }

    private void SaveWayMarkFavorites(List<WayMarkFavorite> favorites)
    {
        ExecuteDataDirectoryManagedWrite(() => SaveWayMarkFavoritesCore(favorites));
    }

    private void SaveWayMarkFavoritesCore(List<WayMarkFavorite> favorites)
    {
        if (wayMarkFavoritesFileInvalid)
        {
            throw new InvalidOperationException("waymark-favorites.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        List<WayMarkFavorite> normalizedFavorites = NormalizeWayMarkFavorites(favorites);
        EnsureDataDirectory();
        WriteJson(WayMarkFavoritesFilePath, new WayMarkFavoritesData
        {
            Favorites = normalizedFavorites
        });

        WayMarkFavorites.Clear();
        foreach (WayMarkFavorite favorite in normalizedFavorites)
        {
            WayMarkFavorites.Add(WayMarkSnapshotConverter.CloneFavorite(favorite));
        }
    }

    private static List<WayMarkFavorite> CloneWayMarkFavorites(IEnumerable<WayMarkFavorite> favorites)
    {
        return favorites
            .Where(favorite => favorite != null)
            .Select(WayMarkSnapshotConverter.CloneFavorite)
            .ToList();
    }

    private static List<WayMarkFavorite> NormalizeWayMarkFavorites(IEnumerable<WayMarkFavorite> favorites)
    {
        List<WayMarkFavorite> result = [];
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (WayMarkFavorite? favorite in favorites)
        {
            if (favorite == null) continue;

            string id = string.IsNullOrWhiteSpace(favorite.Id)
                ? Guid.NewGuid().ToString("N")
                : favorite.Id.Trim();
            if (!usedIds.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                usedIds.Add(id);
            }

            DateTime createdAt = favorite.CreatedAt == default ? DateTime.Now : favorite.CreatedAt;
            DateTime updatedAt = favorite.UpdatedAt == default ? createdAt : favorite.UpdatedAt;
            result.Add(new WayMarkFavorite
            {
                Id = id,
                CommentName = NormalizeFavoriteCommentName(favorite.CommentName),
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                Marker = WayMarkSnapshotConverter.Clone(favorite.Marker)
            });
        }

        return result;
    }

    private static string NormalizeFavoriteCommentName(string? commentName)
    {
        return (commentName ?? string.Empty).Trim();
    }
}
