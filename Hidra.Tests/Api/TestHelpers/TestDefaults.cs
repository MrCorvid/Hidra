// Hidra.Tests/Api/TestDefaults.cs

using Hidra.Core;

namespace Hidra.Tests.Api
{
    public static class TestDefaults
    {
        /// <summary>
        /// Provides a standard, deterministic HidraConfig for predictable API tests.
        /// This is a compatibility wrapper that calls the consolidated TestDefaults.
        /// </summary>
        public static HidraConfig GetDeterministicConfig() => Hidra.Tests.TestDefaults.DeterministicConfig();

        /// <summary>
        /// Provides the minimal valid genome required to boot a world.
        /// This is a compatibility wrapper that calls the consolidated TestDefaults.
        /// </summary>
        public const string MinimalGenome = Hidra.Tests.TestDefaults.MinimalGenome;
    }
}