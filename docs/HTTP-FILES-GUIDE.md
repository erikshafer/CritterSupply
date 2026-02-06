# HTTP Files - Manual Testing Guide

This document explains how to use the `.http` files in JetBrains IDEs (Rider, IntelliJ IDEA, etc.) for manual API testing.

## What are .http Files?

`.http` files are plain-text files containing HTTP requests that can be executed directly from your IDE. They support:
- **Variables** - Define once, reuse everywhere
- **JavaScript assertions** - Validate responses automatically
- **State management** - Save response data for subsequent requests
- **Comments** - Document test scenarios inline

**Why use them?**
- ✅ Faster than Swagger UI or Postman
- ✅ Version-controlled with your code
- ✅ Living API documentation
- ✅ Easy to share test scenarios with team
- ✅ Built-in support in JetBrains IDEs (no plugins needed)

---

## Available .http Files

All `.http` files are located in their respective API project folders:

| API | Location | Port |
|-----|----------|------|
| **Shopping** | `src/Shopping Management/Shopping.Api/Shopping.Api.http` | 5236 |
| **Orders** | `src/Order Management/Orders.Api/Orders.Api.http` | 5231 |
| **Customer Identity** | `src/Customer Identity/CustomerIdentity.Api/CustomerIdentity.Api.http` | 5235 |
| **Product Catalog** | `src/Product Catalog/Catalog.Api/Catalog.Api.http` | 5133 |
| **Storefront (BFF)** | `src/Customer Experience/Storefront.Api/Storefront.Api.http` | 5237 |

---

## How to Use in JetBrains IDEs

### 1. Open the .http file

In Rider or IntelliJ IDEA:
1. Navigate to the API project folder
2. Open the `*.Api.http` file
3. You'll see green "Run" icons (▶) next to each request

### 2. Run a single request

Click the green ▶ icon next to the request you want to execute.

**Example:**
```http
### Health Check
GET {{HostAddress}}/health
accept: */*
```

Click ▶ → Request executes → Response appears in bottom panel

### 3. Run multiple requests sequentially

Use the "Run All Requests in File" option from the IDE's context menu.

**Use Case:** Setting up test data
```http
### 1. Create Customer
POST {{HostAddress}}/api/customers
...

###

### 2. Add Address
POST {{HostAddress}}/api/customers/{{TestCustomerId}}/addresses
...
```

Variables from previous requests (`TestCustomerId`) are available in subsequent requests.

### 4. View response

After running a request, the response panel shows:
- **Status Code** (200, 404, 400, etc.)
- **Headers**
- **Body** (formatted JSON)
- **Test Results** (✅ pass / ❌ fail)

---

## Common Patterns

### Variables

Define variables at the top of the file:
```http
@HostAddress = http://localhost:5236
@CustomerId = 11111111-1111-1111-1111-111111111111
```

Use variables in requests:
```http
GET {{HostAddress}}/api/carts/{{CustomerId}}
```

### Assertions (JavaScript)

Validate responses using JavaScript:
```http
POST {{HostAddress}}/api/carts
Content-Type: application/json

{
  "customerId": "{{CustomerId}}"
}

> {%
    client.test("Cart initialized successfully", function() {
        client.assert(response.status === 200, "Response status is not 200");
        client.assert(response.body.id !== undefined, "Cart ID not returned");
    });
%}
```

### Save Response Data

Capture response data for use in subsequent requests:
```http
POST {{HostAddress}}/api/carts
...

> {%
    client.global.set("CartId", response.body.id);
%}
```

Later requests can use `{{CartId}}`:
```http
POST {{HostAddress}}/api/carts/{{CartId}}/items
```

---

## Port Configuration Issue

### Problem

When running APIs with `dotnet run` from the command line, ASP.NET ignores `launchSettings.json` and defaults to `http://localhost:5000`.

### Solutions

**Option 1: Run with launch profile (Recommended)**
```bash
dotnet run --launch-profile ShoppingApi --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
```

**Option 2: Set environment variable**
```powershell
# PowerShell
$env:ASPNETCORE_URLS="http://localhost:5236"
dotnet run --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
```

```bash
# Bash
export ASPNETCORE_URLS="http://localhost:5236"
dotnet run --project "src/Shopping Management/Shopping.Api/Shopping.Api.csproj"
```

**Option 3: Use IDE's run configuration**

In Rider or Visual Studio:
1. Right-click project → Run
2. Launch profile automatically used with correct port

---

## End-to-End Test Scenarios

### Scenario 1: Cart Workflow with RabbitMQ Verification

