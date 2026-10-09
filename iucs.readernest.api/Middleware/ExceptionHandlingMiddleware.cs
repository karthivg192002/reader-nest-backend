using iucs.readernest.application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Middleware
{
    /// <summary>
    /// Maps expected business failures (AppException) to their status codes as
    /// ProblemDetails, and shields unexpected errors behind a logged 500.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (AppException ex)
            {
                await WriteProblemAsync(context, ex.StatusCode, ex.Message);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                // With local file storage an upload can fill the server's disk; say so plainly
                // instead of a generic "unexpected error" nobody can act on.
                _logger.LogError(ex, "Disk full during {Method} {Path}", context.Request.Method, context.Request.Path);
                await WriteProblemAsync(context, StatusCodes.Status507InsufficientStorage,
                    "The server's storage is full, so the file couldn't be saved. Free up space on the server " +
                    "(or switch file storage back to S3) and try again.");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected before the request finished — not an application error,
                // and there's no one left to write a response to.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        /// <summary>"No space left on device": ENOSPC (28) on Linux, ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL on Windows.</summary>
        public static bool IsDiskFull(IOException ex) =>
            ex.HResult == 28 || (ex.HResult & 0xFFFF) is 0x70 or 0x27
            || ex.Message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase);

        private static Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
        {
            context.Response.StatusCode = statusCode;
            return context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = statusCode,
                Title = ReasonPhrases(statusCode),
                Detail = detail,
            });
        }

        private static string ReasonPhrases(int statusCode) => statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            507 => "Insufficient Storage",
            _ => "Server Error",
        };
    }
}
