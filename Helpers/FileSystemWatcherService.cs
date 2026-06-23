using System.IO;

namespace FileManager.Helpers
{
    public static class FileSystemWatcherService
    {
        public static FileSystemWatcher CreateAndStart(string rootDirectory, Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime)
        {
            if (!Directory.Exists(rootDirectory))
            {
                return null;
            }

            var watcher = new FileSystemWatcher(rootDirectory)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
            };

            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Changed += OnChanged;
            watcher.Error += OnError;

            lifetime.ApplicationStopping.Register(() => watcher.Dispose());

            return watcher;
        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    try
                    {
                        var size = new FileInfo(e.FullPath).Length;
                        DirectoryStatisticsCache.HandleFileCreated(e.FullPath, size);
                    }
                    catch
                    {
                        var parentDir = Path.GetDirectoryName(e.FullPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            DirectoryStatisticsCache.RemoveDirectoryFromCache(parentDir);
                        }
                    }
                }
                else if (Directory.Exists(e.FullPath))
                {
                    DirectoryStatisticsCache.HandleFolderCreate(e.FullPath);
                }
            }
            catch { }
        }

        private static void OnDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                var parentDir = Path.GetDirectoryName(e.FullPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    DirectoryStatisticsCache.RemoveDirectoryFromCache(parentDir);
                }
            }
            catch { }
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    try
                    {
                        var size = new FileInfo(e.FullPath).Length;
                        DirectoryStatisticsCache.HandleFileCreated(e.FullPath, size);
                    }
                    catch
                    {
                        var parentDir = Path.GetDirectoryName(e.FullPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            DirectoryStatisticsCache.RemoveDirectoryFromCache(parentDir);
                        }
                    }
                }
                else if (Directory.Exists(e.FullPath))
                {
                    DirectoryStatisticsCache.HandleFolderCreate(e.FullPath);
                }

                var oldParent = Path.GetDirectoryName(e.OldFullPath);
                if (!string.IsNullOrEmpty(oldParent))
                {
                    DirectoryStatisticsCache.RemoveDirectoryFromCache(oldParent);
                }
            }
            catch { }
        }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    var parentDir = Path.GetDirectoryName(e.FullPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        DirectoryStatisticsCache.RemoveDirectoryFromCache(parentDir);
                    }
                }
            }
            catch { }
        }

        private static void OnError(object sender, ErrorEventArgs e)
        {
            Console.WriteLine($"FileSystemWatcher error: {e.GetException().Message}");
        }
    }
}