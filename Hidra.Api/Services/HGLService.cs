// Hidra.API/Services/HglService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hidra.API.DTOs;
using Hidra.Core;
using ProgrammingLanguageNr1;

namespace Hidra.API.Services
{
    /// <summary>
    /// Builds and exposes the HGL (Hidra Genesis Language) specification used by the assembler and tests.
    /// </summary>
    public class HglService
    {
        public HglSpecificationDto Specification { get; }

        public HglService()
        {
            Specification = new HglSpecificationDto();

            // Case-insensitive maps for mnemonics/operators.
            Specification.Instructions = new Dictionary<string, byte>(HGLOpcodes.OpcodeLookup, StringComparer.OrdinalIgnoreCase);
            Specification.Operators    = new Dictionary<string, byte>(HGLOpcodes.OperatorLookup, StringComparer.OrdinalIgnoreCase);

            // Populate API functions: reflect methods on HidraSprakBridge with [SprakAPI].
            var apiMethods = typeof(HidraSprakBridge)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(SprakAPI), inherit: false))
                .ToList();

            foreach (var method in apiMethods)
            {
                // Instruction table keys are the opcode mnemonics.
                // In your setup, these are the method names like "API_CreateNeuron", etc.
                if (Specification.Instructions.TryGetValue(method.Name, out var opcode))
                {
                    Specification.ApiFunctions.Add(new HglApiFunctionDto
                    {
                        Name       = method.Name,
                        Opcode     = opcode,
                        Parameters = method.GetParameters().Select(p => p.Name ?? "arg").ToList()
                    });
                }
                else
                {
                    // Some assemblers prefer the sans-API_ alias (e.g., "CreateNeuron").
                    // If present, add it too.
                    var alias = method.Name.StartsWith("API_", StringComparison.Ordinal) ? method.Name.Substring(4) : method.Name;
                    if (Specification.Instructions.TryGetValue(alias, out opcode))
                    {
                        Specification.ApiFunctions.Add(new HglApiFunctionDto
                        {
                            Name       = alias,
                            Opcode     = opcode,
                            Parameters = method.GetParameters().Select(p => p.Name ?? "arg").ToList()
                        });
                    }
                }
            }

            // === System Variables ===
            // IMPORTANT: LVarIndex is a TOP-LEVEL enum (not nested in HidraWorld),
            // so reflect it directly to include entries like "Health".
            foreach (var raw in Enum.GetValues(typeof(LVarIndex)))
            {
                var idx = (int)raw;
                var name = raw.ToString()!;
                Specification.SystemVariables[idx] = name;
            }
        }
    }
}
