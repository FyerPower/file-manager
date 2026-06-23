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

            // Add services to the container.
            builder.Services.AddControllers();

            var app = builder.Build();

            // Get the root directory from configuration
            var rootDirectory = DirectoryHelper.NormalizePath(builder.Configuration.GetValue<string>("RootDirectory") ?? "C:/TestFiles");

            // Start file system watcher to keep directory statistics cache up to date
            FileSystemWatcherService.CreateAndStart(rootDirectory, app.Lifetime);

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