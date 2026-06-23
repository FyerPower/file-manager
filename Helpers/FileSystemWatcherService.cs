using System.IO;

namespace FileManager.Helpers
{
    public class FileSystemWatcherService
    {
        private readonly DirectoryStatisticsCache _directoryStatisticsCache;

        public FileSystemWatcherService(DirectoryStatisticsCache directoryStatisticsCache)
        {
            _directoryStatisticsCache = directoryStatisticsCache;
        }

        public void CreateAndStart(string rootDirectory, Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime)
        {
            // Initialize the FileSystemWatcher and have it monitor Subdirectories too
            var watcher = new FileSystemWatcher(rootDirectory)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
            };

            // Subscribe to the events
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            // Monitor the lifetime of our application and on its stop we need to properly clean up the file system watcher
            lifetime.ApplicationStopping.Register(() => watcher.Dispose());
        }

        /**
         *  Listen to the OnCreated Event (handles both file and folder creation)
         */
        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    var size = new FileInfo(e.FullPath).Length;
                    _directoryStatisticsCache.HandleFileCreated(e.FullPath, size);
                }
                else if (Directory.Exists(e.FullPath))
                {
                    _directoryStatisticsCache.HandleFolderCreate(e.FullPath);
                }
            }
            catch { }
        }

        /**
         *  Listen to the OnDeleted Event (handles both file and folder creation)
         */
        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                _directoryStatisticsCache.ForceRefreshDirectory(e.FullPath);
            }
            catch { }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    FileInfo fileInfo = new FileInfo(e.FullPath);
                    _directoryStatisticsCache.HandleFileMoved(e.OldFullPath, e.FullPath, fileInfo.Length);
                }
                else if (Directory.Exists(e.FullPath))
                {
                    _directoryStatisticsCache.HandleDirectoryMoved(e.OldFullPath, e.FullPath);
                }
            }
            catch { }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Console.WriteLine($"FileSystemWatcher error: {e.GetException().Message}");
        }
    }
}
