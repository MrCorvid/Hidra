using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Threading.Tasks;
using Hidra.Core.Logging;

namespace Hidra.API.Middleware
{
    /// <summary>
    /// A middleware for catching unhandled exceptions and transforming them into a standardized
    /// JSON error response, as defined by the API specification.
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            Logger.Log("API_ERROR", Hidra.Core.Logging.LogLevel.Error, $"An unhandled exception has occurred: {exception}");

            var code = HttpStatusCode.InternalServerError;
            string errorType = "InternalServerError";

            switch (exception)
            {
                case InvalidOperationException ex when ex.Message.Contains("Cycle detected"):
                    code = HttpStatusCode.Conflict;
                    errorType = "Conflict";
                    break;
                    
                case ArgumentException _:
                case FormatException _:
                case JsonSerializationException _:
                    code = HttpStatusCode.BadRequest;
                    errorType = "BadRequest";
                    break;
                case KeyNotFoundException _:
                    code = HttpStatusCode.NotFound;
                    errorType = "NotFound";
                    break;
            }

            var result = JsonConvert.SerializeObject(new 
            {
                error = errorType,
                message = exception.Message
            });

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;
            return context.Response.WriteAsync(result);
        }
    }
}