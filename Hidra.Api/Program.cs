// Hidra.API/Program.cs

using Hidra.Core.Logging;
using Hidra.API.Services;
using Hidra.API.Middleware;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
Logger.Init("logging_config.json");

// --- Configure Services ---
builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    // Use Newtonsoft.Json for serialization to support polymorphic types.
    // Setting TypeNameHandling to 'Auto' includes the '$type' property for interfaces
    // and derived classes.
    options.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
    options.SerializerSettings.Converters.Add(new StringEnumConverter());
    
    // CRITICAL FIX: Enforce CamelCase (e.g., "CurrentTick" -> "currentTick").
    // This ensures the Python client, which uses dictionary keys like frame['tick'],
    // can correctly parse the response.
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
});

builder.Services.AddSingleton<ExperimentManager>();
builder.Services.AddSingleton<HglService>();
builder.Services.AddSingleton<HglAssemblerService>(); 
builder.Services.AddSingleton<HglDecompilerService>();

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
    if (!app.Environment.IsDevelopment())
    {
        Logger.Shutdown();
    }
}

public partial class Program { }