// In Hidra.API/Controllers/AssemblerController.cs

using Microsoft.AspNetCore.Mvc;
using Hidra.API.Services;
using static Hidra.API.Services.HglAssemblerService; // For HglAssemblyException
using System; // For Exception

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

    // DTO for the decompile request
    public class DecompileRequestDto
    {
        public required string HexBytecode { get; set; }
    }

    // DTO for the decompile response
    public class DecompileResponseDto
    {
        public string SourceCode { get; set; } = "";
    }

    /// <summary>
    /// Provides endpoints to compile HGL assembly into bytecode and decompile bytecode back into assembly.
    /// </summary>
    [ApiController]
    [Route("api/assembler")]
    public class AssemblerController : ControllerBase
    {
        private readonly HglAssemblerService _assembler;
        private readonly HglDecompilerService _decompiler;

        public AssemblerController(HglAssemblerService assembler, HglDecompilerService decompiler)
        {
            _assembler = assembler;
            _decompiler = decompiler;
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

        /// <summary>
        /// Decompiles a hexadecimal bytecode string into human-readable HGL assembly source code.
        /// </summary>
        /// <param name="request">A DTO containing the HGL hexadecimal bytecode.</param>
        /// <returns>A 200 OK with the decompiled source code, or a 400 Bad Request if decompilation fails.</returns>
        [HttpPost("decompile")]
        [ProducesResponseType(typeof(DecompileResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public IActionResult Decompile([FromBody] DecompileRequestDto request)
        {
            try
            {
                string sourceCode = _decompiler.Decompile(request.HexBytecode);
                return Ok(new DecompileResponseDto { SourceCode = sourceCode });
            }
            catch (Exception ex)
            {
                // Catch any unexpected errors during decompilation.
                return BadRequest(new ProblemDetails
                {
                    Title = "An unexpected error occurred during decompilation.",
                    Detail = ex.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }
    }
}