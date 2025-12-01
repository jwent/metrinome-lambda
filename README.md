# OnTrack

OnTrack is a subscription-based analytics platform with a **React frontend** and a **.NET 6 GraphQL backend**, deployed on **AWS** and integrated with **Stripe** for billing.

This repository contains the main application code for OnTrack:

- A single-page application (SPA) frontend (React)
- A GraphQL API backend (.NET 6)
- Scripts and notes for local development and deployment

> ⚠️ **Note:** Infrastructure as code (Terraform, RDS, API Gateway, CloudFront, etc.) typically lives in a separate repo, e.g. `on-track-infra`. This README focuses on building and deploying the app itself.

---

## Table of Contents

- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Repository Layout](#repository-layout)
  - [Environment Variables](#environment-variables)
- [Backend (.NET 6 GraphQL API)](#backend-net-6-graphql-api)
  - [Running Locally](#running-locally-backend)
  - [Database Migrations](#database-migrations)
  - [Running Tests](#running-tests-backend)
  - [Building for Production](#building-for-production-backend)
- [Frontend (React)](#frontend-react)
  - [Running Locally](#running-locally-frontend)
  - [Building for Production](#building-for-production-frontend)
- [Deployment](#deployment)
  - [Backend Deployment (AWS Lambda / API Gateway)](#backend-deployment-aws-lambda--api-gateway)
  - [Frontend Deployment (S3 / CloudFront)](#frontend-deployment-s3--cloudfront)
  - [Stripe Webhooks](#stripe-webhooks)
- [Common Workflows](#common-workflows)
- [Troubleshooting](#troubleshooting)

---

## Architecture

At a high level:

- **Frontend**
  - React SPA
  - Talks to the GraphQL API via HTTPS
  - Handles authentication and subscription management UI

- **Backend**
  - `.NET 6` GraphQL server (e.g. Hot Chocolate or GraphQL.NET)
  - Uses **PostgreSQL** (Aurora RDS in production)
  - Integrates with **Stripe** for:
    - Customer creation
    - Subscription creation/changes
    - Trial logic
    - Webhook handling

- **Infrastructure (external repo)**
  - AWS Lambda for backend API
  - API Gateway (HTTP API) for public GraphQL endpoint
  - S3 + CloudFront for hosting the React app
  - Route 53 / ACM for custom domains and TLS certificates

---

## Tech Stack

**Backend**

- .NET 6
- GraphQL
- Entity Framework Core
- PostgreSQL
- Stripe .NET SDK

**Frontend**

- React
- TypeScript (if enabled)
- GraphQL client (e.g. Apollo)
- Tailwind CSS / CSS modules (depending on current setup)

**Infrastructure**

- AWS Lambda
- AWS API Gateway
- AWS S3 / CloudFront
- AWS RDS (PostgreSQL / Aurora)
- Terraform (in `on-track-infra`, if used)

---

## Getting Started

### Prerequisites

You’ll need:

- **.NET 6 SDK**
- **Node.js** (LTS) + **npm** or **Yarn**
- **PostgreSQL** (local or remote)
- A **Stripe** account + API keys
- (Optional for prod) AWS CLI configured with appropriate credentials

### Repository Layout

Adjust to match your actual structure, but commonly:

```text
/ontrack
  /backend           # .NET 6 GraphQL API
  /frontend          # React SPA
  README.md
