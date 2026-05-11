using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Storefront.Web.Tests;

/// <summary>
/// Base class for Storefront.Web bUnit component tests.
/// Registers MudBlazor services, configures JSInterop in loose mode,
/// and pre-renders a MudPopoverProvider so that popover-based
/// MudBlazor components (MudSelect, MudMenu, MudTable, etc.) work correctly.
///
/// <para>
/// Storefront customer pages (OrderHistory, OrderConfirmation, etc.)
/// inject <see cref="IHttpClientFactory"/> (BFF calls) and
/// <see cref="AuthenticationStateProvider"/> (customer claim lookup).
/// Both are registered here with safe defaults so any page can render
/// without infrastructure: HttpClient calls go to a localhost loopback
/// (which then errors and is swallowed by the page's try/catch — exactly
/// what the unauthenticated empty-state tests want), and the
/// authentication state is anonymous unless a derived test overrides it.
/// </para>
/// </summary>
public abstract class BunitTestBase : BunitContext, IAsyncLifetime
{
    protected BunitTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddHttpClient();
        Services.AddSingleton<AuthenticationStateProvider, AnonymousAuthenticationStateProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    /// <summary>
    /// Authenticated test fallback so customer pages that gate behaviour
    /// on a "CustomerId" claim reach their main rendering branch. The
    /// HTTP calls those pages then make hit the localhost loopback,
    /// fail, and are swallowed by the page's try/catch — which is
    /// exactly what the empty-state tests expect (null collection →
    /// empty-state markup). Tests that need anonymous behaviour or a
    /// specific customer should register their own provider after
    /// construction.
    /// </summary>
    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        private static readonly Guid TestCustomerId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim("CustomerId", TestCustomerId.ToString())],
                authenticationType: "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    /// <summary>
    /// Renders a component with MudPopoverProvider pre-rendered in the same tree.
    /// Use this for components that rely on MudBlazor popover-based controls
    /// (MudSelect, MudMenu, MudTable, MudAutocomplete, etc.).
    /// </summary>
    protected IRenderedComponent<TComponent> RenderWithMud<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : IComponent
    {
        // Pre-render the popover provider so MudBlazor popover components work
        Render<MudPopoverProvider>();

        return parameterBuilder is null
            ? Render<TComponent>()
            : Render<TComponent>(parameterBuilder);
    }
}
