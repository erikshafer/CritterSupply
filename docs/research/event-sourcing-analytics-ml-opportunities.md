# Event Sourcing Analytics & ML Opportunities

**Date:** 2026-03-12
**Author:** Research Task
**Status:** 🔍 Discovery Phase
**Time Box:** 15 minutes

## Executive Summary

CritterSupply's event-sourced architecture captures **100+ distinct domain event types** across 7 bounded contexts (Orders, Shopping, Payments, Inventory, Fulfillment, Returns, Pricing). This rich event data presents immediate opportunities for analytics, machine learning, and business intelligence without requiring architectural changes.

---

## Current State: Event Sourcing Coverage

### Event-Sourced Bounded Contexts (7 of 13)

| BC | Aggregate(s) | Key Events | Stream Type |
|----|-------------|------------|-------------|
| **Orders** | Checkout, Order (saga) | OrderPlaced, PaymentCaptured, ShipmentDelivered, ReturnRequested (14+ events) | Guid |
| **Shopping** | Cart | ItemAdded, ItemRemoved, CartAbandoned, CheckoutInitiated (6 events) | Guid |
| **Payments** | Payment | PaymentAuthorized, PaymentCaptured, PaymentFailed, PaymentRefunded (5 events) | Guid |
| **Inventory** | ProductInventory | StockReserved, ReservationCommitted, LowStockDetected (6 events) | Guid |
| **Fulfillment** | Shipment | ShipmentDispatched, ShipmentDelivered, ShipmentDeliveryFailed (5 events) | Guid |
| **Returns** | Return | ReturnRequested, InspectionPassed/Failed, ReturnCompleted (11 events) | Guid |
| **Pricing** | ProductPrice | PriceChanged, ScheduledPriceActivated, PriceCorrected (9 events) | UUID v5 (SKU-based) |

**Total Event Types:** ~56 internal events + ~63 integration messages = **119 distinct events**

**Event Store:** Marten (PostgreSQL-based) with inline projections for real-time read models

---

## High-Value Use Cases

### 1. Product Recommendation Engine

**Event Sources:**
- `ItemAdded` (Shopping) → Product co-occurrence (collaborative filtering)
- `OrderPlaced` (Orders) → Purchase history (user-based CF)
- `CartAbandoned` (Shopping) → Negative signals (low conversion products)
- `ReturnRequested` (Returns) → Quality signals (avoid recommending high-return SKUs)

**Recommendation Strategies:**
- **Collaborative Filtering:** "Customers who bought X also bought Y"
- **Content-Based:** SKU attributes (category, price range) + purchase history
- **Hybrid:** Combine both for cold-start problem (new customers/products)

**Proposed Architecture:**
```
Marten Events → Async Projection → Feature Store (Parquet/Redis) → ML Model (ML.NET) → REST API
```

---

### 2. Demand Forecasting & Inventory Optimization

