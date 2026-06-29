using System.IO;
using FileManager.Helpers;

namespace FileManager
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.AddEnvironmentVariables();

            // Get the root directory from configuration
            var rootDirectory = builder.Configuration.GetValue<string>("RootDirectory");

            // Validate root directory exists before starting the app
            if (!Directory.Exists(rootDirectory))
            {
                Console.WriteLine($"Fatal: RootDirectory does not exist: {rootDirectory}");
                return;
            }

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddSingleton(new DirectoryHelper(rootDirectory));
            builder.Services.AddSingleton<DirectoryStatisticsCache>();
            builder.Services.AddSingleton<FileSystemWatcherService>();

            var app = builder.Build();

            // Start file system watcher to keep directory statistics cache up to date
            var watcherService = app.Services.GetRequiredService<FileSystemWatcherService>();
            watcherService.CreateAndStart(rootDirectory, app.Lifetime);

            // Configure the HTTP request pipeline.
            if (builder.Configuration.GetValue<bool>("UseHttpsRedirection", true))
            {
                app.UseHttpsRedirection();
            }

            app.UseStaticFiles();

            app.MapControllers();

            // Whenever a route comes in that does not match the above usages (redirect, static files, or controllers), then send them to `index.html`
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}