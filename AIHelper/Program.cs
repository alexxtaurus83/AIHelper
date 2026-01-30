
using Serilog;
using AIHelper.Data;
using AIHelper.Interfaces;
using AIHelper.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace AIHelper
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("Starting AutoRemediator application...");
                // Set default environment variables in DEBUG mode
                #if DEBUG
                    Environment.SetEnvironmentVariable("AI_SYSTEM_PROMPT", @"You are a Senior Security Engineer. You will receive a source file and a list of security issues. Your task is to fix ALL listed issues by generating a diff in the **unified format**.

- The diff must be the ONLY thing you return.
- Do not include any explanations, markdown, or any other text outside of the diff.
- The diff should represent the changes needed to fix the original file.
- The diff will be used to programmatically patch the file, so it must be clean and valid.
- The file paths in the diff header should be `a/original.txt` and `b/patched.txt`.

Example of the expected output format:
```diff
--- a/original.txt
+++ b/patched.txt
@@ -1,5 +1,5 @@
 class Program
 {
-    static void Main(string[] args)
+    static void Main()
     {
         Console.WriteLine(""Hello, World!"");
     }
 }
```");
                    Environment.SetEnvironmentVariable("AI_API_URL", "https://openrouter.ai/api/v1");
                    Environment.SetEnvironmentVariable("AI_MODEL", "qwen/qwen3-coder:free");
                    Environment.SetEnvironmentVariable("MAX_FILE_SIZE_BYTES", "2097152"); // 2MB
                    Environment.SetEnvironmentVariable("SONAR_API_URL", "http://localhost:9000");
                    Environment.SetEnvironmentVariable("FORTIFY_API_URL", "https://api.ams.fortify.com");
                    Environment.SetEnvironmentVariable("GITLAB_API_URL", "https://gitlab.com/api/v4");
                #endif

                var builder = WebApplication.CreateBuilder(args);

                // Add services to the container.
                builder.Host.UseSerilog(); // Use Serilog for logging

                builder.Services.AddControllers();
                
                builder.Services.AddEndpointsApiExplorer();
                

                // Read API URLs from environment variables
                var aiApiUrl = Environment.GetEnvironmentVariable("AI_API_URL");
                Log.Debug("Using AI API URL: {AIUrl}", aiApiUrl);
                var sonarApiUrl = Environment.GetEnvironmentVariable("SONAR_API_URL");
                Log.Debug("Using Sonar API URL: {SonarUrl}", sonarApiUrl);
                var fortifyApiUrl = Environment.GetEnvironmentVariable("FORTIFY_API_URL");
                Log.Debug("Using Fortify API URL: {FortifyUrl}", fortifyApiUrl);
                var gitlabApiUrl = Environment.GetEnvironmentVariable("GITLAB_API_URL");
                Log.Debug("Using GitLab API URL: {GitlabUrl}", gitlabApiUrl);
                
                // Read Sonar polling configuration values from environment variables
                var pollingTimeoutSeconds = int.Parse(Environment.GetEnvironmentVariable("SONAR_POLLING_TIMEOUT_SECONDS") ?? "300");
                var pollingIntervalSeconds = int.Parse(Environment.GetEnvironmentVariable("SONAR_POLLING_INTERVAL_SECONDS") ?? "10");
                Log.Debug("Using Sonar polling timeout: {Timeout}s, interval: {Interval}s", pollingTimeoutSeconds, pollingIntervalSeconds);
                
                // Read AI configuration values from environment variables
                var aiApiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? "";
                var aiModel = Environment.GetEnvironmentVariable("AI_MODEL");
                Log.Debug("Using AI Model: {AIModel}", aiModel);
                // Register services with API URLs

                builder.Services.AddSwaggerGen(c => {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AIHelper", Version = "v1" });
                });
                builder.Services.AddSingleton<ScanProviderFactory>(provider => new ScanProviderFactory(sonarApiUrl, fortifyApiUrl, pollingTimeoutSeconds, pollingIntervalSeconds, provider.GetService<ILogger<ScanProviderFactory>>(), provider.GetService<ILogger<SonarProvider>>(), provider.GetService<ILogger<FortifyProvider>>()));
                builder.Services.AddScoped<IAiAgent>(provider => new AiAgent(aiApiKey, aiModel, aiApiUrl, provider.GetService<ILogger<AiAgent>>()));
                // Note: These service registrations may not be needed since RemediationService handles provider selection
                // If they are needed, they would need to be configured differently with enums
                builder.Services.AddScoped<IGitProvider>(provider => new GitLabProvider(gitlabApiUrl, provider.GetService<ILogger<GitLabProvider>>()));
                
                // Register the new Remediation Service
                builder.Services.AddScoped<IRemediationService, RemediationService>();

                // Add Entity Framework Core DbContext
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql("Host=localhost;Database=autofix;Username=postgres;Password=password"));

                var app = builder.Build();

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI(c => {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
                    });
                }

                app.UseAuthorization();


                app.MapControllers();

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
