namespace FileManager.Helpers
{
    public class DirectoryHelper
    {
        private readonly string _rootDirectory;

        public DirectoryHelper(string rootDirectory)
        {
            _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        }

        public string RootDirectory => _rootDirectory;

        /**
         *  Normalize a path by:
         *     1. trim trailing / or \
         *     2. replace the wrong \ with right / depending on the particular OS.
         */
        public string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path
                .Trim()
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                .Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
        }

        /**
         * Security Guard: Validate the path is within the root directory
         * This will prevent users from performing directory traversal attacks (e.g., using "../" to access parent directories)
         * 
         * The method works by:
         *   - Getting the full absolute path of the root directory.
         *   - Checking if the provided full path starts with the full root path (case-insensitive comparison).
         *   - Ensuring that the provided full path is longer than the full root path, which prevents access to the root directory itself.
         */
        public bool IsWithinRootDirectory(string fullPath, bool allowRootAccess = false)
        {
            string fullRootPath = System.IO.Path.GetFullPath(_rootDirectory);
            return fullPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase) && ((allowRootAccess && fullPath.Length == fullRootPath.Length) || fullPath.Length > fullRootPath.Length);
        }
    }
}