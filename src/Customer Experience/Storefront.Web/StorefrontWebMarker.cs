namespace Storefront.Web;

/// <summary>
/// Marker type used by <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// in E2E test projects. Provides a named-namespace accessor to the Storefront.Web assembly
/// without requiring a reference to the global-namespace <c>Program</c> class.
///
/// Usage: <c>new WebApplicationFactory&lt;StorefrontWebMarker&gt;()</c>
/// </summary>
public sealed class StorefrontWebMarker { }
