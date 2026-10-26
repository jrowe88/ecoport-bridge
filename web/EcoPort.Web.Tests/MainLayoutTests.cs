using Bunit.TestDoubles;
using EcoPort.Web.Layout;
using EcoPort.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace EcoPort.Web.Tests;

public sealed class MainLayoutTests : BunitContext
{
    private readonly FakeAuthStatus _auth = new();

    public MainLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAuthStatus>(_auth);
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=body>page</p>"))));

    [Fact]
    public void WithBenchRole_RendersThePage()
    {
        _auth.HasBenchRole = true;

        var cut = RenderLayout();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#body")));
        Assert.DoesNotContain("login.html", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void WithoutBenchRole_GoesToLoginPage_AndHidesThePage()
    {
        _auth.HasBenchRole = false;

        var cut = RenderLayout();

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.EndsWith("/login.html", nav.Uri));
        Assert.Empty(cut.FindAll("#body"));
    }

    [Fact]
    public void SignOut_ReturnsToLoginPage()
    {
        _auth.HasBenchRole = true;

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("a[href='/.auth/logout?post_logout_redirect_uri=/login.html']")));
    }

    private sealed class FakeAuthStatus : IAuthStatus
    {
        public bool HasBenchRole { get; set; }

        public Task<bool> HasBenchRoleAsync(CancellationToken ct = default) => Task.FromResult(HasBenchRole);
    }
}