**Prerequisites:**
1. Start infrastructure: `docker-compose --profile all up -d`
2. Start Shopping.Api (port 5236)
3. Start Storefront.Api (port 5237)
4. Start Storefront.Web (port 5238)

**Steps:**
1. Open `Shopping.Api.http`
2. Run "Initialize Cart" → Captures `CartId`
3. Run "Add Item to Cart (Dog Bowl)" → Publishes to RabbitMQ
4. Open browser to `http://localhost:5238/cart`
5. **Expected:** Cart page updates in real-time via SSE

**Verification:**
- Check Storefront.Api console logs for: `"Wolverine: Received message Shopping.ItemAdded"`
- Check browser DevTools → Network tab → Look for SSE connection (`/sse/storefront`)
- Cart page should display the added item without page refresh

---

### Scenario 2: Complete Checkout Flow

**Prerequisites:**
1. Start all infrastructure (Postgres, RabbitMQ)
2. Start Shopping.Api, Orders.Api, CustomerIdentity.Api, Storefront.Api

**Steps:**
1. **Create test data** (CustomerIdentity.Api.http):
   - Create Customer → Captures `TestCustomerId`
   - Add Address → Captures `HomeAddressId`

2. **Initialize cart** (Shopping.Api.http):
   - Initialize Cart → Captures `CartId`
   - Add 2-3 items

3. **Initiate checkout** (Shopping.Api.http):
   - Initiate Checkout → Captures `CheckoutId`

4. **Complete checkout** (Orders.Api.http):
   - Select Shipping Address
   - Select Shipping Method
   - Provide Payment Method
   - Complete Checkout → Creates Order

5. **Verify in BFF** (Storefront.Api.http):
   - Get Order by ID
   - Get Orders by Customer ID

---

## Troubleshooting

### Request fails with "Connection refused"

**Cause:** API not running or wrong port

**Solution:**
1. Check API is running: `netstat -ano | findstr :5236` (Windows) or `lsof -i :5236` (macOS/Linux)
2. Verify port in launchSettings.json matches @HostAddress variable
3. Restart API with correct launch profile

---

### Variable not found

**Cause:** Variable not set by previous request

**Solution:**
1. Run requests in order (variables depend on previous responses)
2. Check assertion block saved variable: `client.global.set("VarName", value)`
3. Use `client.global.get("VarName")` to debug variable values

---

### Test assertions fail

**Cause:** Response doesn't match expected format

**Solution:**
1. Check response body in IDE's response panel
2. Update assertion to match actual response structure
3. Verify API is returning correct status code (200, 404, etc.)

---

## Tips & Best Practices

### 1. Use Descriptive Test Names

```http
### Initialize Cart (Authenticated Customer)
POST {{HostAddress}}/api/carts
```

### 2. Add Comments for Complex Scenarios

```http
### Add Item to Cart (Triggers RabbitMQ Message)
# Expected: Shopping.ItemAdded published to storefront-notifications queue
# Expected: Storefront.Api receives message and broadcasts via SSE
POST {{HostAddress}}/api/carts/{{CartId}}/items
```

### 3. Group Related Requests

```http
###############################################
###   Cart Workflow - Authenticated User   ###
###############################################

### 1. Initialize Cart
...

### 2. Add Item
...
```

### 4. Test Error Scenarios

```http
### Get Non-Existent Cart (404 Expected)
GET {{HostAddress}}/api/carts/00000000-0000-0000-0000-000000000000

> {%
    client.test("Non-existent cart returns 404", function() {
        client.assert(response.status === 404, "Expected 404");
    });
%}
```

---

## Reference: HTTP Request Syntax

### Basic GET Request
```http
GET {{HostAddress}}/api/endpoint
accept: application/json
```

### POST Request with Body
```http
POST {{HostAddress}}/api/endpoint
Content-Type: application/json

{
  "property": "value"
}
```

### PUT/PATCH/DELETE Requests
```http
PUT {{HostAddress}}/api/endpoint/{id}
Content-Type: application/json

{
  "property": "updated value"
}

###

DELETE {{HostAddress}}/api/endpoint/{id}
```

### Request Separator

Use `###` to separate requests:
```http
GET {{HostAddress}}/api/endpoint1

###

GET {{HostAddress}}/api/endpoint2
```

---

## Additional Resources

- **JetBrains HTTP Client Docs:** https://www.jetbrains.com/help/rider/Http_client_in__product__code_editor.html
- **HTTP Request Syntax:** https://www.jetbrains.com/help/rider/http-client-reference.html
- **JavaScript API Reference:** https://www.jetbrains.com/help/rider/http-response-handling-api-reference.html

---

**Last Updated:** 2026-02-05
**Maintained By:** Erik Shafer / Claude AI Assistant
