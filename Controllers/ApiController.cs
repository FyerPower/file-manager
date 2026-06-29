using Microsoft.AspNetCore.Mvc;
using FileManager.Helpers;

namespace FileManager.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ApiController : ControllerBase
    {
        private readonly ILogger<ApiController> _logger;
        private readonly DirectoryHelper _directoryHelper;
        private readonly DirectoryStatisticsCache _directoryStatisticsCache;

        public ApiController(IConfiguration configuration, ILogger<ApiController> logger, DirectoryHelper directoryHelper, DirectoryStatisticsCache directoryStatisticsCache)
        {
            _logger = logger;
            _directoryHelper = directoryHelper;
            _directoryStatisticsCache = directoryStatisticsCache;
        }

        /**
         * POST: /api
         * Request Body:
         *   { Path: string, Search: string }
         * Description:
         *   Retrieves the list of files and directories at the specified path. The path is relative to the root directory.
         * Validations:
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If no path is provided, it will return the contents of the root directory.
         *   - If a path is provided, it will return the contents of that subdirectory.
         * Responses:
         *   - 200 OK: If the directory was read successfully, along with a JSON array of items (files and folders).
         *   - 400 Bad Request: If the validation fails (e.g., invalid path).
         *   - 404 Not Found: If the specified directory does not exist.
         *   - 500 Internal Server Error: If an error occurs during the directory reading process.
         */
        [HttpPost("list")]
        public IActionResult List([FromBody] ListRequest? request)
        {
            try
            {
                var path = request?.Path;
                var search = request?.Search;

                // Determine the target path based on the provided path parameter
                //   - If no path is provided, use the root directory
                //   - If the path is provided, combine it with the root directory
                string fullPath = string.IsNullOrWhiteSpace(path) ? _directoryHelper.RootDirectory : Path.Combine(_directoryHelper.RootDirectory, path);
                fullPath = _directoryHelper.NormalizePath(fullPath);

                // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
                if (!_directoryHelper.IsWithinRootDirectory(fullPath, true))
                {
                    return BadRequest(new { error = "Invalid path - access denied" });
                }

                // Check if directory exists
                if (!Directory.Exists(fullPath))
                {
                    return NotFound(new { error = "Directory not found" });
                }

                // Create a list to hold the folder items (both files and directories)
                var items = new List<FolderItem>();

                // Get Directory Information
                var directory = new DirectoryInfo(fullPath);

                // *** Potential Performance Improvement: Instead of using "*" it might be more performance to use null.   Using null might cut around the filtering logic where having a search value / wildcard would require search matching.
                string query = string.IsNullOrWhiteSpace(search) ? "*" : $"*{search}*";

                // Get all directories
                // *** Potential Performance Improvement:  Use EnumerateDirectories instead of GetDirectories.   (Enumerate will stream directories instead of potentially getting back a very large DirectoryInfo[] filling memory unnecessarily)
                foreach (var dir in directory.GetDirectories(query))
                {
                    var stats = _directoryStatisticsCache.GetStatistics(dir.FullName);
                    items.Add(new FolderItem { Name = dir.Name, Type = "folder", Size = stats.TotalSize, FileCount = stats.FileCount });
                }

                // Get all files
                foreach (var file in directory.GetFiles(query))
                {
                    items.Add(new FolderItem { Name = file.Name, Type = "file", Size = file.Length, FileCount = 1 });
                }

                // Return with a status code 200 OK and the list of items in the directory
                return Ok(items);
            }
            catch (Exception ex)
            {
                // If we caught an exception during the directory reading process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error reading directory");
                return StatusCode(500, new { error = "Error reading directory" });
            }
        }



        /**
         * POST: /api/download
         * Request Body:
         *   { Path: string }
         * Description: 
         *   Downloads a file from the server. The request body must include a JSON payload with a `path` property.
         * Validations:
         *   - The path parameter is required and cannot be null, empty, or whitespace.
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If the path points to a file, the file will be returned to the client for download.
         *   - If the path points to a directory or does not exist, an error will be returned.
         * Responses:
         *   - 200 OK: If the file was found and downloaded successfully.
         *   - 400 Bad Request: If the validation fails (e.g., missing path, invalid path).
         *   - 404 Not Found: If the file does not exist.
         *   - 500 Internal Server Error: If an error occurs during the download process.
         */
        [HttpPost("download-file")]
        public IActionResult DownloadFile([FromBody] DownloadRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest(new { error = "Path is required" });
            }

            try
            {
                // Combine the provided path with the root directory to get the full path of the target file
                string fullPath = Path.Combine(_directoryHelper.RootDirectory, request.Path);
                fullPath = _directoryHelper.NormalizePath(fullPath);

                // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
                if (!_directoryHelper.IsWithinRootDirectory(fullPath))
                {
                    return BadRequest(new { error = "Invalid path - access denied" });
                }

                // Check if the path points to a file
                if (!System.IO.File.Exists(fullPath))
                {
                    return NotFound(new { error = "File not found" });
                }

                // Get file information for the response headers
                var fileInfo = new FileInfo(fullPath);

                // Return the file to the client with appropriate content type and filename
                return PhysicalFile(fullPath, "application/octet-stream", fileInfo.Name);
            }
            catch (Exception ex)
            {
                // If we caught an exception during the file download process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error downloading file");
                return StatusCode(500, new { error = "Error downloading file" });
            }
        }

        /**
         * POST: /api/create
         * Request Form Data:
         *   { Path: string }
         * Description: 
         *   Creates a new Folder into the file system
         * Validations:
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If the folder already exists, it will return a conflict response.
         *   - If the folder does not exist, it will be created at the specified path.
         * Responses:
         *   - 200 OK: If the folder was created successfully.
         *   - 400 Bad Request: If the validation fails (e.g., invalid path).
         *   - 409 Conflict: If a file or folder already exists at the specified path.
         *   - 500 Internal Server Error: If an error occurs during the folder creation process.
         */
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] PathRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Path))
            {
                return BadRequest(new { error = "Path is required" });
            }

            string fullPath = Path.Combine(_directoryHelper.RootDirectory, request.Path);
            fullPath = _directoryHelper.NormalizePath(fullPath);

            // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
            if (!_directoryHelper.IsWithinRootDirectory(fullPath))
            {
                return BadRequest(new { error = "Invalid path - access denied" });
            }

            // Fail if the same path already exists as a folder
            if (Directory.Exists(fullPath))
            {
                return Conflict(new { error = "A folder already exists at the specified path" });
            }

            Directory.CreateDirectory(fullPath);
            return Ok(new { message = "Folder created successfully" });
        }

        /**
         * POST: /api/upload
         * Request Data:
         *   { path: string, file: File }
         * Description: 
         *   Uploads a new file into the file system
         * Validations:
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If the path points to a non-existent directory, the necessary directories will be created automatically.
         *   The uploaded file will be saved at the specified path with its original file name.
         * Responses:
         *   - 200 OK: If the file was uploaded successfully.
         *   - 400 Bad Request: If the validation fails (e.g., invalid path).
         *   - 409 Conflict: If a file or folder already exists at the target path.
         *   - 500 Internal Server Error: If an error occurs during the file upload process.
         */
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload([FromForm] string? path, IFormFile? file)
        {
            // If a file is included in the payload, then consider this a file upload to the path directory
            if (file == null)
            {
                return BadRequest(new { error = "File is required for upload" });
            }

            // Determine the fullpath based on the provided path parameter
            string fullPath = string.IsNullOrWhiteSpace(path) ? _directoryHelper.RootDirectory : Path.Combine(_directoryHelper.RootDirectory, path);
            fullPath = _directoryHelper.NormalizePath(fullPath);

            // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
            if (!_directoryHelper.IsWithinRootDirectory(fullPath))
            {
                return BadRequest(new { error = "Invalid path - access denied" });
            }

            try
            {
                // Fail fast if the target path already exists as a file or folder
                if (System.IO.File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    return Conflict(new { error = "A file or folder already exists at the specified path" });
                }

                string directoryPath = Path.GetDirectoryName(fullPath) ?? _directoryHelper.RootDirectory;
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                await using var targetStream = System.IO.File.Create(fullPath);
                await file.CopyToAsync(targetStream);

                return Ok(new { message = "File created successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating file");
                return StatusCode(500, new { error = "Error creating file" });
            }
        }

        /**
         * POST: /api/delete
         * Request Data:
         *   { path: string }
         * Description: 
         *   Deletes a file or directory at the specified path. The path is relative to the root directory.
         * Validations:
         *   - The path parameter is required and cannot be null, empty, or whitespace.
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If the path points to a file, it will be deleted.
         *   - If the path points to a directory, the entire directory and its contents will be deleted recursively.
         * Responses:
         *   - 200 OK: If the deletion was successful.
         *   - 400 Bad Request: If the validation fails (e.g., missing path, invalid path).
         *   - 404 Not Found: If the file or directory does not exist.
         *   - 500 Internal Server Error: If an error occurs during the deletion process.
         */
        [HttpPost("delete")]
        public IActionResult Delete([FromBody] PathRequest request)
        {
            var path = request?.Path;
            // If the path provided is null, empty, or whitespace, return a 400 Bad Request response with an error message indicating that the path is required
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "Path is required" });
            }

            try
            {
                // Get the full target path
                string fullPath = Path.Combine(_directoryHelper.RootDirectory, path);
                fullPath = _directoryHelper.NormalizePath(fullPath);

                // Validate the file or directory is within the root directory ( prevents ../ attacks )
                if (!_directoryHelper.IsWithinRootDirectory(fullPath))
                {
                    return BadRequest(new { error = "Invalid path - access denied" });
                }

                // Delete if its a file
                if (System.IO.File.Exists(fullPath))
                {
                    long size = 0;
                    try { var fi = new FileInfo(fullPath); size = fi.Length; } catch { }
                    System.IO.File.Delete(fullPath);
                    return Ok(new { message = "Deleted successfully" });
                }

                // Delete if its a directory (recursively)
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    return Ok(new { message = "Deleted successfully" });
                }

                // If the file or directory does not exist, return a 404 Not Found
                return NotFound(new { error = "File or directory not found" });
            }
            catch (Exception ex)
            {
                // If we caught an exception during the deletion process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error deleting path");
                return StatusCode(500, new { error = "Error deleting path" });
            }
        }

        /**
         * POST: /api/move
         * Request Data:
         *   { sourcePath: string, destinationPath: string }
         * Description: 
         *   Moves a file or directory from sourcePath to destinationPath. Both paths are relative to the root directory.
         * Validations:
         *    - Both sourcePath and destinationPath are required and cannot be null, empty, or whitespace.
         *    - Both paths must be within the root directory (prevents ../ attacks).
         *    - Source and destination paths cannot be the same.
         *    - If moving a directory, the destination cannot be inside the source directory.
         * Behavior:
         *    - If the source path is a file, it will be moved to the destination path. If the destination is an existing directory, the file will be moved inside that directory.
         *    - If the source path is a directory, it will be moved to the destination path. The entire directory and its contents will be moved.
         * Responses:
         *    - 200 OK: If the move operation was successful.
         *    - 400 Bad Request: If any validation fails (e.g., missing paths, invalid paths, same source and destination).
         *    - 404 Not Found: If the source file or directory does not exist.
         *    - 500 Internal Server Error: If an error occurs during the move operation.
         */
        [HttpPost("move")]
        public IActionResult MoveFile([FromBody] MoveRequest request)
        {
            var sourcePath = request?.SourcePath;
            var destinationPath = request?.DestinationPath;

            // If the sourcePath is null, empty, or whitespace, return a 400 Bad Request response with an error message indicating that both paths are required
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return BadRequest(new { error = "sourcePath is required" });
            }

            try
            {
                string sourceFullPath = Path.Combine(_directoryHelper.RootDirectory, sourcePath);
                sourceFullPath = _directoryHelper.NormalizePath(sourceFullPath);
                string destinationFullPath = Path.Combine(_directoryHelper.RootDirectory, destinationPath ?? "");
                destinationFullPath = _directoryHelper.NormalizePath(destinationFullPath);

                // Validate both source and destination paths are within the root directory ( prevents ../ attacks )
                if (!_directoryHelper.IsWithinRootDirectory(sourceFullPath) || !_directoryHelper.IsWithinRootDirectory(destinationFullPath))
                {
                    return BadRequest(new { error = "Source and destination must be within the root directory" });
                }

                // Validate both the source and destination paths are different
                if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "Source and destination cannot be the same" });
                }

                // If we're moving a file and it exists, move it to the destination accordingly.
                if (System.IO.File.Exists(sourceFullPath))
                {
                    // If the destination is a directory, append the source file name to the destination path
                    if (string.IsNullOrEmpty(Path.GetFileName(destinationFullPath)))
                    {
                        destinationFullPath = Path.Combine(destinationFullPath, Path.GetFileName(sourceFullPath));
                    }

                    // If the destination folder does not exist yet, lets create it
                    string destinationDirectory = Path.GetDirectoryName(destinationFullPath) ?? throw new InvalidOperationException("Invalid path");
                    if (!Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    // Move the file to the destination path
                    long size = 0;
                    try { var fi = new FileInfo(sourceFullPath); size = fi.Length; } catch { }
                    System.IO.File.Move(sourceFullPath, destinationFullPath);

                    // Return a 200 OK response with a success message indicating that the file was moved successfully
                    return Ok(new { message = "Moved successfully" });
                }

                // If we're moving a directory..
                if (Directory.Exists(sourceFullPath))
                {
                    // Validate that the destination is not inside the source directory (to prevent moving a directory into itself or one of its subdirectories)
                    //   Force append a DirectorySeparatorChar to the sourceFullPath to ensure we are comparing directories correctly (e.g., C:/TestFiles/Source vs C:/TestFiles/SourceSubfolder)
                    if (destinationFullPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { error = "Destination cannot be inside the source directory" });
                    }

                    // If the destination folder does not exist yet, lets create it
                    string destinationDirectory = Path.GetDirectoryName(destinationFullPath) ?? throw new InvalidOperationException("Invalid destination path");
                    if (!Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    // Move the directory to the destination path
                    Directory.Move(sourceFullPath, destinationFullPath);

                    // Return a 200 OK response with a success message indicating that the directory was moved successfully
                    return Ok(new { message = "Moved successfully" });
                }

                // If the source file or directory does not exist, return a 404 Not Found
                return NotFound(new { error = "Source file or directory not found" });
            }
            catch (Exception ex)
            {
                // If we caught an exception during the move operation, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error moving path");
                return StatusCode(500, new { error = "Error moving path" });
            }
        }

        /**
         * POST: /api/duplicate
         * Request Data:
         *   { Path: string }
         * Description: 
         *   Duplicates a file or directory at the specified path. The path is relative to the root directory.
         * Validations:
         *    - The path parameter is required and cannot be null, empty, or whitespace.
         *    - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *    - If the source path is a file, it will be copied to the same directory with a " - Copy" suffix before the extension.
         *    - If the source path is a directory, it will be recursively copied to the same parent directory with a " - Copy" suffix.
         * Responses:
         *    - 200 OK: If the duplication was successful.
         *    - 400 Bad Request: If the validation fails (e.g., missing path, invalid path).
         *    - 404 Not Found: If the source file or directory does not exist.
         *    - 500 Internal Server Error: If an error occurs during the duplication process.
         */
        [HttpPost("duplicate")]
        public IActionResult Duplicate([FromBody] PathRequest request)
        {
            var path = request?.Path;
            // Validate a path was sent
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest(new { error = "Path is required" });
            }

            try
            {
                string sourceFullPath = Path.Combine(_directoryHelper.RootDirectory, path);
                sourceFullPath = _directoryHelper.NormalizePath(sourceFullPath);

                // Security Guard: Validate the path is within the root directory
                if (!_directoryHelper.IsWithinRootDirectory(sourceFullPath))
                {
                    return BadRequest(new { error = "Invalid path - access denied" });
                }

                // Check if we're duplicating a file
                if (System.IO.File.Exists(sourceFullPath))
                {
                    string directory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException("Invalid path"); ;
                    string fileName = Path.GetFileNameWithoutExtension(sourceFullPath);
                    string fileExtension = Path.GetExtension(sourceFullPath);
                    string destinationPath = Path.Combine(directory, $"{fileName} - Copy{fileExtension}");

                    // Find the next counter. Continue searching until we find the next value that does not exist.
                    int counter = 1;
                    while (System.IO.File.Exists(destinationPath))
                    {
                        destinationPath = Path.Combine(directory, $"{fileName} - Copy ({counter}){fileExtension}");
                        counter++;
                    }

                    // Copy the file
                    System.IO.File.Copy(sourceFullPath, destinationPath);

                    // Update cache for the new file
                    var fileInformation = new FileInfo(destinationPath);

                    return Ok(new { message = $"File duplicated successfully to {path}" });
                }

                // If we're duplicating a directory
                if (Directory.Exists(sourceFullPath))
                {
                    string parentDirectory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException("Invalid path"); ;
                    string directoryName = Path.GetFileName(sourceFullPath);
                    string destinationPath = Path.Combine(parentDirectory, $"{directoryName} - Copy");

                    // Find the next counter. Continue searching until we find the next value that does not exist.
                    int counter = 1;
                    while (Directory.Exists(destinationPath))
                    {
                        destinationPath = Path.Combine(parentDirectory, $"{directoryName} - Copy ({counter})");
                        counter++;
                    }

                    // Recursively copy directory
                    CopyDirectory(sourceFullPath, destinationPath);

                    return Ok(new { message = $"Directory duplicated successfully to {path}" });
                }

                // If the source file or directory does not exist
                return NotFound(new { error = "Source file or directory not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error duplicating path");
                return StatusCode(500, new { error = "Error duplicating path" });
            }
        }

        // Helper method to recursively copy a directory and its contents
        private void CopyDirectory(string sourceDir, string destDir)
        {
            // Create the destination directory
            Directory.CreateDirectory(destDir);

            // Get source directory information
            var sourceDirectory = new DirectoryInfo(sourceDir);

            // Copy all files
            foreach (var file in sourceDirectory.GetFiles())
            {
                string destFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(destFilePath, overwrite: false);
            }

            // Recursively copy all subdirectories
            foreach (var dir in sourceDirectory.GetDirectories())
            {
                string destSubDir = Path.Combine(destDir, dir.Name);
                CopyDirectory(dir.FullName, destSubDir);
            }
        }

    }

    public class PathRequest
    {
        public string? Path { get; set; }
    }

    public class ListRequest
    {
        public string? Path { get; set; }
        public string? Search { get; set; }
    }

    public class MoveRequest
    {
        public string? SourcePath { get; set; }
        public string? DestinationPath { get; set; }
    }

    public class FolderItem
    {
        public required string Name { get; set; }
        public required string Type { get; set; }
        public required long? Size { get; set; }
        public required long? FileCount { get; set; }
    }

    // REFACTOR: Get rid of this class and use PathRequest instead.
    public class DownloadRequest
    {
        public string? Path { get; set; }
    }
}