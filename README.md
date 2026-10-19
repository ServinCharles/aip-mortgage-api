# AIP Mortgage API

.NET 8 microservice for a mortgage **Agreement in Principle (AIP)**: it decides whether an applicant is Accepted, Referred or Declined, and calculates borrowing power. It runs on Amazon EKS alongside the [aip-webapp](https://github.com/ServinCharles/aip-webapp) React frontend, and is tested end to end by the [Agentic-qa-demo](https://github.com/ServinCharles/Agentic-qa-demo) framework.

This repo holds the code and the Jenkins **build** pipeline. Infrastructure and deployment live in [aip-deployment-pipeline](https://github.com/ServinCharles/aip-deployment-pipeline).

```
 Agentic-qa-demo (WDIO BDD / API / a11y / perf)
          │  BASE_URL=http://<load balancer>
          ▼
 sit-01-dip: aip-webapp (React + nginx) ──/api/*──▶ sit-01-api: aip-api (.NET, this repo)
```

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Kubernetes liveness/readiness probe |
| POST | `/api/mortgages/dip/decision` | AIP decision: Accepted / Referred / Declined |
| POST | `/api/mortgages/dip/calculate-borrowing-power` | Affordability calculator |
| GET | `/api/applications/{applicationId}` | A saved application (only when persistence is on, otherwise 404) |

```bash
curl -X POST http://localhost:5080/api/mortgages/dip/decision \
  -H "Content-Type: application/json" \
  -d '{"annualIncome": 45000, "loanAmount": 150000, "existingCommitments": 0}'
```

Decision rules: monthly commitments above 40% of monthly income are **Declined**; otherwise a loan above 4.5x income is **Referred**; otherwise **Accepted**.

Every decision response includes an `applicationId`, e.g. `AIP-20261019-7F3C2A9B`, alongside the original fields.

## Saving applications and decision events (feature flag)

`PERSISTENCE_ENABLED` switches on saving each application to **MongoDB** and publishing an `AipDecisionMade` event to **Kafka** (read by [aip-audit-service](https://github.com/ServinCharles/aip-audit-service)). It is **off by default**, so the API runs exactly as before wherever MongoDB and Kafka don't exist (locally, CI, sit-02).

| Variable | Default | Purpose |
| --- | --- | --- |
| `PERSISTENCE_ENABLED` | `false` | The switch |
| `MONGODB_URI` | `mongodb://localhost:27017` | MongoDB connection |
| `MONGODB_DATABASE` | `aip` | Database; applications go in the `applications` collection |
| `KAFKA_BOOTSTRAP_SERVERS` | `localhost:9092` | Kafka broker |
| `KAFKA_TOPIC` | `aip.decisions` | Topic for decision events, keyed by `applicationId` |
| `APPLICATION_RETENTION_DAYS` | `0` (keep) | TTL index so test environments clean up old applications |

- A MongoDB or Kafka failure never blocks a decision: it is logged, and the customer still gets an answer.
- Events carry IDs and decision figures only, never income.
- Event contract (schemaVersion 1): `eventId`, `eventType` (`AipDecisionMade`), `schemaVersion`, `occurredAt`, `applicationId`, `decision`, `loanAmount`, `maxBorrowing`.

```bash
PERSISTENCE_ENABLED=true dotnet run --project src/AipApi --urls http://localhost:5080
```

## Run locally

Locally the API uses port **5080**, because 8080 is Jenkins and 3000 is the test framework's mock server.

```bash
dotnet run --project src/AipApi --urls http://localhost:5080
dotnet test tests/AipApi.Tests --collect:"XPlat Code Coverage"
```

Or in Docker: `docker build -t aip-api . && docker run -p 5080:8080 aip-api`

## Code coverage from end-to-end tests

The `coverage` Docker target runs the API under `dotnet-coverage`, so the report shows what the test framework's API and BDD suites actually exercise in this code:

```bash
docker build --target coverage -t aip-api:coverage .
docker run -d --name aip-api -p 5080:8080 -v "$PWD/coverage:/coverage" aip-api:coverage
# ...run the Agentic-qa-demo suites...
docker stop aip-api        # writes coverage/aip-api.e2e.cobertura.xml
```

## Deployment

Deployment lives in [aip-deployment-pipeline](https://github.com/ServinCharles/aip-deployment-pipeline): the AWS infrastructure (Terraform), the Kubernetes config for sit-01 and sit-02, and the Jenkins **AIP Deployment Pipeline**. This service runs in the `<env>-api` namespace (e.g. `sit-01-api`), and only the web app can reach it.

## Jenkins build pipeline

`Jenkinsfile` builds the service once, tests and scans it, then publishes one image tagged with the git commit (e.g. `aip-api:3f2a1c9`). The AIP Deployment Pipeline then promotes that exact image; set `TRIGGER_DEPLOY` to deploy it to sit-01 automatically after a green build.

```
Checkout → Build & Unit Test → SonarQube → Snyk → Docker Build → Image Scan → Push to ECR → Trigger Deploy
```

| Stage | What it does | Fails the build when |
| --- | --- | --- |
| Build & Unit Test | `dotnet test` in the .NET SDK container, JUnit results + Cobertura/OpenCover coverage | any test fails |
| SonarQube | .NET scanner with coverage, waits for the quality gate | quality gate fails |
| Snyk | dependency (SCA) scan of the project | a high or critical vulnerability is found |
| Docker Build | runtime image, cross-compiled for `PLATFORM` (default `linux/amd64` for EKS) | the image doesn't build |
| Image Scan | Trivy (default) or Twistlock `twistcli`, JSON report archived | a fixable HIGH/CRITICAL CVE is found |
| Push to ECR | pushes the scanned image, archives `ecr-image.properties` for the deploy pipeline | the push fails |

Every tool runs in a Docker container, so the Jenkins machine only needs Docker. SonarQube, Snyk and the ECR push are off by default; switch them on with **Build with Parameters** once their credentials exist.

### Jenkins setup

1. **New Item → Pipeline**, then *Pipeline script from SCM* → Git → `https://github.com/ServinCharles/aip-mortgage-api.git`, branch `*/main`, script path `Jenkinsfile`.
2. Plugins: Pipeline, Git, Credentials Binding, JUnit (all in the suggested set).
3. Credentials (*Manage Jenkins → Credentials*), only for the stages you switch on:

| ID | Type | Used by |
| --- | --- | --- |
| `sonar-token` | Secret text | SonarQube / SonarCloud token |
| `snyk-token` | Secret text | Snyk API token |
| `aws-credentials` | Username with password (access key ID / secret key) | ECR push |
| `prisma-cloud` | Username with password | Twistlock scan |
