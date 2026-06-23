using System.Collections.Concurrent;

namespace FileManager.Helpers
{
    // Thread-safe cache for directory statistics (size and file count).
    // Populates cache recursively and exposes methods to update cache on file create/delete/move.
    public class DirectoryStatisticsCache
    {
        private readonly ConcurrentDictionary<string, (long TotalSize, long FileCount)> _cache = new();
        private readonly DirectoryHelper _directoryHelper;

        public DirectoryStatisticsCache(DirectoryHelper directoryHelper)
        {
            _directoryHelper = directoryHelper;
        }

        public (long? TotalSize, long? FileCount) GetStatistics(string dirFullPath)
        {
            try
            {
                return GetStatistics(new DirectoryInfo(dirFullPath));
            }
            catch
            {
                return (null, null);
            }
        }

        // Public getter. Returns (TotalSize, FileCount) or (null,null) on failure.
        public (long? TotalSize, long? FileCount) GetStatistics(DirectoryInfo dir)
        {
            try
            {
                // Try to get from cachce
                if (_cache.TryGetValue(dir.FullName, out var v))
                {
                    return (v.TotalSize, v.FileCount);
                }

                // If directory does not exist
                if (!dir.Exists) return (0, 0);

                // Init counters
                long total = 0;
                long count = 0;

                // Process Files
                try
                {
                    foreach (var f in dir.GetFiles())
                    {
                        try
                        {
                            total += f.Length;
                            count++;
                        }
                        catch { }
                    }
                }
                catch { }

                // Process Directories (Recursively)
                try
                {
                    foreach (var subDir in dir.GetDirectories())
                    {
                        try
                        {
                            var child = GetStatistics(subDir);
                            total += child.TotalSize ?? 0;
                            count += child.FileCount ?? 0;
                        }
                        catch { }
                    }
                }
                catch { }

                // Set cache
                _cache[dir.FullName] = (total, count);

                // Return directory statistics
                return (total, count);
            }
            catch
            {
                return (null, null);
            }
        }

