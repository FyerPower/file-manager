using Microsoft.AspNetCore.Mvc;
using FileManager.Helpers;

namespace FileManager.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class ApiController : ControllerBase {

        private readonly ILogger<ApiController> _logger;
        private readonly string _rootDirectory;

        public ApiController(IConfiguration configuration, ILogger<ApiController> logger) {
            _logger = logger;

            // Get the root directory from configuration (appsettings.json)
            _rootDirectory = Normalize(Path.GetFullPath(configuration.GetValue<string>("RootDirectory") ?? "C:/TestFiles"));
        }

        /**
         * GET: 
         *   /api
         *   /api?path=subfolder
         * Retrieves the list of files and directories at the specified path. The path is relative to the root directory.
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
        [HttpGet]
        public IActionResult Get(string? path = null, string? search = null) {
            try {
                // Determine the target path based on the provided path parameter
                //   - If no path is provided, use the root directory
                //   - If the path is provided, combine it with the root directory
                string fullPath = string.IsNullOrWhiteSpace(path) ? _rootDirectory : Path.Combine(_rootDirectory, path);
                fullPath = Normalize(fullPath);

                // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
                if (!IsWithinRootDirectory(fullPath, true)) {
                    return BadRequest("Invalid path - access denied");
                }
                
                // Check if directory exists
                if (!Directory.Exists(fullPath)) {
                    return NotFound("Directory not found");
                }
                
                // Create a list to hold the folder items (both files and directories)
                var items = new List<FolderItem>();

                // Get Directory Information
                var directory = new DirectoryInfo(fullPath);

                string query = string.IsNullOrWhiteSpace(search) ? "*" : $"*{search}*";
                
                // Get all directories
                foreach (var dir in directory.GetDirectories(query)) {
                    var stats = DirectoryStatisticsCache.GetStatistics(dir.FullName);
                    items.Add(new FolderItem { Name = dir.Name, Type = "folder", Size = stats.TotalSize, FileCount = stats.FileCount });
                }

                // Get all files
                foreach (var file in directory.GetFiles(query)) {
                    items.Add(new FolderItem { Name = file.Name, Type = "file", Size = file.Length, FileCount = 1 });
                }
                
                // Return with a status code 200 OK and the list of items in the directory
                return Ok(items);
            } catch (Exception ex) {
                // If we caught an exception during the directory reading process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error reading directory");
                return StatusCode(500, "Error reading directory");
            }
        }

        

        /**
         * POST: /api/download
         * Downloads a file from the server. The request body must include a JSON payload with a `path` property.
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
        [HttpPost("download")]
        public IActionResult DownloadFile([FromBody] DownloadRequest request) {
            if (request == null || string.IsNullOrWhiteSpace(request.Path)) {
                return BadRequest("Path is required");
            }

            try {
                // Combine the provided path with the root directory to get the full path of the target file
                string fullPath = Path.Combine(_rootDirectory, request.Path);
                fullPath = Normalize(fullPath);

                // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
                if (!IsWithinRootDirectory(fullPath)) {
                    return BadRequest("Invalid path - access denied");
                }

                // Check if the path points to a file
                if (!System.IO.File.Exists(fullPath)) {
                    return NotFound("File not found");
                }

                // Get file information for the response headers
                var fileInfo = new FileInfo(fullPath);

                // Return the file to the client with appropriate content type and filename
                return PhysicalFile(fullPath, "application/octet-stream", fileInfo.Name);
            } catch (Exception ex) {
                // If we caught an exception during the file download process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error downloading file");
                return StatusCode(500, "Error downloading file");
            }
        }


        
        /**
         * Create a new file / folder: 
         *   Create Folder: POST /api?path=subfolder/newfolder
         *   Upload File:   POST /api?path=subfolder/newfile.txt (with file in form-data)
         * Creates a new folder or file at the specified path. The path is relative to the root directory. If a file 
         * is included in the form-data, it will be saved at the specified path; otherwise, a new folder will be created.
         */
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Create([FromQuery] string? path, IFormFile? file) {
            // If a file is included in the payload, then consider this a file upload to the path directory
            if(file != null) {
                return await UploadFile(path, file);
            } 
            // Otherwise, if only path is included, consider this a create folder request
            else if (!string.IsNullOrWhiteSpace(path)) {
                return await CreateFolder(path);
            }
            return BadRequest("File or Path are required");
        }

