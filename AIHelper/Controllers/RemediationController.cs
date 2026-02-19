using Microsoft.AspNetCore.Mvc;
using AIHelper.Interfaces;
using AIHelper.Services;
using AIHelper.Models;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AIHelper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RemediationController : ControllerBase
    {
        private readonly ILogger<RemediationController> _logger;
        private readonly IRemediationService _remediationService;

        public RemediationController(
            ILogger<RemediationController> logger,
            IRemediationService remediationService)
        {
            _logger = logger;
            _remediationService = remediationService;
        }

        /// <summary>
        /// Initiates remediation for a Sonar project.
        /// </summary>
        /// <param name="request">The Sonar remediation request.</param>
        /// <returns>The remediation result.</returns>
        [HttpPost("sonar")]
        public async Task<IActionResult> RemediateSonarProjectAsync([FromBody] SonarRemediationRequestDto request)
        {
            _logger.LogInformation("Starting Sonar remediation for {ProjectKeyOrReleaseId}...", request.ProjectKeyOrReleaseId);
            _logger.LogDebug("Sonar remediation request details: {@RemediationRequest}", request);
            
            var result = await _remediationService.RemediateAsync(request);
            _logger.LogDebug("Sonar remediation result: {@Result}", result);
            
            return Ok(result);
        }

        /// <summary>
        /// Initiates remediation for a Fortify project.
        /// </summary>
        /// <param name="request">The Fortify remediation request.</param>
        /// <returns>The remediation result.</returns>
        [HttpPost("fortify")]
        public async Task<IActionResult> RemediateFortifyProjectAsync([FromBody] FortifyRemediationRequestDto request)
        {
            _logger.LogInformation("Starting Fortify remediation for {ProjectKeyOrReleaseId}...", request.ProjectKeyOrReleaseId);
            _logger.LogDebug("Fortify remediation request details: {@RemediationRequest}", request);
            
            var result = await _remediationService.RemediateAsync(request);
            _logger.LogDebug("Fortify remediation result: {@Result}", result);
            
            return Ok(result);
        }
    }
}
