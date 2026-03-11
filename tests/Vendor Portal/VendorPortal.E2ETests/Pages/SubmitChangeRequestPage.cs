namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the submit change request page (/change-requests/submit).
/// </summary>
public sealed class SubmitChangeRequestPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/change-requests/submit");

    public async Task FillSkuAsync(string sku) =>
        await page.GetByTestId("sku-field").Locator("input").FillAsync(sku);

    public async Task FillTitleAsync(string title) =>
        await page.GetByTestId("title-field").Locator("input").FillAsync(title);

    public async Task FillDetailsAsync(string details) =>
        await page.GetByTestId("details-field").Locator("textarea").First.FillAsync(details);

    public async Task ClickSaveDraftAsync() =>
        await page.GetByTestId("save-draft-btn").ClickAsync();

    public async Task ClickSubmitAsync() =>
        await page.GetByTestId("submit-btn").ClickAsync();
}
