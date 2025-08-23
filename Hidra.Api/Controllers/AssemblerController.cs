// --- START OF FILE AssemblerController.cs ---
// In Hidra.API/Controllers/AssemblerController.cs

using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using static Hidra.API.Services.HglAssemblerService; // For HglAssemblyException

namespace Hidra.API.Controllers
{
    // DTO for the assembly request
    public class AssembleRequestDto
    {
        public required string SourceCode { get; set; }
    }

    // DTO for the assembly response
    public class AssembleResponseDto
    {
        public string HexBytecode { get; set; } = "";
    }

    /// <summary>
    /// Provides an endpoint to compile HGL assembly language into bytecode.
    /// </summary>
    [ApiController]
    [Route("api/assembler")]
    public class AssemblerController : ControllerBase
    {
        private readonly HglAssemblerService _assembler;

        public AssemblerController(HglAssemblerService assembler)
        {
            _assembler = assembler;
        }

        /// <summary>
        /// Compiles a string of HGL assembly source into a hexadecimal bytecode string.
        /// </summary>
        /// <param name="request">A DTO containing the HGL assembly source code.</param>
        /// <returns>A 200 OK with the compiled hex string, or a 400 Bad Request if assembly fails.</returns>
        [HttpPost("assemble")]
        [ProducesResponseType(typeof(AssembleResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public IActionResult Assemble([FromBody] AssembleRequestDto request)
        {
            try
            {
                string bytecode = _assembler.Assemble(request.SourceCode);
                return Ok(new AssembleResponseDto { HexBytecode = bytecode });
            }
            catch (HglAssemblyException ex)
            {
                // Return a structured error for assembly failures.
                return BadRequest(new ProblemDetails
                {
                    Title = "HGL Assembly Error",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
            catch (Exception ex)
            {
                // Catch any other unexpected errors during assembly.
                 return BadRequest(new ProblemDetails
                {
                    Title = "An unexpected error occurred during assembly.",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
    }
}