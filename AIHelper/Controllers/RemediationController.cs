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

        [HttpPost]
        public async Task<IActionResult> Remediate([FromBody] RemediationRequestDto remediationRequest)
        {
            _logger.LogInformation("Starting remediation for {ProjectKeyOrReleaseId}...", remediationRequest.ProjectKeyOrReleaseId);
            _logger.LogDebug("Remediation request details: {@RemediationRequest}", remediationRequest);
            
            var result = await _remediationService.RemediateProjectAsync(remediationRequest);
            _logger.LogDebug("Remediation result: {@Result}", result);
            
            return Ok(result);
        }
    }
}