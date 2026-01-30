using AIHelper.Interfaces;
using AIHelper.Models;

namespace AIHelper.Services
{
    public class ScanProviderFactory
    {
        private readonly string _sonarApiUrl;
        private readonly string _fortifyApiUrl;
        private readonly int _pollingTimeoutSeconds;
        private readonly int _pollingIntervalSeconds;
        private readonly ILogger<ScanProviderFactory> _logger;
        private readonly ILogger<SonarProvider> _sonarLogger;
        private readonly ILogger<FortifyProvider> _fortifyLogger;
        
        public ScanProviderFactory(string? sonarApiUrl = null, string? fortifyApiUrl = null, int pollingTimeoutSeconds = 300, int pollingIntervalSeconds = 10, ILogger<ScanProviderFactory> logger = null, ILogger<SonarProvider> sonarLogger = null, ILogger<FortifyProvider> fortifyLogger = null)
        {
            _sonarApiUrl = sonarApiUrl ?? "http://localhost:9000";
            _fortifyApiUrl = fortifyApiUrl ?? "https://api.ams.fortify.com";
            _pollingTimeoutSeconds = pollingTimeoutSeconds;
            _pollingIntervalSeconds = pollingIntervalSeconds;
            _logger = logger;
            _sonarLogger = sonarLogger;
            _fortifyLogger = fortifyLogger;
        }

        public IScanProvider GetProvider(ScanType scanType, string? apiUrl = null)
        {
            _logger?.LogInformation("Creating provider for scan type {ScanType}", scanType);
            return scanType switch
            {
                ScanType.SONAR => new SonarProvider(apiUrl ?? _sonarApiUrl, _pollingTimeoutSeconds, _pollingIntervalSeconds, _sonarLogger),
                ScanType.FORTIFY => new FortifyProvider(apiUrl ?? _fortifyApiUrl, _fortifyLogger),
                _ => throw new ArgumentOutOfRangeException(nameof(scanType), $"Unknown scan type: {scanType}")
            };
        }
    }
}