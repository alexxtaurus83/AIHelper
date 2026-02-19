
using Serilog;
using AIHelper.Data;
using AIHelper.Interfaces;
using AIHelper.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Threading.Tasks;

namespace AIHelper {
    public class Program {
        public static async Task Main(string[] args) {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try {
                Log.Information("Starting AutoRemediator application...");
                // Set default environment variables in DEBUG mode
#if DEBUG
                Environment.SetEnvironmentVariable("LOCALIZE_AI_API_URL", "http://localhost:8080/v1"); //https://openrouter.ai/api/v1
                Environment.SetEnvironmentVariable("LOCALIZE_AI_MODEL_NAME", "gpt-5.3-codex"); //qwen/qwen3-coder:free deepseek/deepseek-r1-0528:free
                Environment.SetEnvironmentVariable("APPLY_AI_API_URL", "http://localhost:8080/v1"); 
                Environment.SetEnvironmentVariable("APPLY_AI_MODEL_NAME", "gpt-5.3-codex"); 
                Environment.SetEnvironmentVariable("MAX_FILE_SIZE_BYTES", "2097152"); // 2MB
                Environment.SetEnvironmentVariable("SONAR_API_URL", "http://localhost:9000");
                Environment.SetEnvironmentVariable("FORTIFY_API_URL", "https://api.ams.fortify.com");
                Environment.SetEnvironmentVariable("GITLAB_API_URL", "https://gitlab.com/api/v4");
#endif

                Log.Debug($"System prompts to AI - LOCALIZE: '{File.ReadAllText("system_prompt_localize.md")}', APPLY: '{File.ReadAllText("system_prompt_apply.md")}'");

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

                // Read required LOCALIZE and APPLY AI configuration values
                var localizeApiUrl = Environment.GetEnvironmentVariable("LOCALIZE_AI_API_URL");
                if (string.IsNullOrEmpty(localizeApiUrl))
                {
                    throw new InvalidOperationException("LOCALIZE_AI_API_URL environment variable is required but not set.");
                }

                var localizeModel = Environment.GetEnvironmentVariable("LOCALIZE_AI_MODEL_NAME");
                if (string.IsNullOrEmpty(localizeModel))
                {
                    throw new InvalidOperationException("LOCALIZE_AI_MODEL_NAME environment variable is required but not set.");
                }

                var applyApiUrl = Environment.GetEnvironmentVariable("APPLY_AI_API_URL");
                if (string.IsNullOrEmpty(applyApiUrl))
                {
                    throw new InvalidOperationException("APPLY_AI_API_URL environment variable is required but not set.");
                }

                var applyModel = Environment.GetEnvironmentVariable("APPLY_AI_MODEL_NAME");
                if (string.IsNullOrEmpty(applyModel))
                {
                    throw new InvalidOperationException("APPLY_AI_MODEL_NAME environment variable is required but not set.");
                }

                Log.Debug("Using LOCALIZE AI API URL: {LocalizeApiUrl}, Model: {LocalizeModel}", localizeApiUrl, localizeModel);
                Log.Debug("Using APPLY AI API URL: {ApplyApiUrl}, Model: {ApplyModel}", applyApiUrl, applyModel);

                // Register services with API URLs
                builder.Services.AddSwaggerGen(c => {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AIHelper", Version = "v1" });
                });
                builder.Services.AddSingleton<SonarProvider>(provider => new SonarProvider(sonarApiUrl, pollingTimeoutSeconds, pollingIntervalSeconds, provider.GetService<ILogger<SonarProvider>>()));
                builder.Services.AddSingleton<FortifyProvider>(provider => new FortifyProvider(fortifyApiUrl, provider.GetService<ILogger<FortifyProvider>>()));
                builder.Services.AddScoped<IAiAgent>(provider => new AiAgent(aiApiKey, localizeApiUrl, localizeModel, applyApiUrl, applyModel, provider.GetService<ILogger<AiAgent>>()));
                builder.Services.AddScoped<IGitProvider>(provider => new GitLabProvider(gitlabApiUrl, provider.GetService<ILogger<GitLabProvider>>()));

                // Register the new Remediation Service
                builder.Services.AddScoped<IRemediationService, RemediationService>();

                // Add Entity Framework Core DbContext
                builder.Services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql("Host=localhost;Database=autofix;Username=postgres;Password=password"));

                var app = builder.Build();

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment()) {
                    app.UseSwagger();
                    app.UseSwaggerUI(c => {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
                    });
                }

                app.UseAuthorization();


                app.MapControllers();

#if DEBUG
                SonarRemediationRequestDto sonarRemediationRequest = new SonarRemediationRequestDto() {
                    GitlabToken = "",
                    ProjectKeyOrReleaseId = "evial1_testapp_76a60952-d567-4be8-b851-3aa7ad880158",
                    RepoId = "79481670",
                    SourceBranch = "feature/demo",
                    TargetBranch = "main",
                    ScannerToken = "",
                    TaskId = "295361c0-91ec-4a5b-9b74-8371eefba7cb",
                    ImpactSoftwareQualities = "SECURITY,RELIABILITY", //,MAINTAINABILITY
                    ImpactSeverities = "HIGH,BLOCKER", //,MEDIUM
                    SystemPromptLocalize = File.ReadAllText("system_prompt_localize.md"),
                    SystemPromptApply = File.ReadAllText("system_prompt_apply.md")
                };
                using (var scope = app.Services.CreateScope()) {
                    var remediationService = scope.ServiceProvider.GetRequiredService<IRemediationService>();
                    await remediationService.RemediateAsync(sonarRemediationRequest);
                }

# else
                 app.Run();
# endif
            } catch (Exception ex) {
                Log.Fatal(ex, "Application terminated unexpectedly");
            } finally {
                Log.CloseAndFlush();
            }
        }
    }
}
