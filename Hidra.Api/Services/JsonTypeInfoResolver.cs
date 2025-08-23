// Hidra.API/Services/JsonTypeInfoResolver.cs

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hidra.Core;
using Hidra.Core.Brain;

namespace Hidra.API.Services
{
    public class PolymorphicTypeResolver : DefaultJsonTypeInfoResolver
    {
        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo typeInfo = base.GetTypeInfo(type, options);

            // Configure IBrain Polymorphism
            if (typeInfo.Type == typeof(IBrain))
            {
                typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
                {
                    TypeDiscriminatorPropertyName = "$type",
                    IgnoreUnrecognizedTypeDiscriminators = true,
                    DerivedTypes =
                    {
                        new JsonDerivedType(typeof(DummyBrain), "DummyBrain"),
                        new JsonDerivedType(typeof(NeuralNetworkBrain), "NeuralNetworkBrain"),
                        new JsonDerivedType(typeof(LogicGateBrain), "LogicGateBrain")
                    }
                };
            }
            
            // Configure ICondition Polymorphism
            if (typeInfo.Type == typeof(ICondition))
            {
                 typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
                {
                    TypeDiscriminatorPropertyName = "$type",
                    IgnoreUnrecognizedTypeDiscriminators = true,
                    DerivedTypes =
                    {
                        // Add concrete ICondition types here as they are created
                    }
                };
            }

            return typeInfo;
        }
    }
}