        // Remove cache entries for a directory subtree
        public void RemoveDirectoryFromCache(string dirFullPath)
        {
            // Get a list of all cached entries that existed in the source folder.   We will need to update those records
            var directoryAndChildrenDir = _cache.Where(kvp => kvp.Key.StartsWith(dirFullPath, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var kvPair in directoryAndChildrenDir)
            {
                _cache.TryRemove(kvPair.Key, out _);
            }
        }

        /**
         *  Handle file moves.   Under the covers, it essentially treats this as a delete / create
         */
        public void HandleFileCreated(string newFullPath, long size)
        {
            WalkParentCacheAndModify(newFullPath, size, 1);
        }

        /**
         *  Handle file moves.   Under the covers, it essentially treats this as a delete / create
         */
        public void HandleFileDeleted(string oldFullPath, long size)
        {
            WalkParentCacheAndModify(oldFullPath, -1 * size, -1);
        }

        /**
         *  Handle file moves.   Under the covers, it essentially treats this as a delete / create
         */
        public void HandleFileMoved(string oldFullPath, string newFullPath, long size)
        {
            WalkParentCacheAndModify(oldFullPath, -1 * size, -1);
            WalkParentCacheAndModify(newFullPath, size, 1);
        }

        /**
         *  Handle file moves.   Under the covers, it essentially treats this as a delete / create
         */
        public void HandleFolderCreate(string newFullPath)
        {
            _cache[newFullPath] = (0, 0);
        }



        // Handle directory moved: transfer cached subtree entries if present, otherwise fallback to recalculation
        public void HandleDirectoryMoved(string oldFullPath, string newFullPath)
        {
            try
            {
                // Get a list of all cached entries that existed in the source folder.   We will need to update those records
                var movedDict = _cache.Where(kvPair => kvPair.Key.StartsWith(oldFullPath, StringComparison.OrdinalIgnoreCase)).ToList();
                // Find the cached record for the root folder (oldFullPath)
                var rootEntry = movedDict.FirstOrDefault(kvp => string.Equals(kvp.Key, oldFullPath, StringComparison.OrdinalIgnoreCase));
                // If we found the root entry
                if (rootEntry.Key != null)
                {
                    // Transfer entries from old to new paths
                    foreach (var kvp in movedDict)
                    {
                        // Get the suffix (everything after the folder we're moving from)
                        var suffix = kvp.Key.Substring(oldFullPath.Length);
                        // Set the new path, and remove the old
                        _cache[newFullPath + suffix] = kvp.Value;
                        _cache.TryRemove(kvp.Key, out _);
                    }

                    var rootEntryStats = rootEntry.Value;

                    // Subtract from old parents
                    WalkParentCacheAndModify(newFullPath, -1 * rootEntryStats.TotalSize, -1 * rootEntryStats.FileCount);

                    // Add to new parents
                    WalkParentCacheAndModify(newFullPath, rootEntryStats.TotalSize, rootEntryStats.FileCount);

                    // Since we transferred the entries, and updated both parents, lets be done!
                    return;
                }

                // Fallback... No cache exists currently for the old to modify, lets add cache for new directory
                // Calculate the new director
                var newDirStats = GetStatistics(newFullPath);
                long totalSize = newDirStats.TotalSize ?? -1;
                long fileCount = newDirStats.FileCount ?? -1;
                if (totalSize != -1 && fileCount != -1)
                {
                    WalkParentCacheAndModify(newFullPath, totalSize, fileCount);
                }
            }
            catch { }
        }

        // Handle directory deletion: remove subtree and update ancestors using cached totals when available
        public void HandleDirectoryDeleted(string dirFullPath)
        {
            try
            {
                if (_cache.TryGetValue(dirFullPath, out var removed))
                {
                    // Remove subtree entries
                    RemoveDirectoryFromCache(dirFullPath);
                    // Subtract totals from cached ancestors only
                    WalkParentCacheAndModify(dirFullPath, -1 * removed.TotalSize, -1 * removed.FileCount);
                }
            }
            catch { }
        }

        public void ForceRefreshDirectory(string dirFullPath)
        {
            // Remove the current cache values, if so, we can programmatically shortcut the parent size modifications
            if (_cache.TryRemove(dirFullPath, out var oldStats))
            {
                // Set the new stats into cache (including children)
                var newStats = GetStatistics(dirFullPath);

                // Calculate the difference between the current size and the old size.
                var diffSize = ((newStats.TotalSize ?? 0) - oldStats.TotalSize);
                var diffCount = ((newStats.FileCount ?? 0) - oldStats.FileCount);

                // Update the difference through all the parents
                WalkParentCacheAndModify(dirFullPath, diffSize, diffCount);
            }
            else
            {
                GetStatistics(dirFullPath);

                // Navigate the parents
                var parentDir = Path.GetDirectoryName(dirFullPath);
                if (parentDir != null && parentDir != String.Empty && _directoryHelper.IsWithinRootDirectory(parentDir))
                {
                    ForceRefreshDirectory(parentDir);
                }
            }
        }

        private void WalkParentCacheAndModify(string fileFullPath, long size, long count)
        {
            try
            {
                // Start updating from the current folder
                var dir = Path.GetDirectoryName(fileFullPath);
                // While dir is set
                while (!string.IsNullOrEmpty(dir))
                {
                    // Try to load the directory from cache and decrement the size / count
                    if (_cache.TryGetValue(dir, out var cur))
                    {
                        var newSize = Math.Max(0, cur.TotalSize + size);
                        var newCount = Math.Max(0, cur.FileCount + count);
                        _cache[dir] = (newSize, newCount);
                    }
                    // Fallback: no cache? getStatstics will recalculate & cache new metrics
                    else
                    {
                        GetStatistics(dir);
                    }
                    // Because we changed this directory, we now need to update the parent directory
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
        }
    }
}
