using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Backoffice.Web.Tests;

/// <summary>
/// Base class for Backoffice.Web bUnit component tests.
/// Registers MudBlazor services, configures JSInterop in loose mode,
/// and pre-renders a MudPopoverProvider so that popover-based
/// MudBlazor components (MudSelect, MudMenu, MudTable, etc.) work correctly.
/// Also provides cascading authentication state for components using [Authorize].
/// </summary>
public abstract class BunitTestBase : BunitContext, IAsyncLifetime
{
    protected BunitTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddAuthorizationCore();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    /// <summary>
    /// Renders a component with MudPopoverProvider pre-rendered.
    /// Use this for components that rely on MudBlazor popover-based controls
    /// (MudSelect, MudMenu, MudTable, MudAutocomplete, etc.).
    /// Note: Components with [Authorize] attribute need to be wrapped manually.
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

    /// <summary>
    /// Renders a component wrapped in CascadingAuthenticationState.
    /// Use this for components with [Authorize] attribute.
    /// </summary>
    protected IRenderedFragment RenderAuthorized<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : IComponent
    {
        // Pre-render the popover provider so MudBlazor popover components work
        Render<MudPopoverProvider>();

        return Render<CascadingAuthenticationState>(cascade => cascade
            .AddChildContent(builder =>
            {
                builder.OpenComponent<TComponent>(0);
                parameterBuilder?.Invoke(new ComponentParameterCollectionBuilder<TComponent>(builder, 0));
                builder.CloseComponent();
            }));
    }
}
