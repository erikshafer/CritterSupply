# JetBrains Run Configurations

This project includes pre-configured compound run configurations for launching multiple services simultaneously in Rider or other JetBrains IDEs.

## Available Configurations

### 1. All APIs (Customer Experience Testing)
**Purpose:** Launch the minimal set of APIs needed to test Customer Experience (Storefront) features.

**Services Started:**
- Product Catalog API (port 5133)
- Customer Identity API (port 5235)
- Shopping API (port 5236)
- Orders API (port 5231)
- Storefront API / BFF (port 5237)

**Use Case:**
- Testing Storefront BFF query/command endpoints
- Testing cart and checkout workflows
- HTTP file testing via Storefront.Api.http

**Missing:**
- Storefront.Web (Blazor UI) - Not included, launch separately if needed
- Payments, Inventory, Fulfillment APIs - Not needed for basic customer experience testing

---

### 2. Full Stack (APIs + Blazor UI)
**Purpose:** Launch all Customer Experience services including the Blazor UI for browser testing.

**Services Started:**
- Product Catalog API (port 5133)
- Customer Identity API (port 5235)
- Shopping API (port 5236)
- Orders API (port 5231)
- Storefront API / BFF (port 5237)
- **Storefront.Web / Blazor UI (port 5238)** ⭐

**Use Case:**
- Manual browser testing at http://localhost:5238
- End-to-end testing with real UI interactions
- SSE real-time update verification in browser

**Missing:**
- Payments, Inventory, Fulfillment APIs - Not needed for Phase 1 customer experience

---

### 3. All APIs (Complete System)
**Purpose:** Launch the entire CritterSupply system for comprehensive integration testing.

**Services Started:**
- Product Catalog API (port 5133)
- Customer Identity API (port 5235)
- Shopping API (port 5236)
- Orders API (port 5231)
- Payments API (port 5232)
- Inventory API (port 5233)
- Fulfillment API (port 5234)
- Storefront API / BFF (port 5237)
- Storefront.Web / Blazor UI (port 5238)

**Use Case:**
- Full order lifecycle testing (place order → payment → inventory → fulfillment)
- Testing saga orchestration across all BCs
- Performance testing with all services running

---

## How to Use

### In Rider / JetBrains IDE:

1. **Locate Run Configurations Dropdown:**
   - Top-right toolbar → Run configuration dropdown

2. **Select Configuration:**
   - Choose one of:
     - `All APIs (Customer Experience Testing)`
     - `Full Stack (APIs + Blazor UI)`
     - `All APIs (Complete System)`

3. **Click Run (▶) or Debug (🐞)**
   - All services in the configuration will start simultaneously
   - Each service runs in its own console tab
   - Logs appear in the "Run" tool window

4. **Stop All Services:**
   - Click Stop (⏹) button in toolbar
   - All services in the compound configuration will terminate

### Prerequisites

**Before running any configuration:**

1. **Start Infrastructure:**
   ```bash
   docker-compose --profile all up -d
   ```
   This starts:
   - PostgreSQL (port 5433)
   - RabbitMQ (ports 5672, 15672)

2. **Verify Docker Services:**
   ```bash
   docker ps
   ```
   Should show:
   - `crittersupply-postgres`
   - `crittersupply-rabbitmq`

3. **Seed Test Data (Optional):**
   - See `docs/MANUAL-TESTING-SETUP.md` for data seeding instructions

---

## Configuration File Location

Run configurations are stored in `.run/` directory:
- `.run/All APIs (Customer Experience Testing).run.xml`
- `.run/Full Stack (APIs + Blazor UI).run.xml`
- `.run/All APIs (Complete System).run.xml`

These files are committed to Git so all team members have the same configurations.

---

## Troubleshooting

### Configuration Not Appearing in Dropdown
- Close and reopen Rider
- Right-click `.run/*.run.xml` → "Run Configuration" → "Edit"
- Verify `.run/` directory is in project root

### Port Already in Use
- Check if service is already running:
  ```bash
  lsof -i :5237  # Example for Storefront.Api
  ```
- Kill existing process or change port in `launchSettings.json`

### Service Fails to Start
- Check individual service logs in "Run" tool window
- Verify connection strings in `appsettings.json`
- Ensure Postgres and RabbitMQ are running

### RabbitMQ Connection Errors
- Verify RabbitMQ container is running: `docker ps | grep rabbitmq`
- Check RabbitMQ Management UI: http://localhost:15672 (guest/guest)
- Restart RabbitMQ: `docker-compose restart rabbitmq`

---

## Port Reference

| Service | Port | URL |
|---------|------|-----|
| Postgres | 5433 | `localhost:5433` |
| RabbitMQ (AMQP) | 5672 | `amqp://localhost:5672` |
| RabbitMQ (Management UI) | 15672 | http://localhost:15672 |
| Product Catalog API | 5133 | http://localhost:5133 |
| Orders API | 5231 | http://localhost:5231 |
| Payments API | 5232 | http://localhost:5232 |
| Inventory API | 5233 | http://localhost:5233 |
| Fulfillment API | 5234 | http://localhost:5234 |
| Customer Identity API | 5235 | http://localhost:5235 |
| Shopping API | 5236 | http://localhost:5236 |
| **Storefront API (BFF)** | **5237** | http://localhost:5237 |
| **Storefront.Web (Blazor)** | **5238** | http://localhost:5238 |

---

## Recommended Workflow

**For Cycle 17 Customer Experience Testing:**

1. Start infrastructure: `docker-compose --profile all up -d`
2. Launch: `Full Stack (APIs + Blazor UI)` run configuration
3. Seed test data (see `MANUAL-TESTING-SETUP.md`)
4. Open browser: http://localhost:5238
5. Test customer journey: Browse → Add to Cart → Checkout → Order Confirmation
6. Verify SSE real-time updates
7. Stop services when done

**For Quick API Testing (No UI Needed):**

1. Start infrastructure: `docker-compose --profile all up -d`
2. Launch: `All APIs (Customer Experience Testing)` run configuration
3. Use HTTP files for testing (Storefront.Api.http, Shopping.Api.http, etc.)
4. Stop services when done
