# AIP Mortgage API

.NET 8 microservice for a mortgage **Agreement in Principle (AIP)**: it decides whether an applicant is Accepted, Referred or Declined, and calculates borrowing power. It runs on Amazon EKS alongside the [aip-webapp](https://github.com/ServinCharles/aip-webapp) React frontend, and is tested end to end by the [Agentic-qa-demo](https://github.com/ServinCharles/Agentic-qa-demo) framework.

This repo also owns the shared AWS infrastructure (VPC, EKS cluster, ECR repositories) in `infra/terraform`.

```
 Agentic-qa-demo (WDIO BDD / API / a11y / perf)
          │  BASE_URL=http://<load balancer>
          ▼
 ┌──────────────── EKS namespace: aip-mortgage ────────────────┐
 │  aip-webapp (React + nginx) ──/api/*──▶ aip-api (.NET, this repo) │
 └──────────────────────────────────────────────────────────────┘
```

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Kubernetes liveness/readiness probe |
| POST | `/api/mortgages/dip/decision` | AIP decision: Accepted / Referred / Declined |
| POST | `/api/mortgages/dip/calculate-borrowing-power` | Affordability calculator |

```bash
curl -X POST http://localhost:8080/api/mortgages/dip/decision \
  -H "Content-Type: application/json" \
  -d '{"annualIncome": 45000, "loanAmount": 150000, "existingCommitments": 0}'
```

Decision rules: monthly commitments above 40% of monthly income are **Declined**; otherwise a loan above 4.5x income is **Referred**; otherwise **Accepted**.

## Run locally

```bash
dotnet run --project src/AipApi --urls http://localhost:8080
dotnet test tests/AipApi.Tests --collect:"XPlat Code Coverage"
```

Or in Docker: `docker build -t aip-api . && docker run -p 8080:8080 aip-api`

## Code coverage from end-to-end tests

The `coverage` Docker target runs the API under `dotnet-coverage`, so the report shows what the test framework's API and BDD suites actually exercise in this code:

```bash
docker build --target coverage -t aip-api:coverage .
docker run -d --name aip-api -p 8080:8080 -v "$PWD/coverage:/coverage" aip-api:coverage
# ...run the Agentic-qa-demo suites...
docker stop aip-api        # writes coverage/aip-api.e2e.cobertura.xml
```

## Deploy to AWS

Prerequisites: AWS CLI v2 (logged in), Terraform 1.6+, kubectl, Docker.

```bash
# 1. Infrastructure, once (about 15 minutes; London region by default)
terraform -chdir=infra/terraform init
terraform -chdir=infra/terraform apply

# 2. This service (deploy it before aip-webapp)
./scripts/deploy.sh

# 3. Then run ./scripts/deploy.sh in the aip-webapp repo, which prints the public URL
```

Check it with `kubectl -n aip-mortgage get pods,svc`.

## Cost and teardown

Left running, the environment costs roughly **$180 to $190 a month** (EKS control plane about $73, two t3.medium nodes about $60, NAT gateway about $35, load balancer about $18). Remove everything after a demo:

```bash
./scripts/teardown.sh
```

It deletes the `aip-mortgage` namespace first (so the load balancer is removed), then runs `terraform destroy`.