        /**
          * Handles the uploading of a file to the server. It takes an optional path parameter to determine where to 
          * save the file within the root directory. If no path is provided, it saves the file directly in the root 
          * directory. The method includes security checks to prevent directory traversal attacks and ensures that the 
          * necessary directories are created if they do not exist.
          * 
          * Validations:
          *   - The path must be within the root directory (prevents ../ attacks).
          * Behavior:
          *   - If the path points to a non-existent directory, the necessary directories will be created automatically.
          *   The uploaded file will be saved at the specified path with its original file name.
          * Responses:
          *   - 200 OK: If the file was uploaded successfully.
          *   - 400 Bad Request: If the validation fails (e.g., invalid path).
          *   - 500 Internal Server Error: If an error occurs during the file upload process.
          */
        private async Task<IActionResult> UploadFile(string? path, IFormFile file) {
            // Determine the fullpath based on the provided path parameter
            string fullPath = string.IsNullOrWhiteSpace(path) ? _rootDirectory : Path.Combine(_rootDirectory, path);
            fullPath = Normalize(fullPath);

            // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
            if (!IsWithinRootDirectory(fullPath)) {
                return BadRequest("Invalid path - access denied");
            }

            try {
                string directoryPath = Path.GetDirectoryName(fullPath) ?? _rootDirectory;
                if (!Directory.Exists(directoryPath)) {
                    Directory.CreateDirectory(directoryPath);
                }

                await using var targetStream = System.IO.File.Create(fullPath);
                await file.CopyToAsync(targetStream);

                // Update cache for newly created file
                try { DirectoryStatisticsCache.HandleFileCreated(fullPath, file.Length); } catch { }

                return Ok("File created successfully");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error creating file");
                return StatusCode(500, "Error creating file");
            }
        }

        /**
         * Handles the creation of a new folder at the specified path. It includes security checks to prevent directory 
         * traversal attacks and ensures that the folder is created successfully if it does not already exist.
         * 
         * Validations:
         *   - The path must be within the root directory (prevents ../ attacks).
         * Behavior:
         *   - If the folder already exists, it will return a success response without creating a new one.
         *   - If the folder does not exist, it will be created at the specified path.
         * Responses:
         *   - 200 OK: If the folder was created successfully or already exists.
         *   - 400 Bad Request: If the validation fails (e.g., invalid path).
         *   - 500 Internal Server Error: If an error occurs during the folder creation process.
         */
        private async Task<IActionResult> CreateFolder(string path) {
            string fullPath = Path.Combine(_rootDirectory, path);
            fullPath = Normalize(fullPath);

            // Security Guard: Validate the path is within the root directory ( prevents ../ attacks )
            if (!IsWithinRootDirectory(fullPath)) {
                return BadRequest("Invalid path - access denied");
            }

            // If the folder does not already exist, lets create it.
            if (!Directory.Exists(fullPath)) {
                Directory.CreateDirectory(fullPath);
                // Ensure cache updated for new (empty) directory
                try { DirectoryStatisticsCache.HandleFolderCreate(fullPath); } catch { }
                return Ok("Folder created successfully");
            }
            
            // If we got here, that means the folder already exists, lets return a fail response indicating that.
            _logger.LogError("Folder at path already exists: {Path}", fullPath);
            return StatusCode(500, $"Folder at path already exists: {path}");
        }

