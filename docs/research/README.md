# CritterSupply Research

This directory contains research documents exploring potential enhancements and capabilities for the CritterSupply reference architecture.

## Active Research

### Event Sourcing Analytics & ML Opportunities (2026-03-12)
**Document:** [`event-sourcing-analytics-ml-opportunities.md`](./event-sourcing-analytics-ml-opportunities.md)

**Summary:** Explores how CritterSupply's 119 distinct event types across 7 event-sourced bounded contexts can power machine learning and analytics capabilities.

**Key Findings:**
- **Current State:** 7 BCs using event sourcing (Orders, Shopping, Payments, Inventory, Fulfillment, Returns, Pricing)
- **Event Richness:** 56 internal events + 63 integration messages = 119 total event types
- **High-Value Use Cases:** Product recommendations, demand forecasting, customer segmentation, fraud detection, return prediction, pricing optimization

**Recommended Tech Stack:**
- **ML Framework:** ML.NET (native .NET, no Python interop)
- **Feature Store:** Redis (low-latency online serving)
- **Event Streaming:** Keep RabbitMQ (or add Kafka for warehouse)
- **Data Warehouse:** ClickHouse or Apache Druid (optional, Phase 4)
- **Experimentation:** Polyglot Notebooks (.NET Interactive)

**MVP Timeline:** 2-4 weeks for working product recommendations in Storefront.Web

**Quick Wins (No Code):**
1. Export Marten events to CSV → analyze in Excel/Jupyter
2. Create PostgreSQL views on event streams → connect Grafana
3. Use Polyglot Notebooks for cart abandonment funnel analysis

**Next Steps:**
1. Add Redis to `docker-compose.yml`
2. Implement `ProductPerformanceView` async projection
3. Train ML.NET recommendation model on exported events
4. Deploy recommendation API endpoint to Storefront.Api

---

## Research Conventions

### File Naming
- Use kebab-case: `event-sourcing-analytics-ml-opportunities.md`
- Include date in H1 or metadata: `**Date:** 2026-03-12`
- Mark status: 🔍 Discovery / ⚠️ Proposed / ✅ Implemented

### Structure
1. **Executive Summary** (2-3 sentences)
2. **Current State** (what exists today)
3. **Opportunities** (what's possible)
4. **Recommendations** (specific tools/approaches)
5. **Implementation Plan** (phased approach)
6. **Risk Assessment** (what could go wrong)
7. **Success Metrics** (how to measure ROI)

### Sign-offs
Research documents should include placeholder sign-offs:
- [ ] Product Owner (business value)
- [ ] Engineering Lead (feasibility)
- [ ] Data Science / Domain Expert (technical validation)

---

## Index

| Document | Date | Status | Summary |
|----------|------|--------|---------|
| [Event Sourcing Analytics & ML](./event-sourcing-analytics-ml-opportunities.md) | 2026-03-12 | 🔍 Discovery | Leverage 119 event types for recommendations, forecasting, segmentation |

---

## Contributing

Research documents are **exploratory** and **time-boxed** (typically 15-30 minutes). They should:
- ✅ Identify opportunities based on existing architecture
- ✅ Propose concrete tools/frameworks with links
- ✅ Estimate effort and ROI
- ✅ Include quick wins (no-code or minimal-code)
- ❌ Not implement changes (use ADRs + cycle plans for that)

**When to create research docs:**
- Exploring new capabilities (ML, analytics, integrations)
- Evaluating technology choices (before ADR)
- Documenting domain discovery (market analysis, competitive research)

**When NOT to create research docs:**
- Architectural decisions (use ADRs instead)
- Implementation work (use cycle plans + GitHub Issues)
- Bug investigations (use GitHub Issues with `type:investigation` label)
