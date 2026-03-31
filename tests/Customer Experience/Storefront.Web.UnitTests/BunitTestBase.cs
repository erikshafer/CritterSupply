using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace Storefront.Web.Tests;

/// <summary>
/// Base class for Storefront.Web bUnit component tests.
/// Registers MudBlazor services, configures JSInterop in loose mode,
/// and pre-renders a MudPopoverProvider so that popover-based
/// MudBlazor components (MudSelect, MudMenu, MudTable, etc.) work correctly.
/// </summary>
public abstract class BunitTestBase : BunitContext, IAsyncLifetime
{
    protected BunitTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
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
