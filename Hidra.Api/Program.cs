// Hidra.API/Program.cs

using Hidra.Core.Logging;
using Hidra.API.Services;
using Hidra.API.Middleware;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

var builder = WebApplication.CreateBuilder(args);
Logger.Init("logging_config.json");

// --- Configure Services ---
builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    // Use Newtonsoft.Json for serialization to support polymorphic types.
    // Setting TypeNameHandling to 'Auto' includes the '$type' property for interfaces
    // and derived classes, which is required for correct deserialization of IBrain implementations.
    options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
    options.SerializerSettings.Converters.Add(new StringEnumConverter());
});

builder.Services.AddSingleton<ExperimentManager>();
builder.Services.AddSingleton<HglService>();
builder.Services.AddSingleton<HglAssemblerService>(); 
builder.Services.AddSingleton<HglDecompilerService>(); // <-- This line was added to register the new service.

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- Configure HTTP Request Pipeline ---
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    Logger.Log("HOST", Hidra.Core.Logging.LogLevel.Fatal, $"Application host terminated unexpectedly: {ex}");
}
finally
{
    // Ensure the logger flushes all messages on shutdown in non-development environments.
    if (!app.Environment.IsDevelopment())
    {
        Logger.Shutdown();
    }
}

// This makes the Program class visible to the WebApplicationFactory in tests.
public partial class Program { }