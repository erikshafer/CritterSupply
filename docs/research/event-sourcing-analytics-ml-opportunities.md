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
- **Best For:** Real-time event streaming, ETL to data warehouses
- **Why Kafka:**
  - Industry-standard event streaming platform
  - High-throughput (millions of events/sec)
  - Durable log (replay capability for backfilling)
  - Connect ecosystem (Snowflake, BigQuery, Elasticsearch connectors)

**CritterSupply Integration:**
```
Marten Events → Wolverine Subscriber → Kafka Producer → Kafka Topics
                                                          ↓
                                                    Kafka Streams App
                                                          ↓
                                                    Feature Store (Redis)
                                                          ↓
                                                    ML Model (ML.NET)
```

**Alternative (Lighter Weight):** Keep RabbitMQ, add subscriber that writes to TimescaleDB (PostgreSQL extension for time-series).

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

## Recommended Tech Stack (Phased Approach)

### Minimal Viable Stack (Phase 1-2)
```
Marten (existing) → Async Projections → Redis (features) → ML.NET (models) → REST API
                                                                    ↓
                                                            Polyglot Notebooks (EDA)
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
