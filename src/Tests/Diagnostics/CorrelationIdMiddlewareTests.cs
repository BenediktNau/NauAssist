using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NauAssist.Api.Diagnostics;
using NauAssist.Common.Logging;

namespace NauAssist.Tests.Diagnostics;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task PreservesIncomingHeader_E0_4()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "incoming-id-42";

        var captured = await RunAsync(context);

        Assert.Equal("incoming-id-42", captured);
        Assert.Equal("incoming-id-42", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.Equal("incoming-id-42", context.Items[CorrelationIdMiddleware.ItemKey]);
    }

    [Fact]
    public async Task GeneratesIdWhenAbsent_E0_4()
    {
        var context = new DefaultHttpContext();

        var captured = await RunAsync(context);

        Assert.NotNull(captured);
        Assert.False(string.IsNullOrWhiteSpace(captured));
        Assert.Equal(captured, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task RestoresContextAfterRequest_E0_4()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc";

        var correlation = new CorrelationContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, correlation);

        Assert.Null(correlation.CurrentId);
    }

    private static async Task<string?> RunAsync(HttpContext context)
    {
        string? captured = null;
        var correlation = new CorrelationContext();

        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            captured = correlation.CurrentId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, correlation);
        return captured;
    }
}
