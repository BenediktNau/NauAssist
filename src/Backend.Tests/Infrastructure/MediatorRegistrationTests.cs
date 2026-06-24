using AwesomeAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Infrastructure;

public sealed class MediatorRegistrationTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public MediatorRegistrationTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Mediator_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();

        var mediator = scope.ServiceProvider.GetService<IMediator>();

        mediator.Should().NotBeNull();
    }
}