**Event Sources:**
- `OrderPlaced` (Orders) → Sales velocity by SKU/time
- `StockReserved` (Inventory) → Lead indicators (reservations spike before orders)
- `LowStockDetected` (Inventory) → Reorder point triggers
- `CartAbandoned` (Shopping) → Latent demand (couldn't complete purchase)
- `PriceChanged` (Pricing) → Price elasticity (demand response to pricing)

**Forecast Models:**
- **Time Series:** SARIMA, Prophet (Facebook), LSTM (for seasonal products)
- **Features:** Historical sales, price, seasonality, promotions, stock-out events
- **Output:** 7-day, 30-day, 90-day demand forecasts per SKU

**Business Impact:**
- Reduce stock-outs (capture lost revenue)
- Optimize reorder points (reduce holding costs)
- Predict promotional lift (plan inventory for scheduled price drops)

---

### 3. Customer Segmentation & Lifetime Value (CLV)

**Event Sources:**
- `OrderPlaced` (Orders) → Purchase frequency, recency, monetary value (RFM)
- `ReturnRequested` (Returns) → Return rate (quality-sensitive vs bargain hunters)
- `CheckoutStarted` → `CheckoutCompleted` (Orders) → Conversion funnel completion
- `CartAbandoned` (Shopping) → Price sensitivity (abandoned after browsing)

**Segmentation Dimensions:**
- **High-Value Loyalists:** Frequent buyers, low return rate, high AOV
- **Price-Sensitive Shoppers:** Cart abandonment after price increases
- **Quality-Conscious:** High return rate with "Defective" reasons
- **Bargain Hunters:** Purchase spike during `ScheduledPriceActivated` events

**ML Approach:**
- K-means clustering on RFM + return rate + price sensitivity features
- CLV prediction: Regression model (XGBoost, Linear Regression) on customer tenure, order frequency, AOV

---

### 4. Fraud Detection & Payment Anomaly Detection

**Event Sources:**
- `PaymentFailed` (Payments) → Repeated failed attempts (card testing)
- `PaymentAuthorized` → `PaymentCaptured` (Payments) → Capture success rate
- `RefundCompleted` (Payments) → Refund abuse patterns (frequent refunds)
- `ShipmentDeliveryFailed` (Fulfillment) + `ReturnRequested` (Returns) → Address fraud

**Anomaly Detection Approaches:**
- **Supervised:** Train on labeled fraud cases (if available)
- **Unsupervised:** Isolation Forest, Autoencoders (detect outliers)
- **Features:** Payment failure rate, refund frequency, delivery failure + return correlation

---

### 5. Return Rate Prediction & Quality Control

**Event Sources:**
- `ReturnRequested` (Returns) → Return reasons (Defective, WrongItem, DamagedInTransit, Unwanted)
- `InspectionPassed`/`InspectionFailed` (Returns) → Actual condition vs customer claim
- `OrderPlaced` (Orders) → SKU correlation with returns

**Predictive Models:**
- **Return Probability:** Logistic regression on SKU, category, price, vendor, season
- **NLP on Return Reasons:** Classify free-text explanations (sentiment analysis, topic modeling)
- **Quality Control:** Flag SKUs with >15% defective return rate for vendor review

**Business Impact:**
- Proactive quality interventions (audit high-return vendors)
- Optimize restocking fee policy (lower for defective returns)
- Improve product descriptions (reduce "Unwanted" returns)

---

### 6. Pricing Optimization & Elasticity Analysis

**Event Sources:**
- `PriceChanged` (Pricing) → Price adjustments
- `ItemAdded` (Shopping) → Demand response (velocity after price change)
- `ScheduledPriceActivated` (Pricing) → Promotional pricing
- `OrderPlaced` (Orders) → Conversion rate at different price points

**Pricing Science:**
- **Price Elasticity:** Regression model (demand = f(price, seasonality, competition))
- **Dynamic Pricing:** Adjust prices based on inventory levels, competitor pricing
- **Promotional ROI:** A/B test scheduled price changes, measure lift in `OrderPlaced` events

---

## Recommended Tools & Frameworks

### A. .NET-Native Machine Learning

#### 1. **ML.NET** (Microsoft)
- **Website:** https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet
- **NuGet:** `Microsoft.ML`, `Microsoft.ML.Recommender`
- **Best For:** Recommendation engines, classification, regression, forecasting
- **Why ML.NET:**
  - Native .NET integration (no Python interop)
  - Model training in C# (can run on same infrastructure)
  - AutoML support (automated feature engineering)
  - Production-ready (used in Microsoft products)

**Example Use Cases:**
- **Recommendation:** Matrix factorization for product recommendations
- **Forecasting:** Time series prediction (demand forecasting)
- **Classification:** Return reason classification, fraud detection

**Sample Code Pattern:**
```csharp
// Train recommendation model on Marten event projections
var mlContext = new MLContext();
var data = mlContext.Data.LoadFromEnumerable(cartEventProjections);
var model = mlContext.Recommendation()
    .Trainers.MatrixFactorization("userId", "sku", "score");
```

---

### B. Event Stream Processing

#### 2. **Apache Kafka + Kafka Streams** (Confluent)
- **Website:** https://kafka.apache.org/
- **NuGet (Client):** `Confluent.Kafka`
- **Best For:** Real-time event streaming, ETL to data warehouses, multi-consumer fan-out
- **Why Kafka:**
  - Industry-standard event streaming platform
  - High-throughput (millions of events/sec)
  - Durable log (replay capability for backfilling)
  - Connect ecosystem (Snowflake, BigQuery, Elasticsearch connectors)
  - Consumer groups (independent consumption rates per subscriber)

---

### When to Add Kafka to CritterSupply

**Current State: RabbitMQ + Marten**
- RabbitMQ handles integration messages (cross-BC choreography)
- Marten event store captures domain events (event sourcing)

**Why Add Kafka?**
1. **Multiple Consumer Speeds:** Analytics consumers (slow batch processing) shouldn't block operational consumers (fast saga coordination)
2. **Replay Capability:** Re-process historical events for new projections without replaying entire Marten event store
3. **Fan-Out:** Broadcast same event to 10+ consumers (ClickHouse, Elasticsearch, Redis, data science notebooks, external partners)
4. **Data Lake Integration:** Stream events to S3/Parquet for long-term storage + ML training (without hitting Marten database)

---

### Kafka Architecture Patterns for CritterSupply

#### Pattern 1: Kafka as Event Bus (Parallel to RabbitMQ)

**Use Case:** Publish Marten domain events to both RabbitMQ (operational) and Kafka (analytics)

```
Marten Event Store
      ↓
Wolverine Subscriber (Dual Publish Handler)
      ├─→ RabbitMQ (operational choreography: Orders saga, Inventory reservation)
      └─→ Kafka Topics (analytics: ClickHouse, Redis, ML training)
```

**Implementation:**
```csharp
// Wolverine handler publishes to both transports
public sealed class OrderPlacedHandler
{
    public async Task Handle(OrderPlaced e, IMessageBus bus)
    {
        // Publish to RabbitMQ (durable outbox, saga coordination)
        await bus.PublishAsync(e);

        // Publish to Kafka (analytics fan-out)
        await bus.PublishAsync(e, new DeliveryOptions
        {
            EndpointName = "kafka://orders-placed-topic"
        });
    }
}
```

**Why This Works:**
- RabbitMQ remains authoritative for operational flows (Orders saga)
- Kafka consumers can lag without blocking order placement
- Analytics failures (ClickHouse down) don't affect order processing

---

#### Pattern 2: Kafka as CDC Stream (Change Data Capture from Marten)

**Use Case:** Stream all Marten events to Kafka without custom handlers

```
Marten Event Store (mt_events table)
      ↓
PostgreSQL Logical Replication / Debezium CDC
      ↓
Kafka Topics (one per BC: orders-events, shopping-events, inventory-events)
      ↓
Kafka Consumers (ClickHouse, Parquet export, Redis projections)
```

**Tools:**
- **Debezium PostgreSQL Connector:** Streams `mt_events` table changes to Kafka
- **Kafka Connect:** No-code CDC pipeline (no .NET code changes)

**Pros:**
- ✅ Zero CritterSupply code changes (Debezium reads PostgreSQL WAL)
- ✅ Captures ALL events (no risk of forgetting to publish)
- ✅ Low-latency (<1 second from event append to Kafka)

**Cons:**
- ❌ Operational complexity (Debezium + Kafka Connect + Schema Registry)
- ❌ Kafka topics contain PostgreSQL internals (JSONB format, Marten metadata)
- ❌ Requires PostgreSQL replication slot (increases disk I/O)

**When to Use:** Phase 4+ when analytics workload justifies CDC infrastructure

---

#### Pattern 3: Kafka for ML Feature Pipelines

**Use Case:** Real-time feature engineering (streaming aggregations)

```
Kafka Topics (orders-placed, items-added, payments-captured)
      ↓
Kafka Streams App (.NET with `Streamiz.Kafka.Net`)
      ↓
Stateful Aggregations (customer order count, SKU co-occurrence)
      ↓
Redis (feature cache for ML.NET inference)
```

**Example: Real-Time RFM Calculation**
```csharp
// Kafka Streams topology (stateful aggregation)
var builder = new StreamsBuilder();

builder.Stream<string, OrderPlaced>("orders-placed-topic")
    .GroupByKey()
    .Aggregate(
        () => new CustomerRfm(),
        (customerId, order, rfm) => rfm with
        {
            LastOrderDate = order.PlacedAt,
            TotalOrders = rfm.TotalOrders + 1,
            TotalSpent = rfm.TotalSpent + order.TotalAmount
        },
        Materialized.As<string, CustomerRfm>("rfm-store")
    )
    .ToStream()
    .ForeachAsync(async (customerId, rfm) =>
    {
        // Write to Redis for ML.NET inference
        await redis.StringSetAsync($"customer:{customerId}:rfm", JsonSerializer.Serialize(rfm));
    });
```

**Why Kafka Streams?**
- Exactly-once processing semantics (no duplicate RFM calculations)
- State store checkpointing (recover from failures)
- Scalable (partition by `customerId` for parallel processing)

**Alternative:** Keep Marten async projections (simpler, fewer moving parts). Use Kafka Streams when:
- Projections need to join multiple event streams (e.g., Cart + Orders + Returns)
- Real-time aggregations across BCs (cross-partition joins)
- Throughput exceeds 100K events/sec (Kafka Streams scales horizontally)

---

### Kafka vs RabbitMQ: When to Use Each

| Feature | RabbitMQ (Current) | Kafka (Optional) |
|---------|-------------------|------------------|
| **Use Case** | Operational choreography (saga coordination) | Analytics, data lakes, ML pipelines |
| **Message Delivery** | At-most-once (Wolverine durable outbox → exactly-once) | At-least-once (consumer commits offsets) |
| **Consumption Model** | Competing consumers (one consumer per queue) | Consumer groups (independent offsets) |
| **Message Retention** | Transient (deleted after ack) | Durable log (configurable retention, e.g., 7 days) |
| **Replay** | Not supported | Full replay from offset 0 (historical analysis) |
| **Throughput** | ~50K msgs/sec (single broker) | ~1M msgs/sec (partitioned topics) |
| **Fan-Out** | Multiple queues (copy per consumer) | Single topic, multiple consumer groups (zero-copy) |
| **Ordering** | Per queue (FIFO) | Per partition (key-based routing) |
| **Best For** | Orders saga, Inventory reservation, Payment coordination | ClickHouse ingestion, ML feature pipelines, Parquet export |

**Key Insight:** RabbitMQ for **operational workflows** (must be fast, reliable). Kafka for **analytics workflows** (can be slow, replayable).

---

### Recommended Kafka Architecture for CritterSupply

**Phase 1-2 (MVP):** Skip Kafka, use RabbitMQ + Marten projections

**Phase 3 (Production):** Add Kafka for analytics fan-out
```
Marten Events → Wolverine Dual-Publish Handler → RabbitMQ (operational) + Kafka (analytics)
                                                                ↓
                                    Kafka Topics (orders, shopping, inventory, ...)
                                                                ↓
                            ┌───────────────────┬───────────────────┬─────────────────┐
                            ↓                   ↓                   ↓                 ↓
                      ClickHouse           Parquet Export      Redis Cache      External Partners
                    (BI dashboards)     (ML training data)  (ML inference)      (vendor APIs)
```

**Phase 4 (Advanced):** Replace dual-publish with Debezium CDC (zero code changes)

---

### CritterSupply Integration:**
```
Marten Events → Wolverine Subscriber → Kafka Producer → Kafka Topics
                                                          ↓
                                                    Kafka Streams App
                                                          ↓
                                                    Feature Store (Redis)
                                                          ↓
                                                    ML Model (ML.NET)
```

**Alternative (Lighter Weight for Phase 1-2):** Keep RabbitMQ, add subscriber that writes to TimescaleDB (PostgreSQL extension for time-series) or directly to Redis via async projections.

---

#### 3. **KurrentDB (formerly EventStoreDB)**
- **Website:** https://kurrent.io/
- **NuGet:** `EventStore.Client.Grpc.Streams`
- **Best For:** Specialized event sourcing database (alternative to Marten)
- **Why KurrentDB:**
  - Purpose-built for event sourcing (optimized for append-only streams)
  - Projections engine built-in (JavaScript projections)
  - Persistent subscriptions (catch-up, competing consumers)
  - Event versioning support

**When to Consider:** If Marten projections become performance bottleneck, or need multi-language projection support (KurrentDB supports JavaScript projections).

**Note:** CritterSupply uses Marten today, which is excellent. KurrentDB is an alternative if scaling event sourcing beyond 1M events/day.

---

### C. Feature Stores (Online/Offline)

#### 4. **Feast (Open Source Feature Store)**
- **Website:** https://feast.dev/
- **GitHub:** https://github.com/feast-dev/feast
- **Best For:** Bridging offline training (batch) and online serving (real-time)
- **Why Feast:**
  - Low-latency feature retrieval (<10ms)
  - Time-travel (point-in-time correctness for training data)
  - Registry (centralized feature definitions)
  - Supports PostgreSQL as offline store (leverage existing Marten DB)

**CritterSupply Integration:**
```
Marten DB → Feast Offline Store (PostgreSQL views/projections)
                  ↓
            Feast Materialize Command
                  ↓
            Feast Online Store (Redis) → ML Model Inference (low latency)
```

**Feature Examples:**
- `customer_total_orders_30d` (from OrderPlaced events)
- `sku_return_rate_90d` (from ReturnRequested events)
- `sku_avg_price_7d` (from PriceChanged events)

---

#### 5. **Redis as Lightweight Feature Store**
- **NuGet:** `StackExchange.Redis`
- **Best For:** Simple key-value features without full Feast complexity
- **Why Redis:**
  - Sub-millisecond latency
  - Native .NET client (StackExchange.Redis is battle-tested)
  - Can run in Docker Compose (minimal infrastructure)

**Pattern:**
```csharp
// Async projection writes to Redis
public sealed class ProductRecommendationsProjection : MultiStreamProjection<ProductRecommendations, Guid>
{
    public ProductRecommendations Create(ItemAdded e) { ... }

    // Side effect: write to Redis
    public void OnApply(ProductRecommendations doc, IDatabase redis)
    {
        redis.StringSet($"product:{doc.SKU}:recs", JsonSerializer.Serialize(doc.TopRecommendations));
    }
}
```

---

### D. Data Warehouse / OLAP

#### 6. **Apache Druid** (Real-Time Analytics)
- **Website:** https://druid.apache.org/
- **Best For:** Real-time dashboards, event-driven analytics
- **Why Druid:**
  - Sub-second query latency on billion+ event datasets
  - Stream ingestion (Kafka → Druid)
  - Time-series optimized (event timestamp indexing)
  - SQL interface (familiar query language)

**CritterSupply Use Cases:**
- Real-time vendor sales dashboard (OrderPlaced events)
- Cart abandonment funnel analysis (CartAbandoned events)
- Payment success rate monitoring (PaymentCaptured vs PaymentFailed)

---

#### 7. **ClickHouse** (Column-Store OLAP)
- **Website:** https://clickhouse.com/
- **Best For:** Fast aggregations on historical event data
- **Why ClickHouse:**
  - 100x faster than PostgreSQL for analytical queries
  - MergeTree engine (optimized for event logs)
  - MaterializedView support (pre-aggregated metrics)
  - SQL interface (no learning curve)

**Integration Pattern:**
```
Marten Events → Wolverine Subscriber → ClickHouse Bulk Insert (batch every 5 seconds)
                                                ↓
                                          ClickHouse MaterializedViews
                                                ↓
                                          Grafana Dashboards
```

---

### E. Notebooks & Experimentation

#### 8. **Polyglot Notebooks (.NET Interactive)**
- **Website:** https://github.com/dotnet/interactive
- **VS Code Extension:** Polyglot Notebooks
- **Best For:** Exploratory data analysis (EDA) on Marten events
- **Why Polyglot Notebooks:**
  - Native C# support (query Marten directly)
  - Inline charting (Plotly.NET)
  - SQL cells (query PostgreSQL directly)
  - Share notebooks with team (reproducible analysis)

**Example Notebook:**
```csharp
// Cell 1: Connect to Marten
#r "nuget: Marten"
var store = DocumentStore.For("Host=localhost;...");

// Cell 2: Query events
var orderEvents = await store.QuerySession
    .Query<IEvent>()
    .Where(e => e.EventType.Name == "OrderPlaced")
    .Take(1000)
    .ToListAsync();

// Cell 3: Plot order volume by day
#r "nuget: Plotly.NET"
var chart = Chart2D.Chart.Line(orderEvents.GroupBy(e => e.Timestamp.Date)...);
```

---

## Proposed Implementation Phases

### Phase 1: Foundation (Weeks 1-2)
**Goal:** Prove out event data quality and projection patterns

1. **Create async projections** for analytics:
   - `ProductPerformanceView` (SKU-level metrics: views, carts, orders, returns)
   - `CustomerOrderHistoryView` (RFM calculation)
   - `InventoryVelocityView` (daily stock movement)

2. **Export to Parquet files** (for offline ML training):
   - Wolverine handler subscribes to all `*Placed`, `*Completed` events
   - Writes to S3/local filesystem as Parquet (columnar, compressed)
   - Use `ParquetSharp` NuGet package

3. **Set up Polyglot Notebook** for EDA:
   - Query Marten events directly
   - Visualize patterns (cart-to-order conversion, return rate trends)
   - Validate data quality (check for missing SKUs, null customers)

**Deliverable:** Jupyter-style notebooks showcasing event data richness

---

### Phase 2: ML Proof-of-Concept (Weeks 3-4)
**Goal:** Build and evaluate first ML model

1. **Product Recommendation Model (ML.NET):**
   - Train matrix factorization on `ItemAdded` + `OrderPlaced` events
   - Evaluate with NDCG@10 (Normalized Discounted Cumulative Gain)
   - Serve via REST API endpoint (`GET /api/recommendations/{customerId}`)

2. **Demand Forecasting Model:**
   - Extract time series from `OrderPlaced` events (daily sales by SKU)
   - Train Prophet model (via Python interop or ML.NET time series forecaster)
   - Compare forecast accuracy (MAPE, RMSE) vs naive baseline (last week's sales)

3. **Integration Testing:**
   - Add recommendation calls to Storefront.Web cart page
   - A/B test: 50% control (no recommendations), 50% treatment (show ML recommendations)
   - Track `ItemAdded` conversion rate

**Deliverable:** Working recommendation endpoint with <100ms latency

---

### Phase 3: Production Deployment (Weeks 5-6)
**Goal:** Operationalize ML models

1. **Feature Store (Redis):**
   - Deploy Redis container in Docker Compose
   - Async projection writes features to Redis (customer RFM, SKU return rate)
   - ML model queries Redis for inference (no direct Marten queries)

2. **Model Retraining Pipeline:**
   - Scheduled job (Hangfire, Quartz.NET) runs nightly
   - Exports last 90 days of events to Parquet
   - Retrains ML.NET model, evaluates on holdout set
   - Deploys new model if accuracy improves (semantic versioning)

3. **Monitoring & Observability:**
   - OpenTelemetry traces for recommendation API latency
   - Log model version served with each request
   - Alert on recommendation API error rate >1%

**Deliverable:** Self-updating recommendation system in production

---

### Phase 4: Advanced Analytics (Future)
**Goal:** Enable business intelligence and data science

1. **Data Warehouse (ClickHouse or Druid):**
   - Stream all Marten events to warehouse (via Kafka or direct insert)
   - Create materialized views for dashboards (sales by vendor, return rate trends)
   - Connect Grafana or Tableau for self-service BI

2. **Advanced ML Models:**
   - **Return Prediction:** Logistic regression to flag high-risk orders
   - **Fraud Detection:** Isolation Forest on payment failure patterns
   - **Pricing Optimization:** Reinforcement learning for dynamic pricing

3. **MLOps Platform:**
   - MLflow for experiment tracking (model accuracy, hyperparameters)
   - Model registry (staging → production promotion)
   - CI/CD pipeline for model deployment (GitHub Actions)

---

## Redis vs PostgreSQL for ML Feature Serving

### Why Redis for Online Inference?

**TL;DR:** Redis provides **sub-millisecond latency** for real-time feature lookups during ML inference. PostgreSQL is excellent for training data (batch queries), but Redis is optimized for the read-heavy, latency-sensitive inference workload.

---

### The Problem: Inference Latency Budget

**ML Inference API SLA:** `GET /api/recommendations/{customerId}` must respond in **<100ms (p99)**

**Latency Breakdown:**
```
Total Budget: 100ms
├── Network (API Gateway → Service): ~10ms
├── ML Model Inference (ML.NET prediction): ~20ms
├── Feature Retrieval: ~5ms ← THIS IS CRITICAL
├── Response Serialization: ~5ms
└── Buffer (safety margin): ~60ms
```

**Feature Retrieval Examples:**
- Customer RFM scores (Recency, Frequency, Monetary)
- SKU co-occurrence vectors (products frequently bought together)
- Real-time cart contents (current session state)
- SKU metadata (price, category, vendor)

**Why 5ms matters:** If feature retrieval takes 50ms instead of 5ms, the API exceeds its 100ms SLA. At scale (1000s of requests/sec), this compounds into a poor user experience.

---

### Option 1: Query PostgreSQL Directly (Discouraged)

**Pattern:**
```csharp
public async Task<RecommendationResponse> GetRecommendations(string customerId)
{
    // Query 1: Get customer RFM from Marten projection
    var customerRfm = await session.Query<CustomerRfmView>()
        .FirstOrDefaultAsync(c => c.CustomerId == customerId);

    // Query 2: Get top SKUs by co-occurrence
    var topSkus = await session.Query<ProductCooccurrenceView>()
        .Where(p => p.SourceSkus.Contains(currentCart.Skus))
        .OrderByDescending(p => p.Score)
        .Take(10)
        .ToListAsync();

    // Query 3: Get SKU metadata (price, category)
    var skuMetadata = await session.Query<Product>()
        .Where(p => topSkus.Select(s => s.Sku).Contains(p.Sku))
        .ToListAsync();

    return mlModel.Predict(customerRfm, topSkus, skuMetadata);
}
```

**Latency Profile:**
- Query 1 (Customer RFM): ~15ms (index lookup on CustomerId)
- Query 2 (Co-occurrence): ~30ms (array overlap + sort + limit)
- Query 3 (SKU metadata): ~10ms (IN clause on 10 SKUs)
- **Total: ~55ms** (already exceeds 5ms budget by 11x)

**Why PostgreSQL is Slow for This:**
1. **Query planning overhead:** PostgreSQL planner evaluates execution plans (~5-10ms per query)
2. **Index lookups:** Even with B-tree indexes, multi-column lookups are slower than in-memory hash tables
3. **Network roundtrips:** Each query requires a separate connection pool checkout + TCP roundtrip
4. **JSON deserialization:** `Query<T>()` must deserialize JSONB → C# object (Marten's document model)

**When PostgreSQL is Fine:**
- **Batch/offline workloads:** Training data exports (nightly job, 10+ minutes is acceptable)
- **Admin dashboards:** OLAP queries (1-5 seconds acceptable)
- **Low-throughput APIs:** Internal tools (<10 requests/min)

---

### Option 2: Redis Feature Store (Recommended)

**Pattern:**
```csharp
public async Task<RecommendationResponse> GetRecommendations(string customerId)
{
    var redis = connectionMultiplexer.GetDatabase();

    // Fetch features in parallel (Redis pipelining)
    var customerRfmTask = redis.StringGetAsync($"customer:{customerId}:rfm");
    var topSkusTask = redis.SortedSetRangeByScoreAsync($"customer:{customerId}:recs", order: Order.Descending, take: 10);
    var skuMetadataTask = redis.HashGetAllAsync($"sku:{sku}:metadata");

    await Task.WhenAll(customerRfmTask, topSkusTask, skuMetadataTask);

    var customerRfm = JsonSerializer.Deserialize<CustomerRfm>(customerRfmTask.Result);
    // ... deserialize other features

    return mlModel.Predict(customerRfm, topSkus, skuMetadata);
}
```

**Latency Profile:**
- Redis GET/ZRANGE (pipelined): **~1-2ms** (in-memory hash table lookup)
- JSON deserialization: ~1ms (same as PostgreSQL)
- **Total: ~3ms** (within 5ms budget)

**Why Redis is Fast:**
1. **In-memory:** No disk I/O, pure RAM lookups (O(1) hash table access)
2. **Pipelining:** Multiple commands sent in single TCP roundtrip
3. **No query planning:** Pre-computed keys (no SQL parsing/optimization)
4. **Connection pooling:** Persistent connections (StackExchange.Redis handles this efficiently)

---

### How Features Get Into Redis

**Async Projection Pattern:**
```csharp
// Marten async projection writes to Redis as side effect
public sealed class CustomerRfmProjection : MultiStreamProjection<CustomerRfmView, string>
{
    private readonly IConnectionMultiplexer _redis;

    public CustomerRfmProjection(IConnectionMultiplexer redis)
    {
        _redis = redis;
        Identity<OrderPlaced>(e => e.CustomerId);
    }

    public CustomerRfmView Create(OrderPlaced e) => new(e.CustomerId)
    {
        LastOrderDate = e.PlacedAt,
        TotalOrders = 1,
        TotalSpent = e.TotalAmount
    };

    public CustomerRfmView Apply(OrderPlaced e, CustomerRfmView view) => view with
    {
        LastOrderDate = e.PlacedAt,
        TotalOrders = view.TotalOrders + 1,
        TotalSpent = view.TotalSpent + e.TotalAmount
    };

    // Side effect: write to Redis after Marten commit
    public async Task AfterCommitAsync(CustomerRfmView view, IDocumentSession session)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(view);
        await db.StringSetAsync($"customer:{view.CustomerId}:rfm", json);
    }
}
```

**Data Flow:**
```
OrderPlaced event → Marten async projection → CustomerRfmView (PostgreSQL) → Redis cache
                                                       ↑                          ↓
                                              (source of truth)            (inference cache)
```

**Why Keep PostgreSQL Projections?**
- **Durability:** Redis can evict keys under memory pressure (LRU policy)
- **Backfill:** If Redis restarts, re-populate from PostgreSQL projections
- **Auditing:** SQL queries on historical features (debugging, compliance)
- **Training data:** Batch queries for model retraining (read from PostgreSQL, not Redis)

---

### Redis as "Very Fast Document Store"

**You're absolutely right!** Redis is effectively a **denormalized read store** optimized for:
1. **Key-value lookups:** `GET customer:12345:rfm` → O(1) hash table access
2. **Sorted sets:** `ZRANGE customer:12345:recs` → top-N recommendations pre-ranked
3. **Hashes:** `HGETALL sku:DOG-FOOD-5LB:metadata` → structured SKU data
4. **Lists:** `LPUSH recent:views:12345` → user browsing history (capped at 100 items)

**Redis vs Marten (Document Store Mode):**

| Feature | Marten (PostgreSQL) | Redis |
|---------|---------------------|-------|
| Latency (GET) | ~15ms (JSONB index lookup) | ~1ms (in-memory hash) |
| Throughput | ~10K reads/sec (single instance) | ~100K reads/sec (single instance) |
| Data Model | JSON documents (flexible schema) | Key-value, sorted sets, hashes (flexible) |
| Query Language | LINQ → SQL (rich querying) | Key patterns only (no joins/filters) |
| Durability | ACID (write-ahead log) | Optional AOF/RDB (eventual consistency) |
| Use Case | Source of truth (projections) | Ephemeral cache (inference features) |

**Key Insight:** Redis is **not replacing PostgreSQL**—it's a **denormalized read cache** for the inference hot path.

---

### Could ML.NET Read PostgreSQL Directly? (Yes, but...)

**Option: Skip Redis, Query Marten Projections**

**Pros:**
- ✅ Simpler architecture (one less service)
- ✅ Strong consistency (no cache invalidation issues)
- ✅ Rich querying (LINQ, SQL aggregations)

**Cons:**
- ❌ **Latency:** 15-50ms per query (vs 1-2ms Redis)
- ❌ **Throughput:** ~10K reads/sec (vs 100K+ Redis)
- ❌ **Database load:** Inference queries compete with transactional workload (orders, carts, payments)
- ❌ **Scaling:** Vertical scaling only (bigger PostgreSQL instance) vs horizontal Redis sharding

**When This Works:**
- **Low traffic:** <100 recommendations/sec (early MVP, internal tools)
- **Batch inference:** Pre-compute recommendations nightly, store in PostgreSQL
- **Relaxed latency:** API SLA is >500ms (e.g., admin dashboard, not customer-facing)

**When This Breaks:**
- **High traffic:** 1000+ recommendations/sec (Black Friday, marketing campaigns)
- **Strict latency:** <100ms p99 SLA (customer-facing, affects conversion rate)
- **Mixed workload:** Inference queries slow down transactional writes (order placement)

---

### Recommended Architecture: Polyglot Persistence

**Training (Offline / Batch):**
```
Marten Events → Parquet Export (nightly) → ML.NET Training → Model Artifact
```
**Source:** PostgreSQL (rich SQL queries, time-travel, joins)

**Inference (Online / Real-Time):**
```
Marten Async Projections → Redis Cache → ML.NET Inference → REST API
```
**Source:** Redis (sub-millisecond latency, high throughput)

**Observability (Analytics / BI):**
```
Marten Events → Kafka → ClickHouse → Grafana Dashboards
```
**Source:** ClickHouse (columnar, fast aggregations)

**Why Polyglot?**
- Each datastore optimized for its workload (OLTP, OLAP, caching, streaming)
- PostgreSQL remains source of truth (Marten projections)
- Redis is ephemeral (can be rebuilt from PostgreSQL if needed)
- ClickHouse is append-only (historical analytics, no deletes)

---

### Quick Start: PostgreSQL-Only MVP (Phase 1)

**For initial exploration (Polyglot Notebooks, Parquet exports):**
- ✅ Query Marten projections directly
- ✅ Train ML.NET models on PostgreSQL data
- ✅ Prototype recommendation API with PostgreSQL queries
- ✅ Measure baseline latency (establish SLA)

**When to Add Redis (Phase 2):**
- Inference API latency exceeds SLA (>100ms p99)
- Throughput requirements increase (>100 req/sec)
- Database CPU spikes during inference load
- Product owner requests real-time recommendations

**Migration Path:**
1. Create async projection with `AfterCommitAsync` side effect (write to Redis)
2. Update inference API to check Redis first, fallback to PostgreSQL
3. Warm Redis cache from PostgreSQL projections (backfill script)
4. Monitor cache hit rate (should be >95%)
5. Gradually increase traffic to Redis-backed API

---

## Recommended Tech Stack (Phased Approach)

### Minimal Viable Stack (Phase 1-2)
```
Marten (existing) → Async Projections → Redis (features) → ML.NET (models) → REST API
                          ↓                                         ↓
                    PostgreSQL                              Polyglot Notebooks (EDA)
                  (source of truth)
```

**Infrastructure:** Add Redis container to `docker-compose.yml` (minimal overhead)

**Libraries:**
- `Microsoft.ML` (recommendation, forecasting)
- `StackExchange.Redis` (feature store)
- `ParquetSharp` (offline training data export)

---

### Production-Grade Stack (Phase 3-4)
```
Marten Events → Wolverine → RabbitMQ → Event Subscribers
                                            ↓
                            ┌───────────────┴───────────────┐
                            ↓                               ↓
                      Redis (online)                  ClickHouse (offline)
                            ↓                               ↓
                      ML.NET Models                   Grafana Dashboards
                            ↓                               ↓
                    Storefront.Web                    Data Science Team
```

**New Infrastructure:**
- **Redis:** Feature store (online serving)
- **ClickHouse:** Data warehouse (OLAP queries)
- **Grafana:** Dashboards (pre-built ClickHouse datasource)
- **MLflow:** Model tracking (optional, Python-based)

---

## Quick Wins (No Code Changes Required)

### 1. **Export Marten Events to CSV** (Today)
```bash
# Run SQL query against Marten event store
psql -h localhost -p 5433 -U postgres -d postgres -c \
  "COPY (SELECT * FROM orders.mt_events WHERE type = 'OrderPlaced') TO STDOUT WITH CSV HEADER" \
  > order_placed_events.csv
```

**Then:** Load CSV into Excel/Google Sheets, create pivot tables (sales by SKU, average order value)

---

### 2. **Polyglot Notebook for Cart Abandonment Analysis**
- Query `CartAbandoned` events
- Join with `CheckoutStarted` events (compute abandonment rate)
- Segment by cart value, item count, customer type
- **Insight:** Identify high-value carts that abandon (proactive follow-up)

---

### 3. **SQL Views for Business Intelligence**
Create PostgreSQL views on Marten event streams:
```sql
-- View: Daily sales by SKU
CREATE VIEW analytics.daily_sales_by_sku AS
SELECT
  (data->>'Sku')::text AS sku,
  date_trunc('day', timestamp) AS sale_date,
  COUNT(*) AS order_count,
  SUM((data->>'Quantity')::int) AS units_sold
FROM orders.mt_events
WHERE type = 'OrderPlaced'
GROUP BY 1, 2;
```

**Then:** Connect Grafana, Metabase, or Tableau to these views (no ETL needed)

---

## Risk Assessment

### Low-Risk (Recommended for MVP)
- **Async Projections:** Marten already supports this (no architectural change)
- **ML.NET:** Native .NET (no Python interop complexity)
- **Redis:** Lightweight, battle-tested, runs in Docker Compose

### Medium-Risk (Phase 2+)
- **Kafka:** Requires operational expertise (monitoring, rebalancing)
- **ClickHouse:** New infrastructure (backup/restore, schema evolution)
- **Model Retraining:** Need CI/CD for model deployments

### High-Risk (Avoid for Now)
- **Real-Time Inference (SageMaker, Azure ML):** Adds cloud dependencies, cost
- **Complex ML Models (Deep Learning):** Requires GPU infrastructure, specialized expertise
- **Custom Event Store Migration:** Marten is excellent; no need to switch

---

## Success Metrics

### Phase 1 (Foundation)
- ✅ Export 90 days of events to Parquet (<1 hour)
- ✅ Create 3 async projections with <5 second lag
- ✅ Generate 5+ insights from Polyglot Notebooks

### Phase 2 (ML Proof-of-Concept)
- ✅ Recommendation API latency <100ms (p99)
- ✅ Recommendation NDCG@10 >0.3 (beat random baseline)
- ✅ A/B test shows >5% lift in cart-to-order conversion

### Phase 3 (Production)
- ✅ 99.9% uptime for recommendation API
- ✅ Model retraining runs nightly without manual intervention
- ✅ Feature store cache hit rate >95%

---

## Next Steps (Action Items)

### Immediate (This Week)
1. **Export sample events** to CSV (validate data quality)
2. **Install Polyglot Notebooks** VS Code extension
3. **Create first notebook:** Cart abandonment funnel analysis

### Short-Term (Next 2 Weeks)
1. **Add Redis to docker-compose.yml**
2. **Implement `ProductPerformanceView` async projection** (SKU-level metrics)
3. **Prototype ML.NET recommendation model** (train on local CSV export)

### Medium-Term (Next Month)
1. **Deploy recommendation API endpoint** (Storefront.Api)
2. **Integrate recommendations into Storefront.Web** (cart page)
3. **Set up Grafana dashboards** (sales, return rate, payment success)

---

## References

### Documentation
- **Marten Projections:** https://martendb.io/events/projections/
- **ML.NET Tutorials:** https://dotnet.microsoft.com/learn/ml-dotnet
- **Feast Feature Store:** https://docs.feast.dev/

### Books
- *Streaming Systems* (Akidau et al.) — Event stream processing patterns
- *Machine Learning Design Patterns* (Lakshmanan et al.) — ML system architecture
- *Designing Data-Intensive Applications* (Kleppmann) — Event sourcing, CQRS

### Tools
- **ML.NET Model Builder:** Visual Studio extension (no-code ML training)
- **Polyglot Notebooks:** https://github.com/dotnet/interactive
- **ParquetSharp:** https://github.com/G-Research/ParquetSharp

---

## Conclusion

CritterSupply's event-sourced architecture is **analytics-ready** today. The 119 distinct event types capture rich behavioral, transactional, and operational data across the full order-to-fulfillment lifecycle.

**Recommended First Step:** Export events to Parquet, train ML.NET recommendation model, deploy to Storefront.Api. This requires **minimal infrastructure** (just add Redis) and leverages **native .NET tooling** (ML.NET, StackExchange.Redis).

**Estimated Time to MVP:** 2-4 weeks for working product recommendations in Storefront.Web.

**ROI Potential:** Even a 1% lift in cart-to-order conversion from better recommendations could generate significant revenue at scale.

---

**Sign-offs:**
- [ ] Product Owner (business value validation)
- [ ] Engineering Lead (feasibility review)
- [ ] Data Science (model approach validation)