        /**
         * DELETE: /api?path=subfolder/file.txt
         * Deletes a file or directory at the specified path. The path is relative to the root directory.
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
        [HttpDelete]
        public IActionResult Delete(string? path) {
            // If the path provided is null, empty, or whitespace, return a 400 Bad Request response with an error message indicating that the path is required
            if (string.IsNullOrWhiteSpace(path)) {
                return BadRequest("Path is required");
            }

            try {
                // Get the full target path
                string fullPath = Path.Combine(_rootDirectory, path);
                fullPath = Normalize(fullPath);

                // Validate the file or directory is within the root directory ( prevents ../ attacks )
                if (!IsWithinRootDirectory(fullPath)) {
                    return BadRequest("Invalid path - access denied");
                }

                // Delete if its a file
                if (System.IO.File.Exists(fullPath)) {
                    long size = 0;
                    try { var fi = new FileInfo(fullPath); size = fi.Length; } catch { }
                    System.IO.File.Delete(fullPath);
                    try { DirectoryStatisticsCache.HandleFileDeleted(fullPath, size); } catch { }
                    return Ok("Deleted successfully");
                }

                // Delete if its a directory (recursively)
                if (Directory.Exists(fullPath)) {
                    Directory.Delete(fullPath, true);
                    try { DirectoryStatisticsCache.HandleDirectoryDeleted(fullPath); } catch { }
                    return Ok("Deleted successfully");
                }

                // If the file or directory does not exist, return a 404 Not Found
                return NotFound("File or directory not found");
            } catch (Exception ex) {
                // If we caught an exception during the deletion process, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error deleting path");
                return StatusCode(500, "Error deleting path");
            }
        }

        /**
         * PUT: 
         *    /api?sourcePath=subfolder/file.txt&destinationPath=otherfolder/
         *    /api?sourcePath=subfolder/file.txt&destinationPath=otherfolder/newfile.txt
         * Moves a file or directory from sourcePath to destinationPath. Both paths are relative to the root directory.
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
        [HttpPut]
        public IActionResult MoveFile(string sourcePath, string destinationPath) {
            // If either the sourcePath or destinationPath is null, empty, or whitespace, return a 400 Bad Request response with an error message indicating that both paths are required
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath)) {
                return BadRequest("Both sourcePath and destinationPath are required");
            }

            try {
                string sourceFullPath = Path.Combine(_rootDirectory, sourcePath);
                sourceFullPath = Normalize(sourceFullPath);
                string destinationFullPath = Path.Combine(_rootDirectory, destinationPath);
                destinationFullPath = Normalize(destinationFullPath);

                // Validate both source and destination paths are within the root directory ( prevents ../ attacks )
                if (!IsWithinRootDirectory(sourceFullPath) || !IsWithinRootDirectory(destinationFullPath)) {
                    return BadRequest("Source and destination must be within the root directory");
                }

                // Validate both the source and destination paths are different
                if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase)) {
                    return BadRequest("Source and destination cannot be the same");
                }

                // If we're moving a file and it exists, move it to the destination accordingly.
                if (System.IO.File.Exists(sourceFullPath)) {
                    // If the destination is a directory, append the source file name to the destination path
                    if (string.IsNullOrEmpty(Path.GetFileName(destinationFullPath))) {
                        destinationFullPath = Path.Combine(destinationFullPath, Path.GetFileName(sourceFullPath));
                    }

                    // If the destination folder does not exist yet, lets create it
                    string destinationDirectory = Path.GetDirectoryName(destinationFullPath) ?? throw new InvalidOperationException("Invalid path");
                    if (!Directory.Exists(destinationDirectory)) {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    // Move the file to the destination path
                    long size = 0;
                    try { var fi = new FileInfo(sourceFullPath); size = fi.Length; } catch { }
                    System.IO.File.Move(sourceFullPath, destinationFullPath);
                    try { DirectoryStatisticsCache.HandleFileMoved(sourceFullPath, destinationFullPath, size); } catch { }

                    // Return a 200 OK response with a success message indicating that the file was moved successfully
                    return Ok("Moved successfully");
                }

                // If we're moving a directory..
                if (Directory.Exists(sourceFullPath)) {
                    // Validate that the destination is not inside the source directory (to prevent moving a directory into itself or one of its subdirectories)
                    //   Force append a DirectorySeparatorChar to the sourceFullPath to ensure we are comparing directories correctly (e.g., C:/TestFiles/Source vs C:/TestFiles/SourceSubfolder)
                    if (destinationFullPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                        return BadRequest("Destination cannot be inside the source directory");
                    }

                    // If the destination folder does not exist yet, lets create it
                    string destinationDirectory = Path.GetDirectoryName(destinationFullPath) ?? throw new InvalidOperationException("Invalid destination path");
                    if (!Directory.Exists(destinationDirectory)) {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    // Move the directory to the destination path
                    Directory.Move(sourceFullPath, destinationFullPath);
                    try { DirectoryStatisticsCache.HandleDirectoryMoved(sourceFullPath, destinationFullPath); } catch { }

                    // Return a 200 OK response with a success message indicating that the directory was moved successfully
                    return Ok("Moved successfully");
                }

                // If the source file or directory does not exist, return a 404 Not Found
                return NotFound("Source file or directory not found");
            } catch (Exception ex) {
                // If we caught an exception during the move operation, log the error and return a 500 Internal Server Error
                _logger.LogError(ex, "Error moving path");
                return StatusCode(500, "Error moving path");
            }
        }

        /**
         * POST: /api/duplicate?path=subfolder/file.txt
         * Duplicates a file or directory at the specified path. The path is relative to the root directory.
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
        public IActionResult Duplicate(string? path) {
            // Validate a path was sent
            if (string.IsNullOrWhiteSpace(path)) {
                return BadRequest("Path is required");
            }

            try {
                string sourceFullPath = Path.Combine(_rootDirectory, path);
                sourceFullPath = Normalize(sourceFullPath);

                // Security Guard: Validate the path is within the root directory
                if (!IsWithinRootDirectory(sourceFullPath)) {
                    return BadRequest("Invalid path - access denied");
                }

                // Check if we're duplicating a file
                if (System.IO.File.Exists(sourceFullPath)) {
                    string directory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException("Invalid path");;
                    string fileName = Path.GetFileNameWithoutExtension(sourceFullPath);
                    string fileExtension = Path.GetExtension(sourceFullPath);
                    string destinationPath = Path.Combine(directory, $"{fileName} - Copy{fileExtension}");

                    // Find the next counter. Continue searching until we find the next value that does not exist.
                    int counter = 1;
                    while (System.IO.File.Exists(destinationPath)) {
                        destinationPath = Path.Combine(directory, $"{fileName} - Copy ({counter}){fileExtension}");
                        counter++;
                    }

                    // Copy the file
                    System.IO.File.Copy(sourceFullPath, destinationPath);
                    
                    // Update cache for the new file
                    var fileInformation = new FileInfo(destinationPath);
                    DirectoryStatisticsCache.HandleFileCreated(destinationPath, fileInformation.Length);

                    return Ok($"File duplicated successfully to {path}");
                }

                // If we're duplicating a directory
                if (Directory.Exists(sourceFullPath)) {
                    string parentDirectory = Path.GetDirectoryName(sourceFullPath) ?? throw new InvalidOperationException("Invalid path");;
                    string directoryName = Path.GetFileName(sourceFullPath);
                    string destinationPath = Path.Combine(parentDirectory, $"{directoryName} - Copy");

                    // Find the next counter. Continue searching until we find the next value that does not exist.
                    int counter = 1;
                    while (Directory.Exists(destinationPath)) {
                        destinationPath = Path.Combine(parentDirectory, $"{directoryName} - Copy ({counter})");
                        counter++;
                    }

                    // Recursively copy directory
                    CopyDirectory(sourceFullPath, destinationPath);

                    // Update cache for the duplicated directory tree
                    DirectoryStatisticsCache.GetStatistics(destinationPath);

                    return Ok($"Directory duplicated successfully to {path}");
                }

                // If the source file or directory does not exist
                return NotFound("Source file or directory not found");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error duplicating path");
                return StatusCode(500, "Error duplicating path");
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
            foreach (var file in sourceDirectory.GetFiles()) {
                string destFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(destFilePath, overwrite: false);
            }

            // Recursively copy all subdirectories
            foreach (var dir in sourceDirectory.GetDirectories()) {
                string destSubDir = Path.Combine(destDir, dir.Name);
                CopyDirectory(dir.FullName, destSubDir);
            }
        }

        /**
         * Security Guard: Validate the path is within the root directory
         * This will prevent users from performing directory traversal attacks (e.g., using "../" to access parent directories)
         * 
         * The method works by:
         *   - Getting the full absolute path of the root directory.
         *   - Checking if the provided full path starts with the full root path (case-insensitive comparison).
         *   - Ensuring that the provided full path is longer than the full root path, which prevents access to the root directory itself.
         * 
         * This was commonly reused in multiple endpoints, so I extracted it to a separate method for better code organization and readability.
         */
        private bool IsWithinRootDirectory(string fullPath, bool allowRootAccess = false) {
            string fullRootPath = Path.GetFullPath(_rootDirectory);
            return fullPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase) && (allowRootAccess || fullPath.Length > fullRootPath.Length);
        }

        /**
         *  Normalize a path by:
         *    1. trim trailing / or \
        *     2. replace the wrong \ with right / depending on the particular OS.
         */
        private static string Normalize(string path) {
            if (string.IsNullOrWhiteSpace(path)) 
                return string.Empty;
            
            return path
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }

    public class FolderItem {
        public required string Name { get; set; }
        public required string Type { get; set; }
        public required long? Size { get; set; }
        public required long? FileCount { get; set; }
    }
    
    public class DownloadRequest {
        public string? Path { get; set; }
    }
}