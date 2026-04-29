using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NauAssist.Common.Logging;
using Serilog.Context;

namespace NauAssist.Api.Diagnostics;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        var id = ResolveId(context);

        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;

        using var scope = correlation.Begin(id);
        using var logScope = LogContext.PushProperty(ItemKey, id);

        await _next(context).ConfigureAwait(false);
    }

    private static string ResolveId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues values))
        {
            var incoming = values.ToString();
            if (!string.IsNullOrWhiteSpace(incoming))
            {
                return incoming;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
