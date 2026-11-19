namespace Gateway.Middleware;

/// <summary>
/// Guarantees every proxied request carries a GUID <c>correlationId</c> header, which is the header and
/// format the backends' <c>UseCorrelationId</c> expects, and echoes it on the response.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "correlationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = Guid.TryParse(incoming, out var parsed)
            ? parsed.ToString("N")
            : Guid.NewGuid().ToString("N");

        context.Request.Headers[HeaderName] = correlationId;
        context.Items[HeaderName] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
