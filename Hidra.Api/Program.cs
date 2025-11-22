// Hidra.API/Program.cs

using Hidra.Core.Logging;
using Hidra.API.Services;
using Hidra.API.Middleware;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.IO; // Required for Path and Directory

var builder = WebApplication.CreateBuilder(args);
Logger.Init("logging_config.json");

// --- Configure Services ---
builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
    options.SerializerSettings.Converters.Add(new StringEnumConverter());
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
});

// 1. Define and create the shared storage path for experiments and the registry
string storagePath = Path.Combine(Directory.GetCurrentDirectory(), "_experiments");
Directory.CreateDirectory(storagePath);

// 2. Register the Registry Service
// This must be registered so EvolutionService and ExperimentsController can resolve it.
builder.Services.AddSingleton<ExperimentRegistryService>(sp => new ExperimentRegistryService(storagePath));

// 3. Register Core Services
builder.Services.AddSingleton<ExperimentManager>();
builder.Services.AddSingleton<EvolutionService>();
builder.Services.AddSingleton<HglService>();
builder.Services.AddSingleton<HglAssemblerService>(); 
builder.Services.AddSingleton<HglDecompilerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- Configure HTTP Request Pipeline ---
app.UseMiddleware<ErrorHandlingMiddleware>();

// Capture this boolean BEFORE running the app to safely use it in the finally block
bool isDevelopment = app.Environment.IsDevelopment();

if (isDevelopment)
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
    if (!isDevelopment)
    {
        Logger.Shutdown();
    }
}

public partial class Program { }