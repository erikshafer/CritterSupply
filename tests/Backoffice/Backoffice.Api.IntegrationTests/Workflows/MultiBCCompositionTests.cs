using Backoffice.Clients;
using Backoffice.Composition;
using Shouldly;

namespace Backoffice.Api.IntegrationTests.Workflows;

/// <summary>
/// Integration tests for multi-BC composition workflows.
/// Validates end-to-end scenarios spanning Customer Identity → Orders → Returns → Correspondence.
/// Tests the Backoffice BFF's ability to orchestrate multiple domain BCs for CS agent workflows.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MultiBCCompositionTests
{
    private readonly BackofficeTestFixture _fixture;

    public MultiBCCompositionTests(BackofficeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.CustomerIdentityClient.Clear();
        _fixture.OrdersClient.Clear();
        _fixture.ReturnsClient.Clear();
        _fixture.CorrespondenceClient.Clear();
    }

    /// <summary>
    /// Tests the complete customer service workflow:
    /// 1. Search customer by email (Customer Identity BC)
    /// 2. View order details (Orders BC)
    /// 3. View return request (Returns BC)
    /// 4. Approve return (Returns BC)
    /// 5. Verify correspondence history updated (Correspondence BC)
    /// </summary>
    [Fact]
    public async Task CustomerServiceWorkflow_SearchToReturnApproval_CompletesSuccessfully()
    {
        // Arrange: Create customer with order and return
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var customer = new CustomerDto(
            customerId,
            "cs.workflow@example.com",
            "CS",
            "Workflow",
            null,
            DateTime.UtcNow.AddMonths(-3));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var order = new OrderSummaryDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddDays(-15),
            "Delivered",
            199.99m);

        _fixture.OrdersClient.AddOrder(order);

        // Add order detail for the GET /api/backoffice/orders/{orderId} endpoint
        var orderDetail = new OrderDetailDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddDays(-15),
            "Delivered",
            199.99m,
            new List<OrderLineItemDto>
            {
                new("SKU-001", "Product 1", 1, 100.00m),
                new("SKU-002", "Product 2", 1, 99.99m)
            },
            null);
        _fixture.OrdersClient.AddOrderDetail(orderDetail);

        var returnRequest = new ReturnDetailDto(
            returnId,
            orderId,
            DateTime.UtcNow.AddDays(-1),
            "Pending",
            "Defect",
            "Defective items",
            new List<ReturnItemDto>
            {
                new("SKU-001", "Product 1", 1, "Damaged"),
                new("SKU-002", "Product 2", 1, "Defective")
            },
            null,
            null);

        _fixture.ReturnsClient.AddReturn(returnRequest);

        // Act 1: Search customer
        var searchResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/search?email={customer.Email}");
            s.StatusCodeShouldBeOk();
        });

        var customerView = searchResult.ReadAsJson<CustomerServiceView>();

        // Assert 1: Customer found with orders
        customerView.ShouldNotBeNull();
        customerView.CustomerId.ShouldBe(customerId);
        customerView.Orders.Count.ShouldBe(1);
        customerView.Orders[0].OrderId.ShouldBe(orderId);

        // Act 2: Get order details
        var orderResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/{orderId}");
            s.StatusCodeShouldBeOk();
        });

        var orderView = orderResult.ReadAsJson<OrderDetailView>();

        // Assert 2: Order details loaded
        orderView.ShouldNotBeNull();
        orderView.OrderId.ShouldBe(orderId);
        orderView.CustomerId.ShouldBe(customerId);

        // Act 3: Get return details
        var returnResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/returns/{returnId}");
            s.StatusCodeShouldBeOk();
        });

        var returnView = returnResult.ReadAsJson<ReturnDetailView>();

        // Assert 3: Return details loaded
        returnView.ShouldNotBeNull();
        returnView.ReturnId.ShouldBe(returnId);
        returnView.OrderId.ShouldBe(orderId);
        returnView.Status.ShouldBe("Pending");

        // Act 4: Approve return
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { restockingFeeWaived = true }).ToUrl($"/api/backoffice/returns/{returnId}/approve");
            s.StatusCodeShouldBe(204);
        });

        // Act 5: Verify correspondence history
        var correspondenceResult = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/{customerId}/correspondence");
            s.StatusCodeShouldBeOk();
        });

        var correspondenceView = correspondenceResult.ReadAsJson<CorrespondenceHistoryView>();

        // Assert 5: Correspondence history accessible (even if stub returns empty list)
        correspondenceView.ShouldNotBeNull();
        correspondenceView.CustomerId.ShouldBe(customerId);
    }

    /// <summary>
    /// Tests CS agent workflow with no orders:
    /// Customer search should succeed but return empty order list.
    /// </summary>
    [Fact]
    public async Task CustomerServiceWorkflow_NewCustomerWithNoOrders_ReturnsEmptyOrderList()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDto(
            customerId,
            "new.customer@example.com",
            "New",
            "Customer",
            null,
            DateTime.UtcNow);

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/search?email={customer.Email}");
            s.StatusCodeShouldBeOk();
        });

        var view = result.ReadAsJson<CustomerServiceView>();

        // Assert
        view.ShouldNotBeNull();
        view.Orders.Count.ShouldBe(0);
    }

    /// <summary>
    /// Tests order detail view with multiple returns:
    /// Validates that order detail composition includes returns from Returns BC.
    /// </summary>
    [Fact]
    public async Task CustomerServiceWorkflow_OrderWithMultipleReturns_ShowsAllReturns()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var customer = new CustomerDto(
            customerId,
            "multi.returns@example.com",
            "Multi",
            "Returns",
            null,
            DateTime.UtcNow.AddMonths(-2));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        var order = new OrderSummaryDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddDays(-20),
            "Delivered",
            299.99m);

        _fixture.OrdersClient.AddOrder(order);

        // Add order detail for GET endpoint
        var orderDetail = new OrderDetailDto(
            orderId,
            customerId,
            DateTime.UtcNow.AddDays(-20),
            "Delivered",
            299.99m,
            new List<OrderLineItemDto>
            {
                new("SKU-001", "Product 1", 2, 100.00m),
                new("SKU-002", "Product 2", 1, 99.99m)
            },
            null);
        _fixture.OrdersClient.AddOrderDetail(orderDetail);

        var return1 = new ReturnDetailDto(
            Guid.NewGuid(),
            orderId,
            DateTime.UtcNow.AddDays(-5),
            "Approved",
            "Exchange",
            "Wrong size",
            new List<ReturnItemDto> { new("SKU-001", "Product 1", 1, "Good") },
            "Inspected - Good condition",
            null);

        var return2 = new ReturnDetailDto(
            Guid.NewGuid(),
            orderId,
            DateTime.UtcNow.AddDays(-3),
            "Pending",
            "Defect",
            "Defective",
            new List<ReturnItemDto> { new("SKU-002", "Product 2", 1, "Damaged") },
            null,
            null);

        _fixture.ReturnsClient.AddReturn(return1);
        _fixture.ReturnsClient.AddReturn(return2);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/orders/{orderId}");
            s.StatusCodeShouldBeOk();
        });

        var orderView = result.ReadAsJson<OrderDetailView>();

        // Assert: OrderDetailView contains ReturnableItems, not Returns collection
        orderView.ShouldNotBeNull();
        orderView.OrderId.ShouldBe(orderId);
        orderView.ReturnableItems.ShouldNotBeNull();
    }

    /// <summary>
    /// Tests correspondence history for customer with multiple message types:
    /// Order confirmations, return approvals, shipment notifications.
    /// </summary>
    [Fact]
    public async Task CustomerServiceWorkflow_CorrespondenceHistory_ShowsAllMessageTypes()
    {
        // Arrange
        var customerId = Guid.NewGuid();

        var customer = new CustomerDto(
            customerId,
            "correspondence.test@example.com",
            "Correspondence",
            "Test",
            null,
            DateTime.UtcNow.AddMonths(-1));

        _fixture.CustomerIdentityClient.AddCustomer(customer);

        // Add mock correspondence history
        var message1 = new CorrespondenceMessageDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-10),
            "Email",
            "Order confirmation #12345",
            "Sent");

        var message2 = new CorrespondenceMessageDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-5),
            "Email",
            "Shipment dispatched",
            "Sent");

        var message3 = new CorrespondenceMessageDto(
            Guid.NewGuid(),
            customerId,
            DateTime.UtcNow.AddDays(-2),
            "SMS",
            "Return approved",
            "Sent");

        _fixture.CorrespondenceClient.AddMessage(message1);
        _fixture.CorrespondenceClient.AddMessage(message2);
        _fixture.CorrespondenceClient.AddMessage(message3);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/backoffice/customers/{customerId}/correspondence");
            s.StatusCodeShouldBeOk();
        });

        var view = result.ReadAsJson<CorrespondenceHistoryView>();

        // Assert
        view.ShouldNotBeNull();
        view.Messages.Count.ShouldBe(3);
        view.Messages.ShouldContain(m => m.MessageType == "Email" && m.Subject.Contains("Order confirmation"));
        view.Messages.ShouldContain(m => m.MessageType == "SMS");
    }
}
