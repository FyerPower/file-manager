namespace TestProject {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.MapControllers();

            // Whenever a route comes in that does not match the above usages (redirect, static files, or controllers), then send them to `index.html`
            app.MapFallbackToFile("index.html");

            app.Run();
        }
    }
}