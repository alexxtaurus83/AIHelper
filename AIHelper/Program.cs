
using Serilog;
using AIHelper.Data;
using AIHelper.Interfaces;
using AIHelper.Services;
using Microsoft.EntityFrameworkCore;

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
                    Environment.SetEnvironmentVariable("AI_SYSTEM_PROMPT", "You are a Senior Security Engineer. You will receive a source file and a list of security issues. You must fix ALL listed issues in the code. Return ONLY the full, valid source code. No markdown, no explanations.");
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
                builder.Services.AddSwaggerGen();

                // Read API URLs from environment variables
                var aiApiUrl = Environment.GetEnvironmentVariable("AI_API_URL"); 
                Log.Debug("Using AI API URL: {AIUrl}", aiApiUrl);
                var sonarApiUrl = Environment.GetEnvironmentVariable("SONAR_API_URL");
                Log.Debug("Using Sonar API URL: {SonarUrl}", sonarApiUrl);
                var fortifyApiUrl = Environment.GetEnvironmentVariable("FORTIFY_API_URL");
                Log.Debug("Using Fortify API URL: {FortifyUrl}", fortifyApiUrl);
                var gitlabApiUrl = Environment.GetEnvironmentVariable("GITLAB_API_URL"); 
                Log.Debug("Using GitLab API URL: {GitlabUrl}", gitlabApiUrl);
                
                // Read AI configuration values from environment variables
                var aiApiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? "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";
                var aiModel = Environment.GetEnvironmentVariable("AI_MODEL");
                Log.Debug("Using AI Model: {AIModel}", aiModel);
                // Register services with API URLs
                builder.Services.AddSingleton<ScanProviderFactory>(provider => new ScanProviderFactory(sonarApiUrl, fortifyApiUrl, provider.GetService<ILogger<ScanProviderFactory>>(), provider.GetService<ILogger<SonarProvider>>(), provider.GetService<ILogger<FortifyProvider>>()));
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
                    app.UseSwaggerUI();
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
