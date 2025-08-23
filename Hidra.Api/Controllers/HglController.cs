using Hidra.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hidra.API.Controllers
{
    /// <summary>
    /// SFR (Hidra Genesis Language) specification.
    /// </summary>
    [ApiController]
    [Route("api/hgl")]
    public class HglController : ControllerBase
    {
        private readonly HglService _hglService;

        /// <summary>
        /// Initializes a new instance of the <see cref="HglController"/> class.
        /// The HglService is injected via dependency injection.
        /// </summary>
        /// <param name="hglService">The singleton service that holds the HGL specification.</param>
        public HglController(HglService hglService)
        {
            _hglService = hglService;
        }

        /// <summary>
        /// Gets the complete HGL specification, including all instructions, operators,
        /// API functions, and system variable names.
        /// </summary>
        /// <returns>A JSON object representing the HGL specification.</returns>
        [HttpGet("specification")]
        public IActionResult GetSpecification()
        {
            // The controller's only job is to return the pre-built specification
            // object from the singleton service.
            return Ok(_hglService.Specification);
        }
    }
